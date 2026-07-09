# GeoServer artifacts

The map's WMS display mode is rendered by GeoServer (workspace `basarsoft`, PostGIS store
`pg_basarsoft`, SQL-view layers `vw_point` / `vw_line` / `vw_polygon` with a `%uid%` view
parameter). That configuration lives in the GeoServer data directory (`~/geoserver_data`),
not in this repo.

## styles/

Attribute-driven SLD styles so the WMS image shows each shape in its saved `color` column,
matching the vector (Vektör) mode styling in `basarsoft-client/src/pages/MapPage.jsx`. Rows
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
