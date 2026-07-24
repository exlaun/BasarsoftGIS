#!/usr/bin/env bash
set -euo pipefail

usage() {
  echo "Usage: $0 --pbf /absolute/turkey-latest.osm.pbf --snapshot-date YYYY-MM-DD --osrm-url http://127.0.0.1:5001"
}

pbf=""
osrm_url=""
snapshot_date=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --pbf) pbf="${2:-}"; shift 2 ;;
    --snapshot-date) snapshot_date="${2:-}"; shift 2 ;;
    --osrm-url) osrm_url="${2:-}"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) usage >&2; exit 2 ;;
  esac
done

if [[
  -z "$pbf"
  || -z "$osrm_url"
  || ! "$snapshot_date" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$
  || "$pbf" != /*
  || ! -f "$pbf"
]]; then
  usage >&2
  exit 2
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
api_dir="$(cd "$script_dir/../.." && pwd)"
repo_dir="$(cd "$api_dir/.." && pwd)"
work_dir="$(mktemp -d "${TMPDIR:-/tmp}/basarsoft-demo-refresh.XXXXXX")"
cleanup() {
  rm -rf -- "$work_dir"
}
trap cleanup EXIT

ogr2ogr_bin="${OGR2OGR_BIN:-ogr2ogr}"
node_bin="${NODE_BIN:-node}"
python_bin="${PYTHON_BIN:-python3}"

"$python_bin" "$repo_dir/tools/demo-fixtures/extract_osm_bus_routes.py" \
  --pbf "$pbf" \
  --output "$work_dir/route-relations.json" \
  --nodes-output "$work_dir/bus-stops.json"
"$python_bin" "$repo_dir/tools/demo-fixtures/extract_osm_stations.py" \
  --pbf "$pbf" \
  --output "$work_dir/bus-stations.json"
"$python_bin" "$repo_dir/tools/demo-fixtures/extract_osm_provinces.py" \
  --pbf "$pbf" \
  --snapshot-date "$snapshot_date" \
  --properties-source "$api_dir/Data/provinces.geojson" \
  --output "$work_dir/provinces.geojson"
"$python_bin" "$repo_dir/tools/demo-fixtures/refresh_osm_shapes.py" refresh \
  --pbf "$pbf" \
  --provinces "$work_dir/provinces.geojson" \
  --shapes "$script_dir/shapes.geojson" \
  --output "$work_dir/reviewed-shapes.geojson"

"$ogr2ogr_bin" -f GeoJSON "$work_dir/places.geojson" "$pbf" points \
  -where "name IS NOT NULL AND place IN ('city','town')" -lco COORDINATE_PRECISION=7
"$ogr2ogr_bin" -f GeoJSON "$work_dir/pois.geojson" "$pbf" points \
  -where "name IS NOT NULL AND (place IS NOT NULL OR other_tags LIKE '%\"amenity\"=>%' OR other_tags LIKE '%\"shop\"=>%' OR other_tags LIKE '%\"tourism\"=>%')" \
  -lco COORDINATE_PRECISION=7
"$ogr2ogr_bin" -f GeoJSON "$work_dir/stops.geojson" "$pbf" points \
  -where "name IS NOT NULL AND highway = 'bus_stop'" -lco COORDINATE_PRECISION=7
"$ogr2ogr_bin" -f GeoJSON "$work_dir/lines.geojson" "$pbf" lines \
  -where "name IS NOT NULL AND (highway IS NOT NULL OR waterway IS NOT NULL OR railway IS NOT NULL)" \
  -lco COORDINATE_PRECISION=7
"$ogr2ogr_bin" -f GeoJSON "$work_dir/polygons.geojson" "$pbf" multipolygons \
  -where "name IS NOT NULL AND (amenity IS NOT NULL OR aeroway IS NOT NULL OR historic IS NOT NULL OR leisure IS NOT NULL OR natural IS NOT NULL OR tourism IS NOT NULL OR landuse IS NOT NULL)" \
  -simplify 0.00005 -lco COORDINATE_PRECISION=7

"$python_bin" "$repo_dir/tools/demo-fixtures/review_poi_baselines.py" \
  --pbf "$pbf" \
  --snapshot-date "$snapshot_date" \
  --provinces "$work_dir/provinces.geojson" \
  --pois "$script_dir/pois.geojson" \
  --report "$work_dir/poi-baseline-candidates.json" \
  --selections "$script_dir/poi-baseline-selections.json" \
  --output "$work_dir/reviewed-pois.geojson"

"$python_bin" "$repo_dir/tools/demo-fixtures/route_manifest.py" \
  --osrm-url "$osrm_url" \
  --snapshot-date "$snapshot_date" \
  --output "$work_dir/reviewed-routes.json"

"$node_bin" "$script_dir/refresh-fixtures.mjs" \
  --source-dir "$work_dir" \
  --province-source "$work_dir/provinces.geojson" \
  --current-shapes "$work_dir/reviewed-shapes.geojson" \
  --current-pois "$work_dir/reviewed-pois.geojson" \
  --reviewed-routes "$work_dir/reviewed-routes.json" \
  --route-relations "$work_dir/route-relations.json" \
  --bus-stations "$work_dir/bus-stations.json" \
  --osrm-url "$osrm_url" \
  --output-dir "$work_dir/output"

for fixture in shapes.geojson pois.geojson routes.json provinces.geojson; do
  test -s "$work_dir/output/$fixture"
done

trap - EXIT
echo "Validated fixture candidates are in: $work_dir/output"
echo "Review them and copy them intentionally; this command never overwrites committed fixtures."
