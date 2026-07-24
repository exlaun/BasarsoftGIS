#!/usr/bin/env python3
"""Extract source-backed stop members for the demo bus-route references.

This helper intentionally writes an intermediate manifest, not application
fixtures.  It is used by the offline demo refresh workflow to inspect a locked
Geofabrik PBF and to match official operator stop lists to OSM coordinates.
"""

from __future__ import annotations

import argparse
import json
from collections import defaultdict
from pathlib import Path

import osmium


TARGET_REFS = {
    "34BZ",
    "34AS",
    "500T",
    "15F",
    "25E",
    "205",
    "303",
    "334-6",
    "413",
    "442",
    "202",
    "515",
    "584",
    "808",
    "950",
    "KL08",
    "VS18",
    "LC07A",
    "ML22",
    "VF63",
    "38/B-2",
    "114",
    "4-A",
    "B39",
    "121",
}


class RouteCollector(osmium.SimpleHandler):
    def __init__(self) -> None:
        super().__init__()
        self.nodes: dict[int, dict[str, object]] = {}
        self.matches: list[dict[str, object]] = []

    def node(self, node: osmium.osm.Node) -> None:
        if not node.location.valid():
            return

        highway = node.tags.get("highway")
        public_transport = node.tags.get("public_transport")
        if highway != "bus_stop" and public_transport not in {
            "platform",
            "stop_position",
        }:
            return

        self.nodes[node.id] = {
            "id": node.id,
            "name": node.tags.get("name"),
            "ref": node.tags.get("ref"),
            "lon": node.location.lon,
            "lat": node.location.lat,
            "highway": highway,
            "publicTransport": public_transport,
        }

    def relation(self, relation: osmium.osm.Relation) -> None:
        if relation.tags.get("type") != "route":
            return
        if relation.tags.get("route") not in {"bus", "trolleybus"}:
            return

        route_ref = relation.tags.get("ref")
        if route_ref not in TARGET_REFS:
            return

        self.matches.append(
            {
                "relationId": relation.id,
                "ref": route_ref,
                "name": relation.tags.get("name"),
                "from": relation.tags.get("from"),
                "to": relation.tags.get("to"),
                "operator": relation.tags.get("operator"),
                "network": relation.tags.get("network"),
                "members": [
                    {
                        "type": member.type,
                        "ref": member.ref,
                        "role": member.role,
                    }
                    for member in relation.members
                ],
            }
        )


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--pbf", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument(
        "--nodes-output",
        type=Path,
        help="Optional output for every named/ref-tagged bus-stop node in the PBF.",
    )
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    collector = RouteCollector()
    # Relation members are matched against nodes retained by this handler. No way
    # geometry is read, so a global node-location index is unnecessary and can
    # consume several gigabytes for the nationwide extract.
    collector.apply_file(str(args.pbf))

    groups: defaultdict[str, list[dict[str, object]]] = defaultdict(list)
    for route in collector.matches:
        seen: set[int] = set()
        stops: list[dict[str, object]] = []
        for member in route.pop("members"):
            member_id = member["ref"]
            if member["type"] != "n" or member_id in seen:
                continue
            node = collector.nodes.get(member_id)
            if node is None:
                continue
            seen.add(member_id)
            stops.append({**node, "role": member["role"]})
        route["stops"] = stops
        groups[str(route["ref"])].append(route)

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(dict(sorted(groups.items())), ensure_ascii=False, indent=2)
        + "\n",
        encoding="utf-8",
    )
    if args.nodes_output:
        args.nodes_output.parent.mkdir(parents=True, exist_ok=True)
        useful_nodes = [
            node
            for node in collector.nodes.values()
            if node["name"] or node["ref"]
        ]
        args.nodes_output.write_text(
            json.dumps(useful_nodes, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )

    summary = {
        route_ref: [
            {
                "relationId": route["relationId"],
                "name": route["name"],
                "operator": route["operator"],
                "stops": len(route["stops"]),
            }
            for route in routes
        ]
        for route_ref, routes in sorted(groups.items())
    }
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
