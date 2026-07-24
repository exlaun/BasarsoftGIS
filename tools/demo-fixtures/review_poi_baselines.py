#!/usr/bin/env python3
"""Audit and apply reviewed nationwide POI baseline replacements.

`pois.geojson` is already the reviewed selection manifest. Its first 243 rows
are the province baseline (hospital, food venue, shopping venue for each of 81
provinces); later rows are major-city/category extras.

This tool resolves those selections against a locked OSM PBF, emits ranked
candidate evidence, and can apply a small explicit replacement manifest. Raw
OSM tags are never trusted alone: lifecycle and semantic-name exclusions are
enforced before a replacement can be written.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import math
import re
import sys
import unicodedata
from collections import defaultdict
from pathlib import Path
from typing import Any

import osmium

from extract_osm_provinces import geometry_covers


EXPECTED_PROVINCES = 81
EXPECTED_POIS = 324
BASELINE_ROWS = EXPECTED_PROVINCES * 3

FOOD_CATEGORIES = {"Restaurant", "Cafe", "Bakery", "Fast Food"}
SHOPPING_CATEGORIES = {"Mall", "Supermarket"}
PHARMACY_24_7_VALUES = {"24/7", "00:00-24:00", "mo-su 00:00-24:00"}

HOSPITAL_REJECTIONS = [
    r"\beski\b",
    r"aile sagligi",
    r"saglik ocagi",
    r"saglik merkezi",
    r"saglik mudurlugu",
    r"\bosgb\b",
    r"ortak saglik",
    r"\bgiris\b",
    r"\bentrance\b",
    r"diyaliz",
    r"\b112\b",
    r"acil servis",
    r"dis hekimligi fakultesi",
    r"agiz ve dis sagligi merkezi",
    r"tip merkezi",
    r"\bcagem\b",
    r"\bunitesi\b",
]
MALL_REJECTIONS = [
    r"^market$",
    r"bilgisayar",
    r"\bkolej",
    r"\bokul",
    r"kafeterya",
    r"\bevent\b",
    r"organizasyon",
    r"yapi market",
    r"konfeksiyon.*mobilya",
    r"\bevkur\b",
]
FOOD_REJECTIONS = [
    r"^lokanta$",
    r"\bcafe$",
    r"\bkafe$",
    r"\bbufe\b",
]
PHARMACY_REJECTIONS = [
    r"saglik merkezi",
    r"tip merkezi",
    r"\bhastane",
    r"\bklinik",
]


def folded(value: str) -> str:
    decomposed = unicodedata.normalize("NFKD", value).casefold()
    plain = "".join(
        character for character in decomposed if not unicodedata.combining(character)
    )
    return plain.translate(str.maketrans({"ı": "i", "ş": "s", "ç": "c", "ğ": "g", "ö": "o", "ü": "u"}))


def rejected(name: str, patterns: list[str]) -> bool:
    normalized = folded(name)
    return any(re.search(pattern, normalized) for pattern in patterns)


def polygon_centroid(coordinates: list[tuple[float, float]]) -> tuple[float, float]:
    if len(coordinates) < 3:
        raise ValueError("Cannot calculate a centroid from fewer than three coordinates.")
    if coordinates[0] != coordinates[-1]:
        coordinates = [*coordinates, coordinates[0]]

    twice_area = 0.0
    longitude_sum = 0.0
    latitude_sum = 0.0
    for first, second in zip(coordinates, coordinates[1:]):
        cross = first[0] * second[1] - second[0] * first[1]
        twice_area += cross
        longitude_sum += (first[0] + second[0]) * cross
        latitude_sum += (first[1] + second[1]) * cross
    if abs(twice_area) < 1e-15:
        return (
            sum(coordinate[0] for coordinate in coordinates[:-1]) / (len(coordinates) - 1),
            sum(coordinate[1] for coordinate in coordinates[:-1]) / (len(coordinates) - 1),
        )
    return (
        longitude_sum / (3 * twice_area),
        latitude_sum / (3 * twice_area),
    )


def lifecycle_rejected(tags: dict[str, str]) -> bool:
    lifecycle_keys = {
        "proposed",
        "construction",
        "abandoned",
        "demolished",
        "removed",
        "disused",
    }
    return any(
        value not in ("", "no")
        and (
            key in lifecycle_keys
            or key.split(":", maxsplit=1)[0] in lifecycle_keys
        )
        for key, value in tags.items()
    )


def semantic_categories(tags: dict[str, str], name: str) -> set[str]:
    if lifecycle_rejected(tags):
        return set()
    categories: set[str] = set()
    sport_values = {
        value.strip() for value in tags.get("sport", "").split(";") if value.strip()
    }
    if (
        tags.get("amenity") == "hospital" or tags.get("healthcare") == "hospital"
    ) and not rejected(name, HOSPITAL_REJECTIONS):
        categories.add("Hospital")
    if tags.get("shop") == "mall" and not rejected(name, MALL_REJECTIONS):
        categories.add("Mall")
    if tags.get("shop") == "supermarket":
        categories.add("Supermarket")
    if tags.get("amenity") == "restaurant" and not rejected(name, FOOD_REJECTIONS):
        categories.add("Restaurant")
    if tags.get("amenity") == "fast_food":
        categories.add("Fast Food")
    if tags.get("amenity") == "cafe":
        categories.add("Cafe")
    if tags.get("shop") == "bakery":
        categories.add("Bakery")

    if (
        tags.get("amenity") == "pharmacy"
        and not rejected(name, PHARMACY_REJECTIONS)
    ):
        categories.add("Pharmacy")
        opening_hours = tags.get("opening_hours", "").strip().casefold()
        if opening_hours in PHARMACY_24_7_VALUES:
            categories.add("24/7 Pharmacy")
    if tags.get("tourism") == "museum":
        categories.add("Museum")
    if tags.get("historic") not in (None, "", "no"):
        categories.add("Historical Site")
    if tags.get("tourism") == "gallery":
        categories.add("Art Gallery")
    if (
        tags.get("tourism") == "visitor_centre"
        or (
            tags.get("tourism") == "information"
            and tags.get("information")
            in {"office", "visitor_centre", "visitor_center"}
        )
    ):
        categories.add("Visitor Center")
    if tags.get("tourism") in {"hotel", "motel"}:
        categories.add("Hotel")

    if (
        tags.get("aeroway") == "aerodrome"
        and tags.get("military") not in {"airfield", "yes"}
        and tags.get("landuse") != "military"
        and tags.get("aerodrome:type") != "military"
        and tags.get("access") not in {"private", "no"}
    ):
        categories.add("Airport")
    if (
        tags.get("railway") in {"station", "halt"}
        and tags.get("station") not in {"subway", "light_rail", "monorail"}
        and tags.get("subway") != "yes"
    ):
        categories.add("Train Station")
    if tags.get("amenity") == "bus_station":
        categories.add("Bus Terminal")
    if (
        tags.get("amenity") == "ferry_terminal"
        or (
            tags.get("public_transport") == "station"
            and tags.get("ferry") == "yes"
        )
    ):
        categories.add("Ferry Terminal")
    if (
        tags.get("railway") == "station"
        and (
            tags.get("station") in {"subway", "light_rail"}
            or tags.get("subway") == "yes"
        )
    ):
        categories.add("Metro Station")

    if tags.get("amenity") == "university":
        categories.add("University")
    if tags.get("amenity") == "library":
        categories.add("Library")
    if tags.get("amenity") == "school" and re.search(
        r"\blise(?:si)?\b|\bhigh school\b", folded(name)
    ):
        categories.add("High School")
    if tags.get("amenity") == "bank":
        categories.add("Bank")
    if tags.get("amenity") == "fuel":
        categories.add("Gas Station")
    if tags.get("amenity") == "post_office":
        categories.add("Post Office")
    if (
        tags.get("amenity") == "townhall"
        or (
            tags.get("office") == "government"
            and (
                tags.get("government") in {"local_authority", "municipality"}
                or re.search(r"belediye|muhtarl", folded(name))
            )
        )
    ):
        categories.add("Municipality")

    if tags.get("boundary") == "national_park":
        categories.add("National Park")
    if tags.get("natural") == "beach" or tags.get("leisure") == "beach_resort":
        categories.add("Beach")
    if tags.get("leisure") == "park":
        categories.add("Park")
    if (
        tags.get("leisure") in {"garden", "park"}
        and (
            tags.get("garden:type") == "botanical"
            or "botanik" in folded(name)
        )
    ):
        categories.add("Botanical Garden")
    if (
        tags.get("leisure") == "stadium"
        or (
            tags.get("leisure") == "sports_centre"
            and re.search(r"\bstad(?:yum|i)?\b", folded(name))
        )
    ):
        categories.add("Stadium")
    if (
        (
            tags.get("landuse") == "winter_sports"
            or "skiing" in sport_values
        )
        and re.search(
            r"kayak (?:merkezi|tesisi)|ski (?:centre|center|resort)", folded(name)
        )
        or (
            tags.get("place") == "locality"
            and re.search(
                r"kayak (?:merkezi|tesisi)|ski (?:centre|center|resort)", folded(name)
            )
        )
    ):
        categories.add("Ski Center")
    if (
        tags.get("leisure") == "fitness_centre"
        or (
            tags.get("leisure") == "sports_centre"
            and bool(sport_values.intersection({"fitness", "yoga", "gymnastics"}))
        )
    ):
        categories.add("Gym")
    return categories


class CandidateHandler(osmium.SimpleHandler):
    def __init__(self) -> None:
        super().__init__()
        self.candidates: dict[str, dict[str, Any]] = {}

    def node(self, node: osmium.osm.Node) -> None:
        if not self._potential(node.tags):
            return
        self._add(
            source_id=f"node/{node.id}",
            tags=dict(node.tags),
            point=(node.location.lon, node.location.lat),
            geometry_source="Mapped OSM node coordinate",
        )

    def way(self, way: osmium.osm.Way) -> None:
        if not self._potential(way.tags):
            return
        tags = dict(way.tags)
        name = tags.get("name")
        try:
            coordinates = [(node.lon, node.lat) for node in way.nodes]
        except osmium.InvalidLocationError:
            return
        if len(coordinates) < 3:
            return
        point, geometry_source = self._representative_way_point(coordinates)
        self._add(
            source_id=f"way/{way.id}",
            tags=tags,
            point=point,
            geometry_source=geometry_source,
        )

    @staticmethod
    def _representative_way_point(
        coordinates: list[tuple[float, float]],
    ) -> tuple[tuple[float, float], str]:
        if coordinates[0] != coordinates[-1]:
            return (
                coordinates[len(coordinates) // 2],
                "Representative point taken from an actual OSM way vertex",
            )

        centroid = polygon_centroid(coordinates)
        footprint = {
            "type": "Polygon",
            "coordinates": [
                [[longitude, latitude] for longitude, latitude in coordinates]
            ],
        }
        if geometry_covers(centroid, footprint):
            return (
                centroid,
                "Representative centroid validated inside the related OSM way footprint",
            )
        return (
            coordinates[0],
            "Representative OSM way boundary vertex used because its centroid falls outside the footprint",
        )

    @staticmethod
    def _potential(tags: osmium.osm.TagList) -> bool:
        if not tags.get("name"):
            return False
        return (
            tags.get("amenity")
            in {
                "hospital",
                "restaurant",
                "cafe",
                "fast_food",
                "pharmacy",
                "bus_station",
                "ferry_terminal",
                "university",
                "library",
                "school",
                "bank",
                "fuel",
                "post_office",
                "townhall",
            }
            or tags.get("healthcare") == "hospital"
            or tags.get("shop") in {"bakery", "mall", "supermarket"}
            or tags.get("tourism")
            in {"museum", "gallery", "information", "visitor_centre", "hotel", "motel"}
            or tags.get("historic") not in {None, "", "no"}
            or tags.get("aeroway") == "aerodrome"
            or tags.get("railway") in {"station", "halt"}
            or tags.get("public_transport") == "station"
            or tags.get("office") == "government"
            or tags.get("boundary") == "national_park"
            or tags.get("natural") == "beach"
            or tags.get("leisure")
            in {
                "beach_resort",
                "park",
                "garden",
                "stadium",
                "fitness_centre",
                "sports_centre",
            }
            or tags.get("landuse") == "winter_sports"
            or tags.get("sport") == "skiing"
        )

    def _add(
        self,
        *,
        source_id: str,
        tags: dict[str, str],
        point: tuple[float, float],
        geometry_source: str,
    ) -> None:
        name = tags.get("name")
        if not name:
            return
        categories = semantic_categories(tags, name)
        if not categories:
            return
        longitude, latitude = point
        if not (
            math.isfinite(longitude)
            and math.isfinite(latitude)
            and 25 <= longitude <= 45
            and 35 <= latitude <= 43.5
        ):
            return
        self.candidates[source_id] = {
            "sourceId": source_id,
            "name": name.strip(),
            "categories": sorted(categories),
            "longitude": longitude,
            "latitude": latitude,
            "geometrySource": geometry_source,
            "tags": tags,
        }


class FixtureSourceHandler(CandidateHandler):
    """Read the exact committed source ids, including semantically rejected rows."""

    def __init__(self, source_ids: set[str]) -> None:
        super().__init__()
        self.source_ids = source_ids
        self.raw_sources: dict[str, dict[str, Any]] = {}

    def node(self, node: osmium.osm.Node) -> None:
        source_id = f"node/{node.id}"
        if source_id not in self.source_ids:
            return
        tags = dict(node.tags)
        self.raw_sources[source_id] = {
            "name": tags.get("name"),
            "tags": tags,
        }
        self._add(
            source_id=source_id,
            tags=tags,
            point=(node.location.lon, node.location.lat),
            geometry_source="Mapped OSM node coordinate",
        )

    def way(self, way: osmium.osm.Way) -> None:
        source_id = f"way/{way.id}"
        if source_id not in self.source_ids:
            return
        tags = dict(way.tags)
        self.raw_sources[source_id] = {
            "name": tags.get("name"),
            "tags": tags,
        }
        try:
            coordinates = [(node.lon, node.lat) for node in way.nodes]
        except osmium.InvalidLocationError:
            return
        if len(coordinates) < 3:
            return
        point, geometry_source = self._representative_way_point(coordinates)
        self._add(
            source_id=source_id,
            tags=tags,
            point=point,
            geometry_source=geometry_source,
        )


def load_provinces(path: Path) -> list[dict[str, Any]]:
    document = json.loads(path.read_text(encoding="utf-8"))
    features = document.get("features")
    if document.get("type") != "FeatureCollection" or len(features or []) != EXPECTED_PROVINCES:
        raise ValueError("Province source must contain exactly 81 features.")
    provinces = []
    for feature in features:
        properties = feature["properties"]
        geometry = feature["geometry"]
        all_coordinates: list[list[float]] = []
        polygons = (
            [geometry["coordinates"]]
            if geometry["type"] == "Polygon"
            else geometry["coordinates"]
        )
        for polygon in polygons:
            for ring in polygon:
                all_coordinates.extend(ring)
        provinces.append(
            {
                "name": properties["name"],
                "capital": (
                    float(properties["capitalLongitude"]),
                    float(properties["capitalLatitude"]),
                ),
                "geometry": geometry,
                "bbox": (
                    min(coordinate[0] for coordinate in all_coordinates),
                    min(coordinate[1] for coordinate in all_coordinates),
                    max(coordinate[0] for coordinate in all_coordinates),
                    max(coordinate[1] for coordinate in all_coordinates),
                ),
            }
        )
    return provinces


def assign_province(candidate: dict[str, Any], provinces: list[dict[str, Any]]) -> str | None:
    point = (candidate["longitude"], candidate["latitude"])
    for province in provinces:
        west, south, east, north = province["bbox"]
        if (
            west <= point[0] <= east
            and south <= point[1] <= north
            and geometry_covers(point, province["geometry"])
        ):
            return province["name"]
    return None


def approximate_km(
    first: tuple[float, float], second: tuple[float, float]
) -> float:
    mean_latitude = math.radians((first[1] + second[1]) / 2)
    x = (first[0] - second[0]) * 111.32 * math.cos(mean_latitude)
    y = (first[1] - second[1]) * 110.57
    return math.hypot(x, y)


def score(
    candidate: dict[str, Any],
    category: str,
    capital: tuple[float, float],
) -> float:
    name = folded(candidate["name"])
    value = -approximate_km(
        (candidate["longitude"], candidate["latitude"]), capital
    )
    if category == "Hospital":
        for token, bonus in [
            ("sehir hastanesi", 120),
            ("egitim ve arastirma", 100),
            ("universite", 90),
            ("tip fakultesi", 80),
            ("devlet hastanesi", 70),
            ("hastanesi", 40),
            ("hospital", 35),
        ]:
            if token in name:
                value += bonus
    elif category == "Mall":
        for token, bonus in [
            ("alisveris merkezi", 80),
            (" avm", 70),
            ("avm ", 70),
            ("forum", 60),
            ("outlet", 50),
            ("carsi", 35),
            ("is hani", 30),
        ]:
            if token in f" {name} ":
                value += bonus
    elif category == "Restaurant":
        value += 20
    return round(value, 3)


def build_report(
    candidates: dict[str, dict[str, Any]], provinces: list[dict[str, Any]]
) -> dict[str, Any]:
    by_province: dict[str, dict[str, list[dict[str, Any]]]] = defaultdict(
        lambda: defaultdict(list)
    )
    province_lookup = {province["name"]: province for province in provinces}
    for candidate in candidates.values():
        province_name = candidate.get("province")
        if not province_name:
            continue
        for category in candidate["categories"]:
            public = {
                key: candidate[key]
                for key in [
                    "sourceId",
                    "name",
                    "categories",
                    "longitude",
                    "latitude",
                    "geometrySource",
                ]
            }
            public["score"] = score(
                candidate, category, province_lookup[province_name]["capital"]
            )
            public["tags"] = dict(sorted(candidate["tags"].items()))
            by_province[province_name][category].append(public)

    for category_groups in by_province.values():
        for rows in category_groups.values():
            rows.sort(key=lambda row: (-row["score"], row["name"], row["sourceId"]))
    return {
        "provinces": {
            province["name"]: by_province[province["name"]]
            for province in provinces
        }
    }


def load_pois(path: Path) -> dict[str, Any]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if document.get("type") != "FeatureCollection" or len(
        document.get("features", [])
    ) != EXPECTED_POIS:
        raise ValueError("POI fixture must contain exactly 324 features.")
    return document


def apply_selections(
    document: dict[str, Any],
    selection_path: Path,
    candidates: dict[str, dict[str, Any]],
    snapshot_date: str,
) -> None:
    manifest = json.loads(selection_path.read_text(encoding="utf-8"))
    if manifest.get("snapshot") != snapshot_date:
        raise ValueError(
            "Selection manifest snapshot must match the explicitly supplied PBF snapshot date."
        )
    selections = manifest.get("selections")
    if not isinstance(selections, list):
        raise ValueError("Selection manifest must contain a selections array.")

    baselines = document["features"][:BASELINE_ROWS]
    replaced: set[tuple[str, ...]] = set()
    for selection in selections:
        province = selection["province"]
        current_category = selection["currentCategory"]
        replacement_category = selection["replacementCategory"]
        replaces_source_id = selection.get("replacesSourceId")
        key = (
            (province, current_category, replaces_source_id)
            if replaces_source_id
            else (province, current_category)
        )
        if key in replaced:
            raise ValueError(f"Duplicate replacement selector {key!r}.")
        replaced.add(key)
        replacement_source_id = selection["sourceId"]
        source_features = document["features"] if replaces_source_id else baselines
        matches = [
            feature
            for feature in source_features
            if feature["properties"]["province"] == province
            and feature["properties"]["category"]
            in {current_category, replacement_category}
            and (
                replaces_source_id is None
                or feature["properties"]["sourceId"]
                in {replaces_source_id, replacement_source_id}
            )
        ]
        if len(matches) != 1:
            raise ValueError(f"Expected one baseline row for {key!r}; found {len(matches)}.")
        candidate = candidates.get(replacement_source_id)
        if candidate is None:
            raise ValueError(f"Replacement {replacement_source_id} is absent from the locked PBF.")
        if candidate.get("province") != province:
            raise ValueError(
                f"Replacement {replacement_source_id} belongs to "
                f"{candidate.get('province')!r}, not {province!r}."
            )
        if replacement_category not in candidate["categories"]:
            raise ValueError(
                f"Replacement {replacement_source_id} validates as "
                f"{candidate['categories']}, not {replacement_category}."
            )
        if candidate["name"] != selection["expectedName"]:
            raise ValueError(
                f"Replacement name changed: expected {selection['expectedName']!r}, "
                f"found {candidate['name']!r}."
            )
        if not isinstance(selection.get("reviewNote"), str) or not selection["reviewNote"].strip():
            raise ValueError(f"Replacement {replacement_source_id} needs a reviewNote.")
        if "sehir hastanesi" in folded(selection["expectedName"]):
            verification_url = selection.get("verificationUrl")
            if (
                not isinstance(verification_url, str)
                or not verification_url.startswith("https://khgm.saglik.gov.tr/")
            ):
                raise ValueError(
                    f"City-hospital replacement {selection['sourceId']} needs an official "
                    "Ministry verificationUrl."
                )

        feature = matches[0]
        properties = feature["properties"]
        properties["name"] = candidate["name"]
        properties["category"] = replacement_category
        properties["sourceKey"] = "openstreetmap"
        properties["sourceId"] = replacement_source_id
        properties["capturedAt"] = snapshot_date
        properties["geometrySource"] = candidate["geometrySource"]
        feature["geometry"] = {
            "type": "Point",
            "coordinates": [candidate["longitude"], candidate["latitude"]],
        }


def refresh_reviewed_representative_points(
    document: dict[str, Any],
    candidates: dict[str, dict[str, Any]],
    snapshot_date: str,
) -> None:
    """Resolve every reviewed coordinate from its exact source object.

    Nodes keep their mapped coordinate. Closed ways use a centroid only after it
    is verified inside the source footprint; a source boundary vertex is the
    deterministic fallback for a concave footprint. Open ways use an actual way
    vertex. This never invents a point outside the referenced OSM geometry.
    """

    problems: list[str] = []
    for feature in document["features"]:
        properties = feature["properties"]
        candidate = candidates.get(properties["sourceId"])
        if candidate is None:
            problems.append(
                f"{properties['province']}: {properties['sourceId']} is absent or "
                "semantically rejected"
            )
            continue
        if candidate.get("province") != properties["province"]:
            problems.append(
                f"{properties['province']}: {properties['sourceId']} maps to "
                f"{candidate.get('province')!r}"
            )
            continue
        if candidate["name"] != properties["name"]:
            problems.append(
                f"{properties['province']}: {properties['sourceId']} is named "
                f"{candidate['name']!r}, not {properties['name']!r}"
            )
            continue
        if properties["category"] not in candidate["categories"]:
            problems.append(
                f"{properties['province']}: {properties['name']} validates as "
                f"{candidate['categories']}, not {properties['category']}"
            )
            continue
        properties["capturedAt"] = snapshot_date
        properties["geometrySource"] = candidate["geometrySource"]
        feature["geometry"] = {
            "type": "Point",
            "coordinates": [candidate["longitude"], candidate["latitude"]],
        }
    if problems:
        raise ValueError(
            "Reviewed POI source validation failed:\n- " + "\n- ".join(problems)
        )


def validate_baselines(
    document: dict[str, Any],
    candidates: dict[str, dict[str, Any]],
) -> None:
    baselines = document["features"][:BASELINE_ROWS]
    by_province: dict[str, list[dict[str, Any]]] = defaultdict(list)
    for feature in baselines:
        by_province[feature["properties"]["province"]].append(feature)
    if len(by_province) != EXPECTED_PROVINCES or any(
        len(features) != 3 for features in by_province.values()
    ):
        raise ValueError("The first 243 POIs must be three baseline rows per province.")

    problems = []
    for province, features in by_province.items():
        categories = [feature["properties"]["category"] for feature in features]
        if categories.count("Hospital") != 1:
            problems.append(f"{province}: baseline must contain one hospital")
        if not any(category in FOOD_CATEGORIES for category in categories):
            problems.append(f"{province}: baseline must contain one food venue")
        if not any(category in SHOPPING_CATEGORIES for category in categories):
            problems.append(f"{province}: baseline must contain one shopping venue")
        for feature in features:
            properties = feature["properties"]
            candidate = candidates.get(properties["sourceId"])
            if candidate is None:
                problems.append(
                    f"{province}: {properties['sourceId']} is not an active semantic PBF candidate"
                )
                continue
            if candidate.get("province") != province:
                problems.append(
                    f"{province}: {properties['sourceId']} maps to {candidate.get('province')}"
                )
            if properties["category"] not in candidate["categories"]:
                problems.append(
                    f"{province}: {properties['name']} is tagged/validated as "
                    f"{candidate['categories']}, not {properties['category']}"
                )
            if candidate["name"] != properties["name"]:
                problems.append(
                    f"{province}: {properties['sourceId']} is named "
                    f"{candidate['name']!r}, not {properties['name']!r}"
                )
            expected_point = [candidate["longitude"], candidate["latitude"]]
            if (
                feature.get("geometry", {}).get("type") != "Point"
                or feature.get("geometry", {}).get("coordinates") != expected_point
            ):
                problems.append(
                    f"{province}: {properties['sourceId']} does not use its validated "
                    "source representative point"
                )
            if properties.get("geometrySource") != candidate["geometrySource"]:
                problems.append(
                    f"{province}: {properties['sourceId']} has stale geometry provenance"
                )
    if problems:
        raise ValueError("Baseline semantic validation failed:\n- " + "\n- ".join(problems))

    source_ids = [
        feature["properties"]["sourceId"] for feature in document["features"]
    ]
    if len(set(source_ids)) != EXPECTED_POIS:
        raise ValueError("POI source ids must remain unique after replacements.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--pbf", type=Path, required=True)
    parser.add_argument("--snapshot-date", required=True)
    parser.add_argument("--provinces", type=Path, required=True)
    parser.add_argument("--pois", type=Path, required=True)
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument("--selections", type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()

    if args.selections is not None and args.output is None:
        parser.error("--output is required with --selections.")
    if args.output is not None and args.selections is None:
        parser.error("--selections is required with --output.")
    try:
        parsed_snapshot = dt.date.fromisoformat(args.snapshot_date)
    except ValueError:
        parser.error("--snapshot-date must use YYYY-MM-DD.")
    if parsed_snapshot.isoformat() != args.snapshot_date:
        parser.error("--snapshot-date must use canonical YYYY-MM-DD.")
    for path in [args.pbf, args.provinces, args.pois]:
        if not path.is_file():
            parser.error(f"Input does not exist: {path}")
    if args.output and args.output.resolve() == args.pois.resolve():
        parser.error("--output must differ from --pois; review before replacement.")

    document = load_pois(args.pois)
    provinces = load_provinces(args.provinces)
    handler = CandidateHandler()
    tag_filter = osmium.filter.TagFilter(
        ("amenity", "hospital"),
        ("amenity", "restaurant"),
        ("amenity", "cafe"),
        ("amenity", "fast_food"),
        ("amenity", "pharmacy"),
        ("amenity", "bus_station"),
        ("amenity", "ferry_terminal"),
        ("amenity", "university"),
        ("amenity", "library"),
        ("amenity", "school"),
        ("amenity", "bank"),
        ("amenity", "fuel"),
        ("amenity", "post_office"),
        ("amenity", "townhall"),
        ("healthcare", "hospital"),
        ("shop", "bakery"),
        ("shop", "mall"),
        ("shop", "supermarket"),
        ("tourism", "museum"),
        ("tourism", "gallery"),
        ("tourism", "information"),
        ("tourism", "visitor_centre"),
        ("tourism", "hotel"),
        ("tourism", "motel"),
        ("historic", "archaeological_site"),
        ("historic", "building"),
        ("historic", "castle"),
        ("historic", "citywalls"),
        ("historic", "church"),
        ("historic", "fort"),
        ("historic", "manor"),
        ("historic", "memorial"),
        ("historic", "monument"),
        ("historic", "mosque"),
        ("historic", "ruins"),
        ("historic", "tomb"),
        ("historic", "yes"),
        ("aeroway", "aerodrome"),
        ("railway", "station"),
        ("railway", "halt"),
        ("public_transport", "station"),
        ("office", "government"),
        ("boundary", "national_park"),
        ("natural", "beach"),
        ("leisure", "beach_resort"),
        ("leisure", "park"),
        ("leisure", "garden"),
        ("leisure", "stadium"),
        ("leisure", "fitness_centre"),
        ("leisure", "sports_centre"),
        ("landuse", "winter_sports"),
        ("sport", "skiing"),
    )
    handler.apply_file(str(args.pbf), locations=True, filters=[tag_filter])

    fixture_source_ids = {
        feature["properties"]["sourceId"] for feature in document["features"]
    }
    fixture_handler = FixtureSourceHandler(fixture_source_ids)
    fixture_numeric_ids = [
        int(source_id.split("/", maxsplit=1)[1])
        for source_id in fixture_source_ids
        if source_id.startswith(("node/", "way/"))
    ]
    fixture_handler.apply_file(
        str(args.pbf),
        locations=True,
        filters=[osmium.filter.IdFilter(fixture_numeric_ids)],
    )
    handler.candidates.update(fixture_handler.candidates)

    for candidate in handler.candidates.values():
        candidate["province"] = assign_province(candidate, provinces)

    report = build_report(handler.candidates, provinces)
    report["fixtureSources"] = {
        source_id: {
            **raw_source,
            "categories": handler.candidates.get(source_id, {}).get("categories", []),
        }
        for source_id, raw_source in sorted(fixture_handler.raw_sources.items())
    }
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(
        json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )

    if args.selections is not None:
        if any(
            feature["properties"].get("capturedAt") != args.snapshot_date
            for feature in document["features"]
        ):
            raise ValueError(
                "Every reviewed POI capture date must match --snapshot-date; "
                "review all selections before advancing the snapshot."
            )
        apply_selections(
            document, args.selections, handler.candidates, args.snapshot_date
        )
        refresh_reviewed_representative_points(
            document, handler.candidates, args.snapshot_date
        )
        validate_baselines(document, handler.candidates)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(
            json.dumps(document, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        print(
            f"Validated 243 province baseline POIs and wrote {len(document['features'])} "
            f"reviewed POIs to {args.output}"
        )
    print(
        f"Wrote {len(handler.candidates)} active semantic OSM candidates to {args.report}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
