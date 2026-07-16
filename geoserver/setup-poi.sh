#!/usr/bin/env bash

# Create or update the shared POI virtual table and its default style through GeoServer REST.
# Safe to rerun: existing resources are updated, missing resources are created.

set -euo pipefail

SCRIPT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
GEOSERVER_URL="${GEOSERVER_URL:-http://localhost:8080/geoserver}"
WORKSPACE="${GEOSERVER_WORKSPACE:-basarsoft}"
DATASTORE="${GEOSERVER_DATASTORE:-pg_basarsoft}"
FEATURE_TYPE="vw_poi"
STYLE="vw_poi_category"
FEATURE_XML="${SCRIPT_DIR}/featuretypes/${FEATURE_TYPE}.xml"
STYLE_SLD="${SCRIPT_DIR}/styles/${STYLE}.sld"

if [[ -z "${GEOSERVER_USER:-}" || -z "${GEOSERVER_PASSWORD:-}" ]]; then
  echo "Set GEOSERVER_USER and GEOSERVER_PASSWORD before running this script." >&2
  exit 2
fi

for command in curl; do
  if ! command -v "${command}" >/dev/null 2>&1; then
    echo "Required command not found: ${command}" >&2
    exit 2
  fi
done

if [[ ! -f "${FEATURE_XML}" || ! -f "${STYLE_SLD}" ]]; then
  echo "POI feature-type or style artifact is missing under ${SCRIPT_DIR}." >&2
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
      --header 'Content-Type: application/vnd.ogc.sld+xml' \
      --data-binary "@${STYLE_SLD}" \
      "${STYLE_URL}"
    echo "Updated ${WORKSPACE}:${STYLE}."
    ;;
  404)
    curl --fail --silent --show-error "${AUTH[@]}" \
      --request POST \
      --header 'Content-Type: application/vnd.ogc.sld+xml' \
      --data-binary "@${STYLE_SLD}" \
      "${STYLE_COLLECTION_URL}?name=${STYLE}"
    echo "Created ${WORKSPACE}:${STYLE}."
    ;;
  *)
    echo "Could not inspect ${WORKSPACE}:${STYLE} (HTTP ${style_status})." >&2
    exit 1
    ;;
esac

curl --fail --silent --show-error "${AUTH[@]}" \
  --request PUT \
  --header 'Content-Type: application/json' \
  --data "{\"layer\":{\"defaultStyle\":{\"name\":\"${WORKSPACE}:${STYLE}\"}}}" \
  "${REST}/layers/${WORKSPACE}:${FEATURE_TYPE}"

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

echo "POI GeoServer provisioning complete and WFS verified."
