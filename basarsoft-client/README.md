# BasarsoftGIS Client

The React 19 and OpenLayers frontend for BasarsoftGIS. It provides the authenticated map,
geometry drawing and analysis tools, shared POI search, WFS/WMS display modes, and the RBAC
administration interface.

## Development

The API must be available at `http://localhost:5032` and GeoServer at
`http://localhost:8080/geoserver`.

```bash
npm install
npm run dev
```

The development server runs at `http://localhost:5173`.

## Checks

```bash
npm test
npm run lint
npm run build
```
