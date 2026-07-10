# GeoServer artifacts

The map's WMS display mode is rendered by GeoServer (workspace `basarsoft`, PostGIS store
`pg_basarsoft`, SQL-view layers `vw_point` / `vw_line` / `vw_polygon` with a `%uid%` view
parameter). That configuration lives in the GeoServer data directory (`~/geoserver_data`),
not in this repo.

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
