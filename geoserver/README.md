# GeoServer artifacts

The map's WMS display mode is rendered by GeoServer (workspace `basarsoft`, PostGIS store
`pg_basarsoft`, SQL-view layers `vw_point` / `vw_line` / `vw_polygon` / `vw_heat` with a
`%uid%` view parameter, plus the parameterless `vw_poi` for the shared POI catalogue).
That configuration lives in the GeoServer data directory (`~/geoserver_data`), not in
this repo.

## styles/

Attribute-driven SLD styles so the WMS image shows each shape in its saved `color` column,
matching the WFS mode styling in `basarsoft-client/src/pages/MapPage.jsx`. Rows
with a null `color` fall back to the app default `#2563eb` via
`if_then_else(isNull(color), '#2563eb', color)`.

They were uploaded and set as each layer's default style through the GeoServer REST API:

```bash
# upload (one per file)
curl -u <admin>:<password> -X POST \
  -H "Content-Type: application/vnd.ogc.sld+xml" \
  --data-binary @geoserver/styles/vw_point_color.sld \
  "http://localhost:8080/geoserver/rest/workspaces/basarsoft/styles?name=vw_point_color"

# set as the layer's default style (one per layer)
curl -u <admin>:<password> -X PUT \
  -H "Content-Type: application/json" \
  -d '{"layer":{"defaultStyle":{"name":"basarsoft:vw_point_color"}}}' \
  "http://localhost:8080/geoserver/rest/layers/basarsoft:vw_point"
```

The backend WMS proxy (`GET /api/geometry/wms`) requests `styles=` (empty), so GeoServer
uses these defaults — no backend change was needed.

## Heat map (vw_heat + vw_heat_heatmap)

The "Heat Map" tool renders a density heat map from a fourth SQL-view layer, `vw_heat`,
which collapses every live shape to a point (points as-is, lines via `ST_Centroid`,
polygons via `ST_PointOnSurface` so the point is guaranteed inside). Same `%uid%`
per-user scoping and soft-delete filtering as the other views; the `uid` parameter
defaults to `-1` (no match → renders nothing) with regex validator `^-?[0-9]+$`.

View SQL (created via REST `JDBC_VIRTUAL_TABLE`, key column `fid`, geometry `geom`
Point/4326):

```sql
SELECT 'point-' || p.id AS fid, p.geom::geometry(Point,4326) AS geom
FROM tbl_point p
WHERE p.is_deleted = false AND p.is_active = true AND p.user_id = %uid%
UNION ALL
SELECT 'line-' || l.id AS fid, ST_Centroid(l.geom)::geometry(Point,4326) AS geom
FROM tbl_line l
WHERE l.is_deleted = false AND l.is_active = true AND l.user_id = %uid%
UNION ALL
SELECT 'polygon-' || g.id AS fid, ST_PointOnSurface(g.geom)::geometry(Point,4326) AS geom
FROM tbl_polygon g
WHERE g.is_deleted = false AND g.is_active = true AND g.user_id = %uid%
```

`styles/vw_heat_heatmap.sld` turns those points into a raster with the `vec:Heatmap`
rendering transformation (radiusPixels 35, pixelsPerCell 10, output size bound to the
WMS request via `env(wms_bbox/wms_width/wms_height)`). The RasterSymbolizer ColorMap is
the 0→1 intensity scale; the frontend legend gradient (`.map-heat-legend-bar` in
`MapPage.css`) must mirror its entries exactly. No WPS service module is needed:
GeoServer 3.0.0 bundles `gt-process-feature`, which provides the `vec:Heatmap` function.

Registered the same way as the color styles:

```bash
curl -u <admin>:<password> -X POST \
  -H "Content-Type: application/vnd.ogc.sld+xml" \
  --data-binary @geoserver/styles/vw_heat_heatmap.sld \
  "http://localhost:8080/geoserver/rest/workspaces/basarsoft/styles?name=vw_heat_heatmap"

curl -u <admin>:<password> -X PUT \
  -H "Content-Type: application/json" \
  -d '{"layer":{"defaultStyle":{"name":"basarsoft:vw_heat_heatmap"}}}' \
  "http://localhost:8080/geoserver/rest/layers/basarsoft:vw_heat"
```

The backend proxy for it is `GET /api/geometry/wms/heatmap` (same contract as
`/api/geometry/wms`, but fixed to the `vw_heat` layer).

## POI layer (vw_poi + vw_poi_category)

The shared POI catalogue is served through a fifth SQL-view layer, `vw_poi`. Unlike the
per-user shape views it takes **no `%uid%` parameter** — POIs are global reference data.
Reads flow API → GeoServer: `GET /api/poi` is backed by a WFS GetFeature on this view
(`GeoServerReadService.GetPoisAsync`), and the view is appended (last, so markers draw on
top) to the `GET /api/geometry/wms` layer list. The shared `viewparams=uid:` group in that
combined GetMap is harmless: GeoServer ignores parameters a virtual table doesn't declare
(curl-verified).

A recursive CTE resolves each category's root-first breadcrumb (`category_path`) and its
**effective color** (`category_color`: the category's own `color` or the nearest ancestor's
— `COALESCE` keeps the deepest non-null while walking up). The `users` join deliberately
skips the soft-delete filter (matches the EF read's `IgnoreQueryFilters`), and the two
`time` columns are cast with `to_char` so WFS GeoJSON carries them as plain
`"HH:MM:SS"` strings.

View SQL (REST `JDBC_VIRTUAL_TABLE`, key column `id`, geometry `geom` Point/4326 — note
the SQL must be XML-escaped inside the REST body: `<` and `>` appear in the CTE):

```sql
WITH RECURSIVE cat_walk AS (
    SELECT c.id AS category_id, c.parent_id, c.name::text AS path, c.color AS color, 1 AS depth
    FROM tbl_poi_category c
    WHERE c.is_deleted = false AND c.is_active = true
  UNION ALL
    SELECT w.category_id, p.parent_id, p.name || ' > ' || w.path,
           COALESCE(w.color, p.color), w.depth + 1
    FROM cat_walk w
    JOIN tbl_poi_category p
      ON p.id = w.parent_id AND p.is_deleted = false AND p.is_active = true
    WHERE w.depth < 20
),
cat AS (
    SELECT DISTINCT ON (category_id) category_id, path, color
    FROM cat_walk
    ORDER BY category_id, depth DESC
)
SELECT p.id,
       p.name,
       p.geom::geometry(Point,4326) AS geom,
       p.category_id,
       c.name AS category_name,
       COALESCE(cat.path, '') AS category_path,
       cat.color AS category_color,
       to_char(p.open_time, 'HH24:MI:SS') AS open_time,
       to_char(p.close_time, 'HH24:MI:SS') AS close_time,
       p.user_id,
       u.username AS created_by,
       p.created_at,
       p.modified_date
FROM tbl_poi p
LEFT JOIN tbl_poi_category c
       ON c.id = p.category_id AND c.is_deleted = false AND c.is_active = true
LEFT JOIN cat ON cat.category_id = p.category_id
LEFT JOIN users u ON u.id = p.user_id
WHERE p.is_deleted = false AND p.is_active = true
```

`styles/vw_poi_category.sld` (the layer's default style) has two rules:

1. **Marker** (all scales): circle Size 14, white stroke 2, fill
   `if_then_else(isNull(category_color), '#e11d48', category_color)` — the fallback rose
   is `POI_COLOR` in `MapPage.jsx`.
2. **Label** (`MaxScaleDenominator 50000`): a bold halo'd `TextSymbolizer` on `name`, so
   POI names appear only when zoomed in close. `conflictResolution` drops colliding labels
   in dense clusters (the client mirrors this with `declutter: true`).

Zoom ↔ scale at EPSG:3857: GeoServer computes the scale denominator as
`resolution / 0.00028` (OGC 0.28 mm pixel), and `res(z) = 156543.03 / 2^z`, so
`SD(z) ≈ 559 082 264 / 2^z` — `50000` ≈ zoom 13.5. The client-side mirror is
`POI_LABEL_MAX_RESOLUTION = 14` m/px (= 50000 × 0.00028) in `MapPage.jsx`; change the SLD
and that constant together so both display modes flip labels at the same zoom.

### Reproducible registration

Apply the EF migration first, then ensure the `basarsoft` workspace and `pg_basarsoft`
PostGIS datastore exist. The tracked setup script creates or updates both the complete
`JDBC_VIRTUAL_TABLE` definition (`featuretypes/vw_poi.xml`) and its SLD, assigns the
default style, recalculates bounds, and verifies a WFS response. It is safe to rerun:

```bash
GEOSERVER_USER=admin \
GEOSERVER_PASSWORD='<password>' \
./geoserver/setup-poi.sh
```

Optional environment overrides are `GEOSERVER_URL`, `GEOSERVER_WORKSPACE`, and
`GEOSERVER_DATASTORE`. Defaults match this project. The equivalent final REST operations
performed by the script are:

```bash
# recompute the bboxes from the data after creating the feature type
curl -u <admin>:<password> -X PUT -H "Content-Type: application/xml" \
  -d '<featureType><name>vw_poi</name><enabled>true</enabled></featureType>' \
  "http://localhost:8080/geoserver/rest/workspaces/basarsoft/datastores/pg_basarsoft/featuretypes/vw_poi?recalculate=nativebbox,latlonbbox"

curl -u <admin>:<password> -X POST \
  -H "Content-Type: application/vnd.ogc.sld+xml" \
  --data-binary @geoserver/styles/vw_poi_category.sld \
  "http://localhost:8080/geoserver/rest/workspaces/basarsoft/styles?name=vw_poi_category"

curl -u <admin>:<password> -X PUT \
  -H "Content-Type: application/json" \
  -d '{"layer":{"defaultStyle":{"name":"basarsoft:vw_poi_category"}}}' \
  "http://localhost:8080/geoserver/rest/layers/basarsoft:vw_poi"
```
