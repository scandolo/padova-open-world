"""Render roads.bin over the surveyed carriageways, to check the graph by eye.

Edges are coloured by class (grey alley, blue local, orange main, red fast, green pedestrian) with
thin lines at +-width/2; arrows mark one-way edges; magenta marks roundabouts; a white core marks
bridges and a black core underpasses. Nodes: black plain, red signal, purple mini-roundabout, hollow
dead end. Street names are written at edge midpoints.

Run:  .context/city/venv/bin/python Tools/city/debug_roads.py --centre 0 0 --span 700 --out /tmp/roads.png
"""
import argparse
import logging
import sys
from pathlib import Path

import numpy as np
from shapely.geometry import LineString, box

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tools/city"))
logging.getLogger("shapefile").setLevel(logging.ERROR)
import compile_city as cc  # noqa: E402
import overview_map  # noqa: E402
import pack  # noqa: E402

CLASS_COLOUR = ["#8a8a8a", "#2f6fd0", "#e08a1e", "#d02f2f", "#2e9e4f"]
NODE_COLOUR = ["#111111", "#e01010", "#8a2be2", "#ffffff"]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--centre", type=float, nargs=2, default=[0, 0], metavar=("X", "Z"))
    parser.add_argument("--span", type=float, default=700, help="window side in metres")
    parser.add_argument("--pixels", type=int, default=1800)
    parser.add_argument("--names", action="store_true", help="write street names")
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    (cx, cz), half = args.centre, args.span / 2
    canvas = overview_map.Canvas(cx - half, cz - half, cx + half, cz + half, args.pixels, args.pixels, 1, "#f4f1ea")
    clip = canvas.box.buffer(50)

    def layer(name):
        return [g for _, g, _ in cc.read_layer(cc.AREAS / name) if g.intersects(clip)]
    canvas.polygons(layer("AR_STR"), "#e6e0d2")
    canvas.polygons(layer("AC_VEI"), "#cfcfcf")
    canvas.polygons(layer("AC_PED") + layer("A_PED"), "#e8cfd6")
    canvas.polygons(layer("AB_CDA"), "#a9cdd6")
    canvas.polygons([g for _, g, _ in cc.read_layer(cc.AREAS / "UN_VOL", clip)], "#e3c9b4", "#c9a98f")
    canvas.lines([g for _, g, _ in cc.read_layer(cc.LINES / "EL_TRV") if g.intersects(clip)], "#b060b0", 1)

    graph = pack.read_roads(cc.OUT / "roads.bin")
    nodes, draw = graph["nodes"], canvas.draw
    metres = args.pixels / args.span
    for e in graph["edges"]:
        line = LineString(e["points"].astype(float))
        if not line.intersects(clip):
            continue
        colour = "#d020c0" if e["flags"] & 16 else CLASS_COLOUR[e["cls"]]
        px = [tuple(p) for p in canvas.px(e["points"])]
        draw.line(px, fill=colour, width=3)
        if e["flags"] & 4:
            draw.line(px, fill="#ffffff", width=1)
        if e["flags"] & 8:
            draw.line(px, fill="#000000", width=1)
        if e["flags"] & 32:
            draw.line(px, fill="#f0e040", width=1)
        for side in (1, -1):                                  # the measured carriageway band
            try:
                band = line.parallel_offset(e["width"] / 2, "left" if side > 0 else "right")
                for part in getattr(band, "geoms", [band]):
                    if len(part.coords) > 1:
                        draw.line([tuple(p) for p in canvas.px(part.coords)], fill=colour, width=1)
            except Exception:  # noqa: BLE001 - offsets of tiny loops can fail; debug drawing only
                pass
        if e["flags"] & 3:                                    # one-way arrows every ~40 m
            n = max(1, int(line.length // 40))
            for k in range(n):
                d = (k + 0.5) * line.length / n
                p, q = np.array(line.interpolate(d).coords[0]), np.array(line.interpolate(min(d + 0.5, line.length)).coords[0])
                t = q - p
                if not t.any():
                    continue
                t = t / np.hypot(*t) * (1 if e["flags"] & 1 else -1)
                size = max(3.0, 5 / metres)
                tip, left, right = p + t * size, p - t * size + np.array([-t[1], t[0]]) * size * 0.7, p - t * size - np.array([-t[1], t[0]]) * size * 0.7
                draw.polygon([tuple(v) for v in canvas.px(np.array([tip, left, right]))], fill="#000000")
        if args.names and e["name"] and line.length > 25:
            m = canvas.px(np.array([line.interpolate(0.5, normalized=True).coords[0]]))[0]
            draw.text((m[0] + 3, m[1] + 3), f"{graph['names'][e['name']]} {e['lanes']}L {e['maxspeed']}", fill="#202020")
    for x, z, kind, degree in nodes.tolist():
        if not clip.contains(box(x, z, x, z).centroid):
            continue
        p = canvas.px(np.array([[x, z]]))[0]
        r = 5 if kind in (1, 2) else 3
        draw.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=NODE_COLOUR[kind], outline="#111111")
    canvas.finish().save(args.out)
    print(args.out)


if __name__ == "__main__":
    main()
