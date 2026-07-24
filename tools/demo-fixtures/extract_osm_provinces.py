#!/usr/bin/env python3
"""Merge exact OSM province relations with the reviewed province metadata.

The input properties file remains the authority for canonical application names,
regions, colors, and administrative-capital points. Geometry is rebuilt from the
81 `network=TR-provinces`, `admin_level=4` relations in a locked Turkey PBF.

No geometry is inferred, buffered, or connected. The output retains every OSM
ring vertex and must be written to a different path for review before replacing
the committed fixture.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import math
import sys
import unicodedata
from pathlib import Path
from typing import Any

import osmium


EXPECTED_PROVINCE_COUNT = 81


def normalized_name(value: str) -> str:
    decomposed = unicodedata.normalize("NFKD", value.strip()).casefold()
    return "".join(character for character in decomposed if not unicodedata.combining(character))


def point_on_segment(
    longitude: float,
    latitude: float,
    first: list[float],
    second: list[float],
) -> bool:
    ax, ay = first
    bx, by = second
    cross = (longitude - ax) * (by - ay) - (latitude - ay) * (bx - ax)
    if abs(cross) > 1e-12:
        return False
    return (
        min(ax, bx) - 1e-12 <= longitude <= max(ax, bx) + 1e-12
        and min(ay, by) - 1e-12 <= latitude <= max(ay, by) + 1e-12
    )


def ring_covers(point: tuple[float, float], ring: list[list[float]]) -> tuple[bool, bool]:
    longitude, latitude = point
    inside = False
    previous = ring[-1]
    for current in ring:
        if point_on_segment(longitude, latitude, previous, current):
            return True, True
        ax, ay = previous
        bx, by = current
        if (ay > latitude) != (by > latitude):
            crossing_longitude = (bx - ax) * (latitude - ay) / (by - ay) + ax
            if longitude < crossing_longitude:
                inside = not inside
        previous = current
    return inside, False


def polygon_covers(point: tuple[float, float], polygon: list[list[list[float]]]) -> bool:
    outer_inside, outer_boundary = ring_covers(point, polygon[0])
    if not outer_inside:
        return False
    if outer_boundary:
        return True
    for hole in polygon[1:]:
        hole_inside, hole_boundary = ring_covers(point, hole)
        if hole_boundary:
            return True
        if hole_inside:
            return False
    return True


def geometry_covers(point: tuple[float, float], geometry: dict[str, Any]) -> bool:
    geometry_type = geometry.get("type")
    coordinates = geometry.get("coordinates")
    if geometry_type == "Polygon":
        return polygon_covers(point, coordinates)
    if geometry_type == "MultiPolygon":
        return any(polygon_covers(point, polygon) for polygon in coordinates)
    raise ValueError(f"Expected Polygon/MultiPolygon, found {geometry_type!r}.")


def validate_geometry(geometry: dict[str, Any], label: str) -> None:
    geometry_type = geometry.get("type")
    polygons = (
        [geometry.get("coordinates")]
        if geometry_type == "Polygon"
        else geometry.get("coordinates")
        if geometry_type == "MultiPolygon"
        else None
    )
    if not isinstance(polygons, list) or not polygons:
        raise ValueError(f"{label} has no Polygon/MultiPolygon coordinates.")

    for polygon in polygons:
        if not isinstance(polygon, list) or not polygon:
            raise ValueError(f"{label} contains an empty polygon.")
        for ring in polygon:
            if not isinstance(ring, list) or len(ring) < 4 or ring[0] != ring[-1]:
                raise ValueError(f"{label} contains an open or undersized ring.")
            for coordinate in ring:
                if (
                    not isinstance(coordinate, list)
                    or len(coordinate) < 2
                    or not all(math.isfinite(value) for value in coordinate[:2])
                    or not 25 <= coordinate[0] <= 45
                    or not 35 <= coordinate[1] <= 43.5
                ):
                    raise ValueError(f"{label} contains an invalid Turkey coordinate.")


class ProvinceAreaHandler(osmium.SimpleHandler):
    def __init__(self, capital_node_ids: set[int]) -> None:
        super().__init__()
        self.factory = osmium.geom.GeoJSONFactory()
        self.by_name: dict[str, dict[str, Any]] = {}
        self.boundary_source_ids: set[str] = set()
        self.capital_node_ids = capital_node_ids
        self.capitals: dict[int, tuple[float, float, str | None]] = {}

    def node(self, node: osmium.osm.Node) -> None:
        if node.id not in self.capital_node_ids:
            return
        if node.id in self.capitals:
            raise ValueError(f"Duplicate capital node/{node.id} in PBF.")
        self.capitals[node.id] = (
            node.location.lon,
            node.location.lat,
            node.tags.get("name:tr") or node.tags.get("name"),
        )

    def area(self, area: osmium.osm.Area) -> None:
        if (
            area.from_way()
            or area.tags.get("boundary") != "administrative"
            or area.tags.get("admin_level") != "4"
            or area.tags.get("network") != "TR-provinces"
        ):
            return

        source_name = area.tags.get("name:tr") or area.tags.get("name")
        if not source_name:
            raise ValueError(f"Province relation/{area.orig_id()} has no Turkish name.")
        key = normalized_name(source_name)
        if key in self.by_name:
            raise ValueError(f"Duplicate OSM province name {source_name!r}.")

        boundary_source_id = f"relation/{area.orig_id()}"
        if boundary_source_id in self.boundary_source_ids:
            raise ValueError(f"Duplicate boundary source id {boundary_source_id}.")
        self.boundary_source_ids.add(boundary_source_id)

        geometry = json.loads(self.factory.create_multipolygon(area))
        validate_geometry(geometry, boundary_source_id)
        self.by_name[key] = {
            "boundarySourceId": boundary_source_id,
            "sourceName": source_name,
            "geometry": geometry,
        }


def load_reviewed_features(path: Path) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if document.get("type") != "FeatureCollection" or not isinstance(
        document.get("features"), list
    ):
        raise ValueError("Properties source is not a GeoJSON FeatureCollection.")
    features = document["features"]
    if len(features) != EXPECTED_PROVINCE_COUNT:
        raise ValueError(
            f"Expected {EXPECTED_PROVINCE_COUNT} reviewed provinces, found {len(features)}."
        )

    names: set[str] = set()
    capital_sources: set[tuple[str, str]] = set()
    for feature in features:
        properties = feature.get("properties")
        if not isinstance(properties, dict):
            raise ValueError("Every reviewed province must contain properties.")
        for key in [
            "name",
            "region",
            "color",
            "capitalName",
            "sourceKey",
            "sourceId",
            "capturedAt",
            "geometrySource",
        ]:
            if not isinstance(properties.get(key), str) or not properties[key].strip():
                raise ValueError(f"Province property {key!r} is required.")
        name_key = normalized_name(properties["name"])
        if name_key in names:
            raise ValueError(f"Duplicate reviewed province name {properties['name']!r}.")
        names.add(name_key)
        capital_source = (properties["sourceKey"], properties["sourceId"])
        if capital_source in capital_sources:
            raise ValueError(f"Duplicate capital source identity {capital_source!r}.")
        capital_sources.add(capital_source)
        for key in ["capitalLongitude", "capitalLatitude"]:
            if not isinstance(properties.get(key), (int, float)) or not math.isfinite(
                properties[key]
            ):
                raise ValueError(f"Province property {key!r} must be finite.")
    return document, features


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--pbf", type=Path, required=True)
    parser.add_argument("--snapshot-date", required=True)
    parser.add_argument("--properties-source", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    for path in [args.pbf, args.properties_source]:
        if not path.is_file():
            parser.error(f"Input file does not exist: {path}")
    if args.output.resolve() == args.properties_source.resolve():
        parser.error("--output must differ from --properties-source; review before replacement.")
    try:
        parsed_snapshot = dt.date.fromisoformat(args.snapshot_date)
    except ValueError:
        parser.error("--snapshot-date must use YYYY-MM-DD.")
    if parsed_snapshot.isoformat() != args.snapshot_date:
        parser.error("--snapshot-date must use canonical YYYY-MM-DD.")

    _, reviewed_features = load_reviewed_features(args.properties_source)
    capital_node_ids: set[int] = set()
    for feature in reviewed_features:
        source_id = feature["properties"]["sourceId"]
        if not source_id.startswith("node/") or not source_id[5:].isdigit():
            raise ValueError(
                f"Province capital source id must be an OSM node, found {source_id!r}."
            )
        capital_node_ids.add(int(source_id[5:]))
    if len(capital_node_ids) != EXPECTED_PROVINCE_COUNT:
        raise ValueError("Capital node source ids are incomplete or duplicated.")

    handler = ProvinceAreaHandler(capital_node_ids)
    handler.apply_file(str(args.pbf), locations=True)
    if len(handler.by_name) != EXPECTED_PROVINCE_COUNT:
        raise ValueError(
            f"Expected {EXPECTED_PROVINCE_COUNT} TR-provinces relations, "
            f"found {len(handler.by_name)}."
        )
    if len(handler.boundary_source_ids) != EXPECTED_PROVINCE_COUNT:
        raise ValueError("Boundary source ids are incomplete or duplicated.")
    if handler.capitals.keys() != capital_node_ids:
        missing_capitals = sorted(capital_node_ids - handler.capitals.keys())
        raise ValueError(f"Locked PBF is missing capital nodes: {missing_capitals!r}.")

    reviewed_names = {
        normalized_name(feature["properties"]["name"]) for feature in reviewed_features
    }
    missing = sorted(reviewed_names - handler.by_name.keys())
    unexpected = sorted(handler.by_name.keys() - reviewed_names)
    if missing or unexpected:
        raise ValueError(
            f"Province-name mismatch. Missing={missing!r}; unexpected={unexpected!r}."
        )

    merged_features: list[dict[str, Any]] = []
    for feature in reviewed_features:
        properties = dict(feature["properties"])
        source = handler.by_name[normalized_name(properties["name"])]
        capital_node_id = int(properties["sourceId"][5:])
        longitude, latitude, source_capital_name = handler.capitals[capital_node_id]
        if not source_capital_name:
            raise ValueError(f"Capital node/{capital_node_id} has no canonical OSM name.")
        properties["capitalLongitude"] = longitude
        properties["capitalLatitude"] = latitude
        properties["capturedAt"] = args.snapshot_date
        properties["boundarySourceId"] = source["boundarySourceId"]
        properties["geometrySource"] = (
            f"Exact OpenStreetMap admin_level=4 {source['boundarySourceId']} geometry "
            f"from the locked Geofabrik Turkey {properties['capturedAt']} snapshot; "
            f"capital coordinates retain {properties['sourceId']}"
        )
        geometry = source["geometry"]
        capital = (
            float(properties["capitalLongitude"]),
            float(properties["capitalLatitude"]),
        )
        if not geometry_covers(capital, geometry):
            raise ValueError(
                f"Capital {properties['capitalName']!r} is outside "
                f"province {properties['name']!r}."
            )
        merged_features.append(
            {"type": "Feature", "properties": properties, "geometry": geometry}
        )

    args.output.parent.mkdir(parents=True, exist_ok=True)
    # Keep one feature per line: exact rings remain reviewable without expanding
    # hundreds of thousands of coordinate pairs into a multi-million-line diff.
    with args.output.open("w", encoding="utf-8") as output:
        output.write('{"type":"FeatureCollection","features":[\n')
        for index, feature in enumerate(merged_features):
            if index:
                output.write(",\n")
            output.write(
                json.dumps(feature, ensure_ascii=False, separators=(",", ":"))
            )
        output.write("\n]}\n")
    print(
        f"Wrote {len(merged_features)} exact OSM province boundaries with "
        f"{len(handler.boundary_source_ids)} unique relation ids to {args.output}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
