# Turkey demo fixture sources

The files in this directory are deterministic demonstration fixtures. Their real-world place and
geometry data was captured on **2026-07-22** from the Geofabrik Turkey OpenStreetMap extract.
Operational labels beginning with `Demo` describe illustrative GIS workflows; they are not official
government, emergency, tourism, environmental, or municipal designations.

## Licence and attribution

- OpenStreetMap data: © OpenStreetMap contributors, Open Database Licence (ODbL).
  <https://www.openstreetmap.org/copyright>
- Snapshot provider: Geofabrik Turkey.
  <https://download.geofabrik.de/europe/turkey.html>
- Province geometry source: the exact OSM `network=TR-provinces`, `admin_level=4` relation recorded
  by each feature's unique `boundarySourceId`. Capital nodes retain a distinct `sourceId`.
- Route identity sources are recorded per route in `routes.json`. Stop coordinates and road
  geometries retain their own source URL/id, geometry source and capture date; road lines were
  generated against the local OSRM driving graph prepared from the same snapshot. OSM-backed
  İstanbul routes are cross-matched to their exact route relations, and every intercity waypoint is
  cross-matched to a PBF bus-station feature.
- City-hospital names selected by the POI review are cross-checked against the Ministry of Health's
  official city-hospital inventory:
  <https://khgm.saglik.gov.tr/TR-92795/sehir-hastanelerimiz.html?Sayfa=1> and
  <https://khgm.saglik.gov.tr/TR-92795/sehir-hastanelerimiz.html?Sayfa=2>.

The committed fixtures are derived data and retain `sourceKey`, `sourceId`, `capturedAt`, and
`geometrySource` metadata. Do not remove attribution when redistributing the demo database.

## Fixture contracts

- `shapes.geojson`: 328 private drawings—218 points, 60 LineStrings and 50 Polygons. Each of five
  planning themes owns exactly 12 lines and 10 polygons. Every line is an exact open OSM way
  LineString with actual source-node endpoint anchors. Every polygon is the exact largest relevant
  Polygon component of its mapped OSM way/relation area and carries a deterministic contained site
  point. The refresh does not simplify geometry or create buffers, bounding boxes, endpoint chords,
  or synthetic connections. Related features carry the same `scenarioId`, theme, and color.
- `pois.geojson`: 324 named mapped POIs. Every province has a hospital, a food venue and a mall
  (a supermarket is used where no mapped mall exists); every leaf category has at least two
  entries. The first 243 rows are the explicit 81 × (hospital, food, shopping) province baseline.
  `poi-baseline-selections.json` records 67 dated source-id replacements made after an independent
  semantic scan of the locked PBF: 51 province-baseline corrections and 16 major-city corrections,
  including the mapped Hakkari Devlet Hastanesi campus. The audit
  rejects lifecycle objects and misleading facility names such as entrances, family-health
  centres, health directorates, generic markets and non-shopping businesses tagged as malls.
  Iğdır uses a mapped fast-food venue because the locked snapshot contains no named restaurant
  there. Operating hours use deterministic category-based demonstration schedules because the
  source commonly lacks a trustworthy simple daily interval. These values are logical UI fixtures,
  not verified current business hours; hospitals and other continuous-service categories use
  `00:00–23:59`.
- `routes.json`: 25 current urban bus/BRT examples and five clearly labelled intercity corridors,
  with 215 ordered stops and persisted local-OSRM geometry/distance/duration.
  Antalya route identity points to the official municipal portal. `LC07A` replaces the inactive
  bare `LC07`; the current public stop-order page is retained separately on its stop records.
- `../provinces.geojson`: 81 province boundaries enriched with the administrative-capital point,
  seven-region name, display color, OSM identity, and capture metadata.

`DemoSeeder.ValidateManifest()` is the authoritative preflight. It rejects wrong counts, missing
source metadata, invalid or out-of-scope geometry, broken scenario relationships, incomplete POI
coverage, unhealthy routes, unordered stops, and authorization escapes before a destructive seed
can begin.

## Refreshing

Run `./refresh-fixtures.sh --pbf /absolute/path/to/turkey-latest.osm.pbf --snapshot-date
YYYY-MM-DD --osrm-url http://127.0.0.1:5001` from this directory. The explicit date prevents a
new extract from silently inheriting stale provenance. The script writes candidate extracts to a
retained temporary directory, rebuilds `routes.json` from the reviewed stop definitions in
`tools/demo-fixtures/route_manifest.py` and the specified local OSRM instance, and invokes
`refresh-fixtures.mjs` to validate a complete output bundle. The last reviewed shapes, POIs and
province file are copied into that bundle; newly extracted non-route candidates remain beside
`output/` for a human spatial/source diff. The command never overwrites committed fixtures.
Review names, source ids, route-source URLs, counts, geometry diffs, and the capture date before
copying and committing any validated output.

Official route identities must never be paired with arbitrary nearby stops. A refresh must resolve
ordered stops from the operator feed/page or the matching OSM route relation. If that source is
unavailable, retain the last verified fixture instead of guessing.

The route extraction helpers require Python 3 and
[`pyosmium`](https://docs.osmcode.org/pyosmium/latest/). The refresh scaffold invokes:

```text
tools/demo-fixtures/extract_osm_bus_routes.py --pbf <PBF> \
  --output <route-relations.json> --nodes-output <bus-stops.json>
tools/demo-fixtures/extract_osm_stations.py --pbf <PBF> \
  --output <bus-stations.json>
tools/demo-fixtures/extract_osm_provinces.py --pbf <PBF> --snapshot-date <YYYY-MM-DD> \
  --properties-source <reviewed-provinces.geojson> --output <exact-provinces.geojson>
tools/demo-fixtures/refresh_osm_shapes.py refresh --pbf <PBF> \
  --provinces <exact-provinces.geojson> --shapes <reviewed-shapes.geojson> \
  --output <reviewed-output.geojson>
tools/demo-fixtures/review_poi_baselines.py --pbf <PBF> --snapshot-date <YYYY-MM-DD> \
  --provinces <exact-provinces.geojson> --pois <reviewed-pois.geojson> \
  --report <ranked-candidates.json> --selections <poi-baseline-selections.json> \
  --output <reviewed-output.geojson>
tools/demo-fixtures/route_manifest.py --osrm-url <local-OSRM-url> --snapshot-date <YYYY-MM-DD> \
  --output <reviewed-routes.json>
```

The extractors read the locked PBF only. The province extractor requires exactly 81 unique
`TR-provinces` relations, preserves reviewed capital/region/color properties, records distinct
boundary and capital source ids, retains exact relation rings, and verifies every capital is
covered before writing. The shape refresher resolves the committed source identities from the PBF,
rebuilds exact line/polygon components and their real source-node/contained-point relationships,
and validates all owner, theme, count and authorization contracts without inventing geometry. The
POI reviewer requires the explicit snapshot date to match both the
committed fixture and the selection manifest, resolves every selected OSM id from the supplied
PBF, validates semantic category and province containment, and writes a separate output for human
review; it never overwrites `pois.geojson`. `route_manifest.py` never discovers stops by proximity: its checked-in
ordered stop definitions retain the reviewed operator/OSM identities, and it uses only the supplied
local OSRM service to produce route geometry and metrics. None of these commands alters committed
fixtures.
