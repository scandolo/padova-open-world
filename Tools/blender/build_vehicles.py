"""Build the vehicle library headlessly.

/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup \
    --python Tools/blender/build_vehicles.py -- [Name ...] [--render] [--lineup]

Writes Blender/Vehicles/<Name>.blend (editable source), Assets/Art/Vehicles/<Name>.fbx and
Assets/Art/Vehicles/Vehicles.json; optional previews go to .context/renders/. Every design is
generic (no badges or brand names) and procedural: no downloaded assets or textures.

Contract of each FBX (see docs/VEHICLES_AND_PROPS.md): metres, origin on the ground at the centre
of the wheelbase, Unity +Z forward and +Y up; root empty <Name> with children Body, Wheel_* (origin
at the wheel centre, axle along local X), optional Steer and Body_LOD1 (wheels merged in).
"""
import json
import math
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import assetkit as ak  # noqa: E402
import carkit as ck  # noqa: E402
from assetkit import Kit  # noqa: E402

ROOT = ak.ROOT
BLEND = ROOT / "Blender/Vehicles"
ART = ROOT / "Assets/Art/Vehicles"
MANIFEST = ART / "Vehicles.json"
GLASS = ("Glass", "Trim", 0.022, -0.012)


class Parts:
    def __init__(self):
        self.body = Kit("Body")
        self.wheels = {}   # name -> (kit, position)
        self.steer = None  # (kit, position on the steering axis)
        self.insets = [GLASS]


VEHICLES = {}


def vehicle(name, **meta):
    def register(fn):
        VEHICLES[name] = (fn, meta)
        return fn
    return register


def unity(p):
    """Asset space (x right, y forward, z up) -> Unity (x right, y up, z forward)."""
    return [round(p[0], 4), round(p[2], 4), round(p[1], 4)]


# ---------------------------------------------------------------- Utilitaria: 1960s rear-engined bubble
@vehicle("Utilitaria", budget=(4500, 700), mass=520, seat=(-0.27, -0.08, 0.42), paint2="#EDEDE8",
         colors=[("Bianco panna", "#EDE6D0"), ("Celeste", "#8FBBD0"), ("Rosso corallo", "#C23B2B"), ("Giallo Positano", "#E6C24A"),
                 ("Verde chiaro", "#A3BF9A"), ("Blu scuro", "#27405E")])
def utilitaria(lod):
    p = Parts()
    b = p.body
    r, fy, ry = 0.245, 0.92, -0.92
    stations = [  # y, W, underside, shoulder, top
        (-1.53, 0.33, 0.36, 0.50, 0.53),
        (-1.505, 0.45, 0.30, 0.565, 0.60),
        (-1.43, 0.555, 0.24, 0.63, 0.68),
        (-1.28, 0.625, 0.19, 0.70, 0.765),
        (-1.05, 0.655, 0.175, 0.77, 0.83),
        (-0.60, 0.66, 0.17, 0.80, 0.82),
        (0.20, 0.66, 0.17, 0.82, 0.84),
        (0.62, 0.66, 0.17, 0.835, 0.895),
        (0.95, 0.65, 0.175, 0.785, 0.85),
        (1.20, 0.61, 0.19, 0.72, 0.78),
        (1.35, 0.53, 0.23, 0.65, 0.70),
        (1.42, 0.43, 0.29, 0.585, 0.625),
        (1.44, 0.32, 0.34, 0.53, 0.56),
    ]
    tub = ck.Tub(stations, dict(tuck=0.06, shoulder=0.035, ntop=2.6, rows=(0.25, 0.55, 0.8), rows1=(0.55,)),
                 [(ry, 0.30, r), (fy, 0.30, r)])
    tub.build(b, lod)
    g = lambda *a, **k: ck.gring(tub, *a, rc=0.15, crown=0.04, **k)
    rings = [
        ck.gflat(tub, 0.60, bow=0.06),
        g(0.60, 0.34, 0.475, 1.245, bow=0.05),
        g(0.52, 0.26, 0.48, 1.27, lod=False),
        g(-0.18, -0.16, 0.485, 1.325),
        g(-0.25, -0.23, 0.485, 1.325, lod=False),
        g(-0.80, -0.62, 0.475, 1.275),
        g(-1.12, -0.72, 0.46, 1.215, bow=-0.04),
        ck.gflat(tub, -1.20, bow=-0.06),
    ]
    roof = "Trim" if lod == 0 else "Paint"  # fabric sunroof
    bands = [(None, "Glass"), ("Paint", "Paint"), ("Glass", roof), ("Paint", roof), ("Glass", roof), ("Paint", "Paint"),
             (None, "Glass:rear")]
    ck.greenhouse(b, rings, bands, dict(pillar=0.06), lod)
    p.insets = [("Glass", "Chrome", 0.016, -0.01), ("Glass:rear", "Paint", 0.11, 0.0), ("Glass:rear", "Chrome", 0.016, -0.01)]
    p.wheels = ck.four_wheels(r, 0.125, 1.12, fy, ry, lod, rim=0.165, dish="Chrome", lip="Paint")
    if lod:
        return p
    ck.cabin_dark(b, rings)
    tub.arch_lips(b, "Paint", out=0.012, width=0.03)
    for sx in (-1, 1):
        ck.lamp(b, (sx * 0.445, 1.375, 0.64), 0.09, (sx * 0.14, 1, 0.05), back=0.18, segs=12)
        b.ellipsoid((sx * 0.45, 1.405, 0.49), (0.03, 0.02, 0.03), "LightFront", 6, 4)
        b.ellipsoid((sx * 0.485, -1.47, 0.61), (0.038, 0.03, 0.07), "LightRear", 6, 4)
        b.ellipsoid((sx * 0.485, -1.455, 0.61), (0.048, 0.03, 0.082), "Chrome", 6, 4)
    ck.bumper(b, tub, 0.365, 1.12, True, height=0.045)
    ck.bumper(b, tub, 0.385, -1.22, False, height=0.045)
    for sx in (-1, 1):  # overriders
        b.rbox((sx * 0.22, 1.485, 0.385), (0.03, 0.03, 0.1), 0.012, "Chrome", axis=2)
        b.rbox((sx * 0.22, -1.575, 0.405), (0.03, 0.03, 0.1), 0.012, "Chrome", axis=2)
    b.box((0, 1.447, 0.505), (0.3, 0.014, 0.02), "Chrome")
    b.rbox((0, 1.45, 0.505), (0.055, 0.018, 0.05), 0.008, "Chrome", axis=1)
    ck.plate(b, 1.44, 0.415, 1, 0.26, 0.07)
    ck.plate(b, -1.53, 0.445, -1, 0.3, 0.09)
    for k in range(5):  # engine lid louvres
        y = -1.30 - k * 0.035
        b.box((0, y, tub.top_at(y) + 0.004), (0.3, 0.012, 0.012), "Trim")
    ck.flank_strip(b, tub, lambda y: 0.62 + 0.02 * y, -1.3, 1.2, "Chrome", height=0.016, out=0.008)   # waist swage
    ck.seam(b, tub, 0.50, 0.24, 0.80)
    ck.seam(b, tub, -0.275, 0.24, 0.80)
    ck.door_handles(b, tub, (-0.15,), 0.70)
    b.cyl((-0.66, 0.40, 0.86), (-0.74, 0.40, 0.93), 0.008, "Chrome", segs=4)
    b.ellipsoid((-0.755, 0.40, 0.955), (0.012, 0.05, 0.05), "Chrome", 8, 4)
    for sx in (-1, 1):  # wipers
        b.cyl((sx * 0.2 - 0.12, 0.615, 0.875), (sx * 0.2 + 0.1, 0.58, 0.935), 0.006, "Trim", segs=4)
    return p


# ---------------------------------------------------------------- Berlina: modern compact hatchback
@vehicle("Berlina", budget=(4500, 700), mass=1150, seat=(-0.37, 0.02, 0.48), paint2="#EDEDE8",
         colors=[("Grigio argento", "#A9ADB0"), ("Bianco", "#F1F0EA"), ("Rosso", "#B3211C"), ("Blu notte", "#1E2F52"),
                 ("Nero", "#17181A"), ("Azzurro", "#6E9CC2")])
def berlina(lod):
    p = Parts()
    b = p.body
    r, fy, ry = 0.31, 1.275, -1.275
    stations = [  # y, W, underside, shoulder, top
        (-1.955, 0.60, 0.33, 0.87, 0.895),
        (-1.925, 0.72, 0.26, 0.93, 0.955),
        (-1.85, 0.81, 0.21, 0.98, 1.005),
        (-1.68, 0.86, 0.19, 1.00, 1.02),
        (-1.30, 0.875, 0.19, 1.00, 1.02),
        (-0.50, 0.875, 0.19, 0.975, 0.99),
        (0.55, 0.875, 0.19, 0.95, 0.965),
        (0.95, 0.875, 0.19, 0.925, 0.965),
        (1.55, 0.87, 0.19, 0.815, 0.87),
        (1.80, 0.845, 0.19, 0.765, 0.81),
        (1.97, 0.775, 0.20, 0.72, 0.755),
        (2.07, 0.66, 0.22, 0.675, 0.705),
        (2.105, 0.52, 0.25, 0.635, 0.66),
    ]
    patches = [
        dict(on="side", y=(1.80, 2.2), f=(0.74, 1.0), mat="LightFront"),
        dict(on="side", y=(-2.0, -1.68), f=(0.74, 1.0), mat="LightRear"),
        dict(on="side", y=(1.97, 2.2), f=(0.0, 0.22), mat="Trim"),
        dict(on="side", y=(-2.0, -1.85), f=(0.0, 0.22), mat="Trim"),
        dict(on="front:side", f=(0.5, 1.0), mat="Trim"),
        dict(on="front:side", f=(0.0, 0.22), mat="Trim"),
        dict(on="rear:side", f=(0.0, 0.22), mat="Trim"),
    ]
    tub = ck.Tub(stations, dict(rows=(0.22, 0.5, 0.74), rows1=(0.74,)), [(ry, 0.375, r), (fy, 0.375, r)], patches)
    tub.build(b, lod)
    g = lambda *a, **k: ck.gring(tub, *a, **k)
    rings = [
        ck.gflat(tub, 0.95, bow=0.10),
        g(0.95, 0.25, 0.63, 1.455, bow=0.07),
        g(0.83, 0.15, 0.635, 1.475, lod=False),
        g(-0.17, -0.17, 0.645, 1.495),
        g(-0.26, -0.26, 0.645, 1.495, lod=False),
        g(-1.06, -1.02, 0.635, 1.475),
        g(-1.70, -1.33, 0.615, 1.435, bow=-0.04),
        ck.gflat(tub, -1.79, bow=-0.06),
    ]
    bands = [(None, "Glass"), ("Paint", "Paint"), ("Glass", "Paint"), ("Trim", "Paint"), ("Glass", "Paint"),
             ("Paint", "Paint"), (None, "Glass")]
    ck.greenhouse(b, rings, bands, lod=lod)
    p.insets = [GLASS, ("LightFront", "Chrome", 0.012, -0.008), ("LightRear", "Trim", 0.012, -0.008)]
    p.wheels = ck.four_wheels(r, 0.2, 1.50, fy, ry, lod)
    if lod:
        return p
    ck.cabin_dark(b, rings)
    tub.arch_lips(b, "Trim", out=0.012, width=0.035)
    ck.plate(b, 2.105, 0.42, 1)
    ck.plate(b, -1.955, 0.62, -1)
    ck.side_mirrors(b, 0.80, 0.78, 1.0)
    ck.door_handles(b, tub, (0.0, -0.95), 0.86, "Paint")
    for y, lean in ((0.74, 0.0), (-0.21, 0.0), (-1.02, -0.25)):
        ck.seam(b, tub, y, 0.27, 0.95, lean=lean)
    ck.flank_strip(b, tub, 0.46, -0.85, 0.85, "Trim", height=0.05, out=0.008)
    b.rbox((0, -1.36, 1.44), (1.05, 0.14, 0.03), 0.012, "Paint", axis=0)  # hatch spoiler lip
    b.wedge([(-0.012, -1.05, 1.47), (0.012, -1.05, 1.47), (0.012, -0.9, 1.48), (-0.012, -0.9, 1.48),
             (-0.004, -1.05, 1.53), (0.004, -1.05, 1.53), (0.004, -0.98, 1.49), (-0.004, -0.98, 1.49)], "Trim")  # aerial fin
    b.cyl((0.45, -1.90, 0.24), (0.45, -1.99, 0.24), 0.03, "Chrome", segs=8)
    return p


# ---------------------------------------------------------------- Pantera and Taxi: three-box saloon
def saloon(lod, police):
    p = Parts()
    b = p.body
    r, fy, ry = 0.33, 1.41, -1.41
    stations = [  # y, W, underside, shoulder, top
        (-2.43, 0.64, 0.36, 0.83, 0.855),
        (-2.40, 0.76, 0.29, 0.885, 0.91),
        (-2.31, 0.86, 0.23, 0.93, 0.96),
        (-2.08, 0.915, 0.20, 0.955, 0.99),
        (-1.60, 0.93, 0.19, 0.96, 0.995),
        (-0.60, 0.93, 0.19, 0.945, 0.96),
        (0.45, 0.93, 0.19, 0.92, 0.935),
        (0.86, 0.93, 0.19, 0.90, 0.945),
        (1.55, 0.925, 0.19, 0.81, 0.865),
        (1.90, 0.90, 0.19, 0.755, 0.805),
        (2.08, 0.825, 0.20, 0.71, 0.75),
        (2.18, 0.70, 0.22, 0.665, 0.70),
        (2.21, 0.56, 0.25, 0.625, 0.655),
    ]
    patches = [
        dict(on="side", y=(1.90, 2.3), f=(0.74, 1.0), mat="LightFront"),
        dict(on="side", y=(-2.5, -2.08), f=(0.74, 1.0), mat="LightRear"),
        dict(on="side", y=(2.08, 2.3), f=(0.0, 0.22), mat="Trim"),
        dict(on="front:side", f=(0.5, 1.0), mat="Trim"),
        dict(on="front:side", f=(0.0, 0.22), mat="Trim"),
        dict(on="rear:side", f=(0.0, 0.22), mat="Trim"),
        dict(on="rear:side", f=(0.74, 1.0), mat="LightRear"),
    ]
    if police:  # livery band along the flanks in the second colour
        patches += [dict(on="side", y=(-2.08, 1.90), f=(0.5, 0.74), mat="Paint2")]
    tub = ck.Tub(stations, dict(rows=(0.22, 0.5, 0.74), rows1=(0.5, 0.74) if police else (0.74,), lod_keep=(0, 2, 7, 9, 11, 12)),
                 [(ry, 0.385, r), (fy, 0.385, r)], patches)
    tub.build(b, lod)
    top = 1.445 if police else 1.47
    g = lambda *a, **k: ck.gring(tub, *a, **k)
    rings = [
        ck.gflat(tub, 0.88, bow=0.11),
        g(0.88, 0.08, 0.66, top - 0.05, bow=0.08),
        g(0.75, -0.03, 0.665, top - 0.03, lod=False),
        g(-0.43, -0.43, 0.675, top),
        g(-0.52, -0.52, 0.675, top, lod=False),
        g(-1.30, -1.22, 0.665, top - 0.02),
        g(-1.96, -1.50, 0.63, top - 0.075, bow=-0.05),
        ck.gflat(tub, -2.02, bow=-0.08),
    ]
    bands = [(None, "Glass"), ("Paint", "Paint"), ("Glass", "Paint"), ("Trim", "Paint"), ("Glass", "Paint"),
             ("Paint", "Paint"), (None, "Glass")]
    ck.greenhouse(b, rings, bands, lod=lod)
    p.insets = [GLASS, ("LightFront", "Chrome", 0.012, -0.008), ("LightRear", "Trim", 0.012, -0.008)]
    p.wheels = ck.four_wheels(r, 0.215, 1.58, fy, ry, lod)
    roof = lambda y: top + 0.005
    slab = (lambda c, size, r, m: b.box(c, size, m)) if lod else (lambda c, size, r, m: b.rbox(c, size, r, m, axis=0))
    if police:  # roof light bar: dark base, two blue lenses, speaker in the middle
        if lod == 0:
            slab((0, -0.47, roof(0) + 0.012), (1.14, 0.25, 0.04), 0.012, "Trim")
        for sx in (-1, 1):
            slab((sx * 0.345, -0.47, roof(0) + (0.07 if lod == 0 else 0.045)), (0.43, 0.22, 0.085), 0.03, "LightBlue")
        if lod == 0:
            slab((0, -0.47, roof(0) + 0.062), (0.22, 0.2, 0.07), 0.02, "Chrome")
    else:  # taxi roof sign on a foot
        slab((0, -0.1, roof(0) + 0.085), (0.38, 0.13, 0.12), 0.03, "Sign")
        if lod == 0:
            b.box((0, -0.1, roof(0) + 0.01), (0.3, 0.1, 0.04), "Trim")
    if lod:
        return p
    ck.cabin_dark(b, rings)
    tub.arch_lips(b, "Paint", out=0.012, width=0.035)
    ck.plate(b, 2.21, 0.42, 1)
    ck.plate(b, -2.43, 0.56, -1)
    ck.side_mirrors(b, 0.855, 0.70, 0.98)
    ck.door_handles(b, tub, (-0.25, -1.12), 0.83, "Chrome")
    for y, lean in ((0.66, 0.0), (-0.475, 0.0), (-1.26, -0.3)):
        ck.seam(b, tub, y, 0.27, 0.92, lean=lean)
    ck.flank_strip(b, tub, lambda y: tub.fK(y) - 0.012, -1.9, 0.8, "Chrome", height=0.014, out=0.006)
    for sx in (-1, 1):
        b.cyl((sx * 0.5, -2.37, 0.25), (sx * 0.5, -2.46, 0.25), 0.032, "Chrome", segs=8)
    if police:
        b.cyl((0.3, -1.75, 0.99), (0.3, -1.8, 1.4), 0.006, "Trim", segs=4)  # radio aerial
    return p


@vehicle("Pantera", budget=(4500, 700), mass=1500, seat=(-0.38, -0.08, 0.45), paint2="#F4F4F0",
         colors=[("Azzurro polizia", "#2B6CB0"), ("Blu Arma", "#14213D"), ("Bianco municipale", "#F1F0EA"), ("Grigio civetta", "#5A5F66")])
def pantera(lod):
    return saloon(lod, True)


@vehicle("Taxi", budget=(4500, 700), mass=1450, seat=(-0.38, -0.08, 0.45), paint2="#F4F4F0",
         colors=[("Bianco", "#F4F3EE"), ("Bianco avorio", "#ECE8DA"), ("Giallo taxi", "#F2C230"), ("Grigio argento", "#B5B8BA")])
def taxi(lod):
    return saloon(lod, False)


# ---------------------------------------------------------------- Scooter: pressed-steel classic
SCOOTER_PIVOT = (0.0, 0.45, 0.93)


@vehicle("Scooter", budget=(3000, 500), mass=95, seat=(0, -0.22, 0.86), steer_axis=(0, -0.231, 0.973), paint2="#EDEDE8",
         colors=[("Verde acqua", "#9FCFC0"), ("Bianco", "#F0EDE2"), ("Rosso", "#B5261E"), ("Azzurro", "#7FB2D9"),
                 ("Giallo", "#EBC845"), ("Grigio", "#8C9093")])
def scooter(lod):
    p = Parts()
    b = p.body
    r, fy, ry = 0.215, 0.6175, -0.6175
    stations = [  # rear monocoque with the two side cowls
        (-0.905, 0.045, 0.50, 0.565, 0.585),
        (-0.86, 0.12, 0.43, 0.61, 0.645),
        (-0.74, 0.215, 0.33, 0.655, 0.70),
        (-0.55, 0.27, 0.27, 0.68, 0.735),
        (-0.30, 0.27, 0.26, 0.685, 0.75),
        (-0.10, 0.235, 0.27, 0.67, 0.745),
        (0.03, 0.17, 0.29, 0.62, 0.69),
        (0.10, 0.115, 0.27, 0.50, 0.55),
        (0.16, 0.10, 0.25, 0.36, 0.38),
    ]
    tub = ck.Tub(stations, dict(tuck=0.10, shoulder=0.05, ntop=2.3, rows=(0.3, 0.62), rows1=(0.5,), na=4, under="Paint"))
    tub.build(b, lod)
    b.rbox((0, 0.36, 0.265), (0.42, 0.58, 0.05), 0.02, "Paint", axis=1, ends=lod == 0)   # floorboard
    b.rbox((0, 0.30, 0.30), (0.13, 0.5, 0.07), 0.03, "Paint", axis=1, ends=lod == 0)     # tunnel
    # leg shield: a curved plate leaning back along the steering column
    width = ak.pchip([(0, 0.215), (0.3, 0.24), (0.62, 0.215), (0.85, 0.15), (1.0, 0.085)])
    nu, nv = (6, 6) if lod == 0 else (2, 3)
    rows = []
    for j in range(nv + 1):
        v = j / nv
        yc, zc = 0.615 - 0.17 * v - 0.02 * math.sin(math.pi * v), 0.25 + 0.71 * v
        front = [(width(v) * (-1 + 2 * i / nu), yc - 0.09 * (-1 + 2 * i / nu) ** 2, zc) for i in range(nu + 1)]
        rows.append(front + [(x * 0.97, y - 0.024, z) for x, y, z in reversed(front)])
    b.grid(rows, "Paint", wrap_u=True, cap0=("ngon", "Paint"), cap1=("ngon", "Paint"))
    b.rbox((0, -0.30, 0.795), (0.30, 0.66, 0.10), 0.04, "Seat", axis=1, ends=lod == 0)
    p.wheels = {"Wheel_F": (ck.wheel(r, 0.09, 1, lod, rim=0.125, dish="Chrome"), (0, fy, r)),
                "Wheel_R": (ck.wheel(r, 0.09, 1, lod, rim=0.125, dish="Chrome"), (0, ry, r))}
    # steering assembly: headset, handlebar, lamp, fork and front mudguard
    s = Kit("Steer")
    s.ellipsoid((0, 0.44, 1.0), (0.105, 0.145, 0.062), "Paint", 8 if lod == 0 else 6, 4)
    s.cyl((-0.23, 0.40, 1.0), (0.23, 0.40, 1.0), 0.022, "Paint", segs=6)
    for sx in (-1, 1):
        s.cyl((sx * 0.23, 0.40, 1.0), (sx * 0.345, 0.40, 1.0), 0.019, "Rubber", segs=6)
    mud = []
    span = (15, 205)
    steps = 9 if lod == 0 else 4
    for k in range(steps + 1):
        a = math.radians(span[0] + (span[1] - span[0]) * k / steps)
        taper = 0.55 + 0.45 * math.sin(math.pi * k / steps) ** 0.6
        hw, crest, r0 = 0.078 * taper, 0.05 * taper, r + 0.012
        ey, ez = math.cos(a), math.sin(a)
        mud.append([(x, fy + ey * rr, r + ez * rr) for x, rr in
                    ((-hw, r0 - 0.04), (-hw, r0 + 0.004), (-hw * 0.6, r0 + crest * 0.75), (0, r0 + crest),
                     (hw * 0.6, r0 + crest * 0.75), (hw, r0 + 0.004), (hw, r0 - 0.04))])
    s.grid(mud, "Paint", wrap_u=True, cap0=("ngon", "Paint"), cap1=("ngon", "Paint"))
    if lod == 0:
        ck.lamp(s, (0, 0.555, 1.0), 0.062, (0, 1, 0), back=0.1, bulge=0.025)
        s.lathe([(0.04, 0), (0.04, 0.012), (0, 0.012)], "Trim", origin=(0, 0.40, 1.052), axis=(0, 0, 1), segs=8)   # speedometer
        for sx in (-1, 1):
            s.cyl((sx * 0.21, 0.455, 1.0), (sx * 0.335, 0.44, 0.995), 0.005, "Chrome", segs=4)   # levers
            s.cyl((sx * 0.20, 0.41, 1.01), (sx * 0.27, 0.39, 1.10), 0.006, "Chrome", segs=4)      # mirror stalks
            s.ellipsoid((sx * 0.28, 0.385, 1.125), (0.045, 0.012, 0.045), "Chrome", 8, 4)
        s.tube([(0.055, fy, r), (0.06, 0.585, 0.33), (0.045, 0.56, 0.45)], 0.016, "Chrome", segs=5)     # fork leg
        s.cyl((-0.02, fy, r), (0.075, fy, r), 0.03, "Chrome", segs=8)
        crest = [(0, fy + math.cos(math.radians(a)) * (r + 0.064), r + math.sin(math.radians(a)) * (r + 0.064)) for a in (40, 65, 90, 115)]
        s.tube(crest, 0.007, "Chrome", segs=4)
    p.steer = (s.shift([-c for c in SCOOTER_PIVOT]), SCOOTER_PIVOT)
    if lod:
        return p
    b.tube([(0, 0.615 - 0.17 * v - 0.02 * math.sin(math.pi * v) + 0.004, 0.25 + 0.71 * v) for v in (0.22, 0.4, 0.6, 0.8)],
           [0.03, 0.036, 0.034, 0.026], "Paint", segs=6)                                   # horn cover
    for x in (-0.16, -0.1, 0.1, 0.16):
        b.box((x, 0.36, 0.293), (0.02, 0.46, 0.008), "Rubber")                              # floor rails
    for sx in (-1, 1):
        path = [(sx * (tub.x_at(y, 0.50) + 0.003), y, 0.50 + 0.03 * math.sin((y + 0.75) * 4)) for y in (-0.75, -0.6, -0.45, -0.3, -0.15)]
        b.tube(path, 0.007, "Chrome", segs=4)                                                # cowl trims
    b.rbox((0, -0.905, 0.595), (0.075, 0.05, 0.055), 0.015, "LightRear", axis=1)
    b.box((0, -0.925, 0.475), (0.17, 0.012, 0.13), "Plate")
    b.box((0, -0.915, 0.52), (0.05, 0.02, 0.1), "Trim")
    b.tube([(-0.11, -0.62, 0.765), (-0.11, -0.89, 0.765), (0.11, -0.89, 0.765), (0.11, -0.62, 0.765)], 0.008, "Chrome", segs=4)   # rack
    b.cyl((0.16, -0.58, 0.225), (0.16, -0.36, 0.23), 0.034, "Chrome", segs=7)                 # silencer
    return p


# ---------------------------------------------------------------- Ape: three-wheeled micro pickup
@vehicle("Ape", budget=(3000, 500), mass=420, seat=(0, 0.33, 0.62), paint2="#EDEDE8",
         colors=[("Verde", "#3F7D5A"), ("Azzurro", "#6FA6CF"), ("Bianco", "#EFEDE4"), ("Rosso", "#B2281F"), ("Giallo", "#E9BE3A")])
def ape(lod):
    p = Parts()
    b = p.body
    r, fy, ry = 0.21, 0.81, -0.81
    stations = [  # cab lower body, from the back wall to the nose
        (-0.03, 0.57, 0.30, 0.90, 0.92),
        (0.0, 0.60, 0.28, 0.92, 0.94),
        (0.55, 0.60, 0.27, 0.92, 0.95),
        (0.85, 0.55, 0.27, 0.90, 0.955),
        (1.05, 0.45, 0.30, 0.82, 0.88),
        (1.20, 0.35, 0.36, 0.725, 0.775),
        (1.28, 0.25, 0.43, 0.635, 0.675),
        (1.31, 0.15, 0.48, 0.575, 0.595),
    ]
    tub = ck.Tub(stations, dict(tuck=0.05, shoulder=0.02, ntop=2.8, rows=(0.3, 0.7), rows1=(0.5,), na=4, under="Trim"))
    tub.build(b, lod)
    g = lambda *a, **k: ck.gring(tub, *a, rc=0.10, crown=0.03, ledge=0.02, **k)
    rings = [
        ck.gflat(tub, 0.86, bow=0.05, ledge=0.02),
        g(0.86, 0.70, 0.49, 1.57, bow=0.04),
        g(0.76, 0.61, 0.495, 1.58, lod=False),
        g(0.12, 0.12, 0.50, 1.58),
        g(-0.025, -0.025, 0.50, 1.575),
    ]
    bands = [(None, "Glass"), ("Paint", "Paint"), ("Glass", "Paint"), ("Paint", "Paint")]
    ck.greenhouse(b, rings, bands, dict(cap1=("pairs", lambda k: "Paint")), lod)
    # flatbed with drop sides on a dark chassis
    b.box((0, -0.67, 0.53), (1.24, 1.26, 0.06), "Paint")
    b.box((0, -0.62, 0.40), (0.62, 1.16, 0.2), "Trim")
    for sx in (-1, 1):
        b.box((sx * 0.605, -0.67, 0.70), (0.03, 1.26, 0.28), "Paint")
    b.box((0, -1.285, 0.70), (1.18, 0.03, 0.28), "Paint")
    b.box((0, -0.055, 0.70), (1.18, 0.03, 0.28), "Paint")
    p.wheels = {"Wheel_F": (ck.wheel(r, 0.10, 1, lod, rim=0.125, dish="Chrome"), (0, fy, r)),
                "Wheel_RL": (ck.wheel(r, 0.10, -1, lod, rim=0.125, dish="Chrome"), (-0.53, ry, r)),
                "Wheel_RR": (ck.wheel(r, 0.10, 1, lod, rim=0.125, dish="Chrome"), (0.53, ry, r))}
    if lod:
        return p
    ck.cabin_dark(b, rings)
    for sx in (-1, 1):
        for y in (-1.0, -0.67, -0.34):
            b.box((sx * 0.626, y, 0.70), (0.014, 0.05, 0.26), "Paint")       # side stiffeners
        b.box((sx * 0.626, -0.67, 0.835), (0.016, 1.26, 0.022), "Trim")       # top rail
        b.rbox((sx * 0.50, -1.30, 0.45), (0.12, 0.03, 0.07), 0.012, "LightRear", axis=1)
        ck.lamp(b, (sx * 0.33, 1.19, 0.60), 0.035, (sx * 0.5, 1, 0), back=0.05, bulge=0.012, segs=6)
        b.cyl((sx * 0.56, 0.72, 1.0), (sx * 0.72, 0.74, 1.12), 0.007, "Trim", segs=4)
        b.ellipsoid((sx * 0.735, 0.74, 1.16), (0.014, 0.05, 0.06), "Trim", 8, 4)
        b.box((sx * 0.36, -0.81, 0.27), (0.04, 0.42, 0.04), "Trim")           # leaf springs
    b.box((0, -1.303, 0.835), (1.18, 0.016, 0.022), "Trim")
    ck.lamp(b, (0, 1.305, 0.64), 0.075, (0, 1, 0.08), back=0.1, bulge=0.03)
    b.rbox((0, 1.26, 0.50), (0.16, 0.05, 0.035), 0.01, "Trim", axis=0)       # nose grille slot
    ck.plate(b, -1.30, 0.62, -1, 0.3, 0.1)
    ck.seam(b, tub, 0.80, 0.34, 0.90)
    ck.seam(b, tub, 0.10, 0.34, 0.90)
    ck.door_handles(b, tub, (0.2,), 0.80, "Trim")
    b.cyl((-0.15, 0.875, 0.985), (0.12, 0.845, 1.2), 0.006, "Trim", segs=4)   # wiper
    b.cyl((-0.50, ry, r), (0.50, ry, r), 0.03, "Trim", segs=6)                # rear axle
    b.tube([(0.05, fy, r), (0.07, 0.83, 0.38)], 0.02, "Trim", segs=5)          # front suspension arm
    return p


# ---------------------------------------------------------------- Furgone: delivery van
@vehicle("Furgone", budget=(4500, 700), mass=2100, seat=(-0.50, 1.25, 1.0), paint2="#EDEDE8",
         colors=[("Bianco", "#F2F1EC"), ("Giallo corriere", "#F0B92B"), ("Rosso", "#B5271F"), ("Blu", "#274B8C"),
                 ("Grigio", "#9A9EA1"), ("Verde", "#2F6B4A")])
def furgone(lod):
    p = Parts()
    b = p.body
    r, fy, ry = 0.35, 1.725, -1.725
    stations = [  # y, W, underside, shoulder (belt), top
        (-2.735, 0.95, 0.34, 1.20, 1.22),
        (-2.715, 0.99, 0.31, 1.225, 1.245),
        (-2.64, 1.01, 0.30, 1.23, 1.25),
        (-2.2, 1.01, 0.29, 1.23, 1.25),
        (0.6, 1.01, 0.29, 1.23, 1.25),
        (1.75, 1.01, 0.29, 1.23, 1.26),
        (2.03, 1.0, 0.29, 1.215, 1.285),
        (2.30, 0.98, 0.29, 1.12, 1.19),
        (2.50, 0.93, 0.30, 1.03, 1.085),
        (2.62, 0.84, 0.31, 0.96, 1.0),
        (2.66, 0.74, 0.33, 0.915, 0.95),
        (2.675, 0.62, 0.36, 0.88, 0.905),
    ]
    patches = [
        dict(on="side", y=(2.30, 2.7), f=(0.55, 0.8), mat="LightFront"),
        dict(on="side", y=(-2.8, -2.64), f=(0.55, 1.0), mat="LightRear"),
        dict(on="side", y=(2.30, 2.7), f=(0.0, 0.3), mat="Trim"),
        dict(on="side", y=(-2.8, -2.64), f=(0.0, 0.3), mat="Trim"),
        dict(on="front:side", f=(0.0, 0.3), mat="Trim"),
        dict(on="front:side", f=(0.55, 0.8), mat="Trim"),
        dict(on="rear:side", f=(0.0, 0.3), mat="Trim"),
    ]
    tub = ck.Tub(stations, dict(tuck=0.05, shoulder=0.012, ntop=4.0, rows=(0.3, 0.55, 0.8), rows1=(0.55,), na=4),
                 [(ry, 0.42, r), (fy, 0.42, r)], patches)
    tub.build(b, lod)
    g = lambda *a, **k: ck.gring(tub, *a, rc=0.12, crown=0.04, ledge=0.012, **k)
    rings = [
        ck.gflat(tub, 2.01, bow=0.10, ledge=0.012),
        g(2.01, 1.42, 0.86, 2.44, bow=0.07),
        g(1.86, 1.29, 0.87, 2.48, lod=False),
        g(0.98, 0.98, 0.89, 2.52),
        g(0.86, 0.86, 0.89, 2.52, lod=False),
        g(-1.2, -1.2, 0.89, 2.52),
        g(-2.62, -2.62, 0.89, 2.51),
        g(-2.71, -2.71, 0.865, 2.48),
    ]
    bands = [(None, "Glass"), ("Paint", "Paint"), ("Glass", "Paint"), ("Paint", "Paint"), ("Paint", "Paint"),
             ("Paint", "Paint"), ("Paint", "Paint")]
    ck.greenhouse(b, rings, bands, dict(rc=0.12, cap1=("pairs", lambda k: "Paint")), lod)
    p.insets = [GLASS, ("LightFront", "Chrome", 0.014, -0.008), ("LightRear", "Trim", 0.012, -0.006)]
    p.wheels = ck.four_wheels(r, 0.225, 1.76, fy, ry, lod, dish="Chrome", rim=0.21)
    if lod:
        return p
    ck.cabin_dark(b, rings[:4])
    tub.arch_lips(b, "Trim", out=0.014, width=0.045)
    ck.plate(b, 2.675, 0.50, 1)
    ck.plate(b, -2.735, 0.80, -1)
    ck.flank_strip(b, tub, 0.72, -2.55, 2.35, "Trim", height=0.07, out=0.012)
    for sx in (-1, 1):  # door mirrors on arms
        b.box((sx * 1.04, 1.80, 1.36), (0.12, 0.04, 0.03), "Trim")
        b.rbox((sx * 1.14, 1.80, 1.44), (0.06, 0.1, 0.24), 0.02, "Trim", axis=2)
    ck.door_handles(b, tub, (0.95, -1.05), 1.02, "Trim")
    for y in (1.92, 0.80, -0.55):
        ck.seam(b, tub, y, 0.36, 1.22)
    for sx in (-1, 1):  # upper door edges and the sliding-door rail on the panel side
        for y in (0.80, -0.55):
            b.box((sx * 0.935, y, 1.76), (0.012, 0.012, 1.04), "Trim", M=ak.rot("Y", -sx * 4.3, (sx * 0.935, y, 1.76)))
        b.box((sx * 0.975, -1.55, 1.26), (0.012, 1.9, 0.02), "Trim")
    b.box((0, -2.752, 1.25), (0.012, 0.012, 1.9), "Trim")      # rear door split
    for sx in (-1, 1):
        b.box((sx * 0.1, -2.75, 1.25), (0.03, 0.02, 0.12), "Trim")   # rear handles
    b.rbox((0, 2.66, 1.035), (0.9, 0.03, 0.06), 0.012, "Trim", axis=0)   # bonnet lip / grille bar
    for sx in (-1, 1):
        b.cyl((sx * 0.35 - 0.18, 2.03, 1.30), (sx * 0.35 + 0.2, 1.95, 1.42), 0.008, "Trim", segs=4)   # wipers
    return p


# ---------------------------------------------------------------- Autobus: 12 m low-floor city bus
BUS_DOORS = ((4.16, 5.30), (-0.20, 1.20), (-4.80, -3.60))


@vehicle("Autobus", budget=(7000, 1000), mass=11500, seat=(-0.72, 4.95, 1.0), paint2="#F2F2EE",
         colors=[("Arancio", "#E8792B"), ("Azzurro", "#3A7CC2"), ("Bianco", "#F0F0EC"), ("Verde", "#3C8D5A"), ("Rosso", "#B8322A")])
def autobus(lod):
    p = Parts()
    b = p.body
    r, fy, ry = 0.48, 2.95, -2.95
    stations = [
        (-6.38, 1.14, 0.38, 1.10, 1.12),
        (-6.36, 1.22, 0.34, 1.115, 1.135),
        (-6.26, 1.27, 0.31, 1.12, 1.14),
        (-6.0, 1.275, 0.30, 1.12, 1.14),
        (0.0, 1.275, 0.30, 1.12, 1.14),
        (5.3, 1.275, 0.30, 1.12, 1.14),
        (5.50, 1.27, 0.31, 1.12, 1.14),
        (5.60, 1.21, 0.34, 1.115, 1.135),
        (5.62, 1.12, 0.38, 1.10, 1.12),
    ]
    patches = [
        dict(on="side", f=(0.62, 1.0), mat="Paint2"),
        dict(on="side", y=(5.3, 5.7), f=(0.3, 0.62), mat="LightFront"),
        dict(on="side", y=(-6.4, -6.0), f=(0.3, 0.62), mat="LightRear"),
        dict(on="side", y=(5.3, 5.7), f=(0.0, 0.3), mat="Trim"),
        dict(on="side", y=(-6.4, -6.0), f=(0.0, 0.3), mat="Trim"),
        dict(on="front:side", f=(0.0, 0.3), mat="Trim"),
        dict(on="rear:side", f=(0.0, 0.3), mat="Trim"),
        dict(on="rear:side", f=(0.3, 1.0), mat="Trim"),
    ] + [dict(on="side", y=d, f=(0.1, 1.0), mat="Glass:door", sx=1) for d in BUS_DOORS]
    tub = ck.Tub(stations, dict(tuck=0.03, shoulder=0.008, ntop=6.0, rows=(0.1, 0.3, 0.62), rows1=(0.3, 0.62), na=2,
                                arch_segs=8, well=0.34, lod_keep=(0, 2, 6, 8)),
                 [(ry, 0.56, r), (fy, 0.56, r)], patches)
    tub.build(b, lod)
    zc, top = 2.40, 2.97

    def g(y, **k):
        return ck.gring(tub, y, y, tub.side_x(y, 1.0) - 0.05, top, ledge=0.012, sink=0.01, zc=zc, rcx=0.16, rcz=0.48, **k)

    ys = [5.61, 5.30, 4.16, 4.06, 2.30, 2.20, 1.30, 1.20, -0.20, -0.30, -2.05, -2.15, -3.50, -3.60, -4.80, -4.90, -6.22, -6.37]
    rings = [g(y, lod=(i % 2 == 0 or i == len(ys) - 1)) for i, y in enumerate(ys)]
    bands = [("Paint" if i in (0, len(ys) - 2) else ("Glass" if i % 2 else "Trim"), "Paint", "Paint") for i in range(len(ys) - 1)]
    front = lambda k: "Glass" if k == 0 else ("Sign" if k == 1 else "Paint")
    rear = lambda k: "Glass" if k == 0 else "Paint"
    ck.greenhouse(b, rings, bands, dict(K=2, M=2, cap0=("pairs", front), cap1=("pairs", rear)), lod)
    p.insets = [("Glass", "Trim", 0.03, -0.012), ("Glass:door", "Trim", 0.03, -0.012), ("LightFront", "Chrome", 0.02, -0.01),
                ("LightRear", "Trim", 0.02, -0.008), ("Sign", "Trim", 0.03, -0.01)]
    p.wheels = ck.four_wheels(r, 0.31, 2.1, fy, ry, lod, rim=0.29, dish="Chrome", hub="Trim")
    b.rbox((0, -2.2, top + 0.06), (1.9, 3.0, 0.2), 0.06, "Paint2", axis=1, ends=lod == 0)    # roof air-conditioning pod
    if lod:
        return p
    ck.cabin_dark(b, rings, d=0.06)
    tub.arch_lips(b, "Trim", out=0.012, width=0.05)
    b.rbox((0, 3.2, top + 0.03), (1.0, 1.1, 0.1), 0.03, "Paint2", axis=1)                    # roof hatch
    ck.plate(b, 5.62, 0.66, 1, 0.42, 0.11)
    ck.plate(b, -6.38, 0.62, -1, 0.42, 0.11)
    for sx in (-1, 1):  # mirrors on curved arms
        b.tube([(sx * 1.16, 5.52, 2.52), (sx * 1.34, 5.78, 2.56), (sx * 1.40, 5.82, 2.28)], 0.02, "Trim", segs=5)
        b.rbox((sx * 1.40, 5.83, 2.06), (0.06, 0.16, 0.42), 0.02, "Trim", axis=2)
    for y0, y1 in BUS_DOORS:  # door leaves meet in the middle
        ym = (y0 + y1) / 2
        b.box((tub.x_at(ym, 0.75) + 0.004, ym, 0.76), (0.012, 0.03, 0.72), "Trim")
        b.box((1.218, ym, 1.76), (0.012, 0.03, 1.26), "Trim")
    for sx in (-1, 1):
        b.cyl((sx * 0.45 - 0.3, 5.635, 1.16), (sx * 0.45 + 0.25, 5.63, 1.7), 0.012, "Trim", segs=4)   # wipers
    b.box((0, -6.385, 0.86), (1.5, 0.012, 0.03), "Paint")      # engine cover lines
    b.box((0, -6.385, 0.62), (1.5, 0.012, 0.03), "Paint")
    return p


# ---------------------------------------------------------------- Tram: rubber-tyred, single guide rail
TRAM_END, TRAM_MID, TRAM_BELLOWS = 8.9, 7.2, 0.25
TRAM_ROOF, TRAM_CANT = 2.66, 2.20


def tram_bellows(b, y0, y1, lod):
    """Accordion gangway between y0 (inside the car end) and y1 (the coupling plane)."""
    def ring(y, w, top):
        z0, c = 0.36, 0.14
        pts = [(w, z0), (w, top - c), (w - c, top), (-(w - c), top), (-w, top - c), (-w, z0)]
        return [(x, y, z) for x, z in pts]
    n = 4 if lod == 0 else 1
    rows = [ring(y0 + (y1 - y0) * i / n, 1.02 if i % 2 == 0 else 0.96, 2.5 if i % 2 == 0 else 2.44) for i in range(n + 1)]
    b.grid(rows, "Trim", wrap_u=True, cap0=("ngon", "Trim"), cap1=("ngon", "Trim"), smooth=False)


def tram_body(kind, lod):
    """One car body in its own frame: nose (or first coupling) towards +Y, centred on y = 0."""
    b = Kit(kind)
    end = kind == "end"
    half = (TRAM_END if end else TRAM_MID) / 2
    y_rear = -half + TRAM_BELLOWS
    if end:
        stations = [(y_rear, 1.10, 0.14, 0.86, 0.88), (2.4, 1.10, 0.14, 0.86, 0.88), (3.2, 1.08, 0.14, 0.87, 0.92),
                    (3.75, 1.0, 0.16, 0.88, 0.95), (4.1, 0.86, 0.19, 0.88, 0.95), (4.33, 0.68, 0.24, 0.85, 0.90),
                    (4.45, 0.46, 0.31, 0.78, 0.81)]
        doors = ((0.2, 1.5), (-3.3, -2.0))
        arches = [(2.85, 0.45, 0.36)]
        keep = (0, 1, 3, 5, 6)
    else:
        stations = [(y_rear, 1.10, 0.14, 0.86, 0.88), (0.0, 1.10, 0.14, 0.86, 0.88), (-y_rear, 1.10, 0.14, 0.86, 0.88)]
        doors = ((-0.7, 0.6),)
        arches = [(-2.75, 0.45, 0.36), (2.75, 0.45, 0.36)]
        keep = (0, 2)
    patches = [dict(on="side", f=(0.0, 0.12), mat="Trim"), dict(on="side", f=(0.75, 1.0), mat="Paint2")]
    patches += [dict(on="side", y=d, f=(0.12, 1.0), mat="Glass:door") for d in doors]
    if end:
        patches += [dict(on="side", y=(4.1, 4.5), f=(0.45, 0.75), mat="LightFront"),
                    dict(on="side", y=(4.1, 4.5), f=(0.12, 0.45), mat="LightRear"),
                    dict(on="front:side", f=(0.12, 0.75), mat="Trim"),
                    dict(on="side", y=(4.1, 4.5), f=(0.75, 1.0), mat="Paint"),
                    dict(on="front:side", f=(0.0, 0.12), mat="Trim")]
    tub = ck.Tub(stations, dict(tuck=0.05, shoulder=0.01, ntop=3.0, rows=(0.12, 0.45, 0.75), rows1=(0.12, 0.75), na=3,
                                arch_segs=6, well=0.3, lod_keep=keep, lod_arches=False), arches, patches)
    tub.build(b, lod)

    def g(y, yt=None, **k):
        kw = dict(ledge=0.012, sink=0.01, zc=TRAM_CANT, rcx=0.16, rcz=0.38)
        kw.update(k)
        return ck.gring(tub, y, y if yt is None else yt, kw.pop("wc", tub.side_x(y, 1.0) - 0.14), kw.pop("zr", TRAM_ROOF), **kw)

    if end:
        rings = [ck.gflat(tub, 3.95, bow=0.32, crown=0.05, ledge=0.012),
                 g(3.95, 2.70, wc=0.90, bow=0.2, zr=TRAM_ROOF - 0.06, zc=TRAM_CANT - 0.03),
                 g(3.70, 2.52, wc=0.93, lod=False)]
        bands = [(None, "Glass", "Paint"), ("Paint", "Paint2", "Paint")]
        ys = [2.35, 2.25, 1.60, 1.50, 0.20, 0.10, -1.90, -2.0, -3.30, -3.40, y_rear + 0.1, y_rear]
    else:
        rings, bands = [g(-y_rear), g(-y_rear - 0.1, lod=False)], [("Paint2", "Paint2", "Paint2")]
        ys = [2.0, 1.9, 0.7, 0.6, -0.7, -0.8, -1.9, -2.0, y_rear + 0.1, y_rear]
    for i, y in enumerate(ys):  # glass up to each pillar, then the pillar itself; the last pair is the end post
        last = i == len(ys) - 1
        bands.append(("Glass", "Paint2", "Paint2") if i % 2 == 0 else ("Paint2" if last else "Trim", "Paint2", "Paint2"))
        rings.append(g(y, lod=i % 2 == 0 or last))
    ck.greenhouse(b, rings, bands, dict(K=2, M=2, cap0=("ngon", "Trim"), cap1=("ngon", "Trim")), lod)
    tram_bellows(b, y_rear + 0.01, -half, lod)
    if not end:
        tram_bellows(b, -y_rear - 0.01, half, lod)
    # roof equipment fairing
    span = (y_rear + 0.5, 2.0) if end else (y_rear + 0.5, -y_rear - 0.5)
    b.rbox((0, (span[0] + span[1]) / 2, TRAM_ROOF + 0.1), (1.5, span[1] - span[0], 0.26), 0.08, "Paint2", axis=1, ends=lod == 0)
    if lod:
        return b
    ck.cabin_dark(b, rings, d=0.06)
    tub.arch_lips(b, "Trim", out=0.01, width=0.04, segs=6)
    for yc, _, zc in arches:
        for sx in (-1, 1):
            b.add(ck.wheel(0.36, 0.2, sx, 1, rim=0.2, dish="Trim", segs=12), Matrix.Translation((sx * 0.87, yc, zc)))
    for d in doors:
        ym = (d[0] + d[1]) / 2
        for sx in (-1, 1):
            b.box((sx * (tub.x_at(ym, 0.6) + 0.004), ym, 0.60), (0.012, 0.03, 0.5), "Trim")
            b.box((sx * 0.99, ym, 1.52), (0.012, 0.03, 1.3), "Trim", M=ak.rot("Y", -sx * 5.5, (sx * 0.99, ym, 1.52)))
    if end:
        for sx in (-1, 1):
            b.cyl((sx * 0.3 - 0.25, 4.22, 0.98), (sx * 0.3 + 0.2, 3.95, 1.55), 0.012, "Trim", segs=4)    # wipers
            b.rbox((sx * 1.12, 2.95, 1.95), (0.05, 0.1, 0.3), 0.02, "Trim", axis=2)                        # mirrors
            b.box((sx * 1.04, 2.95, 2.05), (0.16, 0.03, 0.03), "Trim")
    else:  # pantograph on the middle car
        b.box((0, 0.2, TRAM_ROOF + 0.25), (0.9, 1.3, 0.04), "Trim")
        b.tube([(0, 0.7, TRAM_ROOF + 0.27), (0, -0.3, TRAM_ROOF + 0.42), (0, 0.5, TRAM_ROOF + 0.6)], 0.03, "Trim", segs=5)
        b.cyl((-0.65, 0.5, TRAM_ROOF + 0.6), (0.65, 0.5, TRAM_ROOF + 0.6), 0.025, "Trim", segs=5)
    return b


TRAM_COLORS = [("Blu Padova", "#22479A"), ("Blu notte", "#1B2F63"), ("Azzurro", "#3F7FC4"), ("Grigio metallizzato", "#8E959C")]


@vehicle("Tram", budget=(7000, 1000), mass=23000, seat=(0, 11.3, 1.25), paint2="#F2F3F1", wheel_radius=0.36, colors=TRAM_COLORS,
         extra={"segments": ["TramEnd", "TramMiddle", "TramEnd (rotated 180 degrees about Y)"],
                "segmentOffsets": [(TRAM_END + TRAM_MID) / 2, 0.0, -(TRAM_END + TRAM_MID) / 2]})
def tram(lod):
    p = Parts()
    off = (TRAM_END + TRAM_MID) / 2
    p.body.add(tram_body("end", lod), Matrix.Translation((0, off, 0)))
    p.body.add(tram_body("mid", lod))
    p.body.add(tram_body("end", lod), Matrix.Translation((0, -off, 0)) @ Matrix.Rotation(math.pi, 4, "Z"))
    p.insets = TRAM_INSETS
    return p


TRAM_INSETS = [("Glass", "Trim", 0.03, -0.012), ("Glass:door", "Trim", 0.03, -0.012), ("LightFront", "Chrome", 0.016, -0.008),
               ("LightRear", "Trim", 0.014, -0.006)]


@vehicle("TramEnd", budget=(7000, 1000), mass=8200, seat=(0, 3.25, 1.25), paint2="#F2F3F1", wheel_radius=0.36, colors=TRAM_COLORS,
         extra={"couplingRear": -TRAM_END / 2, "note": "End car: nose at +Z, coupling plane at z = couplingRear."})
def tram_end(lod):
    p = Parts()
    p.body.add(tram_body("end", lod))
    p.insets = TRAM_INSETS
    return p


@vehicle("TramMiddle", budget=(7000, 1000), mass=6600, seat=(0, 0, 1.25), paint2="#F2F3F1", wheel_radius=0.36, colors=TRAM_COLORS,
         extra={"couplingFront": TRAM_MID / 2, "couplingRear": -TRAM_MID / 2, "note": "Middle car: coupling planes at both ends."})
def tram_middle(lod):
    p = Parts()
    p.body.add(tram_body("mid", lod))
    p.insets = TRAM_INSETS
    return p


# ---------------------------------------------------------------- Bicicletta: city bike with basket
BIKE_PIVOT = (0.0, 0.38, 0.76)


def bike_wheel(r, lod):
    k = Kit("Wheel")
    t = 0.018
    big = r - t
    segs, around = (20, 5) if lod == 0 else (10, 3)
    rows = []
    for i in range(segs):
        a = 2 * math.pi * i / segs
        rows.append([(t * math.sin(2 * math.pi * j / around), (big + t * math.cos(2 * math.pi * j / around)) * math.cos(a),
                      (big + t * math.cos(2 * math.pi * j / around)) * math.sin(a)) for j in range(around)])
    k.grid(rows, "Rubber", wrap_u=True, wrap_v=True)
    if lod:
        return k
    rim = big - t
    rows = [[(x, rr * math.cos(2 * math.pi * i / segs), rr * math.sin(2 * math.pi * i / segs))
             for x, rr in ((-0.011, rim - 0.014), (-0.011, rim + 0.004), (0.011, rim + 0.004), (0.011, rim - 0.014))] for i in range(segs)]
    k.grid(rows, "Chrome", wrap_u=True, wrap_v=True)
    k.cyl((-0.045, 0, 0), (0.045, 0, 0), 0.022, "Chrome", segs=6)
    for i in range(10):
        a = 2 * math.pi * i / 10
        sx = 0.035 if i % 2 else -0.035
        k.tube([(sx, 0.02 * math.cos(a + 0.5), 0.02 * math.sin(a + 0.5)), (0, (rim - 0.01) * math.cos(a), (rim - 0.01) * math.sin(a))],
               0.004, "Chrome", segs=3)
    return k


@vehicle("Bicicletta", budget=(3000, 500), mass=16, seat=(0, -0.27, 0.95), steer_axis=(0, -0.196, 0.981), paint2="#EDEDE8",
         colors=[("Nero", "#1D1F21"), ("Verde bottiglia", "#1F4D3A"), ("Rosso", "#A8261F"), ("Celeste", "#8FBAD3"), ("Crema", "#E9E2CC")])
def bicicletta(lod):
    p = Parts()
    b = p.body
    r, fy, ry = 0.345, 0.55, -0.55
    seg = 6 if lod == 0 else 3
    axle_r, axle_f, bb = (0, ry, r), (0, fy, r), (0, -0.10, 0.29)
    seat_top, head_top, head_low = (0, -0.25, 0.83), (0, 0.36, 0.86), (0, 0.40, 0.66)
    b.tube([bb, seat_top], 0.017, "Paint", segs=seg)                                     # seat tube
    b.tube([head_low, bb], 0.019, "Paint", segs=seg)                                     # down tube
    b.tube([(0, 0.385, 0.735), (0, 0.12, 0.56), (0, -0.19, 0.52)], 0.016, "Paint", segs=seg)   # low step-through top tube
    b.tube([head_top, head_low], 0.021, "Paint", segs=seg)                               # head tube
    for sx in (-1, 1):
        b.tube([(sx * 0.03, -0.12, 0.29), (sx * 0.055, ry, r)], 0.010, "Paint", segs=seg)      # chain stays
        b.tube([(sx * 0.02, -0.225, 0.74), (sx * 0.055, ry, r)], 0.009, "Paint", segs=seg)     # seat stays
    b.tube([(0, -0.235, 0.78), (0, -0.265, 0.90)], 0.012, "Chrome", segs=seg)             # seat post
    b.rbox((0, -0.27, 0.925), (0.17, 0.27, 0.06), 0.025, "Seat", axis=1, ends=lod == 0)
    p.wheels = {"Wheel_F": (bike_wheel(r, lod), axle_f), "Wheel_R": (bike_wheel(r, lod), axle_r)}
    s = Kit("Steer")
    for sx in (-1, 1):
        s.tube([(sx * 0.02, 0.40, 0.66), (sx * 0.05, 0.47, 0.50), (sx * 0.055, fy, r)], 0.011, "Paint", segs=seg)   # fork blades
    s.tube([(0, 0.355, 0.86), (0, 0.335, 1.0), (0, 0.29, 1.03)], 0.012, "Chrome", segs=seg)   # stem
    bar = [(-0.29, 0.10, 1.02), (-0.27, 0.22, 1.03), (-0.14, 0.30, 1.03), (0.14, 0.30, 1.03), (0.27, 0.22, 1.03), (0.29, 0.10, 1.02)]
    s.tube(bar, 0.011, "Chrome", segs=seg)
    if lod == 0:
        for sx in (-1, 1):
            s.cyl((sx * 0.29, 0.10, 1.02), (sx * 0.292, -0.01, 1.015), 0.016, "Rubber", segs=6)   # grips
        # wicker basket in front of the bars
        def loop(w, d, z, yc=0.56):
            c = 0.04
            return [(x, yc + y, z) for x, y in ((w, d - c), (w - c, d), (-w + c, d), (-w, d - c), (-w, -d + c), (-w + c, -d), (w - c, -d), (w, -d + c))]
        s.grid([loop(0.13, 0.10, 0.80), loop(0.17, 0.125, 1.02), loop(0.155, 0.11, 1.02), loop(0.118, 0.088, 0.815)], "Seat",
               wrap_u=True, cap0=("ngon", "Seat"), cap1=("ngon", "Seat"), smooth=False)
        s.tube([(-0.05, 0.42, 0.98), (-0.05, 0.445, 0.92)], 0.006, "Chrome", segs=3)
        s.tube([(0.05, 0.42, 0.98), (0.05, 0.445, 0.92)], 0.006, "Chrome", segs=3)
        ck.lamp(s, (0, 0.70, 0.74), 0.035, (0, 1, 0), back=0.05, bulge=0.012, segs=6)
        s.tube([(0, 0.42, 0.70), (0, 0.66, 0.74)], 0.006, "Chrome", segs=3)
        s.lathe([(0.022, 0), (0.018, 0.014), (0, 0.02)], "Chrome", origin=(-0.2, 0.275, 1.04), segs=6)    # bell
    guard = lambda yc, a0, a1, n: [[(x, yc + math.cos(a) * rr, r + math.sin(a) * rr) for x, rr in
                                    ((-0.028, r + 0.012), (-0.02, r + 0.03), (0.02, r + 0.03), (0.028, r + 0.012))]
                                   for a in [math.radians(a0 + (a1 - a0) * i / n) for i in range(n + 1)]]
    n = 8 if lod == 0 else 3
    s.grid(guard(fy, 20, 150, n), "Paint", wrap_u=True, cap0=("ngon", "Paint"), cap1=("ngon", "Paint"))
    b.grid(guard(ry, 35, 200, n), "Paint", wrap_u=True, cap0=("ngon", "Paint"), cap1=("ngon", "Paint"))
    p.steer = (s.shift([-c for c in BIKE_PIVOT]), BIKE_PIVOT)
    if lod:
        return p
    # drivetrain, chain guard, rack, stand and lights
    b.lathe([(0.085, -0.004), (0.085, 0.004)], "Chrome", origin=(0.045, -0.10, 0.29), axis=(1, 0, 0), segs=10)
    b.rbox((0.05, -0.31, 0.31), (0.012, 0.52, 0.10), 0.004, "Paint", axis=1)
    b.cyl((-0.07, -0.10, 0.29), (0.07, -0.10, 0.29), 0.018, "Chrome", segs=6)
    for sx, (dy, dz) in ((1, (0.12, -0.12)), (-1, (-0.12, 0.12))):
        b.box((sx * 0.075, -0.10 + dy / 2, 0.29 + dz / 2), (0.012, 0.022, 0.17), "Chrome", M=ak.rot("X", 45, (sx * 0.075, -0.10 + dy / 2, 0.29 + dz / 2)))
        b.box((sx * 0.125, -0.10 + dy, 0.29 + dz), (0.09, 0.07, 0.02), "Rubber")
    rack = [(-0.07, -0.30, 0.73), (-0.07, -0.86, 0.73), (0.07, -0.86, 0.73), (0.07, -0.30, 0.73)]
    b.tube(rack, 0.007, "Chrome", segs=4)
    for sx in (-1, 1):
        b.tube([(sx * 0.07, -0.72, 0.73), (sx * 0.055, ry, r)], 0.006, "Chrome", segs=3)
    b.box((0, -0.58, 0.728), (0.14, 0.5, 0.008), "Chrome")
    b.rbox((0, -0.905, 0.60), (0.05, 0.02, 0.04), 0.008, "LightRear", axis=1)
    b.tube([(-0.05, -0.16, 0.27), (-0.14, -0.30, 0.02)], 0.008, "Trim", segs=4)           # kickstand
    return p


# ---------------------------------------------------------------- driver
def preview(name, parts, lod, meta):
    out = ak.RENDERS
    out.mkdir(parents=True, exist_ok=True)
    paint = bpy.data.materials.get("Paint")
    if paint and meta.get("colors"):
        ak.tint(paint, hex_rgb(meta["colors"][meta.get("preview", 0)][1]))
    paint2 = bpy.data.materials.get("Paint2")
    if paint2 and meta.get("paint2"):
        ak.tint(paint2, hex_rgb(meta["paint2"]))
    stage = ak.Stage()
    lod.hide_render = True
    shots = []
    for tag, az, el in (("front", 35, 13), ("side", 90, 3), ("rear", 147, 13)):
        path = out / f"_{name}-{tag}.png"
        stage.shot(parts, path, az, el)
        shots.append(path)
    for ob in parts:
        ob.hide_render = True
    lod.hide_render = False
    path = out / f"_{name}-lod.png"
    stage.shot([lod], path, 35, 13)
    shots.append(path)
    ak.sheet(shots, out / f"vehicle-{name}.png", columns=2)
    for ob in parts:
        ob.hide_render = False
    lod.hide_render = True
    stage.remove()
    if paint:
        ak.tint(paint, (1, 1, 1))


def hex_rgb(h):
    h = h.lstrip("#")
    srgb = [int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return tuple(c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in srgb)


def build(name, render):
    fn, meta = VEHICLES[name]
    ak.reset_scene()
    p0, p1 = fn(0), fn(1)
    palette = ak.VEHICLE_MATERIALS
    parts = [ak.to_object(p0.body, palette, insets=p0.insets, name="Body")]
    for wname, (kit, pos) in p0.wheels.items():
        parts.append(ak.to_object(kit, palette, name=wname, location=pos))
    if p0.steer:
        parts.append(ak.to_object(p0.steer[0], palette, name="Steer", location=p0.steer[1]))
    low = p1.body
    for kit, pos in list(p1.wheels.values()) + ([p1.steer] if p1.steer else []):
        low.add(kit, Matrix.Translation(pos))
    lod = ak.to_object(low, palette, name="Body_LOD1")
    lod.display_type = "WIRE"
    lod.hide_render = True
    root = ak.assemble(name, parts + [lod])
    bpy.context.view_layer.update()
    tris0, tris1 = sum(ak.triangles(o) for o in parts), ak.triangles(lod)
    corners = [o.matrix_world @ Vector(c) for o in parts for c in o.bound_box]
    lo = [min(c[i] for c in corners) for i in range(3)]
    hi = [max(c[i] for c in corners) for i in range(3)]
    first = next(iter(p0.wheels.values()), None)
    entry = {
        "name": name, "file": f"Assets/Art/Vehicles/{name}.fbx",
        "length": round(hi[1] - lo[1], 3), "width": round(hi[0] - lo[0], 3), "height": round(hi[2], 3),
        "boundsMin": unity(lo), "boundsMax": unity(hi),
        "wheelRadius": round(meta.get("wheel_radius", first[1][2] if first else 0.0), 3),
        "wheels": [{"name": n, "position": unity(pos)} for n, (_, pos) in p0.wheels.items()],
        "driverSeat": unity(meta["seat"]), "mass": meta["mass"], "tintable": meta.get("tintable", True),
        "colors": [c[1] for c in meta["colors"]], "colorNames": [c[0] for c in meta["colors"]],
        "paint2": meta.get("paint2", "#EDEDE8"),
        "triangles": tris0, "trianglesLod1": tris1,
        "materials": sorted({m.name for o in parts + [lod] for m in o.data.materials}),
    }
    if p0.steer:
        entry["steer"] = {"position": unity(p0.steer[1]), "axis": unity(meta["steer_axis"])}
    entry.update(meta.get("extra", {}))
    budget = meta["budget"]
    status = "OK" if tris0 <= budget[0] and tris1 <= budget[1] else "OVER BUDGET"
    print(f"BUILT {name}: {tris0}/{budget[0]} tris, LOD1 {tris1}/{budget[1]}  {entry['length']} x {entry['width']} x {entry['height']} m  {status}")
    ak.export(root, BLEND / f"{name}.blend", ART / f"{name}.fbx")
    if render:
        preview(name, parts, lod, meta)
    return entry


def write_manifest(entries):
    data = {"note": "Generated by Tools/blender/build_vehicles.py. Positions are in Unity axes (x right, y up, z forward), metres, relative to the vehicle root.",
            "vehicles": []}
    if MANIFEST.exists():
        data["vehicles"] = json.loads(MANIFEST.read_text())["vehicles"]
    known = {v["name"]: v for v in data["vehicles"]}
    known.update({e["name"]: e for e in entries})
    data["vehicles"] = [known[n] for n in VEHICLES if n in known]
    MANIFEST.parent.mkdir(parents=True, exist_ok=True)
    MANIFEST.write_text(json.dumps(data, indent=1) + "\n")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    wanted = [a for a in argv if a in VEHICLES] or list(VEHICLES)
    entries = [build(name, "--render" in argv) for name in wanted]
    write_manifest(entries)


if __name__ == "__main__":
    main()
