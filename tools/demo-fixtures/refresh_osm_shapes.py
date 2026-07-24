#!/usr/bin/env python3
"""Discover and refresh source-backed private demo drawing scenarios.

The committed fixture deliberately separates the illustrative planning purpose
from factual OpenStreetMap place names and geometry.  This tool reads only a
locked local Turkey PBF; it never calls Overpass or another live service.

`discover-polygons` lists eligible real facility/precinct polygons with their
province so the reviewed manifest below can be maintained without guessing.
`refresh` replaces only the reviewed line/polygon scenarios and their related
point anchors while preserving fixture ordering, owners, counts and dates.
"""

from __future__ import annotations

import argparse
import json
import math
import sys
import unicodedata
from pathlib import Path
from typing import Any, Iterable

import osmium


EXPECTED_SHAPES = 328
EXPECTED_POINTS = 218
EXPECTED_LINES = 60
EXPECTED_POLYGONS = 50
CAPTURE_DATE = "2026-07-22"

THEMES = {
    "mobility": ("Mobility", "#2563EB"),
    "emergency": ("Emergency", "#DC2626"),
    "tourism": ("Tourism", "#7C3AED"),
    "environment": ("Environment", "#0F766E"),
    "municipal": ("Municipal", "#EA580C"),
}

# Each line slot retains its owner/theme/date from the committed fixture.  The
# reviewed place is an independently tagged OSM facility/site that makes the
# purpose of the nearby source way understandable.
LINE_TARGETS: dict[str, dict[str, str]] = {
    "line-001": {
        "facility": "way/52530393",
        "way": "way/52529999",
        "place": "Ankara Şehirlerarası Otobüs Terminali",
        "purpose": "terminal access",
    },
    "line-002": {
        "facility": "way/724710677",
        "way": "way/814336411",
        "place": "Ankara Bilkent Şehir Hastanesi",
        "purpose": "hospital access",
    },
    "line-003": {
        "facility": "way/335415778",
        "way": "way/303491257",
        "place": "Topkapı Sarayı",
        "purpose": "palace approach",
    },
    "line-005": {
        "facility": "way/1210548358",
        "way": "way/215209582",
        "place": "İSKİ Ataköy Arıtma Tesisi",
        "purpose": "treatment-plant inspection",
    },
    "line-006": {
        "facility": "way/45345232",
        "way": "way/45345142",
        "place": "Esenler Otogarı",
        "purpose": "terminal access",
    },
    "line-007": {
        "facility": "way/793003829",
        "way": "way/1365693385",
        "place": "Çam ve Sakura Şehir Hastanesi",
        "purpose": "hospital access",
    },
    "line-008": {
        "facility": "way/244582409",
        "way": "way/372695067",
        "place": "Efes",
        "purpose": "archaeological walk",
    },
    "line-010": {
        "facility": "way/403547979",
        "way": "way/522933601",
        "place": "Bursa Büyükşehir Belediyesi",
        "purpose": "municipal-campus access",
    },
    "line-011": {
        "facility": "relation/6427049",
        "way": "way/228050628",
        "place": "Antalya Şehirlerarası Terminali",
        "purpose": "terminal access",
    },
    "line-012": {
        "facility": "way/37190471",
        "way": "way/153815945",
        "place": "Ege Üniversitesi Hastanesi",
        "purpose": "hospital access",
    },
    "line-013": {
        "facility": "way/26045752",
        "way": "way/33347530",
        "place": "Göreme Açık Hava Müzesi",
        "purpose": "museum approach",
    },
    "line-015": {
        "facility": "way/461214303",
        "way": "way/462410780",
        "place": "Adana Büyükşehir Belediyesi",
        "purpose": "municipal-campus access",
    },
    "line-016": {
        "facility": "way/1154800003",
        "way": "way/849097557",
        "place": "Adana Merkez Otogarı",
        "purpose": "terminal access",
    },
    "line-017": {
        "facility": "way/330200468",
        "way": "way/213101356",
        "place": "Adana Şehir Hastanesi",
        "purpose": "hospital access",
    },
    "line-018": {
        "facility": "way/856740625",
        "way": "way/1222695585",
        "place": "Sümela Manastırı",
        "purpose": "monastery approach",
    },
    "line-020": {
        "facility": "way/491204246",
        "way": "way/466222874",
        "place": "Trabzon Büyükşehir Belediyesi Teknik Hizmet Birimleri",
        "purpose": "service-campus inspection",
    },
    "line-021": {
        "facility": "way/191171805",
        "way": "way/438127116",
        "place": "Bursa Şehirlerarası Terminali",
        "purpose": "terminal access",
    },
    "line-022": {
        "facility": "way/632460745",
        "way": "way/882126986",
        "place": "Bursa Şehir Hastanesi",
        "purpose": "hospital access",
    },
    "line-023": {
        "facility": "relation/1558808",
        "way": "way/28351468",
        "place": "Süleymaniye İmareti",
        "purpose": "heritage approach",
    },
    "line-025": {
        "facility": "way/532041985",
        "way": "way/222402071",
        "place": "Çiğli Atıksu Arıtma Tesisleri",
        "purpose": "treatment-plant inspection",
    },
    "line-026": {
        "facility": "way/303623292",
        "way": "way/372063086",
        "place": "Denizli Otobüs Terminali",
        "purpose": "terminal access",
    },
    "line-027": {
        "facility": "way/617806848",
        "way": "way/617806860",
        "place": "Muğla Eğitim ve Araştırma Hastanesi",
        "purpose": "hospital access",
    },
    "line-028": {
        "facility": "relation/2317446",
        "way": "way/1089388437",
        "place": "Aizanoi",
        "purpose": "archaeological walk",
    },
    "line-030": {
        "facility": "way/190557474",
        "way": "way/212331065",
        "place": "Mersin Sebze ve Meyve Hali",
        "purpose": "market inspection",
    },
    "line-031": {
        "facility": "way/214629500",
        "way": "way/1141173480",
        "place": "Hatay Havalimanı Terminali",
        "purpose": "airport access",
    },
    "line-032": {
        "facility": "way/1302089690",
        "way": "way/1309911512",
        "place": "Antalya Şehir Hastanesi",
        "purpose": "hospital access",
    },
    "line-033": {
        "facility": "way/192245191",
        "way": "way/1275338402",
        "place": "Çatalhöyük",
        "purpose": "archaeological walk",
    },
    "line-035": {
        "facility": "way/308850327",
        "way": "way/217431485",
        "place": "Konya Atıksu Arıtma Tesisleri",
        "purpose": "treatment-plant inspection",
    },
    "line-036": {
        "facility": "way/124431920",
        "way": "way/276176988",
        "place": "Eskişehir Otobüs Terminali",
        "purpose": "terminal access",
    },
    "line-037": {
        "facility": "way/181509819",
        "way": "way/499501207",
        "place": "OMÜ Tıp Fakültesi Hastanesi",
        "purpose": "hospital access",
    },
    "line-038": {
        "facility": "relation/1598209",
        "way": "way/1018366804",
        "place": "Hattuşaş",
        "purpose": "archaeological walk",
    },
    "line-040": {
        "facility": "way/554341636",
        "way": "way/212327315",
        "place": "İlkadım Belediyesi Fen İşleri",
        "purpose": "service-yard inspection",
    },
    "line-041": {
        "facility": "way/468233464",
        "way": "way/373459871",
        "place": "Erzurum Otobüs Terminali",
        "purpose": "terminal access",
    },
    "line-042": {
        "facility": "way/702424713",
        "way": "way/702424715",
        "place": "Elazığ Fethi Sekin Şehir Hastanesi",
        "purpose": "hospital access",
    },
    "line-043": {
        "facility": "way/157441696",
        "way": "way/102489811",
        "place": "Ani",
        "purpose": "archaeological walk",
    },
    "line-045": {
        "facility": "way/840420375",
        "way": "way/1140681449",
        "place": "Şanlıurfa Atıksu Arıtma Tesisi",
        "purpose": "treatment-plant inspection",
    },
    "line-046": {
        "facility": "way/128186929",
        "way": "way/1484888246",
        "place": "Gaziantep Otogarı",
        "purpose": "terminal access",
    },
    "line-047": {
        "facility": "way/1067214692",
        "way": "way/1143680839",
        "place": "Gaziantep Şehir Hastanesi",
        "purpose": "hospital access",
    },
    "line-048": {
        "facility": "way/124331302",
        "way": "way/747778366",
        "place": "Göbekli Tepe",
        "purpose": "archaeological walk",
    },
    "line-050": {
        "facility": "relation/6266815",
        "way": "way/691906770",
        "place": "Yenimahalle Belediye Başkanlığı",
        "purpose": "municipal-campus access",
    },
    "line-051": {
        "facility": "way/436371743",
        "way": "way/1520882932",
        "place": "Ankara Lojistik Üssü TIR Parkı",
        "purpose": "logistics access",
    },
    "line-052": {
        "facility": "way/451822397",
        "way": "way/903047736",
        "place": "Ankara Etlik Şehir Hastanesi",
        "purpose": "hospital access",
    },
    "line-053": {
        "facility": "way/109862851",
        "way": "way/1147323572",
        "place": "Ayasofya",
        "purpose": "heritage approach",
    },
    "line-055": {
        "facility": "way/837292510",
        "way": "way/38609611",
        "place": "Bornova Evka 4 Pazar Yeri",
        "purpose": "market inspection",
    },
    "line-056": {
        "facility": "way/739787803",
        "way": "way/31999884",
        "place": "Adnan Menderes Havalimanı Terminali",
        "purpose": "airport access",
    },
    "line-057": {
        "facility": "way/495124232",
        "way": "way/894439064",
        "place": "ALKÜ Alanya Eğitim Araştırma Hastanesi",
        "purpose": "hospital access",
    },
    "line-058": {
        "facility": "way/435948613",
        "way": "way/623953012",
        "place": "Antalya Arkeoloji Müzesi",
        "purpose": "museum approach",
    },
    "line-060": {
        "facility": "way/376957660",
        "way": "way/216726701",
        "place": "Trabzon Büyükşehir Belediyesi",
        "purpose": "municipal-campus access",
    },
}

POLYGON_REPLACEMENTS = {
    "polygon-005": "way/1210548358",
    "polygon-025": "way/532041985",
    "polygon-030": "way/190557474",
    "polygon-036": "way/52529691",
    "polygon-040": "way/786490094",
    "polygon-042": "way/702424713",
    "polygon-046": "way/128186929",
}

POLYGON_PURPOSES = {
    "polygon-001": "shipyard logistics precinct",
    "polygon-002": "emergency-care grounds",
    "polygon-003": "heritage-site review",
    "polygon-004": "national-park survey",
    "polygon-005": "treatment-plant grounds",
    "polygon-006": "airport-terminal precinct",
    "polygon-007": "hospital campus",
    "polygon-008": "heritage-site review",
    "polygon-009": "national-park survey",
    "polygon-010": "municipal campus",
    "polygon-011": "industrial-logistics precinct",
    "polygon-012": "hospital campus",
    "polygon-013": "heritage-site review",
    "polygon-014": "lagoon survey",
    "polygon-015": "market grounds",
    "polygon-016": "airport-terminal precinct",
    "polygon-017": "hospital campus",
    "polygon-018": "heritage-site review",
    "polygon-019": "crater-lake survey",
    "polygon-020": "municipal campus",
    "polygon-021": "vehicle-logistics precinct",
    "polygon-022": "hospital campus",
    "polygon-023": "heritage-site review",
    "polygon-024": "reservoir survey",
    "polygon-025": "treatment-plant grounds",
    "polygon-026": "airport-terminal precinct",
    "polygon-027": "hospital campus",
    "polygon-028": "archaeological-site review",
    "polygon-029": "lake survey",
    "polygon-030": "wholesale-market grounds",
    "polygon-031": "industrial-logistics precinct",
    "polygon-032": "hospital campus",
    "polygon-033": "heritage-site review",
    "polygon-034": "Ramsar-site survey",
    "polygon-035": "market grounds",
    "polygon-036": "bus-terminal precinct",
    "polygon-037": "hospital campus",
    "polygon-038": "archaeological-site review",
    "polygon-039": "reservoir survey",
    "polygon-040": "treatment-plant grounds",
    "polygon-041": "airport precinct",
    "polygon-042": "hospital campus",
    "polygon-043": "heritage-site review",
    "polygon-044": "lake survey",
    "polygon-045": "municipal campus",
    "polygon-046": "bus-terminal precinct",
    "polygon-047": "hospital campus",
    "polygon-048": "heritage-site review",
    "polygon-049": "lake survey",
    "polygon-050": "municipal campus",
}

DISPLAY_NAMES = {
    "polygon-005": "İSKİ Ataköy Arıtma Tesisi",
    "polygon-007": "Erciyes Üniversitesi Hastanesi",
    "polygon-013": "Kargı Han Kervansarayı",
    "polygon-017": "Kayseri Eğitim Araştırma Hastanesi",
    "polygon-022": "İstanbul Tıp Fakültesi",
    "polygon-025": "Çiğli Atıksu Arıtma Tesisleri",
    "polygon-030": "Mersin Sebze ve Meyve Hali",
    "polygon-033": "Ağzıkarahan Kervansarayı",
    "polygon-036": "Ankara Şehirlerarası Terminali",
    "polygon-037": "OMÜ Tıp Fakültesi Hastanesi",
    "polygon-040": "Durugöl Atıksu Arıtma Tesisi",
    "polygon-042": "Elazığ Fethi Sekin Şehir Hastanesi",
    "polygon-043": "Hekimhan Kervansarayı",
    "polygon-046": "Gaziantep Otogarı",
    "line-001": "Ankara AŞTİ",
    "line-011": "Antalya Şehirlerarası Terminali",
    "line-020": "Trabzon Belediyesi Teknik Birimleri",
    "line-025": "Çiğli Atıksu Arıtma Tesisleri",
    "line-026": "Denizli Otobüs Terminali",
    "line-027": "Muğla Eğitim Araştırma Hastanesi",
    "line-037": "OMÜ Tıp Fakültesi Hastanesi",
    "line-042": "Elazığ Fethi Sekin Şehir Hastanesi",
    "line-050": "Yenimahalle Belediye Başkanlığı",
    "line-056": "Adnan Menderes Havalimanı",
    "line-057": "ALKÜ Alanya Hastanesi",
}

ENVIRONMENT_PURPOSES = {
    "line-004": "river survey",
    "line-009": "upper-river survey",
    "line-014": "river survey",
    "line-019": "waterway survey",
    "line-024": "river survey",
    "line-029": "stream survey",
    "line-034": "stream survey",
    "line-039": "river survey",
    "line-044": "river survey",
    "line-049": "river survey",
    "line-054": "stream survey",
    "line-059": "lower-river survey",
}

AREA_TAG_KEYS = {
    "name",
    "name:tr",
    "amenity",
    "aeroway",
    "boundary",
    "building",
    "government",
    "historic",
    "industrial",
    "landuse",
    "leisure",
    "man_made",
    "natural",
    "office",
    "operator",
    "public_transport",
    "railway",
    "tourism",
}


def normalized(value: str) -> str:
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


def ring_contains(point: tuple[float, float], ring: list[list[float]]) -> tuple[bool, bool]:
    longitude, latitude = point
    inside = False
    previous = ring[-1]
    for current in ring:
        if point_on_segment(longitude, latitude, previous, current):
            return True, True
        ax, ay = previous
        bx, by = current
        if (ay > latitude) != (by > latitude):
            crossing = (bx - ax) * (latitude - ay) / (by - ay) + ax
            if longitude < crossing:
                inside = not inside
        previous = current
    return inside, False


def polygon_contains(point: tuple[float, float], polygon: list[list[list[float]]]) -> bool:
    outer_inside, outer_boundary = ring_contains(point, polygon[0])
    if not outer_inside:
        return False
    if outer_boundary:
        return True
    return not any(ring_contains(point, hole)[0] for hole in polygon[1:])


def geometry_contains(point: tuple[float, float], geometry: dict[str, Any]) -> bool:
    if geometry["type"] == "Polygon":
        return polygon_contains(point, geometry["coordinates"])
    if geometry["type"] == "MultiPolygon":
        return any(polygon_contains(point, polygon) for polygon in geometry["coordinates"])
    raise ValueError(f"Expected Polygon/MultiPolygon, got {geometry['type']!r}.")


def ring_area(ring: list[list[float]]) -> float:
    return abs(
        sum(
            first[0] * second[1] - second[0] * first[1]
            for first, second in zip(ring, ring[1:])
        )
        / 2
    )


def largest_polygon(geometry: dict[str, Any]) -> dict[str, Any]:
    if geometry["type"] == "Polygon":
        return geometry
    if geometry["type"] != "MultiPolygon":
        raise ValueError(f"Expected polygonal geometry, got {geometry['type']!r}.")
    polygons = geometry["coordinates"]
    if not polygons:
        raise ValueError("OSM area contains no polygon component.")
    coordinates = max(polygons, key=lambda polygon: ring_area(polygon[0]))
    return {"type": "Polygon", "coordinates": coordinates}


def polygon_representative_point(polygon: dict[str, Any]) -> tuple[float, float]:
    """Return a deterministic interior point without inventing polygon geometry."""

    rings = polygon["coordinates"]
    outer = rings[0]
    signed_twice_area = sum(
        first[0] * second[1] - second[0] * first[1]
        for first, second in zip(outer, outer[1:])
    )
    if abs(signed_twice_area) > 1e-16:
        longitude = sum(
            (first[0] + second[0])
            * (first[0] * second[1] - second[0] * first[1])
            for first, second in zip(outer, outer[1:])
        ) / (3 * signed_twice_area)
        latitude = sum(
            (first[1] + second[1])
            * (first[0] * second[1] - second[0] * first[1])
            for first, second in zip(outer, outer[1:])
        ) / (3 * signed_twice_area)
        if polygon_contains((longitude, latitude), rings):
            return longitude, latitude

    min_y = min(coordinate[1] for coordinate in outer)
    max_y = max(coordinate[1] for coordinate in outer)
    for fraction in (0.5, 0.4, 0.6, 0.3, 0.7, 0.2, 0.8):
        latitude = min_y + (max_y - min_y) * fraction
        intersections: list[float] = []
        for first, second in zip(outer, outer[1:]):
            if (first[1] > latitude) == (second[1] > latitude):
                continue
            intersections.append(
                first[0]
                + (latitude - first[1])
                * (second[0] - first[0])
                / (second[1] - first[1])
            )
        intersections.sort()
        candidates = [
            ((first + second) / 2, latitude)
            for first, second in zip(intersections[::2], intersections[1::2])
        ]
        for candidate in sorted(candidates, key=lambda item: -abs(item[0])):
            if polygon_contains(candidate, rings):
                return candidate
    raise ValueError("Could not derive an interior point for an OSM polygon component.")


def classify_area(tags: dict[str, str]) -> list[str]:
    name = tags.get("name:tr") or tags.get("name") or ""
    folded_name = normalized(name)
    classes: set[str] = set()

    if (
        tags.get("aeroway") in {"aerodrome", "apron", "terminal"}
        or tags.get("amenity") in {"bus_station", "parking"}
        or tags.get("public_transport") == "station"
        or tags.get("railway") in {"station", "depot", "yard"}
        or tags.get("landuse") in {"railway", "port"}
        or tags.get("industrial") in {"logistics", "port"}
        or any(
            token in folded_name
            for token in ("havalimani", "terminal", "otogar", "lojistik", "liman", "garaji")
        )
    ):
        classes.add("mobility")

    if (
        tags.get("amenity") in {"hospital", "fire_station"}
        or any(token in folded_name for token in ("hastane", "acil servis", "itfaiye"))
    ):
        classes.add("emergency")

    if (
        tags.get("tourism") in {"museum", "attraction", "gallery"}
        or tags.get("historic")
        in {
            "archaeological_site",
            "building",
            "castle",
            "city_gate",
            "fort",
            "monument",
            "ruins",
            "yes",
        }
        or any(
            token in folded_name
            for token in ("muzesi", "muzesi", "sarayi", "kalesi", "antik", "oren yeri", "han")
        )
    ):
        classes.add("tourism")

    if (
        tags.get("leisure") in {"park", "nature_reserve", "recreation_ground"}
        or tags.get("boundary") in {"national_park", "protected_area"}
        or tags.get("natural") in {"water", "wetland", "wood", "beach"}
        or tags.get("landuse") == "forest"
    ):
        classes.add("environment")

    if (
        tags.get("amenity")
        in {
            "townhall",
            "marketplace",
            "recycling",
            "waste_transfer_station",
            "sanitary_dump_station",
        }
        or tags.get("man_made") in {"wastewater_plant", "water_works"}
        or tags.get("government") in {"local", "municipality"}
        or any(
            token in folded_name
            for token in (
                "belediyesi",
                "belediye baskanligi",
                "pazar yeri",
                "su aritma",
                "atik su",
                "atik aktarma",
                "hal kompleksi",
            )
        )
    ):
        classes.add("municipal")

    return sorted(classes)


def geometry_bbox(geometry: dict[str, Any]) -> tuple[float, float, float, float]:
    polygons = (
        [geometry["coordinates"]]
        if geometry["type"] == "Polygon"
        else geometry["coordinates"]
    )
    coordinates = [
        coordinate
        for polygon in polygons
        for ring in polygon
        for coordinate in ring
    ]
    return (
        min(coordinate[0] for coordinate in coordinates),
        min(coordinate[1] for coordinate in coordinates),
        max(coordinate[0] for coordinate in coordinates),
        max(coordinate[1] for coordinate in coordinates),
    )


def province_for_point(
    point: tuple[float, float],
    province_features: Iterable[tuple[dict[str, Any], tuple[float, float, float, float]]],
) -> str | None:
    longitude, latitude = point
    for feature, bbox in province_features:
        if not (bbox[0] <= longitude <= bbox[2] and bbox[1] <= latitude <= bbox[3]):
            continue
        if geometry_contains(point, feature["geometry"]):
            return feature["properties"]["name"]
    return None


class AreaDiscoveryHandler(osmium.SimpleHandler):
    def __init__(
        self,
        province_features: list[
            tuple[dict[str, Any], tuple[float, float, float, float]]
        ],
    ) -> None:
        super().__init__()
        self.factory = osmium.geom.GeoJSONFactory()
        self.province_features = province_features
        self.candidates: list[dict[str, Any]] = []

    def area(self, area: osmium.osm.Area) -> None:
        tags = {key: area.tags.get(key) for key in AREA_TAG_KEYS if area.tags.get(key)}
        name = tags.get("name:tr") or tags.get("name")
        if not name:
            return
        classes = classify_area(tags)
        if not classes:
            return
        try:
            geometry = largest_polygon(json.loads(self.factory.create_multipolygon(area)))
            point = polygon_representative_point(geometry)
        except (RuntimeError, ValueError):
            return
        province = province_for_point(point, self.province_features)
        if province is None:
            return
        self.candidates.append(
            {
                "sourceId": f"{'way' if area.from_way() else 'relation'}/{area.orig_id()}",
                "name": name,
                "province": province,
                "classes": classes,
                "point": [round(point[0], 7), round(point[1], 7)],
                "tags": tags,
            }
        )


class TargetAreaHandler(osmium.SimpleHandler):
    def __init__(self, target_source_ids: set[str]) -> None:
        super().__init__()
        self.target_source_ids = target_source_ids
        self.factory = osmium.geom.GeoJSONFactory()
        self.matches: dict[str, dict[str, Any]] = {}

    def area(self, area: osmium.osm.Area) -> None:
        source_id = f"{'way' if area.from_way() else 'relation'}/{area.orig_id()}"
        if source_id not in self.target_source_ids:
            return
        geometry = largest_polygon(json.loads(self.factory.create_multipolygon(area)))
        self.matches[source_id] = {
            "geometry": geometry,
            "point": polygon_representative_point(geometry),
            "name": area.tags.get("name:tr") or area.tags.get("name"),
            "tags": {tag.k: tag.v for tag in area.tags},
        }


def approximate_distance_m(
    point: tuple[float, float],
    first: tuple[float, float],
    second: tuple[float, float],
) -> float:
    """Local equirectangular point-to-segment distance in metres."""

    longitude, latitude = point
    latitude_scale = 111_320.0
    longitude_scale = latitude_scale * math.cos(math.radians(latitude))
    px, py = longitude * longitude_scale, latitude * latitude_scale
    ax, ay = first[0] * longitude_scale, first[1] * latitude_scale
    bx, by = second[0] * longitude_scale, second[1] * latitude_scale
    dx, dy = bx - ax, by - ay
    if dx == 0 and dy == 0:
        return math.hypot(px - ax, py - ay)
    ratio = max(0.0, min(1.0, ((px - ax) * dx + (py - ay) * dy) / (dx * dx + dy * dy)))
    return math.hypot(px - (ax + ratio * dx), py - (ay + ratio * dy))


def line_distance_m(
    point: tuple[float, float], coordinates: list[tuple[float, float]]
) -> float:
    return min(
        approximate_distance_m(point, first, second)
        for first, second in zip(coordinates, coordinates[1:])
    )


def way_is_eligible(theme: str, tags: dict[str, str]) -> bool:
    highway = tags.get("highway")
    if not highway:
        return False
    if tags.get("proposed") or tags.get("construction") or highway in {
        "construction",
        "proposed",
    }:
        return False
    if theme == "tourism":
        return highway in {"pedestrian", "footway", "path", "steps", "track"}
    if theme in {"mobility", "emergency"}:
        return highway in {
            "service",
            "living_street",
            "residential",
            "unclassified",
            "tertiary",
            "secondary",
            "primary",
            "trunk",
        }
    if theme == "municipal":
        return highway in {
            "service",
            "track",
            "path",
            "living_street",
            "residential",
            "unclassified",
            "tertiary",
        }
    return False


def line_candidate_score(theme: str, tags: dict[str, str], distance_m: float) -> float:
    highway = tags.get("highway")
    service = tags.get("service")
    name = normalized(tags.get("name", ""))
    score = distance_m
    if theme == "tourism":
        score += {
            "pedestrian": -28,
            "footway": -22,
            "path": -16,
            "steps": -12,
            "track": 8,
        }.get(highway, 0)
    else:
        if highway == "service":
            score -= 18
        if service in {"driveway", "emergency_access", "parking_aisle"}:
            score -= 8
    keywords = {
        "mobility": ("terminal", "otogar", "havalimani", "lojistik", "garaj"),
        "emergency": ("hastane", "acil", "itfaiye"),
        "tourism": ("muze", "antik", "saray", "kale", "tarihi", "promenad"),
        "municipal": ("belediye", "aritma", "pazar", "hal", "tesis"),
    }[theme]
    if any(keyword in name for keyword in keywords):
        score -= 24
    if not tags.get("name"):
        score += 4
    return score


class NearestLineHandler(osmium.SimpleHandler):
    def __init__(
        self,
        targets: dict[str, tuple[str, tuple[float, float]]],
        limit: int = 12,
    ) -> None:
        super().__init__()
        self.targets = targets
        self.limit = limit
        self.candidates: dict[str, list[dict[str, Any]]] = {
            scenario_id: [] for scenario_id in targets
        }

    def way(self, way: osmium.osm.Way) -> None:
        tags = {tag.k: tag.v for tag in way.tags}
        matching_themes = {
            theme for theme, _ in self.targets.values() if way_is_eligible(theme, tags)
        }
        if not matching_themes:
            return
        try:
            nodes = [
                (node.ref, node.lon, node.lat)
                for node in way.nodes
                if node.location.valid()
            ]
        except osmium.InvalidLocationError:
            return
        if len(nodes) < 2 or nodes[0][0] == nodes[-1][0]:
            return
        coordinates = [(node[1], node[2]) for node in nodes]
        min_x = min(coordinate[0] for coordinate in coordinates)
        min_y = min(coordinate[1] for coordinate in coordinates)
        max_x = max(coordinate[0] for coordinate in coordinates)
        max_y = max(coordinate[1] for coordinate in coordinates)
        for scenario_id, (theme, point) in self.targets.items():
            if theme not in matching_themes:
                continue
            if not (
                min_x - 0.012 <= point[0] <= max_x + 0.012
                and min_y - 0.012 <= point[1] <= max_y + 0.012
            ):
                continue
            distance = line_distance_m(point, coordinates)
            if distance > 800:
                continue
            candidate = {
                "sourceId": f"way/{way.id}",
                "name": tags.get("name"),
                "distanceMeters": round(distance, 1),
                "score": round(line_candidate_score(theme, tags, distance), 1),
                "nodeIds": [node[0] for node in nodes],
                "coordinates": [
                    [round(coordinate[0], 7), round(coordinate[1], 7)]
                    for coordinate in coordinates
                ],
                "tags": {
                    key: tags[key]
                    for key in (
                        "name",
                        "highway",
                        "service",
                        "footway",
                        "access",
                        "surface",
                    )
                    if key in tags
                },
            }
            candidates = self.candidates[scenario_id]
            candidates.append(candidate)
            candidates.sort(key=lambda item: (item["score"], item["sourceId"]))
            del candidates[self.limit :]


class FixtureSourceHandler(osmium.SimpleHandler):
    def __init__(
        self,
        line_source_ids: set[str],
        area_source_ids: set[str],
    ) -> None:
        super().__init__()
        self.line_source_ids = line_source_ids
        self.area_source_ids = area_source_ids
        self.factory = osmium.geom.GeoJSONFactory()
        self.lines: dict[str, dict[str, Any]] = {}
        self.areas: dict[str, dict[str, Any]] = {}

    def way(self, way: osmium.osm.Way) -> None:
        source_id = f"way/{way.id}"
        if source_id not in self.line_source_ids:
            return
        nodes: list[tuple[int, float, float]] = []
        for node in way.nodes:
            if not node.location.valid():
                raise ValueError(f"{source_id} contains an invalid node location.")
            nodes.append((node.ref, node.lon, node.lat))
        if len(nodes) < 2 or nodes[0][0] == nodes[-1][0]:
            raise ValueError(f"{source_id} is not an open OSM LineString component.")
        self.lines[source_id] = {
            "nodes": nodes,
            "name": way.tags.get("name:tr") or way.tags.get("name"),
            "tags": {tag.k: tag.v for tag in way.tags},
        }

    def area(self, area: osmium.osm.Area) -> None:
        source_id = f"{'way' if area.from_way() else 'relation'}/{area.orig_id()}"
        if source_id not in self.area_source_ids:
            return
        geometry = largest_polygon(json.loads(self.factory.create_multipolygon(area)))
        self.areas[source_id] = {
            "geometry": geometry,
            "point": polygon_representative_point(geometry),
            "name": area.tags.get("name:tr") or area.tags.get("name"),
            "tags": {tag.k: tag.v for tag in area.tags},
        }


def demo_name(theme: str, purpose: str, place: str) -> str:
    value = f"{THEMES[theme][0]} · Demo {purpose} · {place}"
    if len(value) > 80:
        raise ValueError(f"Reviewed demo name exceeds 80 characters: {value!r}.")
    return value


def line_length_m(coordinates: list[list[float]]) -> float:
    total = 0.0
    for first, second in zip(coordinates, coordinates[1:]):
        latitude = (first[1] + second[1]) / 2
        total += math.hypot(
            (second[0] - first[0]) * 111_320 * math.cos(math.radians(latitude)),
            (second[1] - first[1]) * 111_320,
        )
    return total


def all_geometry_coordinates(geometry: dict[str, Any]) -> Iterable[list[float]]:
    if geometry["type"] == "Point":
        yield geometry["coordinates"]
    elif geometry["type"] == "LineString":
        yield from geometry["coordinates"]
    elif geometry["type"] == "Polygon":
        for ring in geometry["coordinates"]:
            yield from ring
    else:
        raise ValueError(f"Unexpected private-shape geometry {geometry['type']!r}.")


def validate_refreshed_fixture(
    document: dict[str, Any], province_path: Path | None
) -> None:
    features = document["features"]
    by_type: dict[str, list[dict[str, Any]]] = {
        shape_type: [
            feature
            for feature in features
            if feature["properties"]["type"] == shape_type
        ]
        for shape_type in ("point", "line", "polygon")
    }
    if len(features) != EXPECTED_SHAPES or {
        shape_type: len(rows) for shape_type, rows in by_type.items()
    } != {"point": EXPECTED_POINTS, "line": EXPECTED_LINES, "polygon": EXPECTED_POLYGONS}:
        raise ValueError("Fixture totals changed from 328 = 218/60/50.")

    names = [feature["properties"]["name"] for feature in features]
    if len(names) != len(set(names)) or any(not 1 <= len(name) <= 80 for name in names):
        raise ValueError("Shape names must be unique and 1..80 characters.")
    sources = [
        (feature["properties"]["sourceKey"], feature["properties"]["sourceId"])
        for feature in features
    ]
    if len(sources) != len(set(sources)):
        raise ValueError("Shape source identities are not unique.")

    feature_keys = {
        feature["properties"].get("featureKey"): feature
        for feature in features
        if feature["properties"].get("featureKey")
    }
    for theme, (_, color) in THEMES.items():
        if sum(
            feature["properties"]["type"] == "line"
            and feature["properties"]["theme"] == theme
            for feature in features
        ) != 12:
            raise ValueError(f"{theme} does not contain exactly 12 lines.")
        if sum(
            feature["properties"]["type"] == "polygon"
            and feature["properties"]["theme"] == theme
            for feature in features
        ) != 10:
            raise ValueError(f"{theme} does not contain exactly 10 polygons.")
        if any(
            feature["properties"]["color"] != color
            for feature in features
            if feature["properties"]["theme"] == theme
        ):
            raise ValueError(f"{theme} contains a mismatched color.")

    for line in by_type["line"]:
        properties = line["properties"]
        coordinates = line["geometry"]["coordinates"]
        if (
            line["geometry"]["type"] != "LineString"
            or len(coordinates) < 2
            or line_length_m(coordinates) < 50
            or properties["sourceId"].split("/", 1)[0] != "way"
        ):
            raise ValueError(f"Invalid reviewed line {properties['scenarioId']}.")
        related = [feature_keys[key] for key in properties["relatedFeatureKeys"]]
        if len(related) != 2:
            raise ValueError(f"{properties['scenarioId']} does not have two anchors.")
        for anchor in related:
            if (
                anchor["properties"]["theme"] != properties["theme"]
                or anchor["properties"]["color"] != properties["color"]
                or not anchor["properties"]["sourceId"].startswith("node/")
            ):
                raise ValueError(f"{properties['scenarioId']} has an invalid real-node anchor.")
            point = anchor["geometry"]["coordinates"]
            if point not in coordinates:
                raise ValueError(f"{properties['scenarioId']} anchor is not on its OSM way.")

    for polygon in by_type["polygon"]:
        properties = polygon["properties"]
        geometry = polygon["geometry"]
        if geometry["type"] != "Polygon" or not geometry["coordinates"]:
            raise ValueError(f"Invalid reviewed polygon {properties['scenarioId']}.")
        for ring in geometry["coordinates"]:
            if len(ring) < 4 or ring[0] != ring[-1]:
                raise ValueError(f"{properties['scenarioId']} contains an invalid ring.")
        related = [feature_keys[key] for key in properties["relatedFeatureKeys"]]
        if (
            len(related) != 1
            or related[0]["properties"]["theme"] != properties["theme"]
            or related[0]["properties"]["color"] != properties["color"]
            or not polygon_contains(
                tuple(related[0]["geometry"]["coordinates"]), geometry["coordinates"]
            )
        ):
            raise ValueError(f"{properties['scenarioId']} lacks a contained site point.")

    expected_owners = {
        "admin": 100,
        "marmara_manager": 21,
        "aegean_manager": 21,
        "mediterranean_manager": 21,
        "central_manager": 21,
        "blacksea_manager": 21,
        "eastern_manager": 21,
        "southeast_manager": 21,
        "ankara_editor": 20,
        "istanbul_editor": 12,
        "izmir_editor": 12,
        "antalya_editor": 12,
        "viewer": 25,
    }
    owner_counts = {
        owner: sum(feature["properties"]["owner"] == owner for feature in features)
        for owner in expected_owners
    }
    if owner_counts != expected_owners:
        raise ValueError(f"Owner distribution changed: {owner_counts!r}.")
    for owner in [
        "marmara_manager",
        "aegean_manager",
        "mediterranean_manager",
        "central_manager",
        "blacksea_manager",
        "eastern_manager",
        "southeast_manager",
        "ankara_editor",
        "istanbul_editor",
        "izmir_editor",
        "antalya_editor",
    ]:
        if len(
            {
                feature["properties"]["theme"]
                for feature in features
                if feature["properties"]["owner"] == owner
            }
        ) < 3:
            raise ValueError(f"{owner} has fewer than three themes.")

    if province_path is None:
        return
    province_features = read_feature_collection(province_path)["features"]
    by_name = {
        feature["properties"]["name"]: feature for feature in province_features
    }
    by_region: dict[str, list[dict[str, Any]]] = {}
    for feature in province_features:
        by_region.setdefault(feature["properties"]["region"], []).append(feature)
    allowed: dict[str, list[dict[str, Any]]] = {
        "marmara_manager": by_region["Marmara"],
        "aegean_manager": by_region["Aegean"],
        "mediterranean_manager": by_region["Mediterranean"],
        "central_manager": by_region["Central Anatolia"],
        "blacksea_manager": by_region["Black Sea"],
        "eastern_manager": by_region["Eastern Anatolia"],
        "southeast_manager": by_region["Southeastern Anatolia"],
        "ankara_editor": [by_name["Ankara"]],
        "istanbul_editor": [by_name["İstanbul"]],
        "izmir_editor": [by_name["İzmir"]],
        "antalya_editor": [by_name["Antalya"]],
    }
    for feature in features:
        owner = feature["properties"]["owner"]
        if owner not in allowed:
            continue
        for coordinate in all_geometry_coordinates(feature["geometry"]):
            if not any(
                geometry_contains(tuple(coordinate), province["geometry"])
                for province in allowed[owner]
            ):
                raise ValueError(
                    f"{feature['properties']['name']} has a vertex outside {owner}'s area."
                )


def refresh_fixture(args: argparse.Namespace) -> None:
    document = read_feature_collection(args.shapes)
    features = document["features"]
    scenarios = {
        feature["properties"]["scenarioId"]: feature
        for feature in features
        if feature["properties"]["type"] in {"line", "polygon"}
    }
    if len([feature for feature in features if feature["properties"]["type"] == "line"]) != 60:
        raise ValueError("Expected 60 committed line slots.")
    if set(POLYGON_PURPOSES) != {
        feature["properties"]["scenarioId"]
        for feature in features
        if feature["properties"]["type"] == "polygon"
    }:
        raise ValueError("Polygon purpose manifest does not exactly cover 50 slots.")

    line_sources = {
        scenario_id: (
            LINE_TARGETS[scenario_id]["way"]
            if scenario_id in LINE_TARGETS
            else scenarios[scenario_id]["properties"]["sourceId"]
        )
        for scenario_id in (
            feature["properties"]["scenarioId"]
            for feature in features
            if feature["properties"]["type"] == "line"
        )
    }
    if any(
        scenario_id not in LINE_TARGETS and scenario_id not in ENVIRONMENT_PURPOSES
        for scenario_id in line_sources
    ):
        raise ValueError("Every non-environment line needs an explicit reviewed target.")
    if len(set(line_sources.values())) != 60:
        raise ValueError("Reviewed line source ways are not unique.")

    polygon_sources = {
        scenario_id: POLYGON_REPLACEMENTS.get(
            scenario_id, feature["properties"]["sourceId"]
        )
        for scenario_id, feature in scenarios.items()
        if feature["properties"]["type"] == "polygon"
    }
    if len(set(polygon_sources.values())) != 50:
        raise ValueError("Reviewed polygon source areas are not unique.")

    facility_sources = {target["facility"] for target in LINE_TARGETS.values()}
    handler = FixtureSourceHandler(
        set(line_sources.values()),
        set(polygon_sources.values()) | facility_sources,
    )
    handler.apply_file(str(args.pbf), locations=True, idx="flex_mem")
    missing_lines = sorted(set(line_sources.values()) - handler.lines.keys())
    missing_areas = sorted(
        (set(polygon_sources.values()) | facility_sources) - handler.areas.keys()
    )
    if missing_lines or missing_areas:
        raise ValueError(
            f"Locked PBF is missing reviewed sources: lines={missing_lines!r}, "
            f"areas={missing_areas!r}."
        )

    points_by_key = {
        feature["properties"].get("featureKey"): feature
        for feature in features
        if feature["properties"].get("featureKey")
    }
    scenario_point_keys = {
        key
        for feature in scenarios.values()
        for key in feature["properties"]["relatedFeatureKeys"]
    }
    used_source_ids = {
        feature["properties"]["sourceId"]
        for feature in features
        if feature["properties"].get("featureKey") not in scenario_point_keys
    }

    for scenario_id, line_feature in sorted(
        (
            (feature["properties"]["scenarioId"], feature)
            for feature in features
            if feature["properties"]["type"] == "line"
        )
    ):
        properties = line_feature["properties"]
        theme = properties["theme"]
        source_id = line_sources[scenario_id]
        source = handler.lines[source_id]
        if scenario_id in LINE_TARGETS:
            target = LINE_TARGETS[scenario_id]
            place = DISPLAY_NAMES.get(scenario_id, target["place"])
            purpose = target["purpose"]
            facility = handler.areas[target["facility"]]
            if theme not in classify_area(facility["tags"]):
                raise ValueError(
                    f"{target['facility']} is not tagged for the {theme} scenario."
                )
            distance = line_distance_m(
                facility["point"],
                [(node[1], node[2]) for node in source["nodes"]],
            )
            if distance > 500:
                raise ValueError(
                    f"{scenario_id} source way is {distance:.1f}m from its real place."
                )
            source_name = target["place"]
            geometry_source = (
                f"Exact OpenStreetMap {source_id} LineString from the locked Geofabrik "
                f"Turkey {CAPTURE_DATE} snapshot; serves {target['facility']} "
                f"({target['place']})"
            )
        else:
            if not (
                source["tags"].get("waterway")
                or source["tags"].get("natural") == "coastline"
                or source["tags"].get("highway")
                in {"path", "footway", "cycleway", "track"}
            ):
                raise ValueError(
                    f"{scenario_id} is not a mapped waterway/coastline/greenway/trail."
                )
            place = source["name"] or properties["sourceName"]
            purpose = ENVIRONMENT_PURPOSES[scenario_id]
            source_name = place
            geometry_source = (
                f"Exact OpenStreetMap {source_id} waterway LineString from the locked "
                f"Geofabrik Turkey {CAPTURE_DATE} snapshot"
            )

        coordinates = [[node[1], node[2]] for node in source["nodes"]]
        line_feature["geometry"] = {"type": "LineString", "coordinates": coordinates}
        properties.update(
            {
                "name": demo_name(theme, purpose, place),
                "sourceKey": "openstreetmap",
                "sourceId": source_id,
                "sourceName": source_name,
                "capturedAt": CAPTURE_DATE,
                "geometrySource": geometry_source,
            }
        )

        anchor_nodes: list[tuple[int, float, float]] = []
        for candidates in (source["nodes"], list(reversed(source["nodes"]))):
            for node in candidates:
                candidate_source_id = f"node/{node[0]}"
                if (
                    candidate_source_id not in used_source_ids
                    and all(existing[0] != node[0] for existing in anchor_nodes)
                ):
                    anchor_nodes.append(node)
                    used_source_ids.add(candidate_source_id)
                    break
        if len(anchor_nodes) != 2:
            raise ValueError(f"Could not find two unique OSM nodes for {scenario_id}.")
        for label, key, node in zip(
            ("start", "end"), properties["relatedFeatureKeys"], anchor_nodes
        ):
            anchor = points_by_key[key]
            source_suffix = (
                "başlangıç düğümü" if label == "start" else "bitiş düğümü"
            )
            anchor["geometry"] = {
                "type": "Point",
                "coordinates": [node[1], node[2]],
            }
            anchor["properties"].update(
                {
                    "name": demo_name(
                        theme, f"corridor {label}", f"{place} · {scenario_id}"
                    ),
                    "sourceKey": "openstreetmap",
                    "sourceId": f"node/{node[0]}",
                    "sourceName": f"{source_name} — {source_suffix}",
                    "capturedAt": CAPTURE_DATE,
                    "geometrySource": f"Exact OpenStreetMap node/{node[0]} on {source_id}",
                }
            )

    for scenario_id, polygon_feature in sorted(
        (
            (feature["properties"]["scenarioId"], feature)
            for feature in features
            if feature["properties"]["type"] == "polygon"
        )
    ):
        properties = polygon_feature["properties"]
        theme = properties["theme"]
        source_id = polygon_sources[scenario_id]
        source = handler.areas[source_id]
        canonical_name = source["name"] or properties["sourceName"]
        display_name = DISPLAY_NAMES.get(scenario_id, canonical_name)
        purpose = POLYGON_PURPOSES[scenario_id]
        polygon_feature["geometry"] = source["geometry"]
        properties.update(
            {
                "name": demo_name(theme, purpose, display_name),
                "sourceKey": "openstreetmap",
                "sourceId": source_id,
                "sourceName": DISPLAY_NAMES.get(scenario_id, canonical_name),
                "capturedAt": CAPTURE_DATE,
                "geometrySource": (
                    f"Exact largest Polygon component of OpenStreetMap {source_id} from "
                    f"the locked Geofabrik Turkey {CAPTURE_DATE} snapshot; no buffer, "
                    "bounding box or synthetic connection"
                ),
            }
        )
        point_key = properties["relatedFeatureKeys"][0]
        site_point = points_by_key[point_key]
        longitude, latitude = source["point"]
        site_point["geometry"] = {
            "type": "Point",
            "coordinates": [longitude, latitude],
        }
        site_point["properties"].update(
            {
                "name": demo_name(theme, "site point", display_name),
                "sourceKey": "openstreetmap",
                "sourceId": f"{source_id}#interior-point",
                "sourceName": DISPLAY_NAMES.get(scenario_id, canonical_name),
                "capturedAt": CAPTURE_DATE,
                "geometrySource": (
                    f"Deterministic interior reference point derived from exact {source_id}"
                ),
            }
        )

    validate_refreshed_fixture(document, args.provinces)
    args.output.write_text(
        json.dumps(document, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(
        f"Wrote {EXPECTED_SHAPES} shapes ({EXPECTED_POINTS}/{EXPECTED_LINES}/"
        f"{EXPECTED_POLYGONS}) to {args.output}."
    )


def scenario_theme(path: Path) -> dict[str, str]:
    shapes = read_feature_collection(path)["features"]
    return {
        feature["properties"]["scenarioId"]: feature["properties"]["theme"]
        for feature in shapes
        if feature["properties"]["type"] == "line"
    }


def discover_lines(args: argparse.Namespace) -> None:
    themes = scenario_theme(args.shapes)
    unknown = sorted(set(LINE_TARGETS) - themes.keys())
    if unknown:
        raise ValueError(f"Line manifest has unknown scenarios: {unknown!r}.")

    source_ids = {target["facility"] for target in LINE_TARGETS.values()}
    area_handler = TargetAreaHandler(source_ids)
    area_handler.apply_file(str(args.pbf), locations=True, idx="flex_mem")
    missing = sorted(source_ids - area_handler.matches.keys())
    if missing:
        raise ValueError(f"Missing target OSM areas: {missing!r}.")

    targets = {
        scenario_id: (
            themes[scenario_id],
            area_handler.matches[target["facility"]]["point"],
        )
        for scenario_id, target in LINE_TARGETS.items()
    }
    line_handler = NearestLineHandler(targets)
    line_handler.apply_file(str(args.pbf), locations=True, idx="flex_mem")
    empty = sorted(
        scenario_id
        for scenario_id, candidates in line_handler.candidates.items()
        if not candidates
    )
    if empty:
        raise ValueError(f"No eligible source ways found for: {empty!r}.")
    output = {
        scenario_id: {
            **LINE_TARGETS[scenario_id],
            "theme": themes[scenario_id],
            "facilitySourceName": area_handler.matches[
                LINE_TARGETS[scenario_id]["facility"]
            ]["name"],
            "facilityPoint": [
                round(value, 7)
                for value in area_handler.matches[
                    LINE_TARGETS[scenario_id]["facility"]
                ]["point"]
            ],
            "candidates": line_handler.candidates[scenario_id],
        }
        for scenario_id in sorted(LINE_TARGETS)
    }
    args.output.write_text(
        json.dumps(output, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"Wrote reviewed candidates for {len(output)} line scenarios to {args.output}.")


def read_feature_collection(path: Path) -> dict[str, Any]:
    document = json.loads(path.read_text(encoding="utf-8"))
    if document.get("type") != "FeatureCollection" or not isinstance(
        document.get("features"), list
    ):
        raise ValueError(f"{path} is not a GeoJSON FeatureCollection.")
    return document


def discover_polygons(args: argparse.Namespace) -> None:
    provinces = [
        (feature, geometry_bbox(feature["geometry"]))
        for feature in read_feature_collection(args.provinces)["features"]
    ]
    handler = AreaDiscoveryHandler(provinces)
    handler.apply_file(str(args.pbf), locations=True, idx="flex_mem")
    handler.candidates.sort(
        key=lambda item: (item["province"], item["classes"], normalized(item["name"]))
    )
    args.output.write_text(
        json.dumps(handler.candidates, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )
    print(f"Wrote {len(handler.candidates)} source-backed area candidates to {args.output}.")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)

    discover = subparsers.add_parser("discover-polygons")
    discover.add_argument("--pbf", type=Path, required=True)
    discover.add_argument("--provinces", type=Path, required=True)
    discover.add_argument("--output", type=Path, required=True)

    discover_lines_parser = subparsers.add_parser("discover-lines")
    discover_lines_parser.add_argument("--pbf", type=Path, required=True)
    discover_lines_parser.add_argument("--shapes", type=Path, required=True)
    discover_lines_parser.add_argument("--output", type=Path, required=True)

    refresh = subparsers.add_parser("refresh")
    refresh.add_argument("--pbf", type=Path, required=True)
    refresh.add_argument("--provinces", type=Path, required=True)
    refresh.add_argument("--shapes", type=Path, required=True)
    refresh.add_argument("--output", type=Path, required=True)

    args = parser.parse_args()
    for attribute in ("pbf", "provinces", "shapes"):
        path = getattr(args, attribute, None)
        if path is not None and not path.is_file():
            parser.error(f"Input file does not exist: {path}")
    if getattr(args, "output", None):
        args.output.parent.mkdir(parents=True, exist_ok=True)
    return args


def main() -> int:
    args = parse_args()
    if args.command == "discover-polygons":
        discover_polygons(args)
        return 0
    if args.command == "discover-lines":
        discover_lines(args)
        return 0
    if args.command == "refresh":
        refresh_fixture(args)
        return 0
    raise AssertionError(args.command)


if __name__ == "__main__":
    sys.exit(main())
