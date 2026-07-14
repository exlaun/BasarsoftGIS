# Basarsoft Internship Project — Web GIS

A web GIS built over an internship: draw and manage geographic inventory on a map, with
role-based access control, per-region geographic authorization, a shared points-of-interest
catalogue, and GeoServer-rendered display and analysis layers.

| Layer | Stack |
|---|---|
| Client | React 19 + Vite, OpenLayers |
| API | ASP.NET Core 8, EF Core, JWT auth, BCrypt |
| Database | PostgreSQL + PostGIS (all geometry EPSG:4326) |
| Map services | GeoServer 3 — WFS reads, WMS display, `vec:Heatmap` analysis |

## What it does

- **Drawing** — points, lines and polygons with a name and colour, saved as PostGIS geometry.
  Each user's drawings are **private to them**.
- **RBAC** — users, roles and a shared permission catalogue. A permission can come from a role
  *or* be granted directly to a user; the admin panel shows which, and won't let you re-pick one
  that is already inherited.
- **Geographic authorization** — a user or a role can be restricted to a polygon. Drawing outside
  it is refused (`403 outside_authorized_area`). A user's own area **overrides** their role's.
- **POIs** — a global catalogue with a nested category tree and daily opening hours. Everyone sees
  every POI; only `add_poi` holders can add one.
- **Analysis** — a throwaway polygon that counts the shapes it touches, and a GeoServer heat map of
  your own shape density.

## Running it

Four things need to be up.

```bash
# 1. PostgreSQL (Postgres.app or equivalent) with a `basarsoft_internship` database + PostGIS.
#    Connection string: Basarsoft.Api/appsettings.json

# 2. Apply the schema
cd Basarsoft.Api
dotnet ef database update

# 3. API -> http://localhost:5032  (Swagger at /swagger)
dotnet run

# 4. Client -> http://localhost:5173
cd ../basarsoft-client
npm install
npm run dev
```

GeoServer (workspace `basarsoft`, PostGIS store `pg_basarsoft`) serves the map's WFS/WMS/heat-map
layers on `http://localhost:8080/geoserver`. Its SQL views and styles are documented in
[geoserver/README.md](geoserver/README.md). The app degrades gracefully if it is down — the POI
layer and the admin panel keep working.

## The demo dataset

The database ships empty. One command fills it with a curated scenario and **deletes everything
already in it**:

```bash
cd Basarsoft.Api
dotnet run -- seed-demo          # asks you to type the database name to confirm
dotnet run -- seed-demo --yes    # skip the prompt
```

It refuses to run outside `Development`, wipes and rebuilds inside a single transaction, restarts
every id sequence, and asserts before committing that no seeded shape falls outside its owner's
authorized area. It is re-runnable: every run produces the same ids, so you can always reset to a
known-good state — including mid-demo, after you have drawn all over it.

> After a reseed, **log out in the browser** (or clear `localStorage`). Ids restart at 1, so a token
> minted before the wipe now points at a different user.

### The scenario

A national fiber operator with regional field teams. Points are cabinets, splice closures and base
stations; lines are backbone and ring routes; polygons are service and maintenance zones; POIs are
the customer-facing stores and service centers.

### The accounts

Password for all six: **`secret123`**

| Username | Role | What it shows |
|---|---|---|
| `admin` | Admin | Everything: 35 shapes, the heat map, the query panel, the whole admin panel |
| `marmara.op` | Field Operator | A geographic limit **inherited from the role** (Marmara) |
| `antalya.op` | Field Operator | A **user-level area overriding the role's** — same role as above, boundary in Antalya |
| `surveyor` | Surveyor | `add_point` **from the role** + `add_polygon` **granted directly** |
| `poi.op` | Operator | A permission-scoped toolbar: the POI tool and nothing else |
| `viewer` | Viewer | Read-only — no draw tools, no admin link |

### A demo run-through

1. **`admin`** — the map loads 35 coloured shapes: a dense İstanbul cluster, warm spots at Ankara and
   İzmir, backbone routes between them, isolated nodes out east.
   - **Heat Map** → a red core over İstanbul fading to blue singles in the east.
   - **Shapes** (top right) → 35 drawings across 4 pages; sort by name/type/date; filter `Fiber`.
   - **Analysis** → draw a box over İstanbul; it counts the points, lines and polygons it touches.
   - **Polygon** → draw one over the İstanbul cluster; the toast reports how many shapes it contains.
   - **WMS** → the same map rendered server-side by GeoServer, colours preserved.
   - **Admin Panel** → 6 users, 5 roles. Open `surveyor` → **Permissions**: `add_point` is locked with
     a *From role: Surveyor* badge, `add_polygon` is ticked as *Direct*.
2. **`marmara.op`** — a green dashed boundary around Marmara; his shapes sit inside it. Try to draw
   outside it: refused. No POI tool.
3. **`antalya.op`** — *same role*, but the boundary is in **Antalya**. His own area wins over the
   role's. The POIs are still all there — they are shared.
4. **`poi.op`** — only the POI tool. Place one, pick a category from the 3-level tree, set its hours.
5. **`viewer`** — no draw tools, no admin link, but still sees all 20 POIs.

## Layout

```
Basarsoft.Api/          ASP.NET Core API
  Controllers/          auth, geometry, poi, admin (users/roles/permissions/poi-categories)
  Services/             geometry, geo-authorization, RBAC, GeoServer WFS/WMS reader
  Data/                 AppDbContext, AdminSeeder (RBAC baseline), DemoSeeder + DemoData
  Migrations/
basarsoft-client/       React + Vite client
  src/pages/            LoginPage, MapPage, admin/
  src/components/       DrawToolbar, QueryPanel, modals
geoserver/              SLD styles + the SQL views' documentation
```
