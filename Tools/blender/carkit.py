"""Vehicle shells for build_vehicles.py: lower body with wheel wells, greenhouse with pillars and
glass, wheels. Asset space: X right, Y forward, Z up, metres; origin on the ground at the centre of
the wheelbase. Every shell takes lod (0 full, 1 for Body_LOD1) and samples the same curves."""
import math

from assetkit import Kit, pchip


class Tub:
    """Lower body lofted through cross-sections along Y.

    stations: rows (y, W, zf, zk, zt) from tail tip to nose tip: widest half-width, underside,
    shoulder (fender/belt line) and centre-line top (bonnet, deck). The side is a barrel between
    underside and shoulder; the top is a superellipse over the shoulder. Wheel arches lift the
    lower edge of the side on a circle and open a well behind it.
    patches: dicts {on, y, f, mat, sx}; on = side|top|under (y range, f range of the cell) or
    front:side, front:top, rear:side, rear:top for the end faces; sx = 1 or -1 limits a patch to the
    right or left flank.
    """

    def __init__(self, stations, P=None, arches=(), patches=()):
        self.st = stations
        self.P = dict(tuck=0.07, shoulder=0.03, ntop=3.5, rows=(0.22, 0.5, 0.74), rows1=(0.5,), na=5, na1=2,
                      arch_segs=8, well=0.27, paint="Paint", under="Trim", lod_keep=None, lod_arches=True)
        self.P.update(P or {})
        self.fW, self.fF, self.fK, self.fT = (pchip([(s[0], s[i]) for s in stations]) for i in (1, 2, 3, 4))
        self.arches = list(arches)  # (y centre, radius, z centre)
        self.patches = list(patches)

    def ntop(self, y):
        n = self.P["ntop"]
        return n(y) if callable(n) else n

    def side_x(self, y, s):
        w = self.fW(y)
        return w - min(self.P["tuck"], 0.5 * w) * (1 - s) ** 3 - min(self.P["shoulder"], 0.3 * w) * s ** 4

    def x_at(self, y, z):
        """Half-width of the body side at height z."""
        zf, zk = self.fF(y), self.fK(y)
        return self.side_x(y, min(max((z - zf) / (zk - zf), 0.0), 1.0))

    def top_at(self, y, x=0.0):
        """Height of the upper surface at lateral offset x."""
        zk, zt, n = self.fK(y), self.fT(y), self.ntop(y)
        u = min(abs(x) / self.side_x(y, 1.0), 1.0)
        return zk + (zt - zk) * (1 - u ** n) ** (1 / n)

    def z_low(self, y):
        for yc, ra, zc in self.arches:
            d = abs(y - yc)
            if d <= ra + 1e-6:
                return zc + math.sqrt(max(ra * ra - d * d, 0.0))
        return None

    def columns(self, lod):
        ys = [s[0] for s in self.st]
        if lod:
            keep = self.P["lod_keep"]
            ys = [ys[i] for i in keep] if keep else [ys[0]] + ys[1:-1][::2] + [ys[-1]]
        aseg = self.P["arch_segs"] if lod == 0 else 3
        for yc, ra, _ in (self.arches if lod == 0 or self.P["lod_arches"] else ()):
            ys = [y for y in ys if not (yc - ra - 0.04 < y < yc + ra + 0.04)]
            ys += [yc + ra * math.cos(math.pi * k / aseg) for k in range(aseg + 1)]
            ys += [yc - ra - 0.012, yc + ra + 0.012]
        if lod == 0:
            for p in self.patches:
                ys += [y for y in p.get("y", ()) if self.st[0][0] < y < self.st[-1][0]]
        out = []
        for y in sorted(ys):
            if not out or y - out[-1] > 0.004:
                out.append(y)
        return out

    def fractions(self, lod):
        return [0.0] + list(self.P["rows"] if lod == 0 else self.P["rows1"]) + [1.0]

    def ring(self, y, lod):
        P = self.P
        zf, zk, zt = self.fF(y), self.fK(y), self.fT(y)
        fr = self.fractions(lod)
        na = P["na"] if lod == 0 else P["na1"]
        zl = self.z_low(y) if lod == 0 or P["lod_arches"] else None
        s_low = 0.0 if zl is None else min(max((zl - zf) / (zk - zf), 0.0), 0.92)
        x0 = self.side_x(y, s_low)
        half = [(0.0, zf)]
        if lod:
            pass  # Body_LOD1 has no wheel wells: the floor simply rises to the arch
        elif s_low > 0:
            half += [(x0 - P["well"], zf), (x0 - P["well"], zf + s_low * (zk - zf))]
        else:
            half += [(x0, zf), (x0, zf)]
        for f in fr:
            f = max(f, s_low)
            half.append((self.side_x(y, f), zf + f * (zk - zf)))
        wk, n = half[-1][0], self.ntop(y)
        for k in range(1, na + 1):
            a = k / na * math.pi / 2
            half.append((wk * math.cos(a) ** (2 / n) if k < na else 0.0, zk + (zt - zk) * math.sin(a) ** (2 / n)))
        return half

    def cell_mat(self, k, y, where, lod, sx=0):
        fr = self.fractions(lod)
        ns = len(fr) - 1
        na = self.P["na"] if lod == 0 else self.P["na1"]
        nu = 3 if lod == 0 else 1
        if k < nu:
            kind, lo, hi = "under", 0.0, 0.0
        elif k < nu + ns:
            kind, lo, hi = "side", fr[k - nu], fr[k - nu + 1]
        else:
            j = k - nu - ns
            kind, lo, hi = "top", j / na, (j + 1) / na
        on = kind if where == "body" else where + ":" + kind
        mid = (lo + hi) / 2
        for p in reversed(self.patches):
            if p["on"] != on or (sx and p.get("sx", sx) != sx):
                continue
            if "y" in p and y is not None and not (p["y"][0] <= y <= p["y"][1]):
                continue
            if "f" in p and not (p["f"][0] <= mid <= p["f"][1]):
                continue
            return p["mat"]
        return self.P["under"] if kind == "under" else self.P["paint"]

    def build(self, kit, lod=0):
        cols = self.columns(lod)
        halves = [self.ring(y, lod) for y in cols]
        rings = [[(x, y, z) for x, z in h] + [(-x, y, z) for x, z in reversed(h[1:-1])] for y, h in zip(cols, halves)]
        n, hc = len(rings[0]), len(halves[0]) - 1

        def mat(i, j):
            return self.cell_mat(j if j < hc else n - 1 - j, (cols[i] + cols[i + 1]) / 2, "body", lod, 1 if j < hc else -1)

        kit.grid(rings, mat, wrap_u=True,
                 cap0=("strips", lambda k: self.cell_mat(k, None, "rear", lod)),
                 cap1=("strips", lambda k: self.cell_mat(k, None, "front", lod)))

    def arch_lips(self, kit, mat="Paint", out=0.018, width=0.04, segs=8):
        """Raised lip around every wheel arch."""
        for yc, ra, zc in self.arches:
            for sx in (-1, 1):
                rows = []
                samples = [(yc + ra, self.fF(yc + ra) + 0.005, 0.0)] + \
                          [(yc + ra * math.cos(math.pi * k / segs), zc + ra * math.sin(math.pi * k / segs), math.pi * k / segs) for k in range(segs + 1)] + \
                          [(yc - ra, self.fF(yc - ra) + 0.005, math.pi)]
                for y, z, a in samples:
                    uy, uz = math.cos(a), math.sin(a)
                    yi, zi = y - uy * 0.004, z - uz * 0.004
                    yo, zo = y + uy * width, z + uz * width
                    xi, xo = self.x_at(yi, zi), self.x_at(yo, zo)
                    rows.append([(sx * (xi - 0.03), yi, zi), (sx * (xi + out), yi, zi),
                                 (sx * (xo + out * 0.4), yo, zo), (sx * (xo - 0.03), yo, zo)])
                kit.grid(rows, mat, wrap_u=True, cap0=("ngon", mat), cap1=("ngon", mat))

    def outline(self, z, y0, y1, step=0.12, offset=0.0, side=1):
        """Plan polyline of the body side at height z between y0 and y1 (for bumpers and strips)."""
        ys = sorted({y0, y1} | {s[0] for s in self.st if y0 < s[0] < y1})
        dense = []
        for a, b in zip(ys, ys[1:]):
            m = max(1, int(math.ceil((b - a) / step)))
            dense += [a + (b - a) * i / m for i in range(m)]
        dense.append(ys[-1])
        return [(side * (self.x_at(y, z) + offset), y, z) for y in dense]


def gring(tub, yb, yt, wc, zr, rc=0.09, crown=0.03, ledge=0.03, sink=0.015, **extra):
    """Greenhouse ring standing on the tub shoulder at yb and reaching the roof (centre height zr)
    at yt. wc is the half-width at the cant rail; the rail sits rc + crown below the roof centre."""
    ring = dict(yb=yb, yt=yt, wb=tub.side_x(yb, 1.0) - ledge, wc=wc, zb=tub.fK(yb) - sink, zc=zr - rc - crown, zr=zr, rcx=rc, rcz=rc)
    ring.update(extra)
    return ring


def gflat(tub, y, crown=0.03, ledge=0.03, sink=0.015, **extra):
    """Collapsed ring along the base of the windscreen or rear window."""
    w, z = tub.side_x(y, 1.0) - ledge, tub.fK(y) - sink
    return dict(yb=y, yt=y, wb=w, wc=w, zb=z, zc=z, zr=tub.top_at(y) - sink + crown, **extra)


def greenhouse(kit, rings, bands, P=None, lod=0):
    """Cabin lofted over rings that may lean (yb at the belt, yt at the roof).

    Each half ring is base, cant rail, K corner points, M roof points to the centre line. The
    corner cells are the roof rail along the roof and the pillar along a raked screen, so pillars
    come out with constant width. bands[i] = (side, top[, corner]) materials between ring i and
    i+1; side may be a (right, left) pair. A ring with lod=False is dropped from Body_LOD1
    together with the band that ends on it (flag the rear ring of each pillar).
    P: cap0/cap1 = ('ngon', material) or ('pairs', f(k)) with k = 0 side, 1..K corner, then roof.
    """
    P = dict(rc=0.09, pillar=0.075, K=2, M=3, K1=1, M1=1, paint="Paint", bottom="Interior", cap0=None, cap1=None) | (P or {})
    if lod:
        keep = [i for i, r in enumerate(rings) if r.get("lod", True)]
        bands = [bands[i - 1] for i in keep[1:]]  # the band that arrives at each kept ring
        rings = [rings[i] for i in keep]
    K = P["K"] if lod == 0 else P["K1"]
    Mt = P["M"] if lod == 0 else P["M1"]
    full = []
    for r in rings:
        flat = abs(r["zc"] - r["zb"]) < 1e-6 and abs(r["wc"] - r["wb"]) < 1e-6
        rcx = r.get("rcx", P["pillar"] if flat else P["rc"])
        rcz = r.get("rcz", 0.0 if flat else P["rc"])
        bow = r.get("bow", 0.0)
        xe, ze = r["wc"] - rcx, r["zc"] + rcz
        half = [(r["wb"], r["yb"], r["zb"]), (r["wc"], r["yt"], r["zc"])]
        for k in range(1, K + 1):
            a = k / K * math.pi / 2
            half.append((xe + rcx * math.cos(a), r["yt"], r["zc"] + rcz * math.sin(a)))
        for m in range(1, Mt + 1):
            u = 1 - m / Mt
            half.append((xe * u, r["yt"] + bow * (1 - u * u), r["zr"] - (r["zr"] - ze) * u * u))
        full.append(half + [(-x, y, z) for x, y, z in reversed(half[:-1])])
    n, hc = len(full[0]), 1 + K + Mt

    def mat(i, j):
        if j == n - 1:
            return P["bottom"]
        k = j if j < hc else n - 2 - j
        band = bands[i]
        side = band[0]
        if isinstance(side, tuple):
            side = side[0] if j < hc else side[1]
        if k == 0:
            return side
        if k <= K:
            return band[2] if len(band) > 2 else P["paint"]
        return band[1]

    kit.grid(full, mat, wrap_u=True, cap0=P["cap0"] or ("ngon", P["bottom"]), cap1=P["cap1"] or ("ngon", P["bottom"]))


def cabin_dark(kit, rings, d=0.04):
    """Dark volume just inside the glass, so see-through glass shows a tinted cabin instead of holes."""
    ym = (rings[0]["yb"] + rings[-1]["yb"]) / 2
    span = max(abs(rings[0]["yb"] - rings[-1]["yb"]), 0.5)
    sy = 1 - 2.4 * d / span
    inner = [dict(r, yb=ym + (r["yb"] - ym) * sy, yt=ym + (r["yt"] - ym) * sy, wb=r["wb"] - d, wc=r["wc"] - d,
                  zc=max(r["zc"] - d * 0.7, r["zb"]), zr=max(r["zr"] - d, r["zb"])) for r in rings]
    greenhouse(kit, inner, [("Interior", "Interior")] * (len(rings) - 1), dict(paint="Interior", K=1, M=2))


def wheel(r, width, side=1, lod=0, rim=None, dish="spokes", lip="Chrome", hub="Chrome", tyre="Rubber", segs=None):
    """Wheel around the origin, axle along X, outer face towards side * X.
    dish: 'spokes' (alternating Chrome/Trim), or one material name for a plain disc or hubcap."""
    k = Kit("Wheel")
    w, rr = width / 2, rim or r * 0.62
    sh = min(0.035, (r - rr) * 0.45, w * 0.5)
    if lod:
        prof = [(0, -w), (r, -w), (r, w), (rr, w), (0, w - 0.01)]
        mats = [tyre, tyre, tyre, "Trim" if dish == "spokes" else dish]
        k.lathe([(a, h) for a, h in prof], lambda i, j: mats[i], axis=(side, 0, 0), segs=segs or 8)
        return k
    prof = [(0, -w + 0.02), (r - sh, -w), (r, -w + sh), (r, w - sh), (r - sh, w), (rr + 0.008, w),
            (rr, w - 0.018), (rr * 0.84, w - 0.05), (rr * 0.3, w - 0.035), (0, w - 0.015)]
    segs = segs or 16

    def mat(i, j):
        if i < 5:
            return tyre
        if i == 5:
            return lip
        if i == 6:
            return lip if dish == "spokes" else dish
        if i == 7:
            return ("Chrome" if j % 2 == 0 else "Trim") if dish == "spokes" else dish
        return hub

    k.lathe(prof, mat, axis=(side, 0, 0), segs=segs)
    return k


def four_wheels(r, width, track, front_y, rear_y, lod=0, **style):
    """{name: (kit, position)} for a four-wheeled vehicle; track = distance between wheel centres."""
    out = {}
    for name, sx, y in (("Wheel_FL", -1, front_y), ("Wheel_FR", 1, front_y), ("Wheel_RL", -1, rear_y), ("Wheel_RR", 1, rear_y)):
        out[name] = (wheel(r, width, sx, lod, **style), (sx * track / 2, y, r))
    return out


# ---------------------------------------------------------------- small shared details
def plate(kit, y, z, facing, w=0.36, h=0.11):
    """Number plate as a thin slab; facing +1 looks forward, -1 back."""
    kit.box((0, y + facing * 0.006, z), (w, 0.012, h), "Plate")


def side_mirrors(kit, x, y, z, mat="Paint", size=(0.07, 0.11, 0.1)):
    for sx in (-1, 1):
        kit.rbox((sx * (x + size[0] / 2 + 0.045), y, z), size, 0.025, mat, axis=0)
        kit.box((sx * (x + 0.02), y, z - 0.02), (0.07, 0.04, 0.03), "Trim")


def door_handles(kit, tub, ys, z, mat="Chrome"):
    for y in ys:
        for sx in (-1, 1):
            kit.box((sx * (tub.x_at(y, z) + 0.004), y, z), (0.02, 0.13, 0.028), mat)


def seam(kit, tub, y, z0, z1, mat="Trim", width=0.012, lift=0.004, lean=0.0, steps=4):
    """Door shut line: a narrow strip lying on the body side from z0 to z1 (lean = dy/dz)."""
    for sx in (-1, 1):
        rows = []
        for i in range(steps + 1):
            z = z0 + (z1 - z0) * i / steps
            yy = y + lean * (z - z0)
            x = sx * (tub.x_at(yy, z) + lift)
            rows.append([(x, yy - width / 2, z), (x, yy + width / 2, z)])
        kit.grid(rows, mat, hint=(sx, 0, 0))


def wrap_path(tub, z, y_from, front=True, offset=0.03, step=0.1):
    """Plan path hugging the nose (or tail) at height z: up one flank, across, down the other."""
    tip = tub.st[-1][0] if front else tub.st[0][0]
    a, b = (y_from, tip) if front else (tip, y_from)
    right = tub.outline(z, a, b, step=step)
    left = [(-x, y, zz) for x, y, zz in right]
    pts = left + right[::-1] if front else right[::-1] + left
    out = []
    for i, q in enumerate(pts):
        p0, p1 = pts[max(i - 1, 0)], pts[min(i + 1, len(pts) - 1)]
        tx, ty = p1[0] - p0[0], p1[1] - p0[1]
        n = math.hypot(tx, ty) or 1.0
        out.append((q[0] - ty / n * offset, q[1] + tx / n * offset, q[2]))
    return out


def bumper(kit, tub, z, y_from, front=True, mat="Chrome", offset=0.035, thick=0.024, height=0.05):
    """Blade bumper standing off the body."""
    t, h, c = thick / 2, height / 2, min(thick, height) * 0.3
    section = [(-t, -h + c), (-t + c, -h), (t, -h), (t, h), (-t + c, h), (-t, h - c)]
    kit.sweep(wrap_path(tub, z, y_from, front, offset, step=0.22), section, mat)


def flank_strip(kit, tub, z, y0, y1, mat="Chrome", height=0.03, out=0.012):
    """Moulding along both flanks at height z (a number or f(y))."""
    zf = z if callable(z) else (lambda y: z)
    for sx in (-1, 1):
        ys = [q[1] for q in tub.outline(zf(y0), y0, y1, step=0.35)]
        path = [(sx * tub.x_at(y, zf(y)), y, zf(y)) for y in ys]
        if sx < 0:
            path.reverse()
        kit.sweep(path, [(-0.012, -height / 2), (out, -height / 2), (out, height / 2), (-0.012, height / 2)], mat)


def lamp(kit, pos, r, axis=(0, 1, 0), lens="LightFront", bezel="Chrome", back=0.1, bulge=0.035, segs=10):
    """Round lamp: bezel sunk into the body, domed lens facing along axis."""
    prof = [(r * 0.9, -back), (r, 0.0), (r * 0.8, 0.014), (r * 0.5, 0.014 + bulge * 0.8), (0.0, 0.014 + bulge)]
    mats = [bezel, bezel, lens, lens]
    kit.lathe(prof, lambda i, j: mats[i], origin=pos, axis=axis, segs=segs)
