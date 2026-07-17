#!/usr/bin/env bash

# Create or update the shared POI virtual table and its default style through GeoServer REST.
# Safe to rerun: existing resources are updated, missing resources are created.

set -euo pipefail

MODE="provision"
case "${1:-}" in
  "")
    ;;
  --check)
    MODE="check"
    shift
    ;;
  *)
    echo "Usage: $0 [--check]" >&2
    exit 2
    ;;
esac
if (( $# > 0 )); then
  echo "Usage: $0 [--check]" >&2
  exit 2
fi

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
GEOSERVER_URL="${GEOSERVER_URL:-http://localhost:8080/geoserver}"
WORKSPACE="${GEOSERVER_WORKSPACE:-basarsoft}"
DATASTORE="${GEOSERVER_DATASTORE:-pg_basarsoft}"
FEATURE_TYPE="vw_poi"
STYLE="vw_poi_category"
FEATURE_XML="${SCRIPT_DIR}/featuretypes/${FEATURE_TYPE}.xml"
STYLE_SLD="${SCRIPT_DIR}/styles/${STYLE}.sld"
ICON_SOURCE_DIR="${SCRIPT_DIR}/../basarsoft-client/public/poi-icons"
ICON_KEYS=(
  pin
  food
  coffee
  bakery
  health
  pharmacy
  shopping
  culture
  museum
  hotel
  services
  bank
  fuel
  transport
  airport
  education
  nature
  sports
  mail
  government
)
TEMP_DIR=""

cleanup() {
  if [[ -n "${TEMP_DIR}" && -d "${TEMP_DIR}" ]]; then
    rm -rf -- "${TEMP_DIR}"
  fi
}
trap cleanup EXIT

for command in zip; do
  if ! command -v "${command}" >/dev/null 2>&1; then
    echo "Required command not found: ${command}" >&2
    exit 2
  fi
done

if [[ ! -f "${FEATURE_XML}" || ! -f "${STYLE_SLD}" ]]; then
  echo "POI feature-type or style artifact is missing under ${SCRIPT_DIR}." >&2
  exit 2
fi

if command -v xmllint >/dev/null 2>&1; then
  xmllint --noout "${FEATURE_XML}" "${STYLE_SLD}"
fi

missing_icons=()
for icon_key in "${ICON_KEYS[@]}"; do
  icon_path="${ICON_SOURCE_DIR}/${icon_key}.svg"
  if [[ ! -s "${icon_path}" ]]; then
    missing_icons+=("${icon_key}.svg")
  fi
done

if (( ${#missing_icons[@]} > 0 )); then
  echo "Missing or empty canonical POI SVGs under ${ICON_SOURCE_DIR}:" >&2
  printf '  %s\n' "${missing_icons[@]}" >&2
  exit 2
fi

ICON_LICENSE=""
for license_name in LICENSE.md LICENSE.txt; do
  if [[ -s "${ICON_SOURCE_DIR}/${license_name}" ]]; then
    ICON_LICENSE="${ICON_SOURCE_DIR}/${license_name}"
    break
  fi
done
if [[ -z "${ICON_LICENSE}" ]]; then
  echo "Missing POI icon license attribution (expected LICENSE.md or LICENSE.txt)." >&2
  exit 2
fi

# GeoServer's style-package endpoint installs relative graphics beside the SLD. Build the
# package from the client's canonical icon assets so WFS and WMS modes cannot drift.
TEMP_DIR="$(mktemp -d "${TMPDIR:-/tmp}/basarsoft-poi-style.XXXXXX")"
PACKAGE_DIR="${TEMP_DIR}/package"
STYLE_PACKAGE="${TEMP_DIR}/${STYLE}.zip"
mkdir -p "${PACKAGE_DIR}/poi-icons"
cp "${STYLE_SLD}" "${PACKAGE_DIR}/${STYLE}.sld"
for icon_key in "${ICON_KEYS[@]}"; do
  cp "${ICON_SOURCE_DIR}/${icon_key}.svg" "${PACKAGE_DIR}/poi-icons/${icon_key}.svg"
done
cp "${ICON_LICENSE}" "${PACKAGE_DIR}/poi-icons/$(basename "${ICON_LICENSE}")"
(
  cd "${PACKAGE_DIR}"
  zip -q -r "${STYLE_PACKAGE}" "${STYLE}.sld" poi-icons
)
zip -T "${STYLE_PACKAGE}" >/dev/null

if [[ "${MODE}" == "check" ]]; then
  echo "POI GeoServer artifacts valid: SQL-view XML, SLD, and licensed 20-icon style package."
  exit 0
fi

if ! command -v curl >/dev/null 2>&1; then
  echo "Required command not found: curl" >&2
  exit 2
fi
if [[ -z "${GEOSERVER_USER:-}" || -z "${GEOSERVER_PASSWORD:-}" ]]; then
  echo "Set GEOSERVER_USER and GEOSERVER_PASSWORD before running this script." >&2
  exit 2
fi

REST="${GEOSERVER_URL%/}/rest"
AUTH=(--user "${GEOSERVER_USER}:${GEOSERVER_PASSWORD}")
FEATURE_COLLECTION_URL="${REST}/workspaces/${WORKSPACE}/datastores/${DATASTORE}/featuretypes"
FEATURE_URL="${FEATURE_COLLECTION_URL}/${FEATURE_TYPE}"
STYLE_COLLECTION_URL="${REST}/workspaces/${WORKSPACE}/styles"
STYLE_URL="${STYLE_COLLECTION_URL}/${STYLE}"

http_status() {
  curl --silent --show-error "${AUTH[@]}" --output /dev/null --write-out '%{http_code}' "$1"
}

datastore_status="$(http_status "${REST}/workspaces/${WORKSPACE}/datastores/${DATASTORE}.json")"
if [[ "${datastore_status}" != "200" ]]; then
  echo "GeoServer datastore ${WORKSPACE}:${DATASTORE} is unavailable (HTTP ${datastore_status})." >&2
  exit 1
fi

feature_status="$(http_status "${FEATURE_URL}.json")"
case "${feature_status}" in
  200)
    curl --fail --silent --show-error "${AUTH[@]}" \
      --request PUT \
      --header 'Content-Type: application/xml' \
      --data-binary "@${FEATURE_XML}" \
      "${FEATURE_URL}?recalculate=nativebbox,latlonbbox"
    echo "Updated ${WORKSPACE}:${FEATURE_TYPE}."
    ;;
  404)
    curl --fail --silent --show-error "${AUTH[@]}" \
      --request POST \
      --header 'Content-Type: application/xml' \
      --data-binary "@${FEATURE_XML}" \
      "${FEATURE_COLLECTION_URL}"
    curl --fail --silent --show-error "${AUTH[@]}" \
      --request PUT \
      --header 'Content-Type: application/xml' \
      --data '<featureType><name>vw_poi</name><enabled>true</enabled></featureType>' \
      "${FEATURE_URL}?recalculate=nativebbox,latlonbbox"
    echo "Created ${WORKSPACE}:${FEATURE_TYPE}."
    ;;
  *)
    echo "Could not inspect ${WORKSPACE}:${FEATURE_TYPE} (HTTP ${feature_status})." >&2
    exit 1
    ;;
esac

style_status="$(http_status "${STYLE_URL}.json")"
case "${style_status}" in
  200)
    curl --fail --silent --show-error "${AUTH[@]}" \
      --request PUT \
      --header 'Content-Type: application/zip' \
      --data-binary "@${STYLE_PACKAGE}" \
      "${STYLE_URL}.zip"
    echo "Updated ${WORKSPACE}:${STYLE}."
    ;;
  404)
    curl --fail --silent --show-error "${AUTH[@]}" \
      --request POST \
      --header 'Content-Type: application/zip' \
      --data-binary "@${STYLE_PACKAGE}" \
      "${STYLE_COLLECTION_URL}"
    echo "Created ${WORKSPACE}:${STYLE}."
    ;;
  *)
    echo "Could not inspect ${WORKSPACE}:${STYLE} (HTTP ${style_status})." >&2
    exit 1
    ;;
esac

# The style-package endpoint extracts only top-level zip entries, so the poi-icons/
# subdirectory the SLD references never lands on disk. Push each icon through the
# resource endpoint, which creates intermediate directories.
ICON_RESOURCE_URL="${REST}/resource/workspaces/${WORKSPACE}/styles/poi-icons"
for icon_key in "${ICON_KEYS[@]}"; do
  curl --fail --silent --show-error "${AUTH[@]}" \
    --request PUT \
    --header 'Content-Type: image/svg+xml' \
    --data-binary "@${ICON_SOURCE_DIR}/${icon_key}.svg" \
    "${ICON_RESOURCE_URL}/${icon_key}.svg"
done
curl --fail --silent --show-error "${AUTH[@]}" \
  --request PUT \
  --header 'Content-Type: text/markdown' \
  --data-binary "@${ICON_LICENSE}" \
  "${ICON_RESOURCE_URL}/$(basename "${ICON_LICENSE}")"
echo "Uploaded ${#ICON_KEYS[@]} POI icons to workspace style resources."

curl --fail --silent --show-error "${AUTH[@]}" \
  --request PUT \
  --header 'Content-Type: application/json' \
  --data "{\"layer\":{\"defaultStyle\":{\"name\":\"${WORKSPACE}:${STYLE}\"}}}" \
  "${REST}/layers/${WORKSPACE}:${FEATURE_TYPE}"

wfs_schema="$(curl --fail --silent --show-error "${AUTH[@]}" --get \
  --data-urlencode 'service=WFS' \
  --data-urlencode 'version=2.0.0' \
  --data-urlencode 'request=DescribeFeatureType' \
  --data-urlencode "typeNames=${WORKSPACE}:${FEATURE_TYPE}" \
  --data-urlencode 'outputFormat=application/json' \
  "${GEOSERVER_URL%/}/${WORKSPACE}/ows")"

if [[ "${wfs_schema}" != *category_icon_key* ]]; then
  echo "WFS schema for ${WORKSPACE}:${FEATURE_TYPE} does not expose category_icon_key." >&2
  exit 1
fi

wfs_response="$(curl --fail --silent --show-error "${AUTH[@]}" --get \
  --data-urlencode 'service=WFS' \
  --data-urlencode 'version=2.0.0' \
  --data-urlencode 'request=GetFeature' \
  --data-urlencode "typeNames=${WORKSPACE}:${FEATURE_TYPE}" \
  --data-urlencode 'outputFormat=application/json' \
  --data-urlencode 'srsName=EPSG:4326' \
  --data-urlencode 'count=1' \
  "${GEOSERVER_URL%/}/${WORKSPACE}/ows")"

if [[ "${wfs_response}" != *FeatureCollection* ]]; then
  echo "GeoServer did not return a WFS FeatureCollection for ${WORKSPACE}:${FEATURE_TYPE}." >&2
  exit 1
fi

fallback_icon="$(curl --fail --silent --show-error "${AUTH[@]}" \
  "${ICON_RESOURCE_URL}/${ICON_KEYS[0]}.svg")"
if [[ "${fallback_icon}" != *"<svg"* ]]; then
  echo "GeoServer style resource ${ICON_RESOURCE_URL}/${ICON_KEYS[0]}.svg is missing or not an SVG." >&2
  exit 1
fi

echo "POI GeoServer provisioning complete; packaged icons and WFS schema verified."
