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

A recursive CTE resolves each category's root-first breadcrumb (`category_path`), its
**effective color** (`category_color`) and its **effective icon** (`category_icon_key`).
For both presentation values the category's own value wins, otherwise the nearest
ancestor's value is inherited; an entirely null icon chain falls back to `pin`. The
`users` join deliberately skips the soft-delete filter (matches the EF read's
`IgnoreQueryFilters`), and the two `time` columns are cast with `to_char` so WFS GeoJSON
carries them as plain `"HH:MM:SS"` strings.

View SQL (REST `JDBC_VIRTUAL_TABLE`, key column `id`, geometry `geom` Point/4326 — note
the SQL must be XML-escaped inside the REST body: `<` and `>` appear in the CTE):

```sql
WITH RECURSIVE cat_walk AS (
    SELECT c.id AS category_id,
           c.parent_id,
           c.name::text AS path,
           c.color AS color,
           c.icon_key AS icon_key,
           1 AS depth
    FROM tbl_poi_category c
    WHERE c.is_deleted = false AND c.is_active = true
  UNION ALL
    SELECT w.category_id, p.parent_id, p.name || ' > ' || w.path,
           COALESCE(w.color, p.color),
           COALESCE(w.icon_key, p.icon_key),
           w.depth + 1
    FROM cat_walk w
    JOIN tbl_poi_category p
      ON p.id = w.parent_id AND p.is_deleted = false AND p.is_active = true
    WHERE w.depth < 20
),
cat AS (
    SELECT DISTINCT ON (category_id) category_id, path, color, icon_key
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
       COALESCE(cat.icon_key, 'pin') AS category_icon_key,
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

1. **Badge** (all scales): a Size 26 circle with white stroke 2 and fill
   `if_then_else(isNull(category_color), '#e11d48', category_color)`, overlaid with a
   centered Size 16 white SVG loaded from the dynamic relative path
   `poi-icons/${category_icon_key}.svg`.
2. **Label** (`MaxScaleDenominator 100000`): a bold halo'd `TextSymbolizer` on `name`, so
   POI names appear only when zoomed in close. `conflictResolution` drops colliding labels
   in dense clusters (the client mirrors this with `declutter: true`). Its vertical
   displacement is 18 px so it clears the larger badge.

The 20 accepted icon keys are `pin`, `food`, `coffee`, `bakery`, `health`, `pharmacy`,
`shopping`, `culture`, `museum`, `hotel`, `services`, `bank`, `fuel`, `transport`,
`airport`, `education`, `nature`, `sports`, `mail`, and `government`. Canonical SVGs live
under `basarsoft-client/public/poi-icons`; the setup script packages exactly those files,
plus the required `LICENSE.md` or `LICENSE.txt` attribution. SVG strokes/fills must be
white because GeoServer renders the packaged files directly over the category-colored
badge.

Zoom ↔ scale at EPSG:3857: GeoServer computes the scale denominator as
`resolution / 0.00028` (OGC 0.28 mm pixel), and `res(z) = 156543.03 / 2^z`, so
`SD(z) ≈ 559 082 264 / 2^z` — `100000` ≈ zoom 12.5. The client-side mirror is
`POI_LABEL_MAX_RESOLUTION = 28` m/px (= 100000 × 0.00028) in `MapPage.jsx`; change the SLD
and that constant together so both display modes flip labels at the same zoom.

### Reproducible registration

Apply the EF migration first, then ensure the `basarsoft` workspace and `pg_basarsoft`
PostGIS datastore exist. The tracked setup script creates or updates the complete
`JDBC_VIRTUAL_TABLE` definition (`featuretypes/vw_poi.xml`), builds a temporary SLD ZIP
from `styles/vw_poi_category.sld` and the canonical client SVGs, uploads that package,
assigns the default style, recalculates bounds, and verifies the WFS response and
`category_icon_key` schema field. The temporary package is removed on success or failure.
It is safe to rerun:

```bash
# Local preflight only: parses XML when xmllint is available, verifies all canonical SVGs,
# builds/tests the temporary ZIP, performs no REST calls, and needs no credentials.
./geoserver/setup-poi.sh --check

# Provision/update GeoServer.
GEOSERVER_USER=admin \
GEOSERVER_PASSWORD='<password>' \
./geoserver/setup-poi.sh
```

Optional environment overrides are `GEOSERVER_URL`, `GEOSERVER_WORKSPACE`, and
`GEOSERVER_DATASTORE`. Defaults match this project. Both `curl` and `zip` must be
available. The equivalent final REST operations performed by the script are:

```bash
# recompute the bboxes from the data after creating the feature type
curl -u <admin>:<password> -X PUT -H "Content-Type: application/xml" \
  -d '<featureType><name>vw_poi</name><enabled>true</enabled></featureType>' \
  "http://localhost:8080/geoserver/rest/workspaces/basarsoft/datastores/pg_basarsoft/featuretypes/vw_poi?recalculate=nativebbox,latlonbbox"

curl -u <admin>:<password> -X POST \
  -H "Content-Type: application/zip" \
  --data-binary @/tmp/vw_poi_category.zip \
  "http://localhost:8080/geoserver/rest/workspaces/basarsoft/styles"

curl -u <admin>:<password> -X PUT \
  -H "Content-Type: application/json" \
  -d '{"layer":{"defaultStyle":{"name":"basarsoft:vw_poi_category"}}}' \
  "http://localhost:8080/geoserver/rest/layers/basarsoft:vw_poi"
```

## Konum Analizi (vw_konum + vw_konum_heatmap)

The location-analysis tool renders a **weighted** heat map of the POIs inside a chosen
region (a Turkish province or a polygon drawn on the map), each POI contributing its
criterion's weight (1..100) instead of a flat 1.

The trick is that GeoServer never sees the region or the weights directly. The API stores
each run in `tbl_location_analysis` (region as `geometry(MultiPolygon,4326)`) +
`tbl_location_analysis_criterion` (category + weight rows), and the `vw_konum` SQL view is
parameterized by **`%aid%` — the run's integer id — alone** (`viewparams=aid:<id>`).
That keeps the URL tiny and the SQL-injection surface at "one regex-validated integer",
exactly like `vw_heat`'s `%uid%`:

- default `-1` → matches no run → renders nothing (fails closed),
- regex validator `^-?[0-9]+$` → any non-numeric value is rejected before the SQL runs,
- ownership is enforced in the API proxy (`GET /api/location-analysis/{id}/wms` answers
  404 unless the run belongs to the caller), so one user cannot render another's analysis.

The full virtual-table SQL lives in `featuretypes/vw_konum.xml`. Its shape:

1. `ana` — the run's region polygon; `crit` — the run's live criteria.
2. `cat_up` — a recursive CTE walking every category **up** its ancestor chain
   (`dist` = how far up). A criterion on a parent category thereby matches the whole
   subtree's POIs.
3. `matched` — POIs inside the region (`ST_Intersects`; boundary points count) joined to
   the criteria via that walk; `DISTINCT ON (poi.id) ... ORDER BY dist ASC` makes the
   **most specific criterion win** when both a parent and one of its subcategories are
   criteria. POIs matching no criterion drop out entirely.
4. Output columns: `id`, `geom` (Point/4326), `weight::float8`.

`styles/vw_konum_heatmap.sld` is `vw_heat_heatmap.sld` plus one parameter in the
`vec:Heatmap` transformation:

```xml
<ogc:Function name="parameter">
  <ogc:Literal>weightAttr</ogc:Literal>
  <ogc:Literal>weight</ogc:Literal>
</ogc:Function>
```

The kernel density surface is still normalized to 0..1 against its maximum (same as
`vw_heat`), so the client reuses the same legend gradient; intensities are **relative** —
a region with a single matched POI still shows a full-red core, and the kernel
(radiusPixels 35) bleeds slightly past the region edge by design (the *input* is clipped,
the raster is not).

### Reproducible registration

```bash
GEOSERVER_USER=admin \
GEOSERVER_PASSWORD='<password>' \
./geoserver/setup-konum.sh
```

Identical to `setup-poi.sh` except it does **not** pass `recalculate=nativebbox,latlonbbox`:
under the fail-closed `aid=-1` default the view is empty, so recalculation would store a
degenerate bbox. `featuretypes/vw_konum.xml` declares Turkey's extent
(25.5..45.0 E, 35.8..42.3 N) instead. The script's WFS verification therefore expects an
**empty** FeatureCollection — that proves the SQL parses while a real `aid` stays private.

### Province boundaries (tbl_province)

The region dropdown is backed by `tbl_province`, seeded once at API startup by
`ProvinceSeeder` from `Basarsoft.Api/Data/provinces.geojson` — Turkey's 81 provinces from
OSM `admin_level=4` boundaries (© OpenStreetMap contributors, ODbL; downloaded from the
`izzetkalic/geojsons-of-turkey` dataset). The committed file is pre-simplified so the repo
stays small and `ST_Intersects` stays fast; to regenerate it:

```bash
# 1. download the raw OSM export (12 MB), 2. keep the 81 (Multi)Polygons + name only,
# 3. simplify to ~8% of vertices at ~11 m precision (267 KB):
npx mapshaper turkey-al4-polys.geojson -simplify 8% keep-shapes -o precision=0.0001 provinces.geojson
```

(The two tiny MultiPolygon islets — Antalya's offshore rocks and Giresun Island — collapse
into the mainland polygons at this tolerance; irrelevant for POI analysis.)
