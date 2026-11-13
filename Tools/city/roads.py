"""Compile the drivable road graph of the whole comune from the surveyed street centrelines.

Evidence (DBT 2007, never altered): centreline geometry and connectivity (EL_STR, whose endpoints
are exactly the GZ_STR junction points), bridges and underpasses (EL_STR_SED / EL_STR_LIV), street
names (EL_STR_CVI -> TP_STR), carriageway width (measured against AC_VEI), pedestrian areas
(AC_PED / A_PED), parking areas (SV_STR type 02) and the tram centreline (EL_TRV).
OpenStreetMap (ODbL) only lends traffic attributes to the surveyed edges it runs along: one-way and
its direction, lanes, maxspeed, roundabouts, highway class, signals and mini-roundabouts.
Everything else is a gameplay default. See Tools/city/README.md for the field-by-field provenance.

Output: Assets/StreamingAssets/City/roads.bin (format in README.md) and roads.json (statistics).
Run:    .context/city/venv/bin/python Tools/city/roads.py
"""
import json
import logging
import math
import re
import struct
import sys
import time
import unicodedata
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np
import shapefile
import shapely
from shapely.geometry import LineString, MultiLineString
from shapely.strtree import STRtree

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tools/city"))
logging.getLogger("shapefile").setLevel(logging.ERROR)   # pyshp warns about hole-only source polygons
import compile_city as cc  # noqa: E402

padova = cc.padova
OUT = cc.OUT

SNAP = 0.05             # m: endpoints closer than this are one junction (the survey's are exact; see README)
LINK = 1.0              # m: a dead end this close to another dead end of the same street is a drawing gap
SAMPLE = 4.0            # m between measurement points along an edge
MATCH_RADIUS = 8.0      # m: an OSM way must run this close to the surveyed centreline ...
MATCH_ANGLE = 25.0      # ... within this bearing ...
MATCH_COVER = 0.6       # ... over this share of the edge
DUAL_MARGIN = 3.0       # m: opposite one-way carriageways this evenly spaced around one centreline ...
DUAL_SIDE = 1.0         # m: ... each at least this far to its own side of it, make the surveyed edge two-way
SIGNAL_SNAP = 30.0      # m: OSM traffic signals are mapped on the approaches; snap to the junction
MINI_SNAP = 15.0
ALLEY_WIDTH = 4.5
CLS_ALLEY, CLS_LOCAL, CLS_MAIN, CLS_FAST, CLS_PED = range(5)
CLASS_NAMES = ["alley", "local", "main", "fast", "pedestrian"]
F_AB, F_BA, F_BRIDGE, F_UNDER, F_ROUNDABOUT, F_TRAM = 1, 2, 4, 8, 16, 32
K_PLAIN, K_SIGNAL, K_MINI, K_DEAD = range(4)
DEFAULT_SPEED = {CLS_ALLEY: 30, CLS_LOCAL: 50, CLS_MAIN: 50, CLS_FAST: 90, CLS_PED: 30}
DEFAULT_WIDTH = {CLS_ALLEY: 3.5, CLS_LOCAL: 6.0, CLS_MAIN: 7.5, CLS_FAST: 10.5, CLS_PED: 4.0}
MIN_WIDTH, MAX_WIDTH = 2.0, 30.0   # m: clamp of the measured width (a car is 1.8 m wide; piazzas are wider than any street)
MEDIAN = 2.0            # m: gaps in the vehicle area narrower than this (median strips, kerbs) are part of the road
OFF_AXIS = 1.5          # m: how much wider than twice its shorter side a cross-section may count

OSM_FAST = {"motorway", "motorway_link", "trunk", "trunk_link"}
OSM_MAIN = {"primary", "primary_link", "secondary", "secondary_link", "tertiary", "tertiary_link"}
OSM_LOCAL = {"unclassified", "residential", "road", "busway"}
OSM_LANE = {"service", "track"}
OSM_PED = {"pedestrian", "living_street"}
OSM_DRIVE = OSM_FAST | OSM_MAIN | OSM_LOCAL | OSM_LANE | OSM_PED
OSM_WALK = {"footway", "path", "cycleway", "steps", "bridleway"}   # only when no drivable way matches
OSM_SPEED = {"IT:urban": 50, "IT:rural": 90, "IT:trunk": 110, "IT:motorway": 130, "walk": 10}


# ---------------------------------------------------------------- street names
# After these a leading particle opens a surname ("Via Di Lenna Ermanno"); elsewhere it stays small ("Ponte di Ferro").
SURNAME_TYPES = {"VIA", "VIALE", "VICOLO", "PIAZZA", "PIAZZETTA", "PIAZZALE", "GALLERIA", "PASSAGGIO", "CORSO", "LARGO",
                 "CORTE", "SELCIATO", "LUNGARGINE", "RIVIERA", "CAVALCAVIA", "PASSEGGIATA", "RACCORDO"}
LEADING_LOWER = {"DEL", "DELLA", "DELLO", "DEI", "DEGLI", "DELLE", "DELL'", "DI", "DE'"}
PARTICLES = {"DI", "DEL", "DELLA", "DELLO", "DEI", "DEGLI", "DELLE", "DA", "DAL", "DALLA", "DE", "E", "ED", "IN",
             "AL", "ALLA", "IL", "LA", "SUL"}
SURNAME_PARTICLES = {"DE", "DAL", "DALLA", "DI", "DA", "DEL", "DELLA", "LA"}   # capitalised when they open a surname
ELISIONS = ("DELL'", "DALL'", "ALL'", "SANT'", "D'", "L'")
# Words the registry writes after the name, in brackets: "PIAZZA ERBE (DELLE)" is Piazza delle Erbe.
LEADING = {"DEL", "DELLA", "DELLO", "DEI", "DEGLI", "DELLE", "DELL'", "DI", "DE'", "BEATO", "BEATA", "DON", "PADRE",
           "SUOR", "FRATELLI", "CARDINALE", "COLONNELLO", "GENERALE", "MONSIGNOR", "MONS.", "FRA'", "FR`"}
KEEP_APOSTROPHE = {"CA'": "Ca'", "DE'": "de'", "FRA'": "Fra'", "FR`": "Fra'"}   # truncations, not accents
ACCENT = {"A": "à", "E": "è", "I": "ì", "O": "ò", "U": "ù"}
SPELLING = {"DUPRE'": "Dupré"}                                                 # acute accent the rule cannot know
UNNAMED = {"VIABILITA' SENZA NOME"}
ROMAN = re.compile(r"^(?=[IVX])X{0,3}(IX|IV|V?I{0,3})$")


def _word(word, opens_surname):
    if word in SPELLING:
        return SPELLING[word]
    if word in KEEP_APOSTROPHE:
        return KEEP_APOSTROPHE[word]
    for prefix in ELISIONS:
        if word.startswith(prefix) and len(word) > len(prefix):
            head = prefix.capitalize() if prefix in ("SANT'",) or opens_surname else prefix.lower()
            return head + _word(word[len(prefix):], False)
    if "-" in word:
        return "-".join(_word(w, False) for w in word.split("-"))
    if ROMAN.match(word):
        return word
    if word in PARTICLES and not (opens_surname and word in SURNAME_PARTICLES):
        return word.lower()
    if len(word) > 2 and word[-1] == "'" and word[-2] in ACCENT:      # NICCOLO' -> Niccolò
        return word[:-2].capitalize() + ACCENT[word[-2]]
    if word[:1] in "\"(":
        return word[0] + _word(word[1:], False)
    return word.capitalize()


def display_name(raw):
    """Title-case a registry street name: "VIA SANTO (DEL)" -> "Via del Santo"."""
    raw = " ".join(raw.split())
    if not raw or raw in UNNAMED:
        return ""
    raw = re.sub(r"\b(CA'|DE')(?=[A-Z])", r"\1 ", raw)                # "CA'MAGNO" -> "CA' MAGNO"
    words, lead = raw.split(), None
    tail = re.search(r" \(([^)]*)\)$", raw)
    if tail and tail.group(1) in LEADING:
        words, lead = raw[:tail.start()].split(), tail.group(1)
    surname_first = words[0] in SURNAME_TYPES                         # "VIA DE AMICIS EDMONDO"
    out = [_word(w, surname_first and i == 1) for i, w in enumerate(words)]
    if lead in LEADING_LOWER and lead.endswith("L'"):
        out[1:2] = [lead.lower() + out[1]] if len(out) > 1 else [lead.lower()]   # "dell'" + "Arco"
    elif lead:
        out.insert(1, lead.lower() if lead in LEADING_LOWER else _word(lead, False))
    return " ".join(out)


def name_key(text):
    """Order- and accent-insensitive identity of a street name, to recognise the same name in OSM."""
    text = text.replace("FR`", "FRA").replace("`", "'")
    text = unicodedata.normalize("NFD", text.lower())
    text = "".join(c for c in text if not unicodedata.combining(c))
    return tuple(sorted(re.findall(r"[a-z0-9]+", text)))


def osm_roads():
    path = cc.OSM / "roads.json"
    if not path.exists():
        return None
    return json.loads(path.read_text())


def street_names(osm=None):
    """{street code: display name} for every TP_STR toponym.

    The registry lists surnames first ("VIA TOMMASEO NICCOLO'"). Where OpenStreetMap has a street
    with exactly the same words in natural order ("Via Niccolò Tommaseo"), that spelling is used;
    otherwise the registry name is title-cased by rule. The name itself is always the survey's.
    """
    osm = osm_roads() if osm is None else osm
    natural = defaultdict(Counter)
    for e in (osm or {}).get("elements", ()):
        tags = e.get("tags", {})
        if "highway" in tags and tags.get("name"):
            natural[name_key(tags["name"])][tags["name"]] += 1
    out, from_osm = {}, 0
    for record in shapefile.Reader(str(cc.AREAS / "TP_STR"), encoding="latin1").records():
        raw = " ".join(record["TP_STR_SNO"].split())
        name = display_name(raw)
        seen = natural.get(name_key(raw))
        if name and seen:
            name = sorted(seen.items(), key=lambda kv: (-kv[1], kv[0]))[0][0]
            from_osm += 1
        out[int(record["TP_STR_SCO"])] = name
    street_names.from_osm = from_osm
    return out


def name_table(names):
    """Shared by roads.bin and addresses.bin: index 0 is the empty string, then every toponym, sorted."""
    table = [""] + sorted({n for n in names.values() if n})
    return table, {n: i for i, n in enumerate(table)}


def write_names(blob, table):
    blob.w("I", len(table))
    for name in table:
        data = name.encode("utf-8")
        blob.w("H", len(data))
        blob.parts.append(data)


# ---------------------------------------------------------------- survey graph
class Edge:
    __slots__ = ("row", "a", "b", "pts", "code", "name", "bridge", "under", "length", "width", "width_source",
                 "cls", "flags", "lanes", "speed", "osm", "highway", "ped", "parking", "tram", "oneway",
                 "lanes_osm", "speed_osm", "roundabout", "limited", "why", "linked")


def load_graph(log):
    """Edges from EL_STR; nodes where endpoints coincide. Returns (nodes xy array, edges, report)."""
    rows = cc.read_layer(cc.LINES / "EL_STR")
    junctions = np.array([[g.x, g.y] for _, g, _ in cc.read_layer(cc.POINTS / "GZ_STR")])
    ends = np.array([c for _, g, _ in rows for c in (g.coords[0][:2], g.coords[-1][:2])])
    # Union endpoints closer than SNAP. In this survey 19,052 endpoint pairs coincide exactly and the
    # next closest pair is 14 mm apart (one drawing slip), then nothing below 0.3 m.
    tree = STRtree(shapely.points(ends))
    left, right = tree.query(shapely.points(ends), predicate="dwithin", distance=SNAP)
    parent = list(range(len(ends)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    gaps = []
    for i, j in zip(left.tolist(), right.tolist()):
        if i < j:
            d = float(np.hypot(*(ends[i] - ends[j])))
            if d > 1e-6:
                gaps.append(round(d, 4))
            a, b = find(i), find(j)
            if a != b:
                parent[max(a, b)] = min(a, b)
    index, xy, node_of = {}, [], []
    for i in range(len(ends)):
        r = find(i)
        if r not in index:
            index[r] = len(xy)
            xy.append(ends[r])
        node_of.append(index[r])
    xy = np.array(xy)
    edges = []
    for k, (attrs, geom, row) in enumerate(rows):
        e = Edge()
        e.row, e.a, e.b = row, node_of[2 * k], node_of[2 * k + 1]
        pts = np.asarray(geom.coords)[:, :2].copy()
        pts[0], pts[-1] = xy[e.a], xy[e.b]
        e.pts = pts
        code = attrs["EL_STR_CVI"].strip()
        e.code = int(code) if code.isdigit() else -1
        e.bridge, e.under = attrs["EL_STR_SED"] in ("03", "04"), attrs["EL_STR_LIV"] == "02"
        e.linked = False
        edges.append(e)
    # Every node must be a surveyed junction point.
    jt = STRtree(shapely.points(junctions))
    _, d = jt.query_nearest(shapely.points(xy), return_distance=True, all_matches=False)
    report = {"sourceElements": len(rows), "sourceJunctions": len(junctions), "snapTolerance": SNAP,
              "endpointPairsSnappedApart": sorted(gaps), "nodesFartherThan1mmFromAJunction": int((d > 0.001).sum())}
    log("EL_STR", len(rows), "edges,", len(xy), "nodes;", len(junctions), "GZ_STR junctions; snapped non-identical pairs", gaps)
    return xy, edges, report


def link_gaps(xy, edges, log):
    """Close the hair gaps the survey leaves inside one street: a dead end closer than LINK to another
    junction of the same street code is joined to it.

    The surveyed vertices stay; the edge gains one closing vertex at that junction.
    """
    incident = defaultdict(list)
    for e in edges:
        incident[e.a].append(e)
        incident[e.b].append(e)
    tree = STRtree(shapely.points(xy))
    links = []
    for n in sorted(k for k, es in incident.items() if len(es) == 1):
        e = incident[n][0]
        if len(incident[n]) != 1 or e.a == e.b:
            continue
        near = [(float(np.hypot(*(xy[m] - xy[n]))), int(m)) for m in tree.query(shapely.points(xy[n]), predicate="dwithin", distance=LINK)
                if m not in (e.a, e.b) and any(o.code == e.code for o in incident[m])]
        if not near:
            continue
        gap, m = min(near)
        if e.b == n:
            e.pts, e.b = np.vstack([e.pts, xy[m]]), m
        else:
            e.pts, e.a = np.vstack([xy[m], e.pts]), m
        incident[n].remove(e)
        incident[m].append(e)
        e.linked = True
        links.append({"gap": round(gap, 3), "x": round(float(xy[m][0]), 1), "z": round(float(xy[m][1]), 1), "code": e.code})
    log("gap links", links)
    return links


def components(node_count, edges):
    parent = list(range(node_count))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    for e in edges:
        a, b = find(e.a), find(e.b)
        if a != b:
            parent[max(a, b)] = min(a, b)
    groups = defaultdict(list)
    for e in edges:
        groups[find(e.a)].append(e)
    return sorted(groups.values(), key=lambda g: -sum(e.length for e in g))


# ---------------------------------------------------------------- measurements along the edges
def sample_edges(edges):
    """Points every SAMPLE metres along every edge, with the unit tangent a -> b at each."""
    P, T, owner, start = [], [], [], [0]
    for k, e in enumerate(edges):
        seg = np.diff(e.pts, axis=0)
        seg_len = np.hypot(seg[:, 0], seg[:, 1])
        cum = np.concatenate([[0], np.cumsum(seg_len)])
        n = max(2, int(round(e.length / SAMPLE)))
        d = (np.arange(n) + 0.5) * e.length / n
        i = np.clip(np.searchsorted(cum, d, side="right") - 1, 0, len(seg) - 1)
        t = (d - cum[i]) / np.maximum(seg_len[i], 1e-9)
        P.append(e.pts[i] + seg[i] * t[:, None])
        T.append(seg[i] / np.maximum(seg_len[i], 1e-9)[:, None])
        owner += [k] * n
        start.append(start[-1] + n)
    return np.vstack(P), np.vstack(T), np.array(owner), np.array(start)


def share_inside(P, owner, count, geoms):
    """Per edge, the share of its sample points inside any of the polygons."""
    hit = np.zeros(len(P), dtype=bool)
    hit[np.unique(STRtree(geoms).query(shapely.points(P), predicate="within")[0])] = True
    return np.bincount(owner, weights=hit, minlength=count) / np.bincount(owner, minlength=count)


def near_segments(P, T, seg_a, seg_b, radius, angle):
    """(sample, segment, distance, sense, side) for segments within radius running within angle of the edge.

    sense is +1 where the segment runs with the edge and -1 against it; side is the signed offset of the
    segment from the edge, positive to the left of the edge's direction.
    """
    tree = STRtree(shapely.linestrings(np.stack([seg_a, seg_b], axis=1)))
    si, gi = tree.query(shapely.points(P), predicate="dwithin", distance=radius)
    a, d = seg_a[gi], seg_b[gi] - seg_a[gi]
    length = np.hypot(d[:, 0], d[:, 1])
    t = np.clip(((P[si] - a) * d).sum(1) / length ** 2, 0, 1)
    off = a + d * t[:, None] - P[si]
    dist = np.hypot(off[:, 0], off[:, 1])
    side = T[si][:, 0] * off[:, 1] - T[si][:, 1] * off[:, 0]
    cos = (T[si] * d).sum(1) / length
    keep = np.abs(cos) >= math.cos(math.radians(angle))
    return si[keep], gi[keep], dist[keep], np.sign(cos[keep]), side[keep]


def cross_sections(P, N, area):
    """For each point P with unit normal N: (left, right) reach of the area across the centreline, in metres.

    The carriageway is followed outwards from the centreline across gaps narrower than MEDIAN (a median
    strip or a kerb between lanes belongs to the road). None where the centreline is off the area.
    """
    reach = MAX_WIDTH / 2 + MEDIAN
    chords = shapely.linestrings(np.stack([P - N * reach, P + N * reach], axis=1))
    parts, chord = shapely.get_parts(shapely.intersection(chords, area), return_index=True)
    lines = shapely.get_type_id(parts) == 1
    parts, chord = parts[lines], chord[lines]
    coords, part = shapely.get_coordinates(parts, return_index=True)
    t = ((coords - P[chord[part]]) * N[chord[part]]).sum(axis=1)
    spans = [[] for _ in range(len(P))]
    if len(coords):
        first = np.concatenate([[0], np.nonzero(np.diff(part))[0] + 1])
        for c, lo, hi in zip(chord[part[first]].tolist(), np.minimum.reduceat(t, first).tolist(), np.maximum.reduceat(t, first).tolist()):
            spans[c].append((lo, hi))
    out = []
    for iv in spans:
        iv.sort()
        at = [k for k, (lo, hi) in enumerate(iv) if lo <= 0 <= hi]
        if not at:
            out.append(None)
            continue
        lo, hi = iv[at[0]]
        k = at[0] - 1
        while k >= 0 and lo - iv[k][1] <= MEDIAN:
            lo, k = min(lo, iv[k][0]), k - 1
        k = at[0] + 1
        while k < len(iv) and iv[k][0] - hi <= MEDIAN:
            hi, k = max(hi, iv[k][1]), k + 1
        out.append((-lo, hi))
    return out


def measure_widths(edges, log):
    """Carriageway width, measured on cross-sections of the surveyed vehicle area (AC_VEI) every SAMPLE
    metres away from the junctions; the edge takes the median.

    A cross-section gives the reach of the carriageway on either side of the centreline. Where the
    centreline is centred the width is the full section; where it is not (a parking bay on one side, a
    centreline drawn off-axis) the width is capped at twice the shorter side plus OFF_AXIS, so that lanes
    laid out symmetrically around the centreline stay on the carriageway.
    Falls back to the pedestrian area, then the whole street area (AR_STR), then a default by class
    (set later, when the class is known).
    """
    sources = [("AC_VEI", [g for _, g, _ in cc.read_layer(cc.AREAS / "AC_VEI")]),
               ("AC_PED+A_PED", [g for n in ("AC_PED", "A_PED") for _, g, _ in cc.read_layer(cc.AREAS / n)]),
               ("AR_STR", [g for _, g, _ in cc.read_layer(cc.AREAS / "AR_STR")])]
    trees = [(name, geoms, STRtree(geoms)) for name, geoms in sources]
    for e in edges:
        e.width, e.width_source = 0.0, "default"
        line = LineString(e.pts)
        trim = min(6.0, e.length / 4)                      # junction mouths are wider than the street
        n = max(3, int((e.length - 2 * trim) // SAMPLE))
        d = trim + (np.arange(n) + 0.5) * (e.length - 2 * trim) / n
        P = shapely.get_coordinates(shapely.line_interpolate_point(line, d))
        T = shapely.get_coordinates(shapely.line_interpolate_point(line, np.minimum(d + 0.5, e.length))) \
            - shapely.get_coordinates(shapely.line_interpolate_point(line, np.maximum(d - 0.5, 0)))
        T /= np.maximum(np.hypot(T[:, 0], T[:, 1]), 1e-9)[:, None]
        N = np.column_stack([-T[:, 1], T[:, 0]])
        reach = line.buffer(MAX_WIDTH / 2 + MEDIAN + 1)
        for name, geoms, tree in trees:
            ids = tree.query(reach, predicate="intersects")
            if not len(ids):
                continue
            area = shapely.union_all(np.array([geoms[i] for i in np.sort(ids)], dtype=object))
            sections = [s for s in cross_sections(P, N, area) if s]
            if len(sections) < max(2, 0.4 * n):
                continue
            widths = [min(left + right, 2 * min(left, right) + OFF_AXIS) for left, right in sections]
            e.width = float(min(MAX_WIDTH, max(MIN_WIDTH, np.median(widths))))
            e.width_source = name
            break
    log("widths", dict(Counter(e.width_source for e in edges)))


# ---------------------------------------------------------------- OpenStreetMap attribute transfer
def parse_oneway(tags):
    v = tags.get("oneway")
    if v in ("yes", "true", "1"):
        return 1
    if v in ("-1", "reverse"):
        return -1
    if v is None and (tags.get("junction") in ("roundabout", "circular") or tags.get("highway") in ("motorway", "motorway_link")):
        return 1
    return 0


def parse_int(text, table=()):
    if text in table:
        return table[text]
    m = re.match(r"^\d+", text or "")
    return int(m.group()) if m else None


def load_osm_ways(osm):
    """Highway ways as local-metre polylines with the tags that matter."""
    ways = []
    for e in osm["elements"]:
        tags = e.get("tags", {})
        hw = tags.get("highway")
        if e["type"] != "way" or hw not in OSM_DRIVE | OSM_WALK or len(e.get("geometry", ())) < 2:
            continue
        if hw in OSM_WALK and tags.get("footway") in ("sidewalk", "crossing"):
            continue                                        # a pavement beside a street says nothing about the street
        if tags.get("area") == "yes":
            continue
        ways.append((e["id"], tags, np.array([[p["lon"], p["lat"]] for p in e["geometry"]])))
    lonlat = np.vstack([w[2] for w in ways])
    east, north = padova.PROJECT.transform(lonlat[:, 0], lonlat[:, 1])
    xy = np.column_stack([np.asarray(east) - padova.E0, np.asarray(north) - padova.N0])
    out, offset = [], 0
    for wid, tags, ll in ways:
        hw = tags["highway"]
        # Limited traffic (ZTL, bus-only, private streets): closed to cars, by permit, or only at certain hours.
        rules = [tags.get(k) for k in ("access", "vehicle", "motor_vehicle", "motorcar")]
        limited = hw in OSM_PED or "no" in rules or "permit" in rules or hw not in OSM_LANE and (
            "private" in rules or "motor_vehicle:conditional" in tags or "vehicle:conditional" in tags)
        out.append({"id": wid, "xy": xy[offset:offset + len(ll)], "highway": hw, "drive": hw in OSM_DRIVE,
                    "oneway": parse_oneway(tags), "lanes": parse_int(tags.get("lanes")),
                    "speed": parse_int(tags.get("maxspeed"), OSM_SPEED), "limited": limited,
                    "roundabout": tags.get("junction") in ("roundabout", "circular")})
        offset += len(ll)
    return out


def transfer_osm(edges, P, T, owner, start, osm, log):
    ways = load_osm_ways(osm)
    seg_a = np.vstack([w["xy"][:-1] for w in ways])
    seg_b = np.vstack([w["xy"][1:] for w in ways])
    seg_way = np.concatenate([np.full(len(w["xy"]) - 1, i) for i, w in enumerate(ways)])
    ok = np.hypot(*(seg_b - seg_a).T) > 0.01
    seg_a, seg_b, seg_way = seg_a[ok], seg_b[ok], seg_way[ok]
    si, gi, dist, sense, side = near_segments(P, T, seg_a, seg_b, MATCH_RADIUS, MATCH_ANGLE)
    order = np.lexsort((dist, si))
    si, wi, dist, sense, side = si[order], seg_way[gi[order]], dist[order], sense[order], side[order]
    bounds = np.concatenate([[0], np.nonzero(np.diff(si))[0] + 1, [len(si)]])
    # Per sample: the nearest drivable way decides; a walk-only way counts only where nothing drivable runs.
    picked = {}
    for lo, hi in zip(bounds[:-1], bounds[1:]):
        best = {}
        for k in range(lo, hi):                             # ascending distance: first hit per way is its nearest
            best.setdefault(int(wi[k]), (float(dist[k]), int(sense[k]), float(side[k])))
        drive = [(d, w, s, o) for w, (d, s, o) in best.items() if ways[w]["drive"]]
        if not drive:
            picked[int(si[lo])] = (min((d, w) for w, (d, _, _) in best.items())[1], 0, None, False)
            continue
        d, w, s, o = min(drive)
        direction, lanes = ways[w]["oneway"] * s, ways[w]["lanes"]
        if direction:
            # One surveyed axis between two one-way carriageways is a two-way road: traffic keeps right, so
            # the carriageway running with the edge lies to its right and the opposing one to its left.
            for d2, w2, s2, o2 in drive:
                with_edge, against = (o, o2) if direction > 0 else (o2, o)
                if ways[w2]["oneway"] * s2 == -direction and d2 <= d + DUAL_MARGIN and with_edge < -DUAL_SIDE and against > DUAL_SIDE:
                    lanes = lanes + ways[w2]["lanes"] if lanes and ways[w2]["lanes"] else None
                    direction = 0
                    break
        picked[int(si[lo])] = (w, direction, lanes, True)

    def top(votes):
        return sorted(votes.items(), key=lambda kv: (-kv[1], str(kv[0])))[0][0]
    for k, e in enumerate(edges):
        hits = [picked[s] for s in range(start[k], start[k + 1]) if s in picked]
        n = start[k + 1] - start[k]
        drive = [h for h in hits if h[3]]
        if len(drive) >= MATCH_COVER * n:
            e.osm = "drive"
            e.highway = top(Counter(ways[w]["highway"] for w, *_ in drive))
            direction = Counter(h[1] for h in drive)
            for sign in (1, -1):
                if direction[sign] >= MATCH_COVER * len(drive):
                    e.oneway = sign
            lanes = Counter(h[2] for h in drive if h[2])
            if sum(lanes.values()) >= 0.5 * len(drive):
                e.lanes_osm = top(lanes)
            speeds = Counter(ways[w]["speed"] for w, *_ in drive if ways[w]["speed"])
            if sum(speeds.values()) >= 0.5 * len(drive):
                e.speed_osm = top(speeds)
            e.roundabout = sum(ways[w]["roundabout"] for w, *_ in drive) >= 0.5 * len(drive)
            e.limited = sum(ways[w]["limited"] for w, *_ in drive) >= 0.5 * len(drive)
        elif len(hits) >= MATCH_COVER * n:
            e.osm = "walk"
    log("OSM ways", len(ways), "matched edges", dict(Counter(e.osm or "none" for e in edges)))


def classify(edges, names_raw):
    for e in edges:
        raw = names_raw.get(e.code, "")
        e.why = "surveyed pedestrian area" if e.ped >= 0.5 else "osm pedestrian street" if e.highway in OSM_PED \
            else "osm limited access" if e.limited else "osm walk-only" if e.osm == "walk" else ""
        if e.why:
            e.cls = CLS_PED
        elif e.highway in OSM_FAST or raw.split(" ")[0] in ("AUTOSTRADA", "TANGENZIALE", "RACCORDO"):
            e.cls = CLS_FAST
        elif e.highway in OSM_MAIN:
            e.cls = CLS_MAIN
        elif (e.width and e.width < ALLEY_WIDTH) or e.highway in OSM_LANE or e.parking >= 0.5:
            e.cls = CLS_ALLEY
        else:
            e.cls = CLS_LOCAL
        if not e.width:
            e.width = DEFAULT_WIDTH[e.cls]
        oneway = e.oneway if e.cls != CLS_PED else 0
        e.flags = (F_AB if oneway > 0 else 0) | (F_BA if oneway < 0 else 0) | (F_BRIDGE if e.bridge else 0) \
            | (F_UNDER if e.under else 0) | (F_ROUNDABOUT if e.roundabout else 0) | (F_TRAM if e.tram >= 0.5 else 0)
        if e.lanes_osm:
            e.lanes = min(e.lanes_osm, 12)
        elif oneway:
            e.lanes = 1 if e.width < 6.5 else 2 if e.width < 10 else 3
        else:
            e.lanes = 1 if e.width < ALLEY_WIDTH else 2 if e.width < 12 else 4
        e.speed = min(e.speed_osm, 150) if e.speed_osm and e.cls != CLS_PED else DEFAULT_SPEED[e.cls]


def strong_components(node_count, edges):
    """Tarjan over the directed graph the one-way flags imply; returns a component id per node."""
    out = defaultdict(list)
    for e in edges:
        if not e.flags & F_BA:
            out[e.a].append(e.b)
        if not e.flags & F_AB:
            out[e.b].append(e.a)
    index, low, comp, stack, on = {}, {}, [-1] * node_count, [], set()
    counter = count = 0
    for root in range(node_count):
        if root in index:
            continue
        work = [(root, 0)]
        while work:
            v, i = work[-1]
            if i == 0:
                index[v] = low[v] = counter
                counter += 1
                stack.append(v)
                on.add(v)
            recurse = False
            neighbours = out[v]
            while i < len(neighbours):
                w = neighbours[i]
                i += 1
                if w not in index:
                    work[-1] = (v, i)
                    work.append((w, 0))
                    recurse = True
                    break
                if w in on:
                    low[v] = min(low[v], index[w])
            if recurse:
                continue
            work.pop()
            if work:
                low[work[-1][0]] = min(low[work[-1][0]], low[v])
            if low[v] == index[v]:
                while True:
                    w = stack.pop()
                    on.discard(w)
                    comp[w] = count
                    if w == v:
                        break
                count += 1
    return comp


def relax_one_ways(xy, edges, log):
    """One-way flags must not cut a neighbourhood off: wherever the transferred directions leave nodes that
    cannot be both reached from and left for the main strongly connected part, drop the one-way flags on the
    edges that join them to the rest. Fast-road carriageways that enter or leave the comune are exempt:
    between the boundary and their first junction they are one-way dead ends by nature.

    Returns (flags dropped, indices of the nodes still outside the main strong component)."""
    boundary = cc.read_layer(cc.AREAS / "COMUNE")[0][1].boundary
    relaxed = 0
    for _ in range(40):
        comp = strong_components(len(xy), edges)
        main = Counter(comp).most_common(1)[0][0]
        outside = {n for n, c in enumerate(comp) if c != main}
        if not outside:
            break
        cluster = {n: n for n in outside}                   # stranded nodes joined by any edge form one cluster

        def find(i):
            while cluster[i] != i:
                cluster[i] = cluster[cluster[i]]
                i = cluster[i]
            return i
        for e in edges:
            if e.a in outside and e.b in outside:
                cluster[find(e.a)] = find(e.b)
        members, touching = defaultdict(list), defaultdict(list)
        for n in sorted(outside):
            members[find(n)].append(n)
        for e in edges:
            for r in {find(n) for n in (e.a, e.b) if n in outside}:
                touching[r].append(e)
        changed = 0
        for r, ns in members.items():
            es = touching[r]
            fast = sum(e.length for e in es if e.cls == CLS_FAST) / sum(e.length for e in es)
            if fast >= 0.5 and shapely.distance(shapely.multipoints(xy[ns]), boundary) < 150:
                continue
            one_way = [e for e in es if e.flags & (F_AB | F_BA)]
            links = [e for e in one_way if (e.a in outside) != (e.b in outside)] or one_way
            for e in links:
                e.flags &= ~(F_AB | F_BA)
            changed += len(links)
        relaxed += changed
        if not changed:
            break
    comp = strong_components(len(xy), edges)
    main = Counter(comp).most_common(1)[0][0]
    stranded = [n for n, c in enumerate(comp) if c != main]
    log("one-way flags dropped to keep the graph strongly connected:", relaxed, "; nodes outside the main strong component:", len(stranded))
    return relaxed, stranded


# ---------------------------------------------------------------- output
def simplify(pts):
    line = LineString(pts)
    out = np.asarray(line.simplify(0.2, preserve_topology=False).coords)
    if len(out) < 2 or (np.allclose(pts[0], pts[-1]) and len(out) < 4):
        out = pts                                           # a loop must keep its shape
    return out


def main():
    started = time.time()

    def log(*a):
        print(f"[{time.time() - started:6.1f}s]", *a, flush=True)
    xy, edges, report = load_graph(log)
    for e in edges:
        e.length = float(np.hypot(*np.diff(e.pts, axis=0).T).sum())
    report["gapLinks"] = link_gaps(xy, edges, log)

    # Keep the main connected component; list what the survey leaves unconnected.
    groups = components(len(xy), edges)
    total = sum(e.length for e in edges)
    boundary = cc.read_layer(cc.AREAS / "COMUNE")[0][1].boundary
    main_tree = STRtree([LineString(e.pts) for e in groups[0]])
    dropped = []
    for g in groups[1:]:
        pts = np.vstack([e.pts for e in g])
        lines = MultiLineString([e.pts for e in g])
        dropped.append({"edges": len(g), "metres": round(sum(e.length for e in g), 1),
                        "x": round(float(pts[:, 0].mean()), 1), "z": round(float(pts[:, 1].mean()), 1),
                        "codes": sorted({e.code for e in g}),
                        "metresFromComuneBoundary": round(float(shapely.distance(lines, boundary)), 1),
                        "metresFromMainGraph": round(float(main_tree.query_nearest(lines, return_distance=True, all_matches=False)[1][0]), 1),
                        "bridgeEdges": sum(e.bridge for e in g), "underpassEdges": sum(e.under for e in g)})
    report["sourceKm"] = round(total / 1000, 3)
    report["components"] = {"count": len(groups), "mainShareOfLength": round(sum(e.length for e in groups[0]) / total, 5),
                            "dropped": dropped}
    log("components", len(groups), "main share", report["components"]["mainShareOfLength"])
    keep = set(id(e) for e in groups[0])
    edges = [e for e in edges if id(e) in keep]
    used = sorted({n for e in edges for n in (e.a, e.b)})
    renumber = {n: i for i, n in enumerate(used)}
    xy = xy[used]
    for e in edges:
        e.a, e.b = renumber[e.a], renumber[e.b]

    P, T, owner, start = sample_edges(edges)
    count = len(edges)
    measure_widths(edges, log)
    ped = share_inside(P, owner, count, [g for n in ("AC_PED", "A_PED") for _, g, _ in cc.read_layer(cc.AREAS / n)])
    parking = share_inside(P, owner, count, [g for a, g, _ in cc.read_layer(cc.AREAS / "SV_STR") if a["SV_STR_TY"] == "02"])
    # Tram: the surveyed tram centreline runs inside this edge's carriageway band for most of its length.
    tram_lines = [np.asarray(g.coords)[:, :2] for _, g, _ in cc.read_layer(cc.LINES / "EL_TRV")]
    ta, tb = np.vstack([t[:-1] for t in tram_lines]), np.vstack([t[1:] for t in tram_lines])
    si, _, dist, _, _ = near_segments(P, T, ta, tb, MAX_WIDTH / 2 + 1, 30.0)
    half = np.array([max(e.width, 6.0) / 2 + 1 for e in edges])[owner]
    on_tram = np.zeros(len(P), dtype=bool)
    on_tram[si[dist <= half[si]]] = True
    tram = np.bincount(owner, weights=on_tram, minlength=count) / np.bincount(owner, minlength=count)
    for k, e in enumerate(edges):
        e.ped, e.parking, e.tram = float(ped[k]), float(parking[k]), float(tram[k])
        e.osm, e.highway, e.oneway, e.lanes_osm, e.speed_osm, e.roundabout, e.limited = "", "", 0, None, None, False, False

    osm = osm_roads()
    if osm:
        transfer_osm(edges, P, T, owner, start, osm, log)
    else:
        log("WARNING: no OSM roads (run Tools/city/fetch_osm.py roads): every traffic attribute is a default")

    # Names: by street code; a few elements carry codes the toponym layer lacks and take the named street area they lie in.
    names = street_names(osm)
    names_raw = {int(r["TP_STR_SCO"]): " ".join(r["TP_STR_SNO"].split())
                 for r in shapefile.Reader(str(cc.AREAS / "TP_STR"), encoding="latin1").records()}
    table, name_index = name_table(names)
    toponyms = [(int(a["TP_STR_SCO"]), g) for a, g, _ in cc.read_layer(cc.AREAS / "TP_STR")]
    tree = STRtree([g for _, g in toponyms])
    by_area = 0
    for k, e in enumerate(edges):
        if e.code not in names:
            hits = Counter(toponyms[j][0] for j in tree.query(shapely.points(P[start[k]:start[k + 1]]), predicate="within")[1])
            if hits and max(hits.values()) >= 0.5 * (start[k + 1] - start[k]):
                e.code = sorted(hits.items(), key=lambda kv: (-kv[1], kv[0]))[0][0]
                by_area += 1
        e.name = name_index[names.get(e.code, "")]
    classify(edges, names_raw)
    relaxed, stranded = relax_one_ways(xy, edges, log)

    # Node kinds.
    comune = cc.read_layer(cc.AREAS / "COMUNE")[0][1]
    degree = np.bincount(np.array([n for e in edges for n in (e.a, e.b)]), minlength=len(xy))
    kind = np.where(degree == 1, K_DEAD, K_PLAIN)
    junction_ids = np.nonzero(degree >= 3)[0]
    junction_tree = STRtree(shapely.points(xy[junction_ids]))
    snapped = {}
    for label, k, radius, source, key in (("miniRoundabouts", K_MINI, MINI_SNAP, "road_nodes.json", "mini_roundabout"),
                                         ("trafficSignals", K_SIGNAL, SIGNAL_SNAP, "points.json", "traffic_signals")):
        path = cc.OSM / source
        pts = [padova.local(e["lon"], e["lat"]) for e in json.loads(path.read_text())["elements"]
               if e["type"] == "node" and e.get("tags", {}).get("highway") == key] if path.exists() else []
        inside = [p for p, ok in zip(pts, shapely.contains(comune, shapely.points(pts))) if ok] if pts else []
        hit = 0
        if inside:
            src, dst = junction_tree.query_nearest(shapely.points(inside), max_distance=radius, all_matches=False)
            kind[junction_ids[dst]] = k
            hit = len(set(src.tolist()))
        snapped[label] = {"osmNodes": len(inside), "snapped": hit, "graphNodes": int((kind == k).sum())}
    log("node kinds", snapped)

    # ---- roads.bin
    blob = cc.Blob()
    blob.w("4sI", b"PCRD", 1)
    write_names(blob, table)
    node32 = xy.astype("<f4")
    blob.w("I", len(xy))
    for i in range(len(xy)):
        blob.parts.append(node32[i].tobytes())
        blob.w("BB", int(kind[i]), int(min(degree[i], 255)))
    blob.w("I", len(edges))
    vertices = 0
    for e in edges:
        pts = simplify(e.pts).astype("<f4")
        pts[0], pts[-1] = node32[e.a], node32[e.b]
        vertices += len(pts)
        blob.w("IIHBBfBBH", e.a, e.b, e.name, e.cls, e.flags, e.width, e.lanes, e.speed, len(pts))
        blob.parts.append(pts.tobytes())
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "roads.bin").write_bytes(blob.bytes())

    # ---- roads.json
    def km(select):
        return round(sum(e.length for e in edges if select(e)) / 1000, 2)
    by_class = {CLASS_NAMES[c]: {"edges": sum(e.cls == c for e in edges), "km": km(lambda e, c=c: e.cls == c)}
                for c in range(5)}
    matched = [e for e in edges if e.osm]
    widths = np.array([e.width for e in edges])
    stats = {
        "version": 1, "nodes": len(xy), "edges": len(edges), "vertices": vertices, "names": len(table),
        "km": km(lambda e: True), "byClass": by_class,
        "bbox": [round(float(v), 1) for v in (*xy.min(axis=0), *xy.max(axis=0))],
        "nodeKinds": {"plain": int((kind == K_PLAIN).sum()), "trafficSignal": int((kind == K_SIGNAL).sum()),
                      "miniRoundabout": int((kind == K_MINI).sum()), "deadEnd": int((kind == K_DEAD).sum())},
        "flags": {"oneWay": sum(bool(e.flags & (F_AB | F_BA)) for e in edges), "bridge": sum(bool(e.flags & F_BRIDGE) for e in edges),
                  "underpass": sum(bool(e.flags & F_UNDER) for e in edges), "roundabout": sum(bool(e.flags & F_ROUNDABOUT) for e in edges),
                  "tram": sum(bool(e.flags & F_TRAM) for e in edges)},
        "width": {"source": dict(Counter(e.width_source for e in edges)),
                  "percentiles": {str(p): round(float(np.percentile(widths, p)), 2) for p in (5, 25, 50, 75, 95)}},
        "osm": {"timestamp": (osm or {}).get("osm3s", {}).get("timestamp_osm_base"),
                "matchedEdges": len(matched), "matchedShareOfEdges": round(len(matched) / len(edges), 4),
                "matchedKm": km(lambda e: bool(e.osm)), "matchedShareOfKm": round(km(lambda e: bool(e.osm)) / km(lambda e: True), 4),
                "matchedAsWalkOnly": sum(e.osm == "walk" for e in edges),
                "pedestrianBecause": dict(sorted(Counter(e.why for e in edges if e.why).items())),
                "lanesFromOsm": sum(bool(e.lanes_osm) for e in edges), "maxspeedFromOsm": sum(bool(e.speed_osm) and e.cls != CLS_PED for e in edges),
                "oneWayFlagsDroppedForConnectivity": relaxed, "nodesOutsideMainStrongComponent": len(stranded),
                # Fast-road carriageways between the comune boundary and their first junction: entries can
                # only be left, exits only be reached. A router should not pick these nodes as both ends.
                "oneWayStubNodes": stranded,
                "match": {"radius": MATCH_RADIUS, "angle": MATCH_ANGLE, "cover": MATCH_COVER}, **snapped},
        "streetNames": {"toponyms": len(names), "spelledAsInOsm": street_names.from_osm,
                        "unnamedEdges": sum(e.name == 0 for e in edges), "namedByStreetArea": by_area},
        "survey": report,
        "attribution": "Geometry, connectivity, names, widths: Comune di Padova / Regione del Veneto, DBT 2007, IODL 2.0. "
                       "One-way, lanes, maxspeed, roundabouts, class, signals: OpenStreetMap contributors, ODbL.",
    }
    (OUT / "roads.json").write_text(json.dumps(stats, indent=1, ensure_ascii=False) + "\n")
    log("roads.bin", (OUT / "roads.bin").stat().st_size, "bytes;", len(xy), "nodes,", len(edges), "edges,", stats["km"], "km")
    log(json.dumps({k: stats[k] for k in ("byClass", "nodeKinds", "flags")}))
    log(json.dumps({k: v for k, v in stats["osm"].items() if k != "oneWayStubNodes"}))


if __name__ == "__main__":
    main()
