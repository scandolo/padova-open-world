"""<Display name>: procedural model on the DBT 2007 survey (slug <slug>).

Surveyed / published / typology split and conflicts: Data/Landmarks/<slug>-evidence.md.
Build: Blender -b --factory-startup --python Tools/blender/build_landmarks.py -- <slug> --render
"""
import math

from mathutils import Matrix, Vector

import lmcommon as lm
import santo  # shared vocabulary: walls, cornice, archlets, window, pilaster, ribbed_shell, dome, campanile, turret, plain_unit, cross

NAME = "<FbxName>"
BRICK, LEAD, STONE, PLASTER = "Brick", "Lead sheet", "Istrian stone", "Plaster 3"


def build(src, g):
    units = src["units"]                      # from extract_<slug>: id, role, base, top, outline, ringSizes, cx, cz
    anchor_unit = max(units, key=lambda u: u["area"])
    ax, az = anchor_unit["cx"], anchor_unit["cz"]
    ay = anchor_unit["base"]
    g.anchor = ay
    body = lm.Builder(NAME)
    for u in units:
        # Dispatch on the role assigned in the extractor; add roles as the evidence requires.
        if u["role"] in ("dome", "cone", "treasury"):
            santo.dome(body, u, ay, u["role"])
        elif u["role"] == "campanile":
            santo.campanile(body, u, ay, total_height=u.get("publishedHeight", u["top"] - u["base"] + 6))
        elif u["role"] == "turret":
            santo.turret(body, u, ay)
        else:
            santo.plain_unit(body, u, ay, windows=u["top"] - u["base"] > 12)
    # Landmark-specific features (façade, statues, …) go here, anchored to surveyed outlines.

    col = lm.Builder(NAME + "_Collision")     # simple survey volumes only: hidden MeshCollider in Unity
    for u in units:
        ring = lm.ring_points(u["outline"], u["ringSizes"], 0)
        santo.walls(col, ring, u["base"] - ay, u["top"] - ay, BRICK)
        col.polygon([ring], u["top"] - ay, BRICK)
    for b in (body, col):
        for v in b.v:
            v.x -= ax
            v.y -= az
    # Discovery point: open, walkable ground in front of the main entrance (not inside the building).
    place_x, place_z = ax, az
    return {"anchor": [ax, ay, az], "builders": [body], "collision": col,
            "info": {"units": len(units)},
            "replaces": {"units": [u["id"] for u in units]},
            "places": [{"name": "<Display name>", "x": round(place_x, 2), "z": round(place_z, 2), "source": "<where and why>"}]}


VIEWS = {  # anchor-relative Blender coordinates: (eye, target, lens mm)
    "front": ((-90.0, -30.0, 3.0), (0.0, 0.0, 15.0), 30),
    "aerial": ((-130.0, -130.0, 95.0), (0.0, 0.0, 12.0), 38),
}
