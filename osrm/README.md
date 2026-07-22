# Local OSRM (Turkey, MLD)

The API defaults to `http://localhost:5001`. This directory follows OSRM's official Docker-based
Multi-Level Dijkstra pipeline and pins the backend image to `v6.0.0`. The Turkey extract comes from
Geofabrik. Neither the extract nor generated graph files belong in Git; `osrm/data/` is ignored.

Upstream references:

- [OSRM Docker/MLD quick start](https://github.com/Project-OSRM/osrm-backend#using-docker)
- [Geofabrik Turkey extract](https://download.geofabrik.de/europe/turkey.html)

## Prepare the graph

Turkey preprocessing is CPU-, memory-, and disk-intensive. Assign Docker Desktop at least 12 GB of
memory before preparing the full-country graph (its 8 GB default OOM-kills edge expansion), then run
from this directory. Changing the memory slider is not enough on its own — Docker Desktop applies it
only after a restart, so confirm with `docker info --format '{{.MemTotal}}'` before starting.

A verified run on a 16 GB host took about 12 minutes total: extraction peaked near 5.8 GB across
16.3M edges, partition at 3.0 GB, customize at 3.5 GB.

```bash
mkdir -p data
curl -fL https://download.geofabrik.de/europe/turkey-latest.osm.pbf \
  -o data/turkey-latest.osm.pbf

docker compose run --rm extract
docker compose run --rm partition
docker compose run --rm customize
```

The three preparation services use the `prepare` profile and the commands target them explicitly, so
a plain `docker compose up` cannot accidentally launch all preprocessing stages together. A failed
stage must be fixed before the next one runs. Repeat all three after replacing the PBF with a newer
extract.

## Start and check the service

```bash
docker compose up -d osrm
docker compose ps

curl --fail --silent --show-error \
  'http://localhost:5001/route/v1/driving/32.8597,39.9334;32.8541,39.9208?overview=false'
```

Stop it with `docker compose down`. The service binds to loopback by default, so it is not exposed
to other hosts.

## Configuration overrides

Docker Compose accepts these environment variables:

- `OSRM_PORT`: host port, default `5001`. The container still listens on port `5000`; the different
  host port avoids macOS AirPlay Receiver's common port-5000 conflict.
- `OSRM_DATASET`: filename base inside `data/`, default `turkey-latest`. For example, a file named
  `turkey-260701.osm.pbf` uses `OSRM_DATASET=turkey-260701` for every preparation/start command.
- `OSRM_THREADS`: extraction thread count, default `2`. The conservative default reduces peak
  pressure during Turkey graph preparation; increase it only when Docker has more memory assigned.

The API's `Routing` section is independently configurable through ASP.NET configuration:

```text
Routing__PrimaryBaseUrl=http://localhost:5001
Routing__FallbackBaseUrl=
Routing__Profile=driving
Routing__TimeoutSeconds=10
```

`FallbackBaseUrl` is empty by default. When configured, the API tries it only after a primary
connection error, timeout, or 5xx response. It does not fall back for invalid coordinates, OSRM's
no-route response, or other non-transient responses.

`appsettings.Development.json` points the fallback at the public demo server so route builds keep
working while this graph is being prepared. Use `http://router.project-osrm.org`, not `https://` —
that host's TLS handshake fails against .NET's macOS AppleCrypto stack
(`Interop+AppleCrypto+SslException: handshake failure`) even though `curl` negotiates it fine, and it
serves the same API over plain HTTP without redirecting. Being a third-party host, it should stay out
of the committed `appsettings.json`.
