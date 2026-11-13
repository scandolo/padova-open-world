"""Pure-Python readers for the city pack: region/tile files, roads.bin and addresses.bin.

The writers are the format specification: compile_city.py (compile_region, compile_tile), roads.py
and places.py. Everything is little-endian. See Tools/city/README.md for the layouts in prose.
Only the standard library and numpy are needed, so tests and tools can check a pack without the
survey or shapely.
"""
import json
import struct
from pathlib import Path

import numpy as np

U_SUSPENDED, U_MINOR, U_SIMPLE, U_CAP, U_HERO = 1, 2, 4, 8, 16


class Cursor:
    def __init__(self, data, offset=0):
        self.data, self.at = data, offset

    def take(self, fmt):
        fmt = "<" + fmt
        values = struct.unpack_from(fmt, self.data, self.at)
        self.at += struct.calcsize(fmt)
        return values

    def array(self, dtype, count):
        out = np.frombuffer(self.data, dtype=dtype, count=count, offset=self.at)
        self.at += out.nbytes
        return out

    def raw(self, count):
        out = self.data[self.at:self.at + count]
        self.at += count
        return out


def read_tile(data):
    """One 256 m tile -> dict of plain lists and numpy arrays (see compile_city.compile_tile)."""
    c = Cursor(data)
    magic, tx, tz = c.take("4shh")
    if magic != b"PCT1":
        raise ValueError(f"not a city tile: {magic!r}")
    tile = {"tx": tx, "tz": tz, "units": [], "roofs": [], "patches": [], "trees": [], "walls": []}
    for _ in range(c.take("H")[0]):
        num, part, cls, flags, roof, building, ground, roof_top, top, base, under = c.take("IBBBBIfffff")
        sizes = c.array("<u2", c.take("B")[0])
        total = int(sizes.sum())
        coords = c.array("<f4", total * 2).reshape(-1, 2)
        unit = {"num": num, "part": part, "cls": cls, "flags": flags, "roof": roof,
                "building": None if building == 0xFFFFFFFF else building, "ground": ground, "roofTop": roof_top,
                "top": top, "base": base, "under": under,
                "rings": np.split(coords, np.cumsum(sizes)[:-1]), "edgeFlags": None, "segments": None, "cap": None}
        if flags & U_SIMPLE:
            unit["edgeFlags"] = np.frombuffer(c.raw(total), dtype="u1")
        else:
            # x0, z0, x1, z1, y0, y1, kind, flags
            unit["segments"] = [c.take("ffffffBB") for _ in range(c.take("H")[0])]
        if flags & U_CAP:
            unit["cap"] = c.array("<u2", c.take("H")[0])
        tile["units"].append(unit)
    for _ in range(c.take("H")[0]):
        cls, building, top, slope, node_count, face_count = c.take("BIffHH")
        nodes = c.array("<f4", node_count * 3).reshape(-1, 3)        # x, z, run from the eave (height = run * slope)
        faces = []
        for _ in range(face_count):
            n, flags = c.take("BB")
            faces.append((c.array("<u2", n), flags))
        tile["roofs"].append({"cls": cls, "building": None if building == 0xFFFFFFFF else building, "top": top,
                              "slope": slope, "nodes": nodes, "faces": faces})
    for _ in range(c.take("H")[0]):
        kind, vertex_count, index_count = c.take("BHI")
        tile["patches"].append({"kind": kind, "vertices": c.array("<f4", vertex_count * 2).reshape(-1, 2),
                                "triangles": c.array("<u2", index_count).reshape(-1, 3)})
    tile["curbs"] = c.array("<f4", c.take("I")[0]).reshape(-1, 6)    # x0, z0, x1, z1, low, high
    for _ in range(c.take("H")[0]):
        x, z, height, kind = c.take("ffBB")
        tile["trees"].append((x, z, height / 10.0, kind))
    for _ in range(c.take("H")[0]):
        kind, height, n = c.take("BfH")
        tile["walls"].append({"kind": kind, "height": height, "points": c.array("<f4", n * 2).reshape(-1, 2)})
    if c.at != len(data):
        raise ValueError(f"tile {tx},{tz}: {len(data) - c.at} trailing bytes")
    return tile


def read_region(path):
    """One r_<x>_<z>.bin -> {(tx, tz): tile}."""
    data = Path(path).read_bytes()
    c = Cursor(data)
    magic, n = c.take("4sH")
    if magic != b"PCR1":
        raise ValueError(f"not a city region: {magic!r}")
    tiles = {}
    for _ in range(n * n):
        offset, length = c.take("II")
        if offset:
            tile = read_tile(data[offset:offset + length])
            tiles[(tile["tx"], tile["tz"])] = tile
    return tiles


def read_city(folder):
    """(city.json metadata, {(tx, tz): tile}) for a whole pack folder."""
    folder = Path(folder)
    meta = json.loads((folder / "city.json").read_text())
    tiles = {}
    for rx, rz in meta["regions"]:
        tiles.update(read_region(folder / f"r_{rx}_{rz}.bin"))
    return meta, tiles


def read_terrain(path):
    data = Path(path).read_bytes()
    magic, x0, z0, grid, _, nx, nz = struct.unpack_from("<4sffffII", data)
    if magic != b"PCG1":
        raise ValueError(f"not a city terrain: {magic!r}")
    return {"x0": x0, "z0": z0, "grid": grid, "heights": np.frombuffer(data, "<f4", nx * nz, 28).reshape(nz, nx)}


def _names(c):
    return [c.raw(c.take("H")[0]).decode("utf-8") for _ in range(c.take("I")[0])]


def read_roads(path):
    """roads.bin -> {"names", "nodes": {x, z, kind, degree arrays}, "edges": [dict]} (see roads.py)."""
    data = Path(path).read_bytes()
    c = Cursor(data)
    magic, version = c.take("4sI")
    if magic != b"PCRD" or version != 1:
        raise ValueError(f"not a version 1 road graph: {magic!r} {version}")
    names = _names(c)
    nodes = c.array(np.dtype([("x", "<f4"), ("z", "<f4"), ("kind", "u1"), ("degree", "u1")]), c.take("I")[0])
    edges = []
    for _ in range(c.take("I")[0]):
        a, b, name, cls, flags, width, lanes, maxspeed, n = c.take("IIHBBfBBH")
        edges.append({"a": a, "b": b, "name": name, "cls": cls, "flags": flags, "width": width, "lanes": lanes,
                      "maxspeed": maxspeed, "points": c.array("<f4", n * 2).reshape(-1, 2)})
    if c.at != len(data):
        raise ValueError(f"roads: {len(data) - c.at} trailing bytes")
    return {"names": names, "nodes": nodes, "edges": edges}


def read_addresses(path):
    """addresses.bin -> {"names", "addresses": [(x, z, name index, number, letters)]} (see places.py)."""
    data = Path(path).read_bytes()
    c = Cursor(data)
    magic, version = c.take("4sI")
    if magic != b"PCAD" or version != 1:
        raise ValueError(f"not a version 1 address list: {magic!r} {version}")
    names = _names(c)
    out = []
    for _ in range(c.take("I")[0]):
        x, z, name, number, n = c.take("ffHHB")
        out.append((x, z, name, number, c.raw(n).decode("ascii")))
    if c.at != len(data):
        raise ValueError(f"addresses: {len(data) - c.at} trailing bytes")
    return {"names": names, "addresses": out}
