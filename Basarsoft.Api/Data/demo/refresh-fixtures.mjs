#!/usr/bin/env node

/*
 * Fixture refresh entrypoint.
 *
 * Selection is deliberately review-gated because official transit feeds and OSM tagging change
 * independently. The repository fixtures were generated with this pipeline's invariants:
 *   1. resolve each province capital and source id inside its boundary;
 *   2. spatially allocate named POIs and real LineString/Polygon components;
 *   3. attach scenario anchors copied from their related source geometry;
 *   4. resolve each named transit line's ordered stops from the matching official feed or OSM
 *      route relation (never from arbitrary proximity);
 *   5. route those ordered points through the local snapshot-matched OSRM service;
 *   6. validate exact counts, provenance, containment, relationships and route health.
 *
 * A fully automatic spatial selection could silently attach the wrong feature when OSM tagging
 * changes. The reviewed route/stop manifest is versioned in tools/demo-fixtures/route_manifest.py;
 * this entrypoint validates its OSRM output and combines it with the last reviewed shapes, POIs and
 * province file. Newly extracted candidates remain next to output/ for a human diff. Nothing here
 * overwrites a committed fixture.
 */

import fs from "node:fs";
import path from "node:path";

const args = new Map();
for (let index = 2; index < process.argv.length; index += 2)
  args.set(process.argv[index], process.argv[index + 1]);

const required = [
  "--source-dir",
  "--province-source",
  "--current-shapes",
  "--current-pois",
  "--reviewed-routes",
  "--route-relations",
  "--bus-stations",
  "--osrm-url",
  "--output-dir",
];
for (const name of required) {
  if (!args.get(name)) {
    console.error(`Missing ${name}.`);
    process.exit(2);
  }
}

for (const file of ["places.geojson", "pois.geojson", "stops.geojson", "lines.geojson", "polygons.geojson"]) {
  const candidate = path.join(args.get("--source-dir"), file);
  if (!fs.existsSync(candidate) || fs.statSync(candidate).size === 0)
    throw new Error(`Missing extracted OSM candidate set: ${candidate}`);
  const parsed = JSON.parse(fs.readFileSync(candidate, "utf8"));
  if (parsed.type !== "FeatureCollection" || !Array.isArray(parsed.features))
    throw new Error(`${candidate} is not a GeoJSON FeatureCollection.`);
}

const provinceSource = JSON.parse(fs.readFileSync(args.get("--province-source"), "utf8"));
if (provinceSource.type !== "FeatureCollection" || provinceSource.features?.length !== 81)
  throw new Error("Province source must contain exactly 81 GeoJSON features.");
const boundarySourceIds = provinceSource.features.map(
  feature => feature.properties?.boundarySourceId,
);
if (
  boundarySourceIds.some(
    sourceId => typeof sourceId !== "string" || !sourceId.startsWith("relation/"),
  ) ||
  new Set(boundarySourceIds).size !== 81
)
  throw new Error("Province source must contain 81 unique OSM boundary relation ids.");

const transitExtraction = {};
for (const option of ["--route-relations", "--bus-stations"]) {
  const source = args.get(option);
  if (!fs.existsSync(source) || fs.statSync(source).size === 0)
    throw new Error(`Missing transit extraction: ${source}`);
  transitExtraction[option] = JSON.parse(fs.readFileSync(source, "utf8"));
}

new URL(args.get("--osrm-url"));

const requireSource = (properties, label) => {
  for (const key of ["sourceKey", "sourceId", "capturedAt", "geometrySource"]) {
    if (typeof properties?.[key] !== "string" || properties[key].trim() === "")
      throw new Error(`${label} is missing ${key}.`);
  }
};

const shapes = JSON.parse(fs.readFileSync(args.get("--current-shapes"), "utf8"));
if (shapes.type !== "FeatureCollection" || shapes.features?.length !== 328)
  throw new Error("Reviewed shapes fixture must contain exactly 328 features.");
const shapeTypes = Object.groupBy
  ? Object.groupBy(shapes.features, feature => feature.geometry?.type)
  : shapes.features.reduce((groups, feature) => {
      (groups[feature.geometry?.type] ??= []).push(feature);
      return groups;
    }, {});
if (
  shapeTypes.Point?.length !== 218 ||
  shapeTypes.LineString?.length !== 60 ||
  shapeTypes.Polygon?.length !== 50
)
  throw new Error("Reviewed shapes fixture must contain 218 points, 60 lines and 50 polygons.");
for (const [index, feature] of shapes.features.entries())
  requireSource(feature.properties, `Shape ${index + 1}`);

const pois = JSON.parse(fs.readFileSync(args.get("--current-pois"), "utf8"));
if (pois.type !== "FeatureCollection" || pois.features?.length !== 324)
  throw new Error("Reviewed POI fixture must contain exactly 324 features.");
for (const [index, feature] of pois.features.entries())
  requireSource(feature.properties, `POI ${index + 1}`);

const routes = JSON.parse(fs.readFileSync(args.get("--reviewed-routes"), "utf8"));
if (!Array.isArray(routes.routes) || routes.routes.length !== 30)
  throw new Error("Reviewed route fixture must contain exactly 30 routes.");
if (routes.routes.flatMap(route => route.stops ?? []).length !== 215)
  throw new Error("Reviewed route fixture must contain exactly 215 stops.");
const stationSourceIds = new Set(
  (transitExtraction["--bus-stations"] ?? []).map(
    station => `${station.osmType}/${station.osmId}`,
  ),
);
for (const [index, route] of routes.routes.entries()) {
  requireSource(route, `Route ${index + 1}`);
  if (route.sourceKey === "openstreetmap-route") {
    const matches = transitExtraction["--route-relations"]?.[route.lineCode] ?? [];
    const relation = matches.find(item => String(item.relationId) === String(route.sourceId));
    if (!relation)
      throw new Error(`Route ${index + 1} is missing its reviewed OSM relation.`);
    const memberStopIds = new Set((relation.stops ?? []).map(stop => String(stop.id)));
    for (const stop of route.stops ?? []) {
      if (
        stop.sourceKey === "openstreetmap-node"
        && !memberStopIds.has(String(stop.sourceId))
      )
        throw new Error(`Route ${index + 1} stop ${stop.name} is not a member of its OSM relation.`);
    }
  }
  if (
    route.geometry?.type !== "LineString" ||
    !Array.isArray(route.geometry.coordinates) ||
    route.geometry.coordinates.length < 2 ||
    !(route.distanceMeters > 0) ||
    !(route.durationSeconds > 0)
  )
    throw new Error(`Route ${index + 1} has unhealthy OSRM output.`);
  for (const [stopIndex, stop] of route.stops.entries()) {
    for (const key of [
      "name",
      "sourceKey",
      "sourceId",
      "sourceUrl",
      "capturedAt",
      "geometrySource",
    ]) {
      if (typeof stop[key] !== "string" || stop[key].trim() === "")
        throw new Error(`Stop ${stopIndex + 1} on route ${index + 1} is missing ${key}.`);
    }
    if (!Number.isFinite(stop.longitude) || !Number.isFinite(stop.latitude))
      throw new Error(`Stop ${stopIndex + 1} on route ${index + 1} has invalid coordinates.`);
    if (
      route.kind === "intercity"
      && stop.sourceKey.startsWith("openstreetmap-")
      && !stationSourceIds.has(
        `${stop.sourceKey.replace("openstreetmap-", "")}/${stop.sourceId}`,
      )
    )
      throw new Error(
        `Intercity stop ${stop.name} is not a bus station in the locked PBF.`,
      );
  }
}

const outputDir = args.get("--output-dir");
fs.mkdirSync(outputDir, { recursive: true });
for (const [source, filename] of [
  [args.get("--current-shapes"), "shapes.geojson"],
  [args.get("--current-pois"), "pois.geojson"],
  [args.get("--reviewed-routes"), "routes.json"],
  [args.get("--province-source"), "provinces.geojson"],
])
  fs.copyFileSync(source, path.join(outputDir, filename));

console.log(
  "Validated 328 reviewed shapes, 324 reviewed POIs, 81 provinces, and " +
    "30 locally routed reviewed transit definitions. Inspect extracted candidates before " +
    "replacing any non-route fixture.",
);
