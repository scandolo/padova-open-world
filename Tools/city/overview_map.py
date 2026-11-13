"""Bake north-up map images of the whole comune and of the centre from the survey layers.

Same look as Data/World/Map.png (cream ground, pale roads, sage green, blue-grey water, terracotta
buildings), drawn from the same DBT 2007 polygons the city packs are compiled from, so the map and
the 3D city cannot disagree. No labels: the game draws its own.

Output: Data/City/map/overview.png  4096 x 4096, the whole pack extent padded to a square
        Data/City/map/centre.png    4096 x 4096, x and z in [-2048, 2048] at 1 m per pixel
        and a .json beside each: {"x0","z0","x1","z1","width","height"} in local metres, north up:
        px = (x - x0) / (x1 - x0) * width,  py = (z1 - z) / (z1 - z0) * height.
Run:    .context/city/venv/bin/python Tools/city/overview_map.py
"""
import json
import logging
import math
import sys
import time
from pathlib import Path

import numpy as np
import shapely
from PIL import Image, ImageDraw
from shapely.geometry import box
from shapely.strtree import STRtree

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tools/city"))
logging.getLogger("shapefile").setLevel(logging.ERROR)
import compile_city as cc  # noqa: E402

OUT = ROOT / "Data/City/map"
SIZE, SUPERSAMPLE = 4096, 2
GROUND, ROAD, GREEN, WATER, BUILDING, OUTLINE = "#ece5d6", "#fff9e8", "#b1c5a1", "#89babe", "#bd9980", "#aa8974"
RAIL_BED, RAIL, TRAM = "#ded7c9", "#b3ab9d", "#cf9f8d"       # subtle: a tone or two off the ground and the roads
GREEN_TYPES = ("01", "02", "03", "04", "07")                # garden, lawn, grove, flowerbed, hedge
PITCH_TYPES = ("02", "03", "04", "05", "06", "07", "18", "19")


class Canvas:
    """A north-up drawing surface over a window of local metres, supersampled for clean edges."""

    def __init__(self, x0, z0, x1, z1, width, height, supersample=SUPERSAMPLE, ground=GROUND):
        self.window = (x0, z0, x1, z1)
        self.size = (width, height)
        self.k = supersample
        self.sx, self.sz = width * supersample / (x1 - x0), height * supersample / (z1 - z0)
        self.image = Image.new("RGB", (width * supersample, height * supersample), ground)
        self.draw = ImageDraw.Draw(self.image)
        self.box = box(x0, z0, x1, z1)

    def px(self, coords):
        c = np.asarray(coords, dtype=float)[:, :2]
        return np.column_stack([(c[:, 0] - self.window[0]) * self.sx, (self.window[3] - c[:, 1]) * self.sz])

    def visible(self, geoms):
        if not len(geoms):
            return []
        return [geoms[i] for i in np.sort(STRtree(geoms).query(self.box))]

    def polygons(self, geoms, fill, outline=None):
        for geom in self.visible(geoms):
            for poly in getattr(geom, "geoms", [geom]):
                if poly.geom_type != "Polygon" or poly.is_empty:
                    continue
                ring = self.px(poly.exterior.coords)
                if not poly.interiors:
                    self.draw.polygon([tuple(p) for p in ring], fill=fill, outline=outline)
                    continue
                # Holes must show what is underneath: paint through a mask the size of the polygon.
                lo = np.floor(ring.min(axis=0)).astype(int) - 1
                hi = np.ceil(ring.max(axis=0)).astype(int) + 1
                lo, hi = np.maximum(lo, 0), np.minimum(hi, self.image.size)
                if (hi <= lo).any():
                    continue
                mask = Image.new("L", tuple(int(v) for v in hi - lo), 0)
                md = ImageDraw.Draw(mask)
                md.polygon([tuple(p) for p in ring - lo], fill=255)
                for hole in poly.interiors:
                    md.polygon([tuple(p) for p in self.px(hole.coords) - lo], fill=0)
                self.image.paste(fill, (int(lo[0]), int(lo[1])), mask)
                if outline:
                    for r in [poly.exterior, *poly.interiors]:
                        self.draw.line([tuple(p) for p in self.px(r.coords)], fill=outline, width=1)

    def lines(self, geoms, fill, pixels):
        """Polylines, `pixels` wide in the final image."""
        width = max(1, int(round(pixels * self.k)))
        for geom in self.visible(geoms):
            for line in getattr(geom, "geoms", [geom]):
                self.draw.line([tuple(p) for p in self.px(line.coords)], fill=fill, width=width, joint="curve")

    def finish(self):
        return self.image.reduce(self.k) if self.k > 1 else self.image

    def save(self, path):
        path.parent.mkdir(parents=True, exist_ok=True)
        self.finish().save(path, optimize=True)
        x0, z0, x1, z1 = self.window
        path.with_suffix(".json").write_text(json.dumps(
            {"x0": x0, "z0": z0, "x1": x1, "z1": z1, "width": self.size[0], "height": self.size[1]}) + "\n")


def areas(name, select=None):
    return [g for a, g, _ in cc.read_layer(cc.AREAS / name) if select is None or select(a)]


def survey_layers():
    """Everything the maps draw, bottom to top: (geometries, fill, outline or line width)."""
    green = areas("AR_VRD", lambda a: a["AR_VRD_TY"] in GREEN_TYPES) + areas("BOSCO") \
        + areas("ATTR_SP", lambda a: a["ATTR_SP_TY"] in PITCH_TYPES)
    water = areas("AB_CDA") + areas("SP_ACQ") + areas("ATTR_SP", lambda a: a["ATTR_SP_TY"] == "01")
    streets = [g for n in ("AR_STR", "AC_VEI", "AC_PED", "A_PED", "AR_MARC", "AC_CIC", "AR_TRAM") for g in areas(n)] \
        + areas("SV_STR", lambda a: a["SV_STR_TY"] in ("02", "0401", "04"))
    rails = [g for _, g, _ in cc.read_layer(cc.LINES / "EL_FER")]
    tram = [g for _, g, _ in cc.read_layer(cc.LINES / "EL_TRV")]
    units = cc.load_units(None)          # exactly the volume units the packs are built from (heroes included)
    return {"green": green, "water": water, "railBed": areas("SD_FER"), "streets": streets, "rails": rails,
            "tram": tram, "buildings": [u.raw for u in units], "units": units}


def pack_extent(units):
    """The tile-aligned extent compile_city.py gives a whole-comune pack (its main())."""
    xs, zs = [u.cx for u in units], [u.cz for u in units]
    return [(math.floor(min(xs) / cc.TILE) - 1) * cc.TILE, (math.floor(min(zs) / cc.TILE) - 1) * cc.TILE,
            (math.floor(max(xs) / cc.TILE) + 2) * cc.TILE, (math.floor(max(zs) / cc.TILE) + 2) * cc.TILE]


def paint(canvas, layers, rail_pixels, tram_pixels, outline):
    canvas.polygons(layers["green"], GREEN)
    canvas.polygons(layers["water"], WATER)
    canvas.polygons(layers["railBed"], RAIL_BED)
    canvas.polygons(layers["streets"], ROAD)
    canvas.lines(layers["rails"], RAIL, rail_pixels)
    canvas.lines(layers["tram"], TRAM, tram_pixels)
    canvas.polygons(layers["buildings"], BUILDING, OUTLINE if outline else None)
    return canvas


def main():
    started = time.time()
    layers = survey_layers()
    print(f"[{time.time() - started:5.1f}s] layers", {k: len(v) for k, v in layers.items()}, flush=True)
    x0, z0, x1, z1 = pack_extent(layers["units"])
    side = max(x1 - x0, z1 - z0)                       # pad the shorter axis so a pixel is square
    cx, cz = (x0 + x1) / 2, (z0 + z1) / 2
    paint(Canvas(cx - side / 2, cz - side / 2, cx + side / 2, cz + side / 2, SIZE, SIZE), layers, 1.0, 1.0, False) \
        .save(OUT / "overview.png")
    print(f"[{time.time() - started:5.1f}s] overview.png extent", [x0, z0, x1, z1], "window side", side, flush=True)
    paint(Canvas(-2048, -2048, 2048, 2048, SIZE, SIZE), layers, 1.5, 1.5, True).save(OUT / "centre.png")
    print(f"[{time.time() - started:5.1f}s] centre.png", flush=True)


if __name__ == "__main__":
    main()
