# BasarsoftGIS — Turkey Explorer Demo

BasarsoftGIS is the Web GIS platform. Turkey Explorer is its nationwide demonstration scenario:
private user drawings, shared points of interest, role-based permissions, geographic authorization,
GeoServer WFS/WMS rendering, and weighted location analysis over all 81 Turkish provinces.

| Layer | Stack |
|---|---|
| Client | React 19, Vite, OpenLayers |
| API | ASP.NET Core 8, EF Core, JWT, BCrypt |
| Database | PostgreSQL + PostGIS, EPSG:4326 |
| Map services | GeoServer 3 (WFS/WMS) and OSRM 6.0.0 (road routing, MLD) |

## Scenario at a glance

| Data | Earlier demo | Nationwide demo |
|---|---:|---:|
| Accounts | 6 | **17** |
| Private shapes | 62 | **164** |
| Shared POIs | 36 | **139** |
| POI categories | 19 | **42** |
| Authorization areas | 4 | **15** |
| Transportation routes | — | **8** |
| Transportation stops | — | **56** |
| Provinces represented | about 7 | **all 81** |

The manifest is deterministic and validated before and after insertion. `seed-demo` resets the
application sequences, so the account ids below are stable. Every account uses **`secret123`**.
This common password is for a local demonstration only.

## Accounts and authorization

| ID | Username | Role | Area / purpose |
|---:|---|---|---|
| 1 | `admin` | Admin | Unrestricted; owns its own 100-shape national inventory |
| 2 | `marmara_manager` | Regional Manager | Inherits the role's Marmara area |
| 3 | `aegean_manager` | Regional Manager | User override: Aegean |
| 4 | `mediterranean_manager` | Regional Manager | User override: Mediterranean |
| 5 | `central_manager` | Regional Manager | User override: Central Anatolia |
| 6 | `blacksea_manager` | Regional Manager | User override: Black Sea |
| 7 | `eastern_manager` | Regional Manager | User override: Eastern Anatolia |
| 8 | `southeast_manager` | Regional Manager | User override: Southeastern Anatolia |
| 9 | `ankara_editor` | Editor | Ankara; direct `add_polygon` supplements role point/line |
| 10 | `istanbul_editor` | Editor | İstanbul; role point/line |
| 11 | `izmir_editor` | Editor | İzmir; role point/line |
| 12 | `antalya_editor` | Editor | Antalya; role point/line |
| 13 | `istanbul_operator` | Operator | İstanbul; transportation management, POI read-only |
| 14 | `antalya_operator` | Operator | Antalya; transportation management, POI read-only |
| 15 | `gaziantep_operator` | Operator | Gaziantep; transportation management, POI read-only |
| 16 | `trabzon_operator` | Operator | Trabzon; transportation management, POI read-only |
| 17 | `viewer` | Viewer | Permission-free, genuinely read-only |

The five roles remain Admin, Regional Manager, Editor, Operator, and Viewer. There are 15
authorization areas: one role area, six manager overrides, four editor areas, and four operator
areas. A user area overrides a role area; otherwise role areas are inherited.

Drawing visibility remains private. Admin does **not** see other users' drawings merely because it
is Admin: it owns a separate 100-shape inventory. POIs are the shared catalogue and are visible to
all authenticated users.

The Operator role now has only `manage_transport`. Operators, Viewers, and ordinary users cannot
create or delete POIs; deletion requires the effective `manage_pois` permission. The migration
removes the legacy Operator-role `add_poi` link once, without touching explicit direct user grants.

Creating, updating, moving, and deleting a private drawing all require the matching
`add_point`, `add_line`, or `add_polygon` permission. The Viewer can inspect its three legacy
drawings, but the client hides edit/move/delete controls and the API independently refuses writes.

## Nationwide data

### Private shapes

The exact total is 164:

- Admin: 100 — 81 province markers, 10 geographically correct intercity routes, and 9
  regional/tourism zones.
- Seven regional managers: 7 each — 3 points, 2 lines, and 2 polygons inside each region.
- `ankara_editor`: 6 — inherited points/lines plus polygons enabled by its direct permission.
- The other three editors: 2 point/line shapes each.
- Viewer: 3 read-only legacy drawings.

The type distribution is 109 points, 30 lines, and 25 polygons.

### Provinces and regions

The explicit province manifest must exactly equal the names in `Data/provinces.geojson`.

| Region | Provinces |
|---|---:|
| Marmara | 11 |
| Aegean | 8 |
| Mediterranean | 8 |
| Central Anatolia | 13 |
| Black Sea | 18 |
| Eastern Anatolia | 14 |
| Southeastern Anatolia | 9 |
| **Total** | **81** |

Two distinct covered points are derived deterministically from each province geometry: one private
Admin marker and one shared public-institution POI.

### Shared POIs

The exact total is 139:

- 81 generated province institutions, one covered by every province boundary. The kind rotates
  through 12 Turkish-named templates (`{Province} Şehir Hastanesi`, `{Province} Otogarı`,
  `{Province} Merkez Postanesi`, …) so the nationwide layer mixes hospitals, terminals, museums,
  parks and more instead of repeating one template.
- 58 curated demonstration hotspots — real landmarks (Anıtkabir, Galata Kulesi, Sümela Manastırı…)
  and plausible businesses concentrated in the demo personas' cities.
- Ownership: Admin 106 (81 generated + 25 curated); istanbul_operator 12; the other three
  operators 7 each. Operator-owned rows remain preseeded read-only sample data; current Operator
  permissions do not allow adding or deleting them.

POI names and daily hours are demo/sample content. They are not guaranteed to be current business
names, live opening times, or travel advice.

### Transportation routes and stops

Eight routes and 56 stops, two real transit lines per operator city:

| Owner | Routes | Stops |
|---|---|---:|
| `istanbul_operator` | Metrobüs (Beylikdüzü–Söğütlüçeşme), M4 Kadıköy–Tavşantepe | 7 + 8 |
| `antalya_operator` | Antalya Nostalji Tramvayı, AntRay T1 Fatih–Expo | 6 + 7 |
| `gaziantep_operator` | Gaziantep Tramvay T1, GaziRay (Başpınar–Oğuzeli) | 7 + 6 |
| `trabzon_operator` | Trabzon Sahil Hattı, Trabzon–Uzungöl Hattı | 8 + 7 |

Each route takes a different color, so no two lines' stop markers look alike. Stops are named after
the neighborhoods and landmarks the line actually serves, are ordered `1..N` with no gaps, and sit
inside their operator's authorization area — placing one outside fails the seed rather than producing
a stop that renders but refuses every edit. The two Trabzon routes deliberately share Meydan Parkı and
Değirmendere, the way real lines share a transfer point.

Unlike private drawings, routes and stops are visible to every authenticated user; only the
`manage_transport` permission, held by the Operator role, allows creating, editing, reordering, or
deleting them. Deleting a route deletes its stops with it; deleting one stop renumbers the rest of
its route to a contiguous `1..N` and rebuilds the road geometry without it. Route and stop names are
demo/sample content on the same terms as the POI names above.

Routes store the last successful OSRM `LineString` plus distance and duration. Adding the second or
later stop, changing the exact stop order, or choosing Rebuild calls OSRM in stop order. A routing
failure preserves both the committed stop/order and the previous geometry, marking that geometry
stale and retaining a routing error code. Existing and demo routes start without geometry and need
one successful rebuild before a road line and direction arrows appear.

Route visibility is session-local and synchronizes the line, approximately 1 km direction arrows,
and stop markers. The overlays stay visible in both WFS and WMS display modes. Transportation
administrators have a separate `/admin/transportation` route/stop view gated by
`manage_transport_admin`; this permission is granted to Admin, not Operator.

### Transportation API contracts

- Route responses include nullable `geometryWkt`, `distanceMeters`, `durationSeconds`,
  `isGeometryStale`, and `routingErrorCode`.
- `POST /api/routes/{id}/build` requires `manage_transport`: success is `200`; fewer than two stops
  is `409 insufficient_stops`; routing problems are `422 no_route` / `invalid_coordinates` or
  `503 routing_unavailable`, always carrying the current route state.
- Stop creation returns `{ stop, route }`; exact-set reorder and stop deletion both return
  `{ stops, route }`. If OSRM fails after the database commit, the corresponding `422`/`503` carries
  `stopPersisted: true`, `orderPersisted: true`, or `deletePersisted: true` and the committed state
  instead of rolling it back.
- `DELETE /api/routes/{id}` requires `manage_transport`: `204` on success (its stops are deleted with
  it), `404` if the route doesn't exist. `DELETE /api/stops/{id}` requires `manage_transport` and
  returns the route's renumbered remaining stops plus the rebuilt route.
- `/api/admin/transportation` provides the grouped snapshot, presentation edits, exact-set reorder,
  deletion, and rebuild endpoints under the `manage_transport_admin` policy.

## POI categories and icons

Categories inherit both color and icon from the nearest ancestor. A missing icon ultimately falls
back to `pin`.

| Root category | Child categories | Effective icon behavior |
|---|---|---|
| Food & Drink | Restaurant, Cafe, Bakery, Fast Food | `food`; Cafe → `coffee`; Bakery → `bakery` |
| Health | Hospital, Pharmacy, 24/7 Pharmacy | `health`; pharmacy categories → `pharmacy` |
| Shopping | Mall, Supermarket | `shopping` inherited |
| Culture & Tourism | Museum, Historical Site, Hotel, Visitor Center, Art Gallery | `culture`; Museum → `museum`; Hotel → `hotel` |
| Services | Bank, Gas Station, Post Office, Municipality | `services`; overrides `bank`, `fuel`, `mail`, `government` |
| Transport | Airport, Train Station, Bus Terminal, Ferry Terminal, Metro Station | `transport`; Airport → `airport` |
| Education | University, Library, High School | `education` inherited |
| Nature & Recreation | National Park, Beach, Park, Botanical Garden | `nature` inherited |
| Sports | Stadium, Ski Center, Gym | `sports` inherited |

The authoritative 20-key allowlist is:

`pin`, `food`, `coffee`, `bakery`, `health`, `pharmacy`, `shopping`, `culture`, `museum`,
`hotel`, `services`, `bank`, `fuel`, `transport`, `airport`, `education`, `nature`, `sports`,
`mail`, `government`.

The API exposes it at `GET /api/poi/icons`. Category requests and responses carry nullable own
`iconKey`; POI responses carry the non-null effective `categoryIconKey`. Unknown category icon keys
return `400` with `code: "invalid_icon_key"`.

The client uses a cached two-layer 20 px marker: category-colored circle, white outline, and centered
white SVG glyph. The same effective badge appears in POI search, POI details, overlap selection,
category administration, and location-analysis legends. WMS uses the same packaged SVGs through
the POI SLD. The small Lucide-compatible asset subset and ISC attribution live in
`basarsoft-client/public/poi-icons`.

## Features

- Private point, line, and polygon drawing with names, colors, audit details, and geographic limits.
- RBAC administration showing inherited versus direct permissions.
- Shared POI search, details, category administration, opening hours, colors, and icons.
- Persisted OSRM road routes with distance/duration, direction arrows, visibility controls, and
  state-aware recovery when routing fails after a stop/order/deletion commit.
- Policy-protected transportation administration for route/stop presentation, ordering, deletion, and
  rebuilds.
- WFS editable vectors and WMS server-rendered display layers.
- Shape query, intersection analysis, and per-user shape heat map.
- Location analysis over a selected province or drawn polygon, with 2–5 weighted POI-category
  criteria totaling 100 and a GeoServer-rendered heat map.

## Apply the repository changes

The schema migration, destructive demo seed, and live GeoServer provisioning are separate runtime
actions. Run them in this order when you intentionally want to replace the current demo data:

```bash
# 1. Apply EF migrations, including route geometry/metrics and Operator-role cleanup
cd Basarsoft.Api
dotnet ef database update

# 2. DESTRUCTIVE: type the database name when prompted
dotnet run -- seed-demo

# 3. From the repository root, package/upload the POI SLD + 20 icons and verify WFS
cd ..
GEOSERVER_USER=... GEOSERVER_PASSWORD=... ./geoserver/setup-poi.sh
```

`seed-demo` only runs in `Development`, wipes application tables inside one transaction, resets
their sequences, validates the complete contract, and rolls back on any failure. It preserves the
province reference table and fills it when empty. After reseeding, log out or clear `localStorage`
because old tokens may point at a different reset user id.

If the existing location-analysis GeoServer layer has not been provisioned, follow the separate
`./geoserver/setup-konum.sh` instructions in [geoserver/README.md](geoserver/README.md).

## Local development

```bash
# PostgreSQL/PostGIS must exist and match Basarsoft.Api/appsettings.json

cd Basarsoft.Api
dotnet run                         # API: http://localhost:5032

cd ../basarsoft-client
npm install
npm run dev                       # client: http://localhost:5173
```

GeoServer is expected at `http://localhost:8080/geoserver`, with workspace `basarsoft` and PostGIS
store `pg_basarsoft`. See [geoserver/README.md](geoserver/README.md).

OSRM is expected at `http://localhost:5001`. Download the Turkey PBF, run the pinned Docker MLD
extract/partition/customize stages, and start the router by following [osrm/README.md](osrm/README.md).
The API's `Routing` section defaults to the local service, the `driving` profile, a 10-second timeout,
and no fallback. An opt-in fallback is tried only for a connection error, timeout, or primary 5xx.

## Security notes

- **JWT signing key.** The key is not in `appsettings.json`. Development reads a dev-only key from
  `appsettings.Development.json`; any other environment must provide one (the API refuses to start
  otherwise):

  ```bash
  cd Basarsoft.Api
  dotnet user-secrets set "Jwt:Key" "<your-32+-char-secret>"   # or export Jwt__Key=...
  ```

- **GeoServer credentials.** When GeoServer is locked down (see
  [geoserver/README.md](geoserver/README.md)), give the API its read account the same way:
  `dotnet user-secrets set "GeoServer:Username" ...` / `"GeoServer:Password" ...`. With no
  credentials configured the API talks to GeoServer anonymously (local dev default).
- **Tokens in localStorage.** The client keeps its bearer token in `localStorage` — accepted for
  this project's scope (no XSS sinks; 10-minute expiry bounds the damage). A production deployment
  should move sessions to httpOnly cookies.
- **Mirrored constants.** The POI icon catalogue, category colors, and label zoom thresholds are
  intentionally duplicated across the API (`PoiIconCatalog.cs`), the client
  (`src/utils/poiCategories.js`), and the SLDs; comments cross-reference the copies and tests pin
  them so drift fails the build.

## Safe verification

These checks do not seed the database or provision GeoServer:

```bash
dotnet build Basarsoft.Api/Basarsoft.Api.csproj --no-restore -p:UseAppHost=false
dotnet run --project Basarsoft.Api.Tests/Basarsoft.Api.Tests.csproj

cd basarsoft-client
npm test
npm run lint
npm run build
cd ..

./geoserver/setup-poi.sh --check
docker compose --profile prepare -f osrm/compose.yml config
git diff --check
```

## Known limitations

- The shared demo password is intentionally simple and is not production-safe.
- Password recovery is not implemented.
- POIs use one daily opening/closing pair, not editable weekly schedules.
- Curated names and hours are sample data, not live information.
- Existing/demo routes contain no road geometry until OSRM completes a rebuild successfully.
- Transportation has no stop movement between routes and no undelete: a soft-deleted route or stop
  stays in the database but nothing in the app restores it.
- No OSM PBF or generated OSRM graph is committed; local routing needs the preparation in
  [osrm/README.md](osrm/README.md).
- Dynamic relative GeoServer `ExternalGraphic` resolution must be smoke-tested against the target
  GeoServer after provisioning; the offline package check cannot render a live WMS response.

## Repository layout

```text
Basarsoft.Api/          ASP.NET Core API, EF model/migrations, deterministic demo manifest
Basarsoft.Api.Tests/    API, seed-contract, authorization, and WFS parsing tests
basarsoft-client/       React/OpenLayers client and canonical public POI icon assets
geoserver/              Virtual-table XML, SLD styles, and idempotent provisioning/check scripts
osrm/                   Pinned Docker Compose MLD preparation/runtime workflow (datasets ignored)
```
