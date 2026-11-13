"""Download the OpenStreetMap layers the city compiler uses (ODbL) into .context/city/osm/.

Geometry of buildings always comes from the municipal DBT; OSM supplies road centrelines with
traffic attributes, names, uses, optional roof/level tags and points of interest.
Raw downloads stay in .context (not versioned); compile_city.py distils what the game needs.
"""
import json
import subprocess
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / ".context/city/osm"
BBOX = "45.33,11.79,45.47,11.99"   # south,west,north,east: the Comune di Padova and its ring road
MIRRORS = ["https://overpass-api.de/api/interpreter", "https://overpass.kumi.systems/api/interpreter",
           "https://overpass.private.coffee/api/interpreter"]
QUERIES = {
    "roads": f'[out:json][timeout:240];(way["highway"]({BBOX});way["railway"~"^(tram|rail)$"]({BBOX}););out tags geom;',
    "buildings": f'[out:json][timeout:240];(way["building"]({BBOX});relation["building"]({BBOX}););out tags center;',
    "points": f'[out:json][timeout:240];(node["amenity"]({BBOX});node["shop"]({BBOX});node["tourism"]({BBOX});node["historic"]({BBOX});'
              f'node["highway"~"^(traffic_signals|crossing|bus_stop)$"]({BBOX});node["natural"="tree"]({BBOX});node["place"]({BBOX});'
              f'node["railway"~"^(tram_stop|station)$"]({BBOX}););out;',
    "areas": f'[out:json][timeout:240];(way["leisure"~"^(park|pitch|stadium|garden)$"]({BBOX});way["amenity"~"^(parking|university|hospital|school|place_of_worship|marketplace)$"]({BBOX});'
             f'way["historic"]({BBOX});way["tourism"]({BBOX});way["place"="square"]({BBOX});way["highway"="pedestrian"]["area"="yes"]({BBOX});'
             f'relation["historic"]({BBOX});relation["amenity"~"^(university|hospital)$"]({BBOX}););out tags center;',
}


def fetch(name, query):
    target = OUT / f"{name}.json"
    if target.exists() and target.stat().st_size > 1000:
        print(name, "cached", target.stat().st_size)
        return
    for attempt in range(6):
        url = MIRRORS[attempt % len(MIRRORS)]
        try:
            # curl uses the system TLS stack; the stock macOS Python 3.9 cannot negotiate with some mirrors.
            body = subprocess.run(["curl", "-sS", "--fail", "--max-time", "300", "-A", "padova-open-world city compiler",
                                   "--data-urlencode", "data=" + query, url], check=True, capture_output=True).stdout
            json.loads(body)["elements"]
            target.write_bytes(body)
            print(name, "ok", len(body), url)
            return
        except Exception as error:  # noqa: BLE001 - mirrors fail in many ways; try the next one
            print(name, "retry", attempt, url, repr(error)[:200], flush=True)
            time.sleep(8 + attempt * 6)
    raise SystemExit(f"Could not download {name}")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for key in sys.argv[1:] or QUERIES:
        fetch(key, QUERIES[key])
