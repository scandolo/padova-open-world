"""Compile the Comune di Padova survey into streamed city packs for the Unity runtime.

One rule set turns public data into every building, street, canal, garden wall and tree in the
comune. Nothing here is specific to a landmark: a basilica, a warehouse and a garden shed take the
same path and differ only by the attributes the survey (and, for names/uses, OpenStreetMap) gives.

Evidence (never altered): DBT 2007 volume-unit outlines, eave elevations (QGR), heights (AV),
portico clearances (INH), building type/use (EDIFC), carriageways, sidewalks, cycle and tram areas,
water, green areas by type, sports grounds, dividing walls with heights, tree rows, house numbers
and street centrelines. Typology (labelled as such in docs/CITY_ENGINE.md): roof forms and pitch,
façade vocabulary per class, tree species and scatter inside mapped green areas.

Output: Assets/StreamingAssets/City/
  city.json            metadata, tables, attribution
  terrain.bin          ground grid interpolated from surveyed base elevations
  r_<x>_<z>.bin        1024 m regions of sixteen 256 m tiles (binary, little-endian; see TileWriter)

Run:  .context/city/venv/bin/python Tools/city/compile_city.py [--radius 2000] [--jobs 8]
"""
import argparse
import json
import math
import multiprocessing
import struct
import sys
import time
import zlib
from collections import defaultdict
from pathlib import Path

import mapbox_earcut
import numpy as np
import shapefile
import shapely
from shapely.geometry import LineString, MultiPolygon, Point, Polygon, box
from shapely.geometry.polygon import orient
from shapely.ops import unary_union
from shapely.strtree import STRtree

ROOT = Path(__file__).resolve().parents[2]
sys.path[:0] = [str(ROOT / "Tools/geodata"), str(ROOT / "Tools/architecture")]
import padova  # noqa: E402
import plan_city as pc  # noqa: E402

AREAS = ROOT / ".context/geodata/padova-dbt"
LINES = ROOT / ".context/city/dbt/linee/ComunePadova_linee"
POINTS = ROOT / ".context/city/dbt/punti/ComunePadova_punti"
OSM = ROOT / ".context/city/osm"
OUT = ROOT / "Assets/StreamingAssets/City"
TILE, REGION_TILES, GRID = 256.0, 4, 64.0
VERSION = 1

# ---------------------------------------------------------------- tables shared with the runtime
CLASSES = ["townhouse", "palazzo", "condo", "house", "shed", "church", "tower", "civic", "commercial",
           "minor", "rural", "station", "sport", "office"]
C = {name: i for i, name in enumerate(CLASSES)}
ROOF_NONE, ROOF_FLAT, ROOF_DOME, ROOF_SPIRE = 0, 1, 2, 3          # per-unit roof (0: covered by a roof record)
KINDS = ["base", "asphalt", "sidewalk", "paving", "cycle", "tram", "rail", "parking", "roadside", "garden",
         "lawn", "grove", "flowerbed", "hedge", "courtyard", "wood", "field", "meadow", "water", "pitch",
         "clay", "pool", "portico", "bridge", "island"]
K = {name: i for i, name in enumerate(KINDS)}
# Rendering level above the terrain (m). Typology except water, which approximates banks below street level.
LEVEL = {"sidewalk": 0.13, "portico": 0.14, "cycle": 0.02, "island": 0.16, "hedge": 0.0, "water": -1.7, "pool": -0.25,
         "bridge": 0.13, "flowerbed": 0.1}
U_SUSPENDED, U_MINOR, U_SIMPLE, U_CAP, U_HERO = 1, 2, 4, 8, 16
F_EXPOSED, F_GABLE = 1, 2
STREET, UPPER, PORTICO_BACK, BLANK, ARCADE = range(5)
MERGE_TOLERANCE, PROBE, SAMPLE = 0.35, 0.35, 0.9


def crc(text):
    return zlib.crc32(text.encode()) & 0xFFFFFFFF


def rnd(text):
    return crc(text) / 4294967296.0


# ---------------------------------------------------------------- reading the survey
def to_local(geom):
    """Project a lon/lat shapely geometry to local metres (EPSG:7791 rebased), vectorised."""
    def f(coords):
        e, n = padova.PROJECT.transform(coords[:, 0], coords[:, 1])
        return np.column_stack([np.asarray(e) - padova.E0, np.asarray(n) - padova.N0])
    return shapely.transform(geom, f)


_LAYERS = {}


def read_layer(path, clip=None):
    """(attributes, local geometry, source row) for a shapefile; invalid polygons are skipped, never repaired."""
    key = str(path)
    if key in _LAYERS:
        return _LAYERS[key]
    reader = shapefile.Reader(key, encoding="latin1")
    fields = [f[0] for f in reader.fields[1:]]
    lonlat = None
    if clip is not None:
        x0, z0, x1, z1 = clip.bounds
        lon0, lat0 = padova.PROJECT.transform(x0 + padova.E0, z0 + padova.N0, direction="INVERSE")
        lon1, lat1 = padova.PROJECT.transform(x1 + padova.E0, z1 + padova.N0, direction="INVERSE")
        lonlat = (lon0, lat0, lon1, lat1)
    rows, geoms = [], []
    for i in range(len(reader)):
        s = reader.shape(i)
        if not s.points:
            continue
        if lonlat and (s.bbox[2] < lonlat[0] or s.bbox[0] > lonlat[2] or s.bbox[3] < lonlat[1] or s.bbox[1] > lonlat[3]):
            continue
        try:
            geoms.append(shapely.force_2d(shapely.geometry.shape(s.__geo_interface__)))
            rows.append(i)
        except Exception:  # noqa: BLE001 - malformed source parts are skipped, never repaired
            pass
    out = []
    if geoms:
        for i, g in zip(rows, to_local(np.array(geoms, dtype=object))):
            if g.is_empty or g.geom_type in ("Polygon", "MultiPolygon") and not g.is_valid:
                continue
            if clip is not None and not g.intersects(clip):
                continue
            out.append((dict(zip(fields, reader.record(i))), g, i))
    _LAYERS[key] = out
    return out


class Unit:
    __slots__ = ("id", "num", "part", "poly", "raw", "por", "top", "roof_top", "base", "height", "inh", "ground",
                 "suspended", "under", "minor", "ridge", "b", "cls", "roof", "hero", "cx", "cz", "group", "name")

    def solid(self):
        return (self.under if self.suspended else -math.inf, self.roof_top)


def clean(poly):
    p = orient(poly.simplify(0.05, preserve_topology=True), 1.0)
    return p if p.is_valid and not p.is_empty else orient(poly, 1.0)


def load_units(clip):
    units = []
    for attrs, geom, _ in read_layer(AREAS / "UN_VOL", clip):
        top, height = attrs["UN_VOL_QGR"] - padova.VERTICAL_ORIGIN, attrs["UN_VOL_AV"]
        if not (0 < height < 150 and attrs["UN_VOL_QGR"] > 0):
            continue
        for part, poly in enumerate(padova.polygons(geom)):
            if poly.area < 3:
                continue
            u = Unit()
            u.num, u.part = int(attrs["OBJECTID"]), part
            u.id = f"UN_VOL:{u.num}" + ("" if part == 0 else f"#{part}")
            u.raw, u.poly = orient(poly, 1.0), clean(poly)
            u.por, u.top, u.height, u.inh = attrs["UN_VOL_POR"], top, height, attrs["UN_VOL_INH"]
            u.base = top - height
            u.suspended = u.por in ("02", "03") and 2 < u.inh < height - 1.5
            u.under = u.base + u.inh if u.suspended else 0.0
            u.minor, u.ridge = False, 0.0
            units.append(u)
    minor_cat = {i: a["MNV_CAT"] for a, _, i in read_layer(AREAS / "MN_EDI_VOL", clip)}
    for attrs, geom, index in read_layer(AREAS / "MN_UVOL", clip):
        top, height = attrs["MN_UVO_QGR"] - padova.VERTICAL_ORIGIN, attrs["MN_UVO_ALT"]
        if not (0.8 < height < 40 and attrs["MN_UVO_QGR"] > 0):
            continue
        for part, poly in enumerate(padova.polygons(geom)):
            if poly.area < 3:
                continue
            u = Unit()
            u.num, u.part = 0x40000000 | index, part
            u.id = f"MN_UVOL:{index}" + ("" if part == 0 else f"#{part}")
            u.raw, u.poly = orient(poly, 1.0), clean(poly)
            u.por, u.top, u.height, u.inh = "01", top, height, 0.0
            u.base, u.suspended, u.under, u.minor = top - height, False, 0.0, True
            ridge = attrs["MN_UVO_QCO"] - padova.VERTICAL_ORIGIN
            u.ridge = ridge if attrs["MN_UVO_QCO"] > 0 and 0.2 < ridge - top < 8 else 0.0
            u.name = minor_cat.get(index, "")
            units.append(u)
    for u in units:
        c = u.poly.representative_point()
        u.cx, u.cz = c.x, c.y
        u.roof_top, u.b, u.cls, u.roof, u.hero, u.group = u.top, -1, C["house"], ROOF_NONE, False, None
        if not u.minor:
            u.name = ""
    return units


# ---------------------------------------------------------------- terrain
class Terrain:
    """Ground interpolated from surveyed unit base elevations (QGR - AV); a navigation surface, not a scan."""

    def __init__(self, units, extent):
        self.x0, self.z0 = extent[0], extent[1]
        self.nx = int(round((extent[2] - extent[0]) / GRID)) + 1
        self.nz = int(round((extent[3] - extent[1]) / GRID)) + 1
        cells = defaultdict(list)
        for u in units:
            if u.por == "01" and not u.minor and 3 < u.height < 80 and -12 < u.base < 12:
                cells[(int((u.cx - self.x0) // GRID), int((u.cz - self.z0) // GRID))].append(u.base)
        grid = np.full((self.nz, self.nx), np.nan)
        for j in range(self.nz):
            for i in range(self.nx):
                near = [v for dj in range(-2, 2) for di in range(-2, 2) for v in cells.get((i + di, j + dj), ())]
                if len(near) >= 3:
                    grid[j, i] = float(np.median(near))
        # Fill unsurveyed countryside from the nearest surveyed nodes, then relax to a smooth plain.
        known = ~np.isnan(grid)
        while not known.all():
            padded = np.pad(grid, 1, constant_values=np.nan)
            stack = np.stack([padded[1 + dj:1 + dj + self.nz, 1 + di:1 + di + self.nx]
                              for dj in (-1, 0, 1) for di in (-1, 0, 1)])
            with np.errstate(all="ignore"):
                fill = np.nanmean(stack, axis=0)
            grid = np.where(known, grid, fill)
            known = ~np.isnan(grid)
        for _ in range(3):
            padded = np.pad(grid, 1, mode="edge")
            grid = sum(padded[1 + dj:1 + dj + self.nz, 1 + di:1 + di + self.nx]
                       for dj in (-1, 0, 1) for di in (-1, 0, 1)) / 9.0
        self.grid = grid.astype(np.float32)
        core = json.loads((ROOT / "Assets/Geography/PadovaCentro/CityPlan.json").read_text())
        self.plane, self.core = core["plane"], core["extent"]

    def at(self, x, z):
        fx = min(max((x - self.x0) / GRID, 0), self.nx - 1.001)
        fz = min(max((z - self.z0) / GRID, 0), self.nz - 1.001)
        i, j = int(fx), int(fz)
        a, b = fx - i, fz - j
        g = self.grid
        h = (g[j, i] * (1 - a) + g[j, i + 1] * a) * (1 - b) + (g[j + 1, i] * (1 - a) + g[j + 1, i + 1] * a) * b
        c = self.core
        d = math.hypot(max(c[0] - x, 0, x - c[2]), max(c[1] - z, 0, z - c[3]))
        mix = min(1.0, d / 120)
        mix = mix * mix * (3 - 2 * mix)
        return (self.plane[0] * x + self.plane[1] * z + self.plane[2]) * (1 - mix) + float(h) * mix


# ---------------------------------------------------------------- buildings and classes
def osm_buildings():
    path = OSM / "buildings.json"
    if not path.exists():
        print("  (no OSM building tags: run Tools/city/fetch_osm.py for names and uses)")
        return []
    out = []
    for e in json.loads(path.read_text())["elements"]:
        tags, centre = e.get("tags", {}), e.get("center")
        if not centre:
            continue
        if len(tags) <= 1 and tags.get("building") == "yes":
            continue
        x, z = padova.local(centre["lon"], centre["lat"])
        out.append((x, z, tags))
    return out


OSM_CLASS = {"church": "church", "cathedral": "church", "chapel": "church", "basilica": "church", "monastery": "church",
             "industrial": "shed", "warehouse": "shed", "hangar": "shed", "retail": "commercial",
             "supermarket": "commercial", "commercial": "commercial", "school": "civic", "university": "civic",
             "hospital": "civic", "public": "civic", "civic": "civic", "government": "civic", "college": "civic",
             "kindergarten": "civic", "train_station": "station", "transportation": "station",
             "apartments": "condo", "office": "office", "house": "house", "detached": "house",
             "semidetached_house": "house", "villa": "house", "garage": "minor", "garages": "minor", "shed": "minor",
             "hut": "minor", "roof": "minor", "carport": "minor", "greenhouse": "minor", "tower": "tower",
             "bell_tower": "tower", "sports_hall": "sport", "stadium": "sport", "grandstand": "sport"}


def classify(units, clip, historic):
    """Group volume units into surveyed buildings (EDIFC) and give each a typology class."""
    edifc = read_layer(AREAS / "EDIFC", clip)
    polys = [g for _, g, _ in edifc]
    tree = STRtree(polys)
    points = shapely.points([u.cx for u in units], [u.cz for u in units])
    pairs = tree.query(points, predicate="within")
    for ui, bi in zip(*pairs):
        units[ui].b = int(bi)
    members = defaultdict(list)
    for u in units:
        if u.b >= 0:
            members[u.b].append(u)
    tags = [dict() for _ in polys]
    osm = osm_buildings()
    if osm:
        hits = tree.query(shapely.points([o[0] for o in osm], [o[1] for o in osm]), predicate="within")
        for oi, bi in zip(*hits):
            for k, v in osm[oi][2].items():
                tags[bi].setdefault(k, v)
    info = [None] * len(polys)
    for bi, (attrs, geom, source_index) in enumerate(edifc):
        us = members.get(bi, [])
        ty, uso, t = attrs["EDIFC_TY"], attrs["EDIFC_USO"], tags[bi]
        height = max((u.height for u in us if not u.suspended), default=0)
        area = geom.area
        c = geom.representative_point()
        inside = historic.contains(c)
        cls = OSM_CLASS.get(t.get("building", ""))
        if t.get("amenity") == "place_of_worship":
            cls = "church"
        if cls is None:
            if ty == "11" or uso.startswith("05"):
                cls = "church"
            elif ty in ("07", "50"):
                cls = "tower"
            elif ty == "08" or uso.startswith("08"):
                cls = "shed"
            elif uso.startswith("07"):
                cls = "commercial"
            elif uso.startswith(("02", "03", "04", "1001")):
                cls = "civic"
            elif uso.startswith("06"):
                cls = "station"
            elif uso.startswith("1002"):
                cls = "sport"
            elif uso.startswith("09"):
                cls = "rural"
            elif inside:
                cls = "palazzo" if area > 650 and height >= 11.5 else "townhouse"
            elif height >= 32:
                cls = "office"
            elif height >= 12.8:
                cls = "condo"
            else:
                cls = "house"
        info[bi] = {"cls": C[cls], "name": t.get("name", ""), "tags": t, "historic": inside, "area": area,
                    "height": height, "ty": ty, "uso": uso, "x": c.x, "z": c.y, "source": source_index}
        for u in us:
            u.cls = C["minor"] if u.minor else C[cls]
    for u in units:
        if u.b < 0:
            u.cls = C["minor"] if u.minor else (C["townhouse"] if historic.contains(Point(u.cx, u.cz)) else
                                                C["condo"] if u.height >= 12.8 else C["house"])
    return info, members


def refine_units(units, info, terrain):
    """Unit-level forms the survey maps as separate volumes: towers, domes and their roof kind."""
    for u in units:
        u.ground = terrain.at(u.cx, u.cz)
        area = u.poly.area
        height = u.top - min(u.ground, u.base)
        cls_name = CLASSES[u.cls]
        historic = info[u.b]["historic"] if u.b >= 0 else False
        slender = not u.minor and area < 140 and height > 24 and height / math.sqrt(area) > 2.4
        # Bell towers and medieval towers; a slender modern volume is a lift or stair core, not a campanile.
        if slender and (cls_name == "church" or historic and cls_name in ("townhouse", "palazzo", "civic")):
            u.cls = C["tower"]
            cls_name = "tower"
        round_ = 4 * math.pi * area / (u.poly.exterior.length ** 2) > 0.86 and len(u.poly.exterior.coords) > 8
        modern = cls_name in ("condo", "office", "commercial", "shed", "station", "sport") or cls_name == "civic" and not historic
        if u.minor:
            u.roof = ROOF_NONE if u.ridge else ROOF_FLAT
        elif round_ and 25 < area < 1500 and cls_name in ("church", "civic", "palazzo") and not u.suspended:
            u.roof = ROOF_DOME
        elif cls_name == "tower":
            u.roof = ROOF_SPIRE
        elif modern and (area < 60 or slender):
            u.roof = ROOF_FLAT                      # roof-top plant rooms, lift and stair cores
        elif cls_name in ("office", "commercial"):
            u.roof = ROOF_FLAT
        elif cls_name == "condo":
            u.roof = ROOF_FLAT if rnd(f"flat{u.b if u.b >= 0 else u.id}") < 0.42 or height > 26 else ROOF_NONE
        elif cls_name == "shed":
            u.roof = ROOF_FLAT if area > 2200 or rnd(f"flat{u.b}") < 0.35 else ROOF_NONE
        elif cls_name in ("civic", "station", "sport"):
            u.roof = ROOF_FLAT if not historic and rnd(f"flat{u.b}") < 0.5 else ROOF_NONE


SLOPE = {"church": 0.45, "shed": 0.14, "sport": 0.16, "station": 0.2, "condo": 0.3, "house": 0.36, "rural": 0.38,
         "minor": 0.25, "commercial": 0.12}


# ---------------------------------------------------------------- roofs
def merge_groups(units, tree):
    """Neighbouring units whose eaves agree within survey precision share one continuous roof."""
    parent = list(range(len(units)))
    lo = [u.top for u in units]
    hi = [u.top for u in units]

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i
    roofed = [u.roof == ROOF_NONE and not u.hero for u in units]
    buffered = shapely.buffer(np.array([u.raw for u in units], dtype=object), 0.06, join_style="mitre")
    left, right = tree.query(buffered, predicate="intersects")
    for i, j in zip(left, right):
        if j <= i or not (roofed[i] and roofed[j]):
            continue
        u, v = units[i], units[j]
        if abs(u.top - v.top) > MERGE_TOLERANCE or u.cls != v.cls or u.minor != v.minor or u.ridge or v.ridge:
            continue
        a, b = find(i), find(j)
        if a == b or max(hi[a], hi[b]) - min(lo[a], lo[b]) > MERGE_TOLERANCE:
            continue
        if buffered[i].intersection(buffered[j]).area > 0.4:
            parent[a] = b
            lo[b], hi[b] = min(lo[a], lo[b]), max(hi[a], hi[b])
    groups = defaultdict(list)
    for i, u in enumerate(units):
        if roofed[i]:
            groups[find(i)].append(u)
    for members in groups.values():
        top = max(m.top for m in members)
        for m in members:
            m.roof_top, m.group = top, members
    return list(groups.values())


def gable_faces(ring, exposed, slope_hint):
    """Analytic roof for a four-sided outline: eaves on two opposite sides, gable walls on the others."""
    p = [np.array(v[:2]) for v in ring]
    length = [float(np.hypot(*(p[(k + 1) % 4] - p[k]))) for k in range(4)]
    pair0, pair1 = exposed[0] + exposed[2], exposed[1] + exposed[3]
    if pair0 == pair1:
        eave = 0 if length[0] + length[2] >= length[1] + length[3] else 1   # ridge along the longer sides
    else:
        eave = 0 if pair0 > pair1 else 1
    a, b, c, d = (p[(eave + k) % 4] for k in range(4))                      # eaves: a-b and c-d
    r_bc, r_da = (b + c) / 2, (d + a) / 2

    def dist(q, s, e):
        n = np.array([-(e - s)[1], (e - s)[0]]) / max(np.hypot(*(e - s)), 1e-6)
        return abs(float(np.dot(q - s, n)))
    t_bc = (dist(r_bc, a, b) + dist(r_bc, c, d)) / 2
    t_da = (dist(r_da, a, b) + dist(r_da, c, d)) / 2
    nodes = [(*a, 0.0), (*b, 0.0), (*c, 0.0), (*d, 0.0), (*r_bc, t_bc), (*r_da, t_da)]
    e = [exposed[(eave + k) % 4] for k in range(4)]
    faces = [([0, 1, 4, 5], F_EXPOSED if e[0] else 0), ([2, 3, 5, 4], F_EXPOSED if e[2] else 0),
             ([1, 2, 4], F_GABLE), ([3, 0, 5], F_GABLE)]
    return nodes, faces


def flat_faces(poly):
    """Fallback cover at eave level (enclosed light wells, skeleton failures): one face per triangle."""
    coords, idx = earcut(poly)
    nodes = [(float(x), float(z), 0.0) for x, z in coords]
    return nodes, [([int(idx[k]), int(idx[k + 1]), int(idx[k + 2])], 0) for k in range(0, len(idx), 3)]


GABLE_SHARE = {"church": 1.0, "shed": 0.85, "sport": 0.8, "minor": 0.7, "rural": 0.6, "station": 0.6, "house": 0.5,
               "townhouse": 0.3, "civic": 0.25, "condo": 0.2, "palazzo": 0.1}


def skeleton_faces(faces, poly_exposed, gable_ends=False):
    """Index CGAL skeleton faces: shared nodes, contour edge first, exposure flag per face.
    A hip end against a neighbour's wall always becomes a gable (a roof cannot slope down into a wall);
    free ends become gables when <paramref name="gable_ends"/> says the building has that roof type."""
    index, nodes, out = {}, [], []
    for face in faces:
        m = len(face)
        zero = [k for k in range(m) if face[k, 2] < 1e-6]
        pair = next(((zero[k], zero[(k + 1) % len(zero)]) for k in range(len(zero))
                     if (zero[(k + 1) % len(zero)] - zero[k]) % m == 1), None)
        if pair is None or m > 250:
            continue
        order = [(pair[0] + k) % m for k in range(m)]
        ids = []
        for k in order:
            key = (round(float(face[k, 0]), 3), round(float(face[k, 1]), 3), round(float(face[k, 2]), 3))
            if key not in index:
                index[key] = len(nodes)
                nodes.append(key)
            ids.append(index[key])
        a, b = face[pair[0], :2], face[pair[1], :2]
        out.append((ids, F_EXPOSED if poly_exposed(a, b) else 0))
    use = defaultdict(int)
    for ids, _ in out:
        for i in ids:
            use[i] += 1
    nodes = [list(n) for n in nodes]
    for k, (ids, flags) in enumerate(out):
        if len(ids) != 3 or use[ids[2]] != 3:
            continue
        a, b, c = (nodes[i] for i in ids)
        ex, ez = b[0] - a[0], b[1] - a[1]
        length = math.hypot(ex, ez)
        # A hip end: a triangle whose apex lies about half its edge length inside the outline.
        if not 2.5 < length < 24 or c[2] < 0.6 or abs(c[2] - length / 2) > 0.3 * length:
            continue
        if flags & F_EXPOSED and not gable_ends:
            continue
        along = ((c[0] - a[0]) * ex + (c[1] - a[1]) * ez) / (length * length)
        if not 0.2 < along < 0.8:
            continue
        c[0], c[1] = a[0] + ex * along, a[1] + ez * along       # slide the ridge end out over the wall
        out[k] = (ids, F_GABLE)
    return [tuple(n) for n in nodes], out


# ---------------------------------------------------------------- binary tile writer
class Blob:
    def __init__(self):
        self.parts = []

    def w(self, fmt, *values):
        self.parts.append(struct.pack("<" + fmt, *values))

    def floats(self, values):
        self.parts.append(np.asarray(values, dtype="<f4").tobytes())

    def u16s(self, values):
        self.parts.append(np.asarray(values, dtype="<u2").tobytes())

    def bytes(self):
        return b"".join(self.parts)


def rings_of(poly):
    return [list(poly.exterior.coords)[:-1]] + [list(r.coords)[:-1] for r in poly.interiors]


def earcut(poly):
    rs = rings_of(poly)
    coords = np.array([v[:2] for r in rs for v in r], dtype=np.float64)
    ends = np.cumsum([len(r) for r in rs]).astype(np.uint32)
    return coords, mapbox_earcut.triangulate_float64(coords, ends)


# ---------------------------------------------------------------- per-region work (runs in worker processes)
G = {}   # read-only data shared with forked workers


def exposure(u, units, tree, street_tree):
    """Split every outline edge by what stands outside it at each height (same rule as the core plan)."""
    s0, s1 = u.solid()
    edges = []
    for ring in rings_of(u.poly):
        for a, b in zip(ring, ring[1:] + ring[:1]):
            a, b = np.array(a[:2]), np.array(b[:2])
            d = b - a
            length = float(np.hypot(*d))
            if length < 0.25:
                edges.append(None)
                continue
            e = d / length
            n = np.array([e[1], -e[0]])
            count = max(2, int(math.ceil(length / SAMPLE)))
            ts = (np.arange(count) + 0.5) / count
            edges.append((a, d, n, count, ts))
    live = [e for e in edges if e is not None]
    if not live:
        return [], True, []
    pts = np.vstack([e[0] + np.outer(e[4], e[1]) + e[2] * PROBE for e in live])
    geoms = shapely.points(pts[:, 0], pts[:, 1])
    hit_p, hit_u = tree.query(geoms, predicate="within")
    covers = defaultdict(list)
    for pi, ui in zip(hit_p, hit_u):
        v = units[ui]
        if v is not u:
            covers[int(pi)].append(v.solid())
    street = np.zeros(len(pts), dtype=bool)
    street[np.unique(street_tree.query(geoms, predicate="within")[0])] = True
    segs, simple, edge_flags, offset = [], True, [], 0
    for edge in edges:
        if edge is None:
            edge_flags.append(0)
            continue
        a, d, n, count, ts = edge
        states = []
        for k in range(count):
            cov = covers.get(offset + k, ())
            ground_neighbour = any(lo == -math.inf for lo, _ in cov)
            void_neighbour = any(lo != -math.inf for lo, _ in cov)
            exposed = pc.subtract((s0, s1), cov)
            arcade = u.suspended and not ground_neighbour and not void_neighbour
            key = (tuple((round(lo, 1) if lo != -math.inf else -999, round(hi, 1)) for lo, hi in exposed),
                   arcade, bool(street[offset + k]))
            states.append((ts[k], key, exposed))
        offset += count
        start, pieces = 0, 0
        for k in range(1, count + 1):
            if k < count and states[k][1] == states[start][1]:
                continue
            t0 = 0.0 if start == 0 else (states[start - 1][0] + states[start][0]) / 2
            t1 = 1.0 if k == count else (states[k - 1][0] + states[k][0]) / 2
            p0, p1 = a + d * t0, a + d * t1
            _, (_, arcade, on_street), exposed = states[start]
            base_flags = 0 if on_street else 2
            for lo, hi in exposed:
                reaches_top = hi >= s1 - 0.05
                flags = base_flags | (1 if reaches_top else 0)
                if lo == -math.inf:
                    y0 = min(u.ground, u.base) - 0.4
                    kind = STREET if reaches_top else (PORTICO_BACK if hi - u.ground > 2.2 else BLANK)
                else:
                    y0 = lo
                    if reaches_top and hi - lo >= 2.6:
                        kind = UPPER
                        if u.suspended and abs(lo - u.under) < 0.05:
                            flags |= 4
                    else:
                        kind = BLANK
                segs.append((p0[0], p0[1], p1[0], p1[1], y0, hi, kind, flags))
                pieces += 1
                if not (kind == STREET and t0 == 0.0 and t1 == 1.0):
                    simple = False
            if arcade:
                segs.append((p0[0], p0[1], p1[0], p1[1], u.ground, u.under, ARCADE, base_flags))
                simple = False
            start = k
        if pieces != 1:
            simple = False
        edge_flags.append(segs[-1][7] if pieces == 1 else 0)
    return segs, simple, edge_flags


def roof_exposed(a, b, top, own, units, tree):
    d = b - a
    length = float(np.hypot(*d))
    if length < 1e-6:
        return True
    n = np.array([d[1], -d[0]]) / length
    hits = 0
    for t in (0.25, 0.5, 0.75):
        pt = Point(*(a + d * t + n * PROBE))
        for j in tree.query(pt):
            v = units[j]
            if v.id not in own and v.solid()[1] > top - 0.3 and v.raw.contains(pt):
                hits += 1
                break
    return hits < 2


def compile_region(key):
    rx, rz = key
    units, tree, street_tree, terrain = G["units"], G["tree"], G["street_tree"], G["terrain"]
    tiles = {}
    for tx in range(rx * REGION_TILES, (rx + 1) * REGION_TILES):
        for tz in range(rz * REGION_TILES, (rz + 1) * REGION_TILES):
            blob = compile_tile(tx, tz, units, tree, street_tree, terrain)
            if blob:
                tiles[(tx, tz)] = blob
    if not tiles:
        return key, 0, {}
    header = Blob()
    header.w("4sH", b"PCR1", REGION_TILES)
    offset = 6 + REGION_TILES * REGION_TILES * 8
    body = []
    for tz in range(rz * REGION_TILES, (rz + 1) * REGION_TILES):
        for tx in range(rx * REGION_TILES, (rx + 1) * REGION_TILES):
            data = tiles.get((tx, tz), (b"",))[0]
            header.w("II", offset if data else 0, len(data))
            offset += len(data)
            body.append(data)
    payload = header.bytes() + b"".join(body)
    (OUT / f"r_{rx}_{rz}.bin").write_bytes(payload)
    stats = defaultdict(int)
    for _, s in tiles.values():
        for k, v in s.items():
            stats[k] += v
    return key, len(payload), dict(stats)


def compile_tile(tx, tz, units, tree, street_tree, terrain):
    rect = box(tx * TILE, tz * TILE, (tx + 1) * TILE, (tz + 1) * TILE)
    mine = [units[i] for i in G["unit_tiles"].get((tx, tz), ())]
    stats = defaultdict(int)
    blob = Blob()
    blob.w("4shh", b"PCT1", tx, tz)

    # ---- units
    emit = [u for u in mine if not u.hero]
    blob.w("H", len(emit))
    for u in emit:
        segs, simple, edge_flags = exposure(u, units, tree, street_tree)
        rings = rings_of(u.poly)
        cap = u.roof == ROOF_FLAT or u.suspended or u.roof == ROOF_NONE and u.group is None
        flags = (U_SUSPENDED if u.suspended else 0) | (U_MINOR if u.minor else 0) | (U_SIMPLE if simple else 0) | (U_CAP if cap else 0)
        blob.w("IBBBBIfffff", u.num, u.part, u.cls, flags, u.roof, u.b & 0xFFFFFFFF if u.b >= 0 else 0xFFFFFFFF,
               u.ground, u.roof_top, u.top, u.base, u.under)
        blob.w("B", len(rings))
        blob.u16s([len(r) for r in rings])
        blob.floats([c for r in rings for v in r for c in v[:2]])
        if simple:
            blob.parts.append(bytes(edge_flags))
        else:
            blob.w("H", len(segs))
            for s in segs:
                blob.w("ffffffBB", *s)
            stats["segs"] += len(segs)
        if cap:
            _, idx = earcut(u.poly)
            blob.w("H", len(idx))
            blob.u16s(idx)
        stats["units"] += 1

    stats["bytesUnits"] += sum(map(len, blob.parts))
    mark = sum(map(len, blob.parts))

    # ---- roofs (groups whose representative point falls in this tile)
    roofs = []
    for gi in G["group_tiles"].get((tx, tz), ()):
        members = G["groups"][gi]
        top = members[0].roof_top
        own = {m.id for m in members}
        cls_name = CLASSES[members[0].cls]
        slope = SLOPE.get(cls_name, pc.ROOF_SLOPE)
        for skeleton_key, poly in G["group_polys"][gi]:

            def exposed(a, b, top=top, own=own):
                return roof_exposed(np.asarray(a), np.asarray(b), top, own, units, tree)
            ring = rings_of(poly)[0]
            ridge = members[0].ridge
            quad = poly.simplify(0.35, preserve_topology=True)
            if not poly.interiors and len(quad.exterior.coords) == 5 and quad.area > 0.9 * poly.area:
                qring = rings_of(orient(quad, 1.0))[0]
                ex = [1 if exposed(qring[k], qring[(k + 1) % 4]) else 0 for k in range(4)]
                hip = sum(ex) == 4 and cls_name in ("house", "condo", "townhouse", "palazzo", "rural", "civic") \
                    and rnd(f"hip{members[0].b}{members[0].id}") < (0.55 if cls_name == "house" else 0.8)
                if not hip and sum(ex) >= 1:
                    nodes, faces = gable_faces(qring, ex, slope)
                    if ridge:
                        slope = max(0.05, (ridge - top) / max(max(n[2] for n in nodes), 0.1))
                    roofs.append((members[0], top, slope, nodes, faces))
                    stats["gables"] += 1
                    continue
                if sum(ex) == 0:
                    roofs.append((members[0], top, slope, *flat_faces(poly)))   # enclosed by taller walls
                    continue
            faces = G["skeletons"].get(skeleton_key)
            if not faces:
                stats["roofFailures"] += 1
                roofs.append((members[0], top, slope, *flat_faces(poly)))
                continue
            gable_ends = rnd(f"gable{members[0].b}{members[0].id}") < GABLE_SHARE.get(cls_name, 0.3)
            nodes, indexed = skeleton_faces(faces, exposed, gable_ends)
            roofs.append((members[0], top, slope, nodes, indexed))
            stats["skeletons"] += 1
    blob.w("H", len(roofs))
    for m, top, slope, nodes, faces in roofs:
        blob.w("BIffHH", m.cls, m.b & 0xFFFFFFFF if m.b >= 0 else 0xFFFFFFFF, top, slope, len(nodes), len(faces))
        blob.floats([c for n in nodes for c in n])
        for ids, flags in faces:
            blob.w("BB", len(ids), flags)
            blob.u16s(ids)
    stats["roofs"] += len(roofs)
    stats["bytesRoofs"] += sum(map(len, blob.parts)) - mark
    mark = sum(map(len, blob.parts))

    # ---- ground
    try:
        patches, curbs = ground_plan(tx, tz, rect)
    except Exception as error:  # noqa: BLE001 - one bad overlay must not lose a whole region
        print("  ground failed", tx, tz, repr(error)[:160], flush=True)
        stats["groundFailures"] += 1
        coords, idx = earcut(rect)
        patches, curbs = [(K["base"], coords.astype(np.float32), idx)], []
    blob.w("H", len(patches))
    for kind, coords, idx in patches:
        blob.w("BHI", kind, len(coords), len(idx))
        blob.floats(coords.ravel())
        blob.u16s(idx)
    blob.w("I", len(curbs))
    blob.floats(curbs)
    stats["patches"] += len(patches)
    stats["bytesGround"] += sum(map(len, blob.parts)) - mark
    mark = sum(map(len, blob.parts))

    # ---- props
    trees = G["tree_tiles"].get((tx, tz), ())
    blob.w("H", len(trees))
    for x, z, h, kind in trees:
        blob.w("ffBB", x, z, int(min(255, h * 10)), kind)
    walls = G["wall_tiles"].get((tx, tz), ())
    blob.w("H", len(walls))
    for kind, h, pts in walls:
        blob.w("BfH", kind, h, len(pts))
        blob.floats([c for p in pts for c in p])
    stats["trees"] += len(trees)
    stats["walls"] += len(walls)
    stats["bytesProps"] += sum(map(len, blob.parts)) - mark
    if not emit and not patches and not trees and not walls:
        return None
    return blob.bytes(), stats


# Ground layers in priority order: (kind, layer file, attribute filter). Earlier entries win overlaps.
GROUND_LAYERS = [
    ("water", "AB_CDA", None), ("water", "SP_ACQ", None), ("asphalt", "AC_VEI", None), ("cycle", "AC_CIC", None),
    ("tram", "AR_TRAM", None), ("rail", "SD_FER", None), ("island", "SPART", None), ("sidewalk", "AR_MARC", None),
    ("paving", "AC_PED", None), ("paving", "A_PED", None), ("parking", "SV_STR", None), ("roadside", "AR_STR", None),
    ("pool", "ATTR_SP", ("ATTR_SP_TY", ("01",))), ("clay", "ATTR_SP", ("ATTR_SP_TY", ("03",))),
    ("pitch", "ATTR_SP", ("ATTR_SP_TY", ("02", "04", "05", "06", "07", "18", "19"))),
    ("hedge", "AR_VRD", ("AR_VRD_TY", ("07",))), ("flowerbed", "AR_VRD", ("AR_VRD_TY", ("04",))),
    ("grove", "AR_VRD", ("AR_VRD_TY", ("03",))), ("lawn", "AR_VRD", ("AR_VRD_TY", ("02",))),
    ("garden", "AR_VRD", ("AR_VRD_TY", ("01",))), ("courtyard", "AATT", None), ("wood", "BOSCO", None),
    ("meadow", "PS_INC", None), ("field", "CL_AGR", None),
]


GS = 0.005   # overlay grid: survey layers do not share exact vertices, so overlays snap to 5 mm
HARD = ("asphalt", "sidewalk", "paving", "cycle", "tram", "water", "portico", "bridge", "island", "parking", "roadside", "pool")


def areal(g):
    """Polygonal part only: overlays of unaligned survey layers also leave stray lines and points."""
    if g.geom_type in ("Polygon", "MultiPolygon"):
        return g
    parts = padova.polygons(g)
    return MultiPolygon(parts) if parts else Polygon()


def _overlay(op, a, b):
    a, b = areal(a), areal(b)
    try:
        return areal(op(a, b, grid_size=GS))
    except shapely.errors.GEOSException:
        return areal(op(areal(shapely.make_valid(shapely.set_precision(a, GS))), areal(shapely.make_valid(shapely.set_precision(b, GS))), grid_size=GS))


def diff(a, b):
    return a if b.is_empty else _overlay(shapely.difference, a, b)


def inter(a, b):
    return _overlay(shapely.intersection, a, b)


def union(geoms):
    geoms = [areal(g) for g in geoms if g is not None and not g.is_empty]
    geoms = [g for g in geoms if not g.is_empty]
    if not geoms:
        return Polygon()
    try:
        return areal(shapely.union_all(np.array(geoms, dtype=object), grid_size=GS))
    except shapely.errors.GEOSException:
        fixed = [areal(shapely.make_valid(shapely.set_precision(g, GS))) for g in geoms]
        return areal(shapely.union_all(np.array(fixed, dtype=object), grid_size=GS))


def ground_plan(tx, tz, rect):
    """One surface per point: survey layers resolved by priority, clipped to the tile, with curbs.
    A plain 'base' underlay 4 cm lower covers the tile except water, so unmapped land and hairline
    cracks between independently surveyed layers never show the void."""
    layers = G["ground"]
    cut = inter(G["cuts"], rect)
    area = diff(rect, cut)
    if area.is_empty or area.area < 1:
        return [], []
    solid_ids = G["solid_tree"].query(rect)
    solid = union([G["solids"][i] for i in solid_ids])
    portico_ids = G["portico_tree"].query(rect)
    portico = diff(union([G["porticoes"][i] for i in portico_ids]), solid)
    bridge_ids = G["bridge_tree"].query(rect)
    bridges = union([G["bridges"][i] for i in bridge_ids])
    free = diff(area, solid)
    regions, decks = defaultdict(list), []
    taken = Polygon()
    if not portico.is_empty:
        g = inter(portico, free)
        if not g.is_empty:
            regions["portico"].append(g)
            taken = g
    for kind, layer, _ in GROUND_LAYERS:
        geoms, tree = layers[(kind, layer)]
        ids = tree.query(rect)
        if not len(ids):
            continue
        g = inter(union([geoms[i] for i in ids]), free)
        if kind == "water" and not bridges.is_empty:
            decks.append(inter(g, bridges))
            g = diff(g, bridges)
        g = diff(g, taken)
        if g.is_empty:
            continue
        regions[kind].append(g)
        taken = union([taken, g])
    for deck in decks:      # deck parts no road layer claimed (parapets, footways)
        deck = diff(deck, taken)
        if not deck.is_empty:
            regions["bridge"].append(deck)
            taken = union([taken, deck])
    low = union([g for kind, gs in regions.items() if LEVEL.get(kind, 0.0) < -0.1 for g in gs])
    regions["base"].append(diff(area, low.buffer(0.02)) if not low.is_empty else areal(area))
    patches, level_regions = [], []
    for kind, gs in regions.items():
        g = union(gs)
        for poly in pc.polys(g):
            if poly.area < 0.4:
                continue
            poly = poly.simplify(0.05 if kind in HARD else 0.15, preserve_topology=True)
            for part in pc.polys(poly if poly.is_valid else poly.buffer(0)):
                if part.area < 0.4:
                    continue
                coords, idx = earcut(part)
                if len(coords) > 65000 or len(idx) == 0:
                    continue
                patches.append((K[kind], coords.astype(np.float32), idx))
                if kind != "base":
                    level_regions.append((part, LEVEL.get(kind, 0.0)))
    curbs = []
    if level_regions:
        tree = STRtree([p for p, _ in level_regions])
        x0, z0, x1, z1 = rect.bounds
        shapely.prepare(solid)
        shapely.prepare(cut)
        for poly, level in level_regions:
            if level < 0.05:
                continue
            for ring in rings_of(poly):
                arr = np.array([v[:2] for v in ring])
                nxt = np.roll(arr, -1, axis=0)
                d = nxt - arr
                length = np.hypot(d[:, 0], d[:, 1])
                mid = (arr + nxt) / 2
                n = np.column_stack([d[:, 1], -d[:, 0]]) / np.maximum(length, 1e-6)[:, None]
                probes = mid + n * 0.15
                # Tile borders are cuts, not real edges; walls and hero ground need no curb either.
                ok = (length > 0.05) & (probes[:, 0] > x0) & (probes[:, 0] < x1) & (probes[:, 1] > z0) & (probes[:, 1] < z1)
                if not ok.any():
                    continue
                where = np.nonzero(ok)[0]
                pts = shapely.points(probes[ok, 0], probes[ok, 1])
                blocked = shapely.contains(solid, pts) | shapely.contains(cut, pts)
                low_level = np.full(len(pts), -0.04)
                seen = np.zeros(len(pts), dtype=bool)
                hp, hr = tree.query(pts, predicate="within")
                for pi, ri in zip(hp, hr):
                    lv = level_regions[ri][1]
                    low_level[pi] = lv if not seen[pi] else min(low_level[pi], lv)
                    seen[pi] = True
                for pi in range(len(pts)):
                    if not blocked[pi] and low_level[pi] < level - 0.05:
                        k = where[pi]
                        curbs += [arr[k, 0], arr[k, 1], nxt[k, 0], nxt[k, 1], low_level[pi], level]
    # Banks: land that borders lower water gets a wall down to the water level.
    for poly, level in level_regions:
        if level > -0.1:
            continue
        for ring in rings_of(poly):
            arr = np.array([v[:2] for v in ring])
            nxt = np.roll(arr, -1, axis=0)
            d = nxt - arr
            length = np.hypot(d[:, 0], d[:, 1])
            mid = (arr + nxt) / 2
            x0, z0, x1, z1 = rect.bounds
            ok = (length > 0.05) & (mid[:, 0] > x0 + 0.02) & (mid[:, 0] < x1 - 0.02) & (mid[:, 1] > z0 + 0.02) & (mid[:, 1] < z1 - 0.02)
            for k in np.nonzero(ok)[0]:
                # Reversed so the face looks toward the water.
                curbs += [nxt[k, 0], nxt[k, 1], arr[k, 0], arr[k, 1], level, 0.0]
    return patches, curbs


# ---------------------------------------------------------------- props
def plan_trees(clip, solids_tree, solids):
    """Tree rows are surveyed lines (FIL_AL); scatter inside mapped tree/garden areas is typology."""
    out = defaultdict(list)

    def add(x, z, h, kind):
        out[(math.floor(x / TILE), math.floor(z / TILE))].append((x, z, h, kind))
    for _, geom, index in read_layer(LINES / "FIL_AL", clip):
        for line in getattr(geom, "geoms", [geom]):
            n = max(1, int(line.length // 8.5))
            kind = 1 if rnd(f"row{index}") < 0.18 else 0     # some rows are cypress/poplar-like columns
            for k in range(n + 1):
                p = line.interpolate(k * line.length / n)
                add(p.x, p.y, 7.5 + 4.5 * rnd(f"fil{index}_{k}"), kind)
    for attrs, geom, index in read_layer(AREAS / "AR_VRD", clip):
        for type_codes, density, hmin, hmax in ((("03",), 1 / 70, 6, 13), (("01",), 1 / 420, 4, 9)):
            if attrs["AR_VRD_TY"] not in type_codes or geom.area < 40:
                continue
            inside = geom.buffer(-1.8)
            if inside.is_empty:
                continue
            rng = np.random.default_rng(crc(f"vrd{index}"))
            minx, minz, maxx, maxz = inside.bounds
            want = min(60, max(1, int(geom.area * density)))
            xs, zs = rng.uniform(minx, maxx, want * 5), rng.uniform(minz, maxz, want * 5)
            ok = shapely.contains(inside, shapely.points(xs, zs))
            placed = []
            for x, z in zip(xs[ok], zs[ok]):
                if all((x - px) ** 2 + (z - pz) ** 2 > 20 for px, pz in placed):
                    placed.append((x, z))
                    add(float(x), float(z), float(rng.uniform(hmin, hmax)), 2 if rng.random() < 0.12 else 0)
                    if len(placed) >= want:
                        break
    for _, geom, index in read_layer(AREAS / "BOSCO", clip):
        inside = geom.buffer(-2)
        if inside.is_empty:
            continue
        rng = np.random.default_rng(crc(f"bosco{index}"))
        minx, minz, maxx, maxz = inside.bounds
        want = min(400, int(geom.area / 60))
        xs, zs = rng.uniform(minx, maxx, want * 2), rng.uniform(minz, maxz, want * 2)
        ok = shapely.contains(inside, shapely.points(xs, zs))
        for x, z in list(zip(xs[ok], zs[ok]))[:want]:
            add(float(x), float(z), float(rng.uniform(8, 16)), 0)
    return out


def plan_walls(clip):
    """Surveyed dividing elements (EL_DIV) with their surveyed heights; material by type is typology."""
    out = defaultdict(list)
    for attrs, geom, _ in read_layer(LINES / "EL_DIV", clip):
        h = float(attrs["EL_DIV_H"])
        if not 0.4 <= h <= 6:
            continue
        kind = {"19": 0, "03": 1}.get(attrs["EL_DIV_TY"], 0)
        for line in getattr(geom, "geoms", [geom]):
            line = line.simplify(0.05)
            if line.length < 1.2:
                continue
            pts = [(float(x), float(z)) for x, z in line.coords]
            m = line.interpolate(0.5, normalized=True)
            out[(math.floor(m.x / TILE), math.floor(m.y / TILE))].append((kind, h, pts[:400]))
    return out


# ---------------------------------------------------------------- main
def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--radius", type=float, default=0, help="Only compile tiles within this distance of the origin (0: whole comune)")
    parser.add_argument("--jobs", type=int, default=8)
    args = parser.parse_args()
    started = time.time()

    def log(*a):
        print(f"[{time.time() - started:6.1f}s]", *a, flush=True)
    clip = box(-args.radius, -args.radius, args.radius, args.radius).buffer(300) if args.radius else None
    zones = json.loads((ROOT / "Data/City/zones.json").read_text())
    historic = Polygon([padova.local(lon, lat) for lon, lat in zones["historicCentre"]["ring"]])

    units = load_units(clip)
    log("volume units", len(units), "of which minor", sum(u.minor for u in units))
    xs, zs = [u.cx for u in units], [u.cz for u in units]
    t0x, t0z = math.floor(min(xs) / TILE) - 1, math.floor(min(zs) / TILE) - 1
    t1x, t1z = math.floor(max(xs) / TILE) + 2, math.floor(max(zs) / TILE) + 2
    extent = [t0x * TILE, t0z * TILE, t1x * TILE, t1z * TILE]
    terrain = Terrain(units, extent)
    info, members = classify(units, clip, historic)
    refine_units(units, info, terrain)
    counts = defaultdict(int)
    for u in units:
        counts[CLASSES[u.cls]] += 1
    log("buildings", len(info), "units by class", dict(counts))

    # Hero assets: units drawn by hand-built models or by the core district plan.
    core = json.loads((ROOT / "Assets/Geography/PadovaCentro/CityPlan.json").read_text())
    manifest = json.loads((ROOT / "Assets/Art/Landmarks/Landmarks.json").read_text())
    hero = {u["id"] for u in core["units"]} | set(sum(pc.RAGIONE.values(), [])) | set(manifest["replacedUnits"])
    for u in units:
        u.hero = u.id in hero
    cut_rects = [box(*core["extent"])] + [box(*manifest["ownGround"][i:i + 4]) for i in range(0, len(manifest["ownGround"]), 4)]
    log("hero units", sum(u.hero for u in units))

    tree = STRtree([u.raw for u in units])
    groups = merge_groups(units, tree)
    log("roof groups", len(groups))

    # Straight skeletons for every group outline that is not a simple quad (those are analytic gables).
    shapes, group_tiles, group_polys = [], defaultdict(list), {}
    for gi, ms in enumerate(groups):
        if len(ms) == 1:
            outline = ms[0].raw
        else:
            outline = unary_union([m.raw.buffer(0.06, join_style=2) for m in ms]).buffer(-0.06, join_style=2)
        polys = []
        for pi, poly in enumerate(pc.polys(outline)):
            poly = pc.clean(poly, 0.12)
            if poly.area < 2:
                continue
            polys.append((f"g{gi}_{pi}", poly))
        if not polys:
            continue
        c = polys[0][1].representative_point()
        group_tiles[(math.floor(c.x / TILE), math.floor(c.y / TILE))].append(gi)
        group_polys[gi] = polys
        shapes += polys
    log("skeleton inputs", len(shapes))
    skeletons = {}
    for start in range(0, len(shapes), 4000):
        skeletons.update(pc.run_skeletons(shapes[start:start + 4000]))
    log("skeletons", len(skeletons), "failed", sum(v is None for v in skeletons.values()))

    # Ground layers.
    ground = {}
    for kind, layer, flt in GROUND_LAYERS:
        geoms = []
        for attrs, geom, _ in read_layer(AREAS / layer, clip):
            if flt and attrs[flt[0]] not in flt[1]:
                continue
            if layer == "SV_STR" and attrs["SV_STR_TY"] not in ("02", "0401", "04"):
                continue
            geoms.append(geom)
        ground[(kind, layer)] = (geoms, STRtree(geoms))
    street = [g for key in (("asphalt", "AC_VEI"), ("roadside", "AR_STR"), ("paving", "AC_PED"), ("paving", "A_PED"), ("sidewalk", "AR_MARC"))
              for g in ground[key][0]]
    street = list(shapely.buffer(np.array(street, dtype=object), 0.2))
    bridges = [g for _, g, _ in read_layer(AREAS / "PONTE", clip)]
    solids = [u.raw for u in units if not u.suspended]
    porticoes = [u.raw for u in units if u.suspended]
    log("ground layers", {f"{k}:{l}": len(v[0]) for (k, l), v in ground.items()})

    trees = plan_trees(clip, None, None)
    walls = plan_walls(clip)
    log("trees", sum(map(len, trees.values())), "walls", sum(map(len, walls.values())))

    unit_tiles = defaultdict(list)
    for i, u in enumerate(units):
        unit_tiles[(math.floor(u.cx / TILE), math.floor(u.cz / TILE))].append(i)
    G.update(units=units, tree=tree, street_tree=STRtree(street), terrain=terrain, unit_tiles=unit_tiles,
             groups=groups, group_tiles=group_tiles, group_polys=group_polys, skeletons=skeletons, ground=ground,
             cuts=unary_union(cut_rects), solids=solids, solid_tree=STRtree(solids), porticoes=porticoes,
             portico_tree=STRtree(porticoes) if porticoes else STRtree([]), bridges=bridges,
             bridge_tree=STRtree(bridges) if bridges else STRtree([]), tree_tiles=trees, wall_tiles=walls)

    OUT.mkdir(parents=True, exist_ok=True)
    for old in OUT.glob("r_*.bin"):
        old.unlink()
    keys = sorted({(tx // REGION_TILES, tz // REGION_TILES) for tx in range(t0x, t1x) for tz in range(t0z, t1z)
                   if not args.radius or math.hypot((tx + .5) * TILE, (tz + .5) * TILE) < args.radius + TILE})
    totals, sizes, regions = defaultdict(int), 0, []
    context = multiprocessing.get_context("fork")
    with context.Pool(args.jobs) as pool:
        for n, (key, size, stats) in enumerate(pool.imap_unordered(compile_region, keys, chunksize=1)):
            if size:
                regions.append(list(key))
                sizes += size
                for k, v in stats.items():
                    totals[k] += v
            if n % 20 == 0:
                log("regions", n + 1, "/", len(keys), "bytes", sizes)

    header = struct.pack("<4sffffII", b"PCG1", terrain.x0, terrain.z0, GRID, 0.0, terrain.nx, terrain.nz)
    (OUT / "terrain.bin").write_bytes(header + terrain.grid.tobytes())
    meta = {"version": VERSION, "tile": TILE, "regionTiles": REGION_TILES, "extent": extent,
            "plane": terrain.plane, "coreExtent": terrain.core, "regions": sorted(regions),
            "classes": CLASSES, "kinds": KINDS, "levels": [LEVEL.get(k, 0.0) for k in KINDS], "totals": dict(totals),
            "attribution": "Comune di Padova / Regione del Veneto, DBT 2007, IODL 2.0. Names and uses: "
                           "OpenStreetMap contributors, ODbL. Roof forms, facade vocabulary and tree scatter are typology.",
            "radius": args.radius}
    (OUT / "city.json").write_text(json.dumps(meta, indent=1) + "\n")
    log("done", dict(totals), "bytes", sizes, "regions", len(regions))


if __name__ == "__main__":
    main()
