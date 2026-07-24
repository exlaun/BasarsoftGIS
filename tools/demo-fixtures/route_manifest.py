#!/usr/bin/env python3
"""Build the committed offline route fixture from reviewed representative stops.

This script never discovers stops by proximity. The stop lists below are deliberately
reviewed operator/OSM records. It only asks a local OSRM instance, prepared from the
locked Turkey snapshot, to calculate the road geometry and metrics connecting them.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import math
import sys
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path
from typing import Any


CAPTURED_AT = "2026-07-22"
GEOMETRY_SOURCE = (
    "Local OSRM driving route over Geofabrik Turkey 2026-07-22 snapshot"
)


def stop(
    name: str,
    source_id: str | int,
    longitude: float,
    latitude: float,
    source_key: str = "openstreetmap-node",
) -> dict[str, Any]:
    is_osm = source_key.startswith("openstreetmap-")
    osm_kind = source_key.removeprefix("openstreetmap-")
    return {
        "name": name,
        "longitude": longitude,
        "latitude": latitude,
        "sourceKey": source_key,
        "sourceId": str(source_id),
        "sourceUrl": (
            f"https://www.openstreetmap.org/{osm_kind}/{source_id}"
            if is_osm and osm_kind in {"node", "way", "relation"}
            else None
        ),
        "capturedAt": CAPTURED_AT,
        "geometrySource": (
            f"OpenStreetMap {osm_kind} coordinate from the locked Geofabrik Turkey "
            f"{CAPTURED_AT} snapshot"
            if is_osm
            else f"Published stop coordinate ({source_key}), checked {CAPTURED_AT}"
        ),
    }


def route(
    *,
    owner: str,
    name: str,
    city: str,
    line_code: str,
    color: str,
    source_key: str,
    source_id: str | int,
    source_url: str,
    stops: list[dict[str, Any]],
    stop_source_url: str | None = None,
    kind: str = "urban",
    days_ago: int = 7,
) -> dict[str, Any]:
    for waypoint in stops:
        waypoint["sourceUrl"] = waypoint["sourceUrl"] or stop_source_url or source_url
    return {
        "owner": owner,
        "name": name,
        "city": city,
        "lineCode": line_code,
        "kind": kind,
        "color": color,
        "daysAgo": days_ago,
        "sourceKey": source_key,
        "sourceId": str(source_id),
        "sourceUrl": source_url,
        "capturedAt": CAPTURED_AT,
        "geometrySource": GEOMETRY_SOURCE,
        "stops": stops,
    }


# Route identities and stop order come from the source URL or the matching OSM route
# relation noted by sourceId. Coordinates use the locked snapshot's OSM node when
# available; operator stop ids are retained for official GTFS/API records.
ROUTES: list[dict[str, Any]] = [
    route(
        owner="istanbul_operator",
        name="İstanbul · 34BZ · Beylikdüzü–Zincirlikuyu",
        city="İstanbul",
        line_code="34BZ",
        color="#E11D48",
        source_key="openstreetmap-route",
        source_id=18769049,
        source_url="https://iett.istanbul/RouteDetail?hkod=34BZ",
        stops=[
            stop("Beylikdüzü Sondurak", 11663774838, 28.6254741, 41.0218176),
            stop("Güzelyurt", 12772322403, 28.6659231, 41.0064291),
            stop("Cihangir–Üniversite", 11663774828, 28.7140284, 40.9905399),
            stop("Cennet Mahallesi", 9164537140, 28.7831768, 40.9854181),
            stop("Bahçelievler", 11663774817, 28.8639067, 40.9952169),
            stop("Topkapı–Şehit Mustafa Cambaz", 4974583284, 28.9171833, 41.0202697),
            stop("Okmeydanı", 12773099574, 28.9614207, 41.0566936),
            stop("Zincirlikuyu", 12328413560, 29.0129203, 41.0659364),
        ],
    ),
    route(
        owner="istanbul_operator",
        name="İstanbul · 34AS · Avcılar–Söğütlüçeşme",
        city="İstanbul",
        line_code="34AS",
        color="#F97316",
        source_key="openstreetmap-route",
        source_id=7399563,
        source_url="https://iett.istanbul/RouteDetail?hkod=34AS",
        stops=[
            stop("Avcılar", 12802353452, 28.7307772, 40.9860022),
            stop("Küçükçekmece", 11663774824, 28.7703336, 40.9865458),
            stop("Yenibosna", 2366851849, 28.8336930, 40.9924576),
            stop("Merter", 11663774814, 28.8978876, 41.0080164),
            stop("Ayvansaray–Eyüpsultan", 12773099570, 28.9384376, 41.0395577),
            stop("Okmeydanı Hastane", 12773099579, 28.9765993, 41.0674685),
            stop("Burhaniye", 12773099584, 29.0469814, 41.0319900),
            stop("Söğütlüçeşme", 1732274373, 29.0378226, 40.9920083),
        ],
        days_ago=8,
    ),
    route(
        owner="istanbul_operator",
        name="İstanbul · 500T · Güzelyalı–4. Levent segment",
        city="İstanbul",
        line_code="500T",
        color="#CA8A04",
        source_key="openstreetmap-route",
        source_id=9141838,
        source_url="https://iett.istanbul/RouteDetail?hkod=500T",
        stops=[
            stop("Güzelyalı Köprüsü", 6157344743, 29.2818959, 40.8598487),
            stop("Kaynarca", 10790703989, 29.2604652, 40.8789425),
            stop("Pendik Köprüsü", 5899547432, 29.2382742, 40.8886944),
            stop("Keresteciler", 5936628926, 29.2244660, 40.8977960),
            stop("Cevizli Köprüsü", 12000742088, 29.1733478, 40.9185521),
            stop("Küçükyalı Metro", 11663749991, 29.1240130, 40.9481590),
            stop("Kozyatağı Metro", 11586259586, 29.0992245, 40.9757225),
            stop("4. Levent Metro", 6760608685, 29.0068717, 41.0840123),
        ],
        days_ago=9,
    ),
    route(
        owner="istanbul_operator",
        name="İstanbul · 15F · Kadıköy–Beykoz",
        city="İstanbul",
        line_code="15F",
        color="#16A34A",
        source_key="openstreetmap-route",
        source_id=15542483,
        source_url="https://iett.istanbul/RouteDetail?hkod=15F",
        stops=[
            stop("Kadıköy", 11537117488, 29.0242632, 40.9927180),
            stop("Haydarpaşa Öğrenci Yurdu", 11537117470, 29.0288610, 41.0021260),
            stop("Karacaahmet Cemevi", 11537117480, 29.0214210, 41.0129460),
            stop("Beylerbeyi Sarayı", 11536952845, 29.0426610, 41.0431160),
            stop("Anadolu Hisarı", 11536952860, 29.0676550, 41.0840560),
            stop("Beykoz Sahil Boyu", 11536460875, 29.0675310, 41.1032430),
            stop("Beykoz Belediyesi", 11536395018, 29.0965780, 41.1271390),
            stop("Şahinkaya Garajı", 11537117489, 29.0948160, 41.1415030),
        ],
        days_ago=10,
    ),
    route(
        owner="istanbul_operator",
        name="İstanbul · 25E · Baltalimanı–Sarıyer",
        city="İstanbul",
        line_code="25E",
        color="#2563EB",
        source_key="openstreetmap-route",
        source_id=14702280,
        source_url="https://iett.istanbul/RouteDetail?hkod=25E",
        stops=[
            stop("Ahmet Celalettin Paşa Camii", 11540389556, 29.0400400, 41.1014284),
            stop("Emirgan", 11540389546, 29.0581300, 41.1077690),
            stop("Yalılar", 11540389541, 29.0644280, 41.1174230),
            stop("Kalender", 11540389560, 29.0637490, 41.1310550),
            stop("Tabya Altı", 11540389559, 29.0515300, 41.1471440),
            stop("Balıkçılar Çarşısı", 11540431380, 29.0426970, 41.1594020),
            stop("Sarıyer Merkez", 2201646431, 29.0574230, 41.1670970),
            stop("Sarıyer", 11540594796, 29.0482970, 41.1730580),
        ],
        days_ago=11,
    ),
    route(
        owner="ankara_operator",
        name="Ankara · EGO 205 · İvedik–Anadolu Mahallesi",
        city="Ankara",
        line_code="205",
        color="#DC2626",
        source_key="ego-route",
        source_id="205",
        source_url="https://www.ego.gov.tr/tr/hareketsaatleri?hat_no=205",
        stops=[
            stop("İvedik Metro", 21115, 32.8157158, 39.9579801, "ego-stop"),
            stop("Ersan Konut", 20690, 32.7985734, 39.9712404, "ego-stop"),
            stop("Anadolu 12. Durak", 20271, 32.7939391, 39.9732480, "ego-stop"),
            stop("Yol Ağzı", 20740, 32.8055941, 39.9817811, "ego-stop"),
            stop("Şehit Ahmet Çelik Parkı", 21255, 32.8077181, 39.9926783, "ego-stop"),
            stop("Pamuklar Pazar Yeri", 21893, 32.8074449, 39.9844306, "ego-stop"),
            stop("Kordonboyu Caddesi", 21468, 32.8118885, 39.9736616, "ego-stop"),
            stop("Muhtarlık", 21108, 32.8105774, 39.9600675, "ego-stop"),
        ],
        days_ago=6,
    ),
    route(
        owner="ankara_operator",
        name="Ankara · EGO 303 · Örnek Mahallesi–Ulus",
        city="Ankara",
        line_code="303",
        color="#EA580C",
        source_key="ego-route",
        source_id="303",
        source_url="https://www.ego.gov.tr/tr/hareketsaatleri?hat_no=303",
        stops=[
            stop("Petek Sitesi", 12389, 32.8855536, 39.8834439, "ego-stop"),
            stop("Eski Son Durak", 30183, 32.8858309, 39.8955961, "ego-stop"),
            stop("Arjantin Büyükelçiliği", 12457, 32.8766244, 39.8936651, "ego-stop"),
            stop("Kuğulu Park (gidiş)", 12254, 32.8607804, 39.9023414, "ego-stop"),
            stop("Kızılay", 11622, 32.8546552, 39.9235304, "ego-stop"),
            stop("Ulus 100. Yıl Çarşısı", 40460, 32.8541691, 39.9407648, "ego-stop"),
            stop("Kuğulu Park (dönüş)", 12253, 32.8606737, 39.9015346, "ego-stop"),
            stop("Yamaç Evler", 13526, 32.8810998, 39.8959731, "ego-stop"),
        ],
        days_ago=7,
    ),
    route(
        owner="ankara_operator",
        name="Ankara · EGO 334-6 · Mamak–Kızılay",
        city="Ankara",
        line_code="334-6",
        color="#CA8A04",
        source_key="ego-route",
        source_id="334-6",
        source_url="https://www.ego.gov.tr/tr/hareketsaatleri?hat_no=334-6",
        stops=[
            stop("Kızılay", 11627, 32.8547583, 39.9239946, "ego-stop"),
            stop("Opera", 30003, 32.8544376, 39.9360948, "ego-stop"),
            stop("Ankara Tıp Fakültesi", 30129, 32.8830174, 39.9347681, "ego-stop"),
            stop("Park Vadi Konutları", 30665, 32.9036751, 39.9330278, "ego-stop"),
            stop("Mamak Köprü 2", 30804, 32.9224150, 39.9246880, "ego-stop"),
            stop("Boğaziçi Şelale (gidiş)", 30305, 32.9308066, 39.9195548, "ego-stop"),
            stop("Boğaziçi Şelale (dönüş)", 30306, 32.9315550, 39.9195000, "ego-stop"),
            stop("Kolej", 30055, 32.8628903, 39.9246769, "ego-stop"),
        ],
        days_ago=8,
    ),
    route(
        owner="ankara_operator",
        name="Ankara · EGO 413 · Altınpark–Kırkkonaklar",
        city="Ankara",
        line_code="413",
        color="#16A34A",
        source_key="ego-route",
        source_id="413",
        source_url="https://www.ego.gov.tr/tr/hareketsaatleri?hat_no=413",
        stops=[
            stop("Altınpark 1. Durak", 40194, 32.8853068, 39.9666759, "ego-stop"),
            stop("Veteriner Fakültesi", 40168, 32.8626950, 39.9565848, "ego-stop"),
            stop("Kuğulu Park", 12251, 32.8592551, 39.9013132, "ego-stop"),
            stop("Birlik", 14668, 32.8822405, 39.8850586, "ego-stop"),
            stop("Macaristan Büyükelçiliği", 12302, 32.8666373, 39.8720871, "ego-stop"),
            stop("Seğmenler Parkı", 12268, 32.8620045, 39.8957291, "ego-stop"),
            stop("Dışkapı Hastanesi", 31682, 32.8607686, 39.9544924, "ego-stop"),
            stop("İnönü Lisesi", 40198, 32.8877265, 39.9620094, "ego-stop"),
        ],
        days_ago=9,
    ),
    route(
        owner="ankara_operator",
        name="Ankara · EGO 442 · AŞTİ–Havalimanı",
        city="Ankara",
        line_code="442",
        color="#2563EB",
        source_key="ego-route",
        source_id="442",
        source_url="https://www.ego.gov.tr/tr/hareketsaatleri?hat_no=442",
        stops=[
            stop("AŞTİ", 14367, 32.8122620, 39.9181340, "ego-stop"),
            stop("Güvenpark", 12218, 32.8543880, 39.9194380, "ego-stop"),
            stop("Ankara Spor Salonu", 11555, 32.8432570, 39.9374370, "ego-stop"),
            stop("Dışkapı Hastanesi", 30051, 32.8606800, 39.9543560, "ego-stop"),
            stop("Hasköy Akşemsettin Camii", 40079, 32.8808010, 39.9726500, "ego-stop"),
            stop("Pursaklar Aile Yaşam Merkezi", 40625, 32.9044480, 40.0360860, "ego-stop"),
            stop("Otonomi 1. Kapı", 40746, 32.9676150, 40.0803430, "ego-stop"),
            stop("Esenboğa Havalimanı İç Hatlar", 40813, 32.9915800, 40.1142830, "ego-stop"),
        ],
        days_ago=10,
    ),
    route(
        owner="izmir_operator",
        name="İzmir · ESHOT 202 · Cumhuriyet Meydanı–Havalimanı",
        city="İzmir",
        line_code="202",
        color="#DB2777",
        source_key="eshot-gtfs-route",
        source_id="202",
        source_url="https://www.eshot.gov.tr/gtfs/bus-eshot-gtfs.zip",
        stops=[
            stop("İzmir Mesleki ve Teknik Anadolu Lisesi", 10210, 27.1358568, 38.4270783, "eshot-gtfs-stop"),
            stop("Dokuz Eylül Rektörlük", 10106, 27.1359000, 38.4305000, "eshot-gtfs-stop"),
            stop("Adnan Saygun Sanat Merkezi", 10199, 27.0774790, 38.3997870, "eshot-gtfs-stop"),
            stop("Moda", 14501, 27.0731007, 38.3769004, "eshot-gtfs-stop"),
            stop("Leylak", 10663, 27.1352000, 38.3309000, "eshot-gtfs-stop"),
            stop("Kolej", 10669, 27.1376005, 38.3209003, "eshot-gtfs-stop"),
            stop("Emlak Bankası Evleri", 10675, 27.1423310, 38.3090920, "eshot-gtfs-stop"),
            stop("Havalimanı Dış Hatlar Geliş", 13016, 27.1476004, 38.2948002, "eshot-gtfs-stop"),
        ],
        days_ago=5,
    ),
    route(
        owner="izmir_operator",
        name="İzmir · ESHOT 515 · Tınaztepe–Evka 3 Metro",
        city="İzmir",
        line_code="515",
        color="#DC2626",
        source_key="eshot-gtfs-route",
        source_id="515",
        source_url="https://www.eshot.gov.tr/gtfs/bus-eshot-gtfs.zip",
        stops=[
            stop("Tınaztepe Yerleşke Son Durak", 40894, 27.2108093, 38.3686535, "eshot-gtfs-stop"),
            stop("Papatya", 40086, 27.1857000, 38.3801000, "eshot-gtfs-stop"),
            stop("Buca SGK", 40022, 27.1712012, 38.3889004, "eshot-gtfs-stop"),
            stop("Şirinyer Merkez", 40004, 27.1480816, 38.3932721, "eshot-gtfs-stop"),
            stop("Çamdibi Sağlık Ocağı", 30131, 27.1807002, 38.4352007, "eshot-gtfs-stop"),
            stop("Halide Edip Adıvar", 30281, 27.1789080, 38.4518430, "eshot-gtfs-stop"),
            stop("Bornova Stadı", 30321, 27.2054000, 38.4614000, "eshot-gtfs-stop"),
            stop("Evka 3 Aktarma Merkezi", 31773, 27.2285600, 38.4656580, "eshot-gtfs-stop"),
        ],
        days_ago=6,
    ),
    route(
        owner="izmir_operator",
        name="İzmir · ESHOT 584 · İnönü Mahallesi–Bornova Metro",
        city="İzmir",
        line_code="584",
        color="#D97706",
        source_key="eshot-gtfs-route",
        source_id="584",
        source_url="https://www.eshot.gov.tr/gtfs/bus-eshot-gtfs.zip",
        stops=[
            stop("Belediye Evleri", 31418, 27.2107530, 38.4891070, "eshot-gtfs-stop"),
            stop("Kardelen Sitesi", 31930, 27.2021000, 38.4877000, "eshot-gtfs-stop"),
            stop("Seyit Şanlı Anadolu Lisesi", 32423, 27.2041012, 38.4818543, "eshot-gtfs-stop"),
            stop("Ufuk", 31312, 27.2094700, 38.4744200, "eshot-gtfs-stop"),
            stop("Çiçekli", 31308, 27.2169800, 38.4759800, "eshot-gtfs-stop"),
            stop("Malazgirt İlköğretim Okulu", 31298, 27.2199000, 38.4707400, "eshot-gtfs-stop"),
            stop("Erzene Muhtarlık", 31292, 27.2193501, 38.4667233, "eshot-gtfs-stop"),
            stop("Bornova Metro", 30009, 27.2141838, 38.4585669, "eshot-gtfs-stop"),
        ],
        days_ago=7,
    ),
    route(
        owner="izmir_operator",
        name="İzmir · ESHOT 808 · Cumaovası–ESBAŞ",
        city="İzmir",
        line_code="808",
        color="#059669",
        source_key="eshot-gtfs-route",
        source_id="808",
        source_url="https://www.eshot.gov.tr/gtfs/bus-eshot-gtfs.zip",
        stops=[
            stop("Cumaovası Aktarma Merkezi", 13280, 27.1627023, 38.2629947, "eshot-gtfs-stop"),
            stop("Menderes Pazar Yeri", 11258, 27.1390459, 38.2555805, "eshot-gtfs-stop"),
            stop("Menderes Belediyesi", 10720, 27.1341730, 38.2517290, "eshot-gtfs-stop"),
            stop("Zeytinlik", 10700, 27.1332130, 38.2703740, "eshot-gtfs-stop"),
            stop("Görece Spor Salonu", 10746, 27.1223100, 38.2831400, "eshot-gtfs-stop"),
            stop("Fevzi Çakmak", 10734, 27.1340400, 38.2888700, "eshot-gtfs-stop"),
            stop("Akçay", 10679, 27.1425860, 38.3108710, "eshot-gtfs-stop"),
            stop("ESBAŞ", 13164, 27.1364230, 38.3354609, "eshot-gtfs-stop"),
        ],
        days_ago=8,
    ),
    route(
        owner="izmir_operator",
        name="İzmir · ESHOT 950 · Narlıdere–Konak",
        city="İzmir",
        line_code="950",
        color="#2563EB",
        source_key="eshot-gtfs-route",
        source_id="950",
        source_url="https://www.eshot.gov.tr/gtfs/bus-eshot-gtfs.zip",
        stops=[
            stop("Şehitlik", 50042, 26.9990311, 38.3957984, "eshot-gtfs-stop"),
            stop("İZSU", 50026, 27.0264000, 38.3925000, "eshot-gtfs-stop"),
            stop("Ekonomi Üniversitesi", 50172, 27.0465000, 38.3875000, "eshot-gtfs-stop"),
            stop("Duygu", 50152, 27.0617568, 38.3855914, "eshot-gtfs-stop"),
            stop("Denizmen", 10290, 27.0806000, 38.3936000, "eshot-gtfs-stop"),
            stop("Şoförler Lokali", 10262, 27.1144000, 38.4024000, "eshot-gtfs-stop"),
            stop("Mezarlıkbaşı", 10224, 27.1372000, 38.4207000, "eshot-gtfs-stop"),
            stop("Bahribaba", 10016, 27.1276000, 38.4148000, "eshot-gtfs-stop"),
        ],
        days_ago=9,
    ),
    route(
        owner="antalya_operator",
        name="Antalya · KL08 · Sarısu–Güzeloba",
        city="Antalya",
        line_code="KL08",
        color="#E11D48",
        source_key="antalya-route",
        source_id="KL08",
        source_url="https://ulasim.antalya.bel.tr/",
        stop_source_url="https://www.ulasimburada.com/antalya/route/KL08",
        stops=[
            stop("Sarısu Depolama Merkezi", 11005791509, 30.5963011, 36.8307717),
            stop("Atatürk Bulvarı 4", 8000442486, 30.6188905, 36.8537742),
            stop("Atatürk Bulvarı 13", 6779737827, 30.6479260, 36.8782434),
            stop("Anafartalar Caddesi 4", 5203636826, 30.6954056, 36.8897217),
            stop("Işıklar 3", 6762026888, 30.7105971, 36.8776984),
            stop("İsmet Gökşen 5", 10058, 30.7383706, 36.8586962, "antalya-stop"),
            stop("Terracity 2", 10066, 30.7584275, 36.8515966, "antalya-stop"),
            stop("Lara Caddesi", 13009, 30.8085084, 36.8488106, "antalya-stop"),
        ],
        days_ago=4,
    ),
    route(
        owner="antalya_operator",
        name="Antalya · VS18 · Sarısu–Varsak",
        city="Antalya",
        line_code="VS18",
        color="#16A34A",
        source_key="antalya-route",
        source_id="VS18",
        source_url="https://ulasim.antalya.bel.tr/",
        stop_source_url="https://www.ulasimburada.com/antalya/route/VS18",
        stops=[
            stop("Sarısu Depolama Merkezi 1", 13001, 30.5962667, 36.8308009, "antalya-stop"),
            stop("Atatürk Bulvarı 4", 10010, 30.6190055, 36.8538271, "antalya-stop"),
            stop("Atatürk Bulvarı 12", 10018, 30.6444396, 36.8760390, "antalya-stop"),
            stop("100. Yıl Bulvarı 7", 10028, 30.6883531, 36.8896045, "antalya-stop"),
            stop("Kızılırmak Caddesi 11", 10967, 30.7157709, 36.9110495, "antalya-stop"),
            stop("Yeşilırmak Caddesi 20", 10975, 30.7113388, 36.9370285, "antalya-stop"),
            stop("Süleyman Demirel Bulvarı 21", 10982, 30.7117440, 36.9622280, "antalya-stop"),
            stop("Varsak Cumhuriyet Caddesi 1", 12771, 30.7153050, 36.9901990, "antalya-stop"),
        ],
        days_ago=5,
    ),
    route(
        owner="antalya_operator",
        name="Antalya · LC07A · Kundu–Otogar",
        city="Antalya",
        line_code="LC07A",
        color="#2563EB",
        source_key="antalya-route",
        source_id="LC07A",
        source_url="https://ulasim.antalya.bel.tr/",
        stop_source_url="https://www.ulasimburada.com/antalya/route/LC07A",
        stops=[
            stop("Kundu 22", 16377, 30.9230233, 36.8726232, "antalya-stop"),
            stop("Yaşar Sobutay Bulvarı 29", 11225, 30.8678432, 36.8579485, "antalya-stop"),
            stop("Yaşar Sobutay Bulvarı 42", 11852, 30.8062579, 36.8550304, "antalya-stop"),
            stop("Barınaklar Bulvarı 19", 10114, 30.7659426, 36.8516775, "antalya-stop"),
            stop("Metin Kasapoğlu Caddesi 5", 10131, 30.7271831, 36.8655094, "antalya-stop"),
            stop("Şehit Binbaşı Cengiz Toytunç Caddesi 1", 10326, 30.7016135, 36.8880398, "antalya-stop"),
            stop("2622. Sokak 1", 14328, 30.6914070, 36.9158630, "antalya-stop"),
            stop("Otogar Depolama", 13007, 30.6657810, 36.9183090, "antalya-stop"),
        ],
        days_ago=6,
    ),
    route(
        owner="antalya_operator",
        name="Antalya · ML22 · Meydan–Hurma",
        city="Antalya",
        line_code="ML22",
        color="#F97316",
        source_key="antalya-route",
        source_id="ML22",
        source_url="https://ulasim.antalya.bel.tr/",
        stop_source_url="https://www.ulasimburada.com/antalya/route/ML22",
        stops=[
            stop("Meydan Depolama Merkezi 1", 11930, 30.7324225, 36.8866332, "antalya-stop"),
            stop("Ali Çetinkaya Caddesi 2", 10443, 30.7232536, 36.8882813, "antalya-stop"),
            stop("100. Yıl Bulvarı 3", 10155, 30.6852485, 36.8893877, "antalya-stop"),
            stop("İl Sağlık Müdürlüğü", 10163, 30.6481390, 36.8787640, "antalya-stop"),
            stop("Atatürk Bulvarı 26", 10172, 30.6281360, 36.8616170, "antalya-stop"),
            stop("Boğaçay Caddesi 4", 10474, 30.6059568, 36.8591284, "antalya-stop"),
            stop("37. Cadde 2", 12356, 30.5976470, 36.8558160, "antalya-stop"),
            stop("252. Sokak 5", 10453, 30.6038110, 36.8585330, "antalya-stop"),
        ],
        days_ago=5,
    ),
    route(
        owner="antalya_operator",
        name="Antalya · VF63 · Varsak–Eğitim Araştırma",
        city="Antalya",
        line_code="VF63",
        color="#CA8A04",
        source_key="antalya-route",
        source_id="VF63",
        source_url="https://ulasim.antalya.bel.tr/",
        stop_source_url="https://www.ulasimburada.com/antalya/route/VF63",
        stops=[
            stop("Fevzi Çakmak Depolama", 14943, 30.6910980, 36.9503790, "antalya-stop"),
            stop("Muammer Aksoy Caddesi 2", 11167, 30.7160570, 36.9492940, "antalya-stop"),
            stop("Muammer Aksoy Caddesi 5", 11169, 30.7253340, 36.9497370, "antalya-stop"),
            stop("Turgut Özal Caddesi 1", 11184, 30.7472230, 36.9474990, "antalya-stop"),
            stop("Kepez Devlet Hastanesi 1", 12963, 30.7254277, 36.9328898, "antalya-stop"),
            stop("Mehmet Atay Caddesi 9", 12511, 30.7422502, 36.9257436, "antalya-stop"),
            stop("Yeşilırmak Caddesi 10", 10940, 30.7149640, 36.9186980, "antalya-stop"),
            stop("Eğitim Araştırma Hastanesi 2", 10334, 30.6763550, 36.8908402, "antalya-stop"),
        ],
        days_ago=6,
    ),
    route(
        owner="admin",
        name="Bursa · 38/B-2 · Terminal–Görükle",
        city="Bursa",
        line_code="38/B-2",
        color="#7C3AED",
        source_key="burulas-route",
        source_id="38/B-2",
        source_url="https://www.burulas.com.tr/wp-content/uploads/Burulas-Fiyat-Listesi-07.02.2024.pdf",
        stops=[
            stop("Terminal Peron 1", 4677, 29.0543800, 40.2645300, "burulas-stop"),
            stop("BUTTİM İstasyonu 1", 1584, 29.0628830, 40.2378800, "burulas-stop"),
            stop("Küplüpınar 1", 1351, 29.0605000, 40.2047000, "burulas-stop"),
            stop("Yağcılar 2", 268, 29.0427000, 40.1936300, "burulas-stop"),
            stop("Soğukkuyu", 192, 29.0087961, 40.2132443, "burulas-stop"),
            stop("Ataevler İstasyonu 1", 367, 28.9493100, 40.2126900, "burulas-stop"),
            stop("Uğur Mumcu Bulvarı 8", 3271, 28.9058850, 40.2203460, "burulas-stop"),
            stop("Atatürk Caddesi 3", 644, 28.8459670, 40.2268210, "burulas-stop"),
        ],
        days_ago=10,
    ),
    route(
        owner="admin",
        name="Adana · 114 · Optimum–Real",
        city="Adana",
        line_code="114",
        color="#0891B2",
        source_key="adana-route",
        source_id="21",
        source_url="https://ulasimbilgi.adana.bel.tr/Otobusler/21",
        stops=[
            stop("Optimum AVM 1A", 42585, 35.3396305, 36.9916207, "adana-stop"),
            stop("Çocuk Hakları Durağı", 42411, 35.3634475, 37.0056926, "adana-stop"),
            stop("Mustafa Kemal Paşa Bulvarı 7B", 42435, 35.3358823, 37.0089812, "adana-stop"),
            stop("Barajyolu 6A", 40717, 35.3138697, 37.0291070, "adana-stop"),
            stop("Süleyman Demirel Bulvarı 1B", 42693, 35.2946025, 37.0525361, "adana-stop"),
            stop("Turgut Özal Bulvarı 17A", 43327, 35.2688197, 37.0498272, "adana-stop"),
            stop("Dr. Sadık Ahmet Bulvarı 6B", 41168, 35.2550288, 37.0439416, "adana-stop"),
            stop("M1 AVM 1B", 42344, 35.2415457, 37.0157372, "adana-stop"),
        ],
        days_ago=11,
    ),
    route(
        owner="admin",
        name="Konya · 4-A · Meram Yeniyol–Anasultan",
        city="Konya",
        line_code="4-A",
        color="#EA580C",
        source_key="konya-atus-route",
        source_id="4-0",
        source_url="https://atus.konya.bel.tr/atus/hat-ve-guzergah",
        stops=[
            stop("Meram Son Durak", 2100, 32.4175100, 37.8375430, "konya-atus-stop"),
            stop("Yorgancı", 75, 32.4273540, 37.8492440, "konya-atus-stop"),
            stop("Anahtar", 42, 32.4424750, 37.8608580, "konya-atus-stop"),
            stop("Millet", 10, 32.4804260, 37.8692690, "konya-atus-stop"),
            stop("KOSKİ", 32, 32.4838830, 37.8706660, "konya-atus-stop"),
            stop("Meram Devlet Hastanesi", 41, 32.4450580, 37.8618910, "konya-atus-stop"),
            stop("Dibekbaşı", 74, 32.4246330, 37.8499250, "konya-atus-stop"),
            stop("Uhud", 81, 32.4203740, 37.8375760, "konya-atus-stop"),
        ],
        days_ago=12,
    ),
    route(
        owner="gaziantep_operator",
        name="Gaziantep · B39 · Oğuzeli–İstasyon",
        city="Gaziantep",
        line_code="B39",
        color="#2563EB",
        source_key="gaziulas-route",
        source_id="B39",
        source_url="https://gaziulas.com.tr/hizmetler/otobus-isletmesi/otobus-hat-detaylari-1/B39",
        stops=[
            stop("Delikli Tepe", 12944, 37.5249466, 36.9542339, "gaziulas-stop"),
            stop("Oğuzeli Terlemez Pide Fırını", 11458, 37.5123800, 36.9594500, "gaziulas-stop"),
            stop("Yeşildere Mahallesi", 11471, 37.5023199, 36.9810109, "gaziulas-stop"),
            stop("Oğuzeli Kavşağı", 11476, 37.4641500, 36.9850100, "gaziulas-stop"),
            stop("Hoşgör Kuran Kursu", 11902, 37.4307311, 37.0110079, "gaziulas-stop"),
            stop("Yeşil Vadi Köprüsü", 11885, 37.3780350, 37.0298880, "gaziulas-stop"),
            stop("Hacı Halil Camii", 11394, 37.3806200, 37.0513900, "gaziulas-stop"),
            stop("D.D.Y. Lojistik", 11223, 37.3837700, 37.0725800, "gaziulas-stop"),
        ],
        days_ago=13,
    ),
    route(
        owner="trabzon_operator",
        name="Trabzon · 121 · Beşirli–Kaşüstü",
        city="Trabzon",
        line_code="121",
        color="#0F766E",
        source_key="trabzon-akus-route",
        source_id="13",
        source_url="https://ulasim.trabzon.bel.tr/Web/Mobil?hatIdler=13",
        stops=[
            stop("1 Nolu Beşirli Mahallesi Sahil Camii", 2863, 39.6582660, 40.9956730, "trabzon-akus-stop"),
            stop("Tanjant 5", 1053, 39.6818080, 40.9966150, "trabzon-akus-stop"),
            stop("TS Avni Aker Stadı Karşısı", 1028, 39.7054370, 41.0028800, "trabzon-akus-stop"),
            stop("Esentepe Aile Yönlendirme Merkezi", 1035, 39.7367820, 41.0004400, "trabzon-akus-stop"),
            stop("Pelitli TEİAŞ", 1458, 39.7889520, 40.9920420, "trabzon-akus-stop"),
            stop("Avrasya Üniversitesi Ömer Yıldız Yerleşkesi 1", 1459, 39.8271460, 40.9834120, "trabzon-akus-stop"),
            stop("Kaşüstü Kanuni Hastanesi 1", 5003, 39.8317120, 40.9663510, "trabzon-akus-stop"),
            stop("Kaşüstü Kanuni Hastanesi 6", 5007, 39.8203500, 40.9545040, "trabzon-akus-stop"),
        ],
        days_ago=14,
    ),
    route(
        owner="admin",
        name="Intercity corridor · İstanbul–Ankara",
        city="Intercity corridor",
        line_code="COR-01",
        kind="intercity",
        color="#1D4ED8",
        source_key="demo-corridor",
        source_id="istanbul-ankara",
        source_url="https://download.geofabrik.de/europe/turkey.html",
        stops=[
            stop("Esenler Otogarı", 45345232, 28.8945332, 41.0401995, "openstreetmap-way"),
            stop("Ankara AŞTİ", 52530393, 32.8126753, 39.9182854, "openstreetmap-way"),
        ],
        days_ago=15,
    ),
    route(
        owner="admin",
        name="Intercity corridor · İstanbul–Bursa–İzmir",
        city="Intercity corridor",
        line_code="COR-02",
        kind="intercity",
        color="#B91C1C",
        source_key="demo-corridor",
        source_id="istanbul-bursa-izmir",
        source_url="https://download.geofabrik.de/europe/turkey.html",
        stops=[
            stop("Esenler Otogarı", 45345232, 28.8945332, 41.0401995, "openstreetmap-way"),
            stop("Bursa Şehirlerarası Otobüs Terminali", 191171800, 29.0532336, 40.2650924, "openstreetmap-way"),
            stop("İzmir Şehirlerarası Otobüs Terminali", 23629486, 27.2136082, 38.4302797, "openstreetmap-way"),
        ],
        days_ago=16,
    ),
    route(
        owner="admin",
        name="Intercity corridor · Ankara–Konya–Antalya",
        city="Intercity corridor",
        line_code="COR-03",
        kind="intercity",
        color="#15803D",
        source_key="demo-corridor",
        source_id="ankara-konya-antalya",
        source_url="https://download.geofabrik.de/europe/turkey.html",
        stops=[
            stop("Ankara AŞTİ", 52530393, 32.8126753, 39.9182854, "openstreetmap-way"),
            stop("Konya Şehirlerarası Otobüs Terminali", 1207607587, 32.5094887, 37.9505105, "openstreetmap-way"),
            stop(
                "Antalya İlçeler Terminali",
                845938321,
                30.6645964,
                36.9210487,
                "openstreetmap-way",
            ),
        ],
        days_ago=17,
    ),
    route(
        owner="admin",
        name="Intercity corridor · İzmir–Aydın–Muğla–Antalya",
        city="Intercity corridor",
        line_code="COR-04",
        kind="intercity",
        color="#7E22CE",
        source_key="demo-corridor",
        source_id="izmir-aydin-mugla-antalya",
        source_url="https://download.geofabrik.de/europe/turkey.html",
        stops=[
            stop("İzmir Şehirlerarası Otobüs Terminali", 23629486, 27.2136082, 38.4302797, "openstreetmap-way"),
            stop("Aydın Otogar", 1147636178, 27.8402451, 37.7971236, "openstreetmap-way"),
            stop("Muğla Otogar", 1050282027, 28.3515217, 37.1956346, "openstreetmap-way"),
            stop(
                "Antalya İlçeler Terminali",
                845938321,
                30.6645964,
                36.9210487,
                "openstreetmap-way",
            ),
        ],
        days_ago=18,
    ),
    route(
        owner="admin",
        name="Intercity corridor · Adana–Gaziantep–Şanlıurfa",
        city="Intercity corridor",
        line_code="COR-05",
        kind="intercity",
        color="#C2410C",
        source_key="demo-corridor",
        source_id="adana-gaziantep-sanliurfa",
        source_url="https://download.geofabrik.de/europe/turkey.html",
        stops=[
            stop("Adana Merkez Otogar", 1154800003, 35.2626002, 36.9972210, "openstreetmap-way"),
            stop("Gaziantep Otogar", 128186929, 37.4009552, 37.1019672, "openstreetmap-way"),
            stop("Şanlıurfa Otogarı", 272546045, 38.8043944, 37.1864351, "openstreetmap-way"),
        ],
        days_ago=19,
    ),
]


def validate_sources(routes: list[dict[str, Any]]) -> None:
    if len(routes) != 30:
        raise ValueError(f"Expected 30 route definitions, found {len(routes)}")
    if sum(len(item["stops"]) for item in routes) != 215:
        raise ValueError("Expected exactly 215 representative stops")

    identities: set[tuple[str, str]] = set()
    colors: dict[str, set[str]] = {}
    for item in routes:
        identity = (item["sourceKey"], item["sourceId"])
        if identity in identities:
            raise ValueError(f"Duplicate route source identity: {identity}")
        identities.add(identity)
        if len(item["name"]) > 80:
            raise ValueError(f"Route name exceeds 80 characters: {item['name']}")
        expected = 8 if item["kind"] == "urban" else None
        if expected is not None and len(item["stops"]) != expected:
            raise ValueError(f"{item['name']} must contain eight representative stops")
        if item["kind"] == "intercity" and not 2 <= len(item["stops"]) <= 4:
            raise ValueError(f"{item['name']} must contain two to four terminals")
        city_colors = colors.setdefault(item["city"], set())
        if item["color"].lower() in city_colors:
            raise ValueError(f"Duplicate route color in {item['city']}: {item['color']}")
        city_colors.add(item["color"].lower())

        stop_ids: set[str] = set()
        stop_names: set[str] = set()
        for waypoint in item["stops"]:
            if waypoint["sourceId"] in stop_ids:
                raise ValueError(
                    f"Duplicate stop source id on {item['name']}: {waypoint['sourceId']}"
                )
            if waypoint["name"] in stop_names:
                raise ValueError(
                    f"Duplicate stop name on {item['name']}: {waypoint['name']}"
                )
            stop_ids.add(waypoint["sourceId"])
            stop_names.add(waypoint["name"])
            if not (
                math.isfinite(waypoint["longitude"])
                and math.isfinite(waypoint["latitude"])
                and 25 <= waypoint["longitude"] <= 45
                and 35 <= waypoint["latitude"] <= 43
            ):
                raise ValueError(f"Invalid Turkey coordinate on {item['name']}")
            if not str(waypoint.get("sourceUrl", "")).startswith(("http://", "https://")):
                raise ValueError(f"Missing stop source URL on {item['name']}")
            if not str(waypoint.get("geometrySource", "")).strip():
                raise ValueError(f"Missing stop geometry provenance on {item['name']}")


def apply_snapshot_date(routes: list[dict[str, Any]], snapshot_date: str) -> None:
    try:
        dt.date.fromisoformat(snapshot_date)
    except ValueError as error:
        raise ValueError("--snapshot-date must use YYYY-MM-DD.") from error

    for item in routes:
        item["capturedAt"] = snapshot_date
        item["geometrySource"] = (
            f"Local OSRM driving route over Geofabrik Turkey {snapshot_date} snapshot"
        )
        for waypoint in item["stops"]:
            waypoint["capturedAt"] = snapshot_date
            if waypoint["sourceKey"].startswith("openstreetmap-"):
                osm_kind = waypoint["sourceKey"].removeprefix("openstreetmap-")
                waypoint["geometrySource"] = (
                    f"OpenStreetMap {osm_kind} coordinate from the locked Geofabrik Turkey "
                    f"{snapshot_date} snapshot"
                )
            else:
                waypoint["geometrySource"] = (
                    f"Published stop coordinate ({waypoint['sourceKey']}), "
                    f"checked {snapshot_date}"
                )


def osrm_route(base_url: str, item: dict[str, Any]) -> dict[str, Any]:
    coordinates = ";".join(
        f"{waypoint['longitude']:.7f},{waypoint['latitude']:.7f}"
        for waypoint in item["stops"]
    )
    query = urllib.parse.urlencode(
        {"overview": "full", "geometries": "geojson", "steps": "false"}
    )
    url = f"{base_url.rstrip('/')}/route/v1/driving/{coordinates}?{query}"
    try:
        with urllib.request.urlopen(url, timeout=120) as response:
            payload = json.load(response)
    except (urllib.error.URLError, TimeoutError) as error:
        raise RuntimeError(f"OSRM failed for {item['name']}: {error}") from error

    if payload.get("code") != "Ok" or len(payload.get("routes", [])) != 1:
        raise RuntimeError(
            f"OSRM returned {payload.get('code')!r} for {item['name']}"
        )
    result = payload["routes"][0]
    geometry = result.get("geometry")
    if (
        not isinstance(geometry, dict)
        or geometry.get("type") != "LineString"
        or len(geometry.get("coordinates", [])) < 2
        or result.get("distance", 0) <= 0
        or result.get("duration", 0) <= 0
    ):
        raise RuntimeError(f"OSRM returned unhealthy geometry for {item['name']}")

    built = dict(item)
    built["distanceMeters"] = round(float(result["distance"]), 1)
    built["durationSeconds"] = round(float(result["duration"]), 1)
    built["geometry"] = geometry
    return built


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--osrm-url", required=True)
    parser.add_argument("--snapshot-date", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()

    apply_snapshot_date(ROUTES, args.snapshot_date)
    validate_sources(ROUTES)
    built = [osrm_route(args.osrm_url, item) for item in ROUTES]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(
        json.dumps(
            {"sourceSnapshot": args.snapshot_date, "routes": built},
            ensure_ascii=False,
            indent=2,
        )
        + "\n",
        encoding="utf-8",
    )
    print(
        f"Wrote {len(built)} routes and "
        f"{sum(len(item['stops']) for item in built)} stops to {args.output}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
