#!/usr/bin/env python3
"""Extract bus-station coordinates and source ids from a local OSM PBF."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import osmium


class StationFilter:
    @staticmethod
    def is_station(tags: osmium.osm.TagList) -> bool:
        return tags.get("amenity") == "bus_station" or (
            tags.get("public_transport") == "station"
            and tags.get("bus") in {"yes", "designated"}
        )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--pbf", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    # A nationwide location index is too large for the locked PBF. Native
    # pyosmium filters keep Python callbacks bounded to the small candidate
    # sets instead of invoking Python once for every Turkish OSM object.
    station_tags = osmium.filter.TagFilter(
        ("amenity", "bus_station"),
        ("public_transport", "station"),
    )
    ways: list[dict[str, object]] = []
    for way in (
        osmium.FileProcessor(str(args.pbf), osmium.osm.WAY)
        .with_filter(station_tags)
    ):
        if StationFilter.is_station(way.tags):
            ways.append(
                {
                    "osmId": way.id,
                    "name": way.tags.get("name"),
                    "operator": way.tags.get("operator"),
                    "nodeIds": [node.ref for node in way.nodes],
                }
            )

    way_node_ids = {node_id for way in ways for node_id in way["nodeIds"]}
    coordinates: dict[int, tuple[float, float]] = {}
    for node in (
        osmium.FileProcessor(str(args.pbf), osmium.osm.NODE)
        .with_filter(osmium.filter.IdFilter(way_node_ids))
    ):
        if node.location.valid():
            coordinates[node.id] = (node.location.lon, node.location.lat)

    features: list[dict[str, object]] = []
    for node in (
        osmium.FileProcessor(str(args.pbf), osmium.osm.NODE)
        .with_filter(
            osmium.filter.TagFilter(
                ("amenity", "bus_station"),
                ("public_transport", "station"),
            )
        )
    ):
        if node.location.valid() and StationFilter.is_station(node.tags):
            features.append(
                {
                    "osmType": "node",
                    "osmId": node.id,
                    "name": node.tags.get("name"),
                    "operator": node.tags.get("operator"),
                    "longitude": node.location.lon,
                    "latitude": node.location.lat,
                }
            )

    for way in ways:
        locations = [
            coordinates[node_id]
            for node_id in way["nodeIds"]
            if node_id in coordinates
        ]
        if not locations:
            continue
        features.append(
            {
                "osmType": "way",
                "osmId": way["osmId"],
                "name": way["name"],
                "operator": way["operator"],
                "longitude": sum(location[0] for location in locations) / len(locations),
                "latitude": sum(location[1] for location in locations) / len(locations),
            }
        )

    features.sort(
        key=lambda item: (str(item["name"] or ""), str(item["osmType"]), item["osmId"])
    )
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(features, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"Extracted {len(features)} bus stations.")


if __name__ == "__main__":
    main()
