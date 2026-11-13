"""Named things of the comune for the map, discoveries, banners and missions.

places     discoverable landmarks, from OpenStreetMap tags (ODbL): name, kind, x, z, rank, source
pois       everyday places for missions and street life: name, kind, x, z, source (+ "surveyed" on
           fuel stations that stand on a DBT fuel-station area, SV_STR type 03)
districts  neighbourhood names with one point each
addresses  the surveyed house numbers (DBT N_CIV) joined to the surveyed street names (TP_STR)

Positions are local metres (x east, z north). Nothing is invented: a thing without a name is
skipped, a name is the one OpenStreetMap or the survey gives, a position is the OSM node or the
centre Overpass computes for a way or relation. Only things inside the comune boundary are kept.
The kinds, the rank rule and the binary layout are documented in Tools/city/README.md.

Output: Assets/StreamingAssets/City/places.json and addresses.bin
Run:    .context/city/venv/bin/python Tools/city/places.py
"""
import json
import logging
import re
import sys
import unicodedata
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np
import shapefile
import shapely
from shapely.strtree import STRtree

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tools/city"))
logging.getLogger("shapefile").setLevel(logging.ERROR)
import compile_city as cc  # noqa: E402
import roads  # noqa: E402

padova = cc.padova
OUT = cc.OUT

PLACE_KINDS = ["church", "palace", "museum", "theatre", "castle", "villa", "tower", "gate", "wall", "bridge", "monument",
               "memorial", "shrine", "ruins", "historic", "square", "park", "garden", "cemetery", "stadium", "market",
               "university", "hospital", "library", "townhall", "station", "observatory", "fountain", "cafe", "attraction"]
POI_KINDS = ["bar", "cafe", "pub", "restaurant", "pizzeria", "gelateria", "fast_food", "bakery", "supermarket", "pharmacy",
             "bank", "post_office", "hotel", "school", "police", "fuel", "parking", "taxi", "cinema", "tram_stop", "bus_stop",
             "bus_station"]
# Rank 1, the city icons: a hand-written table keyed by Wikidata id (stable across OSM edits), with the
# name people use and, where the tags would say otherwise, the kind.
ICONS = {
    "Q810139": ("Basilica del Santo", "church"),
    "Q2636862": ("Prato della Valle", "square"),
    "Q2517428": ("Palazzo della Ragione", "palace"),
    "Q963954": ("Cappella degli Scrovegni", "church"),
    "Q2407092": ("Duomo di Padova", "church"),
    "Q3360829": ("Palazzo del Bo", "university"),
    "Q3966414": ("La Specola", "observatory"),
    "Q2739545": ("Caffè Pedrocchi", "cafe"),
    "Q598892": ("Orto Botanico", "garden"),
    "Q1150874": ("Stazione di Padova", "station"),
    "Q115585": ("Stadio Euganeo", "stadium"),
    "Q219502": ("Basilica di Santa Giustina", "church"),
}
# Rank 2 without any notability tag: kinds a visitor would look for on a city map.
MAJOR_KINDS = {"station", "stadium", "cemetery", "gate", "castle", "observatory"}
DISTRICT_PLACES = ("suburb", "quarter", "neighbourhood", "hamlet", "village")
SAME_PLACE, SAME_ENTITY, SAME_POI, FUEL_REACH = 400.0, 400.0, 60.0, 40.0   # metres


def norm(text):
    text = unicodedata.normalize("NFD", text.lower())
    return " ".join(re.findall(r"[a-z0-9]+", "".join(c for c in text if not unicodedata.combining(c))))


def poi_kind(tags):
    amenity, cuisine = tags.get("amenity"), tags.get("cuisine", "")
    if amenity == "restaurant":
        return "pizzeria" if "pizza" in cuisine else "restaurant"
    if amenity == "ice_cream" or amenity == "cafe" and "ice_cream" in cuisine:
        return "gelateria"
    if amenity in ("bar", "cafe", "pub", "fast_food", "pharmacy", "bank", "post_office", "police", "fuel", "taxi",
                   "cinema", "parking", "bus_station"):
        return amenity
    if amenity in ("school", "kindergarten", "college"):
        return "school"
    if tags.get("shop") == "supermarket":
        return "supermarket"
    if tags.get("shop") in ("bakery", "pastry"):
        return "bakery"
    if tags.get("tourism") in ("hotel", "guest_house", "hostel", "motel"):
        return "hotel"
    if tags.get("railway") == "tram_stop":
        return "tram_stop"
    if tags.get("highway") == "bus_stop":
        return "bus_stop"
    return None


def notable(tags):
    """An everyday place (a cafe, a school) is a landmark only when it is also one of these."""
    return "wikidata" in tags or "wikipedia" in tags or tags.get("tourism") == "attraction"


def place_kind(tags, name):
    """The first rule that matches names the kind; None means the element is no landmark."""
    if tags.get("wikidata") in ICONS:
        return ICONS[tags["wikidata"]][1]
    if tags.get("type") == "site" or "boundary" in tags:
        return None                                    # a listing that groups several monuments, not a place
    amenity, tourism, historic, leisure = (tags.get(k) for k in ("amenity", "tourism", "historic", "leisure"))
    building, man_made, low = tags.get("building"), tags.get("man_made"), name.lower()
    if tags.get("railway") in ("station", "halt") or building == "train_station":
        return "station"
    if historic == "city_gate":
        return "gate"
    if historic in ("citywalls", "city_wall") or tags.get("barrier") == "city_wall" or low.startswith(("bastione", "baluardo", "torrion", "mura ")):
        return "wall"
    if man_made == "bridge" or "bridge:name" in tags or low.startswith("ponte ") and ("bridge" in tags or historic):
        return "bridge"
    if man_made == "observatory":
        return "observatory"
    if amenity in ("place_of_worship", "monastery") or building in ("church", "cathedral", "basilica", "chapel") \
            or historic in ("church", "monastery"):
        return "church"
    if low.startswith(("palazzo ", "loggia ", "reggia ")) and (historic or tourism in ("attraction", "museum")
                                                               or building == "historic" or "wikidata" in tags):
        return "palace"
    if tourism in ("museum", "gallery"):
        return "museum"
    if amenity == "theatre":
        return "theatre"
    if historic == "castle":
        return "castle"
    if historic in ("manor", "villa"):
        return "villa"
    if historic == "tower" or (man_made == "tower" or low.startswith("torre ")) and (historic or tourism == "attraction"):
        return "tower"
    if historic == "monument":
        return "monument"
    if historic in ("memorial", "tomb"):
        return None if tags.get("memorial") in ("stolperstein", "plaque", "ghost_bike") else "memorial"
    if historic in ("wayside_shrine", "wayside_cross"):
        return "shrine"
    if historic in ("archaeological_site", "ruins"):
        return "ruins"
    if tags.get("place") == "square" or low.startswith(("piazza", "piazzetta", "piazzale", "largo ", "prato ")) \
            and (tags.get("highway") == "pedestrian" or amenity == "marketplace" or leisure == "park"):
        return "square"
    if tags.get("landuse") == "cemetery" or amenity == "grave_yard":
        return "cemetery"
    if leisure == "stadium":
        return "stadium"
    if amenity == "marketplace":
        return "market"
    if leisure == "park":
        return "park"
    if leisure == "garden":
        return "garden"
    if amenity == "university":
        return "university"
    if amenity == "hospital":
        return "hospital"
    if amenity == "library":
        return "library"
    if amenity == "townhall":
        return "townhall"
    if amenity == "fountain":
        return "fountain"
    if amenity in ("cafe", "bar", "restaurant", "pub") and notable(tags):
        return "cafe"
    if historic:
        return "historic"
    if tourism == "attraction":
        return "attraction"
    return None


def rank_of(tags, kind):
    """1 city icon (ICONS); 2 notable: has a Wikipedia or Wikidata entry, is tagged a tourist attraction
    or museum, carries a heritage listing, or is of a kind every city map shows; 3 everything else."""
    if tags.get("wikidata") in ICONS:
        return 1
    if "wikipedia" in tags or "wikidata" in tags or tags.get("tourism") in ("attraction", "museum") \
            or any(k.startswith("heritage") for k in tags) or kind in MAJOR_KINDS:
        return 2
    return 3


def load_elements(comune):
    """Every tagged OSM element with a position, once, inside the comune: [(type, id, tags, x, z)]."""
    merged = {}
    for name in ("points", "areas", "buildings", "named"):
        path = cc.OSM / f"{name}.json"
        if not path.exists():
            print(f"  (no OSM {name}.json: run Tools/city/fetch_osm.py {name})")
            continue
        for e in json.loads(path.read_text())["elements"]:
            tags, pos = e.get("tags"), e.get("center") or (e if "lat" in e else None)
            if not tags or pos is None:
                continue
            key = (e["type"], e["id"])
            if key in merged:
                merged[key][0].update(tags)
            else:
                merged[key] = (dict(tags), pos["lon"], pos["lat"])
    keys = sorted(merged)
    east, north = padova.PROJECT.transform([merged[k][1] for k in keys], [merged[k][2] for k in keys])
    x, z = np.asarray(east) - padova.E0, np.asarray(north) - padova.N0
    inside = shapely.contains(comune, shapely.points(x, z))
    return [(k[0], k[1], merged[k][0], float(x[i]), float(z[i])) for i, k in enumerate(keys) if inside[i]]


def clusters(items, radius):
    """Group indices of items [(x, z)] that chain together within radius."""
    parent = list(range(len(items)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    for i in range(len(items)):
        for j in range(i + 1, len(items)):
            if (items[i][0] - items[j][0]) ** 2 + (items[i][1] - items[j][1]) ** 2 <= radius * radius:
                parent[find(j)] = find(i)
    groups = defaultdict(list)
    for i in range(len(items)):
        groups[find(i)].append(i)
    return list(groups.values())


def dedupe(rows, radius, identity, best):
    """One row per real thing: rows sharing identity(row) within radius collapse to the best one."""
    buckets = defaultdict(list)
    for row in rows:
        buckets[identity(row)].append(row)
    out = []
    for key in sorted(buckets, key=str):
        bucket = buckets[key]
        if key is None:
            out += bucket
            continue
        for group in clusters([(r["x"], r["z"]) for r in bucket], radius):
            members = [bucket[i] for i in group]
            keep = min(members, key=best)
            if "rank" in keep:
                keep["rank"] = min(m["rank"] for m in members)
            out.append(keep)
    return out


def build_places(elements, fuel_areas):
    places, pois = [], []
    for kind_, ident, tags, x, z in elements:
        icon = ICONS.get(tags.get("wikidata"))
        name = " ".join((tags.get("name") or "").split())
        poi = poi_kind(tags)
        if not name and poi in ("fuel", "supermarket", "bank"):
            name = " ".join((tags.get("brand") or tags.get("operator") or "").split())   # the sign on the building
        if not name:
            continue
        row = {"name": name, "kind": None, "x": round(x, 1), "z": round(z, 1), "source": f"osm {kind_} {ident}",
               "_tags": tags, "_node": kind_ == "node", "_id": (kind_, ident)}
        place = place_kind(tags, name) if (poi is None or notable(tags)) else None
        if place:
            row.update(kind=place, rank=rank_of(tags, place), name=icon[0] if icon else name)
            places.append(row)
        elif poi:
            row["kind"] = poi
            pois.append(row)

    def best(r):
        return (r.get("rank", 3), r["_node"], -len(r["_tags"]), r["_id"])
    # The same landmark mapped as a node and as an outline, or split into parts sharing one Wikidata id.
    places = dedupe(places, SAME_ENTITY, lambda r: r["_tags"].get("wikidata"), best)
    places = dedupe(places, SAME_PLACE, lambda r: norm(r["name"]), best)
    stops = [p for p in pois if p["kind"] in ("tram_stop", "bus_stop")]        # both kerbs of a stop are real stops
    pois = stops + dedupe([p for p in pois if p["kind"] not in ("tram_stop", "bus_stop")], SAME_POI,
                          lambda r: (r["kind"], norm(r["name"])), best)
    # Fuel stations cross-checked against the survey's fuel-station areas.
    tree = STRtree(fuel_areas)
    fuel = [p for p in pois if p["kind"] == "fuel"]
    confirmed = set()
    if fuel:
        pi, ai = tree.query(shapely.points([p["x"] for p in fuel], [p["z"] for p in fuel]), predicate="dwithin", distance=FUEL_REACH)
        for i in set(pi.tolist()):
            fuel[i]["surveyed"] = True
        confirmed = set(ai.tolist())
    fuel_stats = {"osm": len(fuel), "osmOnSurveyedArea": sum(1 for p in fuel if p.get("surveyed")),
                  "surveyedAreas": len(fuel_areas), "surveyedAreasWithoutOsm": len(fuel_areas) - len(confirmed)}

    def clean(rows, order):
        rows = sorted(rows, key=order)
        return [{k: v for k, v in r.items() if not k.startswith("_")} for r in rows]
    places = clean(places, lambda r: (r["rank"], r["kind"], r["name"], r["source"]))
    pois = clean(pois, lambda r: (r["kind"], r["name"], r["source"]))
    return places, pois, fuel_stats


def build_districts(elements):
    rows = [{"name": " ".join(tags["name"].split()), "kind": tags["place"], "x": round(x, 1), "z": round(z, 1),
             "source": f"osm {kind} {ident}", "_node": kind == "node", "_tags": tags, "_id": (kind, ident)}
            for kind, ident, tags, x, z in elements if tags.get("place") in DISTRICT_PLACES and tags.get("name")]
    rows = dedupe(rows, 1500.0, lambda r: norm(r["name"]), lambda r: (not r["_node"], r["_id"]))
    return [{k: v for k, v in r.items() if not k.startswith("_") and k != "rank"} for r in sorted(rows, key=lambda r: r["name"])]


def write_addresses(names):
    """addresses.bin: every surveyed house number whose street code has a toponym, sorted by street, number, letters."""
    table, index = roads.name_table(names)
    reader = shapefile.Reader(str(cc.POINTS / "N_CIV"), encoding="latin1")
    lonlat = np.array([s.points[0] for s in reader.shapes()])
    east, north = padova.PROJECT.transform(lonlat[:, 0], lonlat[:, 1])
    x, z = np.asarray(east) - padova.E0, np.asarray(north) - padova.N0
    rows, unnamed = [], 0
    for i, record in enumerate(reader.records()):
        name = index.get(names.get(int(record["N_CIV_SCOD"]), ""), 0)
        letters = record["N_CIV_LETT"].strip().strip("-")
        if not name:
            unnamed += 1
            continue
        rows.append((name, int(record["N_CIV_NUM"]), letters, float(x[i]), float(z[i])))
    rows.sort()
    blob = cc.Blob()
    blob.w("4sI", b"PCAD", 1)
    roads.write_names(blob, table)
    blob.w("I", len(rows))
    for name, number, letters, px, pz in rows:
        data = letters.encode("ascii")
        blob.w("ffHHB", px, pz, name, number, len(data))
        blob.parts.append(data)
    (OUT / "addresses.bin").write_bytes(blob.bytes())
    return {"addresses": len(rows), "surveyed": len(reader), "skippedWithoutStreetName": unnamed,
            "streets": len({r[0] for r in rows}), "names": len(table)}


def main():
    comune = cc.read_layer(cc.AREAS / "COMUNE")[0][1]
    shapely.prepare(comune)
    elements = load_elements(comune)
    fuel_areas = [g for a, g, _ in cc.read_layer(cc.AREAS / "SV_STR") if a["SV_STR_TY"] == "03"]
    places, pois, fuel_stats = build_places(elements, fuel_areas)
    districts = build_districts(elements)
    OUT.mkdir(parents=True, exist_ok=True)
    address_stats = write_addresses(roads.street_names())
    snapshots = {}
    for name in ("points", "areas", "buildings", "named"):
        path = cc.OSM / f"{name}.json"
        if path.exists():
            with path.open() as f:
                head = f.read(400)
            m = re.search(r'"timestamp_osm_base":\s*"([^"]+)"', head)
            snapshots[name] = m.group(1) if m else None
    stats = {"places": len(places), "pois": len(pois), "districts": len(districts),
             "placesByRank": dict(sorted(Counter(str(p["rank"]) for p in places).items())),
             "placesByKind": dict(sorted(Counter(p["kind"] for p in places).items())),
             "poisByKind": dict(sorted(Counter(p["kind"] for p in pois).items())),
             "fuel": fuel_stats, "addresses": address_stats, "osmSnapshots": snapshots}
    doc = {"version": 1, "placeKinds": PLACE_KINDS, "poiKinds": POI_KINDS, "stats": stats,
           "attribution": "Places, POIs and districts: OpenStreetMap contributors, ODbL. Fuel-station areas and "
                          "addresses: Comune di Padova / Regione del Veneto, DBT 2007, IODL 2.0.",
           "places": places, "pois": pois, "districts": districts}
    (OUT / "places.json").write_text(json.dumps(doc, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    print(json.dumps(stats, ensure_ascii=False, indent=1))
    print("icons found:", sorted(p["name"] for p in places if p["rank"] == 1))


if __name__ == "__main__":
    main()
