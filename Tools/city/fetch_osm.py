"""Download the OpenStreetMap layers the city compiler uses (ODbL) into .context/city/osm/.

Geometry of buildings and streets always comes from the municipal DBT; OSM supplies traffic
attributes for map-matching onto the surveyed centrelines, names, uses and points of interest.
Raw downloads stay in .context (not versioned); roads.py and places.py distil what the game needs.

Big responses (the road geometry) used to die mid-transfer on the public mirrors (curl error 56),
so every query is fetched gzip-compressed and the heavy ones are split into a grid of bounding
boxes, cached per part under osm/parts/ and merged by element id. A part that keeps failing is
split in four and retried.

Run:  python3 Tools/city/fetch_osm.py [name ...]      (no names: everything that is not cached)
"""
import json
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / ".context/city/osm"
BOX = (45.33, 11.79, 45.47, 11.99)   # south, west, north, east: the Comune di Padova and its ring road
# The main instance answers most reliably (with bursts of 502/504 when busy), so it is tried twice as often.
# overpass.kumi.systems and overpass.private.coffee accepted connections but never answered in November 2026.
MAIN = "https://overpass-api.de/api/interpreter"
MIRRORS = [MAIN, MAIN, "https://maps.mail.ru/osm/tools/overpass/api/interpreter"]
# name: (query with {b} for the bounding box, grid splits per side)
QUERIES = {
    "roads": ('[out:json][timeout:240];(way["highway"]({b});way["railway"~"^(tram|rail)$"]({b}););out tags geom;', 4),
    "road_nodes": ('[out:json][timeout:120];node["highway"~"^(mini_roundabout|turning_circle|stop|give_way|motorway_junction)$"]({b});out;', 1),
    "buildings": ('[out:json][timeout:240];(way["building"]({b});relation["building"]({b}););out tags center;', 1),
    "points": ('[out:json][timeout:240];(node["amenity"]({b});node["shop"]({b});node["tourism"]({b});node["historic"]({b});'
               'node["highway"~"^(traffic_signals|crossing|bus_stop)$"]({b});node["natural"="tree"]({b});node["place"]({b});'
               'node["railway"~"^(tram_stop|station)$"]({b}););out;', 1),
    "areas": ('[out:json][timeout:240];(way["leisure"~"^(park|pitch|stadium|garden)$"]({b});way["amenity"~"^(parking|university|hospital|school|place_of_worship|marketplace)$"]({b});'
              'way["historic"]({b});way["tourism"]({b});way["place"="square"]({b});way["highway"="pedestrian"]["area"="yes"]({b});'
              'relation["historic"]({b});relation["amenity"~"^(university|hospital)$"]({b}););out tags center;', 1),
    # Named things the first `areas` query misses (kept separate so the shared cache stays valid).
    "named": ('[out:json][timeout:240];(wr["amenity"]["name"]({b});wr["shop"]["name"]({b});wr["tourism"]["name"]({b});'
               'wr["leisure"]["name"]({b});wr["landuse"~"^(cemetery|religious)$"]({b});wr["man_made"~"^(bridge|tower|observatory)$"]({b});'
               'wr["place"]["name"]({b});relation["boundary"="administrative"]["admin_level"~"^(9|10)$"]({b});'
               'wr["railway"="station"]({b});wr["public_transport"="station"]({b});wr["building"~"^(church|cathedral|basilica|chapel|train_station|stadium)$"]["name"]({b});'
               'way["bridge"]["name"]({b});way["bridge:name"]({b});node["man_made"~"^(tower|observatory)$"]({b});node["leisure"]["name"]({b});'
               'node["public_transport"="station"]({b});node["barrier"="city_wall"]["name"]({b});way["barrier"="city_wall"]({b}););out tags center;', 1),
}


def request(query):
    """One Overpass call through curl (system TLS; the stock macOS Python 3.9 cannot negotiate with some mirrors)."""
    last = ""
    for attempt in range(12):
        url = MIRRORS[attempt % len(MIRRORS)]
        try:
            run = subprocess.run(["curl", "-sS", "--fail", "--compressed", "--max-time", "150", "-A", "padova-open-world city compiler",
                                  "--data-urlencode", "data=" + query, url], capture_output=True)
            if run.returncode:
                raise RuntimeError(f"curl {run.returncode}: {run.stderr.decode(errors='replace').strip()[-120:]}")
            data = json.loads(run.stdout)
            if "remark" in data:        # Overpass reports timeouts and memory limits inside a 200 response
                raise RuntimeError(data["remark"])
            data["elements"]
            return data, url
        except Exception as error:  # noqa: BLE001 - mirrors fail in many ways; try the next one
            last = repr(error)[:200]
            print("  retry", attempt, url, last, flush=True)
            time.sleep(10 + attempt * 5)
    raise RuntimeError(f"all mirrors failed: {last}")


def fetch_box(name, template, box, depth=0):
    """Elements for one bounding box, cached; a box that cannot be downloaded is split in four."""
    part = OUT / "parts" / f"{name}_{box[0]:.5f}_{box[1]:.5f}_{box[2]:.5f}_{box[3]:.5f}.json"
    if part.exists():
        return [json.loads(part.read_text())]
    try:
        data, url = request(template.format(b=",".join(f"{v:.5f}" for v in box)))
    except RuntimeError as error:
        if depth >= 3:
            raise SystemExit(f"Could not download {name} {box}: {error}")
        print(" ", name, box, "failed; splitting in four", flush=True)
        return [d for sub in split(box, 2) for d in fetch_box(name, template, sub, depth + 1)]
    part.parent.mkdir(parents=True, exist_ok=True)
    part.write_text(json.dumps(data, ensure_ascii=False, separators=(",", ":")))
    print(" ", name, box, "ok", len(data["elements"]), url, flush=True)
    return [data]


def split(box, n):
    s, w, north, e = box
    return [(s + (north - s) * j / n, w + (e - w) * i / n, s + (north - s) * (j + 1) / n, w + (e - w) * (i + 1) / n)
            for j in range(n) for i in range(n)]


def fetch(name):
    target = OUT / f"{name}.json"
    if target.exists() and target.stat().st_size > 1000:
        print(name, "cached", target.stat().st_size)
        return
    template, n = QUERIES[name]
    parts = [d for box in split(BOX, n) for d in fetch_box(name, template, box)]
    # A way that crosses a part boundary comes back whole from both sides: keep one copy per id.
    elements = {(e["type"], e["id"]): e for d in parts for e in d["elements"]}
    merged = {"version": parts[0].get("version"), "generator": parts[0].get("generator"),
              "osm3s": {"timestamp_osm_base": min(d["osm3s"]["timestamp_osm_base"] for d in parts),
                        "copyright": parts[0]["osm3s"]["copyright"]},
              "elements": [elements[k] for k in sorted(elements)]}
    target.write_text(json.dumps(merged, ensure_ascii=False, separators=(",", ":")))
    print(name, "ok", target.stat().st_size, "bytes", len(merged["elements"]), "elements from", len(parts), "parts")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for key in sys.argv[1:] or QUERIES:
        fetch(key)
