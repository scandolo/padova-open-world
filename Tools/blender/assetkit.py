"""Shared kit for the procedural vehicle, street-prop and tree library.

Asset space (what every builder models in): X = right, Y = forward, Z = up, metres, origin on
the ground. export() bakes Blender's FBX axis change into the mesh data, so the file holds
(x, z, -y) with identity object rotations. Unity with bakeAxisConversion reads FBX (x, y, z) as
(x, y, -z) (the rule measured for the landmarks, see lmcommon.export), which gives X right,
Y up, Z forward with no instance rotation.

Headless only: /Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python ...
"""
import math
from pathlib import Path

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[2]
RENDERS = ROOT / ".context/renders"

# name -> (base colour, roughness, metallic, emission strength). Preview values only: Unity remaps
# every name onto its own shared material (AgentScripts/BuildVehiclesAndProps.cs).
VEHICLE_MATERIALS = {
    "Paint": ((1, 1, 1), 0.32, 0.0, 0),
    "Paint2": ((0.93, 0.93, 0.91), 0.35, 0.0, 0),
    "Glass": ((0.045, 0.06, 0.075), 0.08, 0.0, 0),
    "Chrome": ((0.78, 0.79, 0.8), 0.18, 1.0, 0),
    "Rubber": ((0.028, 0.028, 0.03), 0.85, 0.0, 0),
    "Trim": ((0.07, 0.072, 0.078), 0.55, 0.0, 0),
    "LightFront": ((1.0, 0.96, 0.82), 0.2, 0.0, 0.6),
    "LightRear": ((0.72, 0.03, 0.03), 0.25, 0.0, 0.25),
    "LightBlue": ((0.08, 0.3, 1.0), 0.2, 0.0, 0.5),
    "Interior": ((0.03, 0.03, 0.035), 0.9, 0.0, 0),
    "Seat": ((0.2, 0.11, 0.07), 0.7, 0.0, 0),
    "Plate": ((0.94, 0.94, 0.92), 0.5, 0.0, 0),
    "Sign": ((1.0, 0.72, 0.16), 0.4, 0.0, 0.5),
}
PROP_MATERIALS = {
    "Iron": ((0.075, 0.085, 0.085), 0.55, 0.3, 0),
    "Stone": ((0.76, 0.73, 0.66), 0.9, 0.0, 0),
    "Wood": ((0.4, 0.26, 0.14), 0.75, 0.0, 0),
    "Glass": ((0.1, 0.14, 0.16), 0.08, 0.0, 0),
    "LampGlow": ((1.0, 0.9, 0.62), 0.3, 0.0, 1.5),
    "Paint": ((1, 1, 1), 0.4, 0.0, 0),
    "Red": ((0.72, 0.06, 0.05), 0.45, 0.0, 0),
    "White": ((0.93, 0.93, 0.91), 0.5, 0.0, 0),
    "Yellow": ((0.95, 0.72, 0.04), 0.45, 0.0, 0),
    "Orange": ((0.95, 0.4, 0.04), 0.45, 0.0, 0),
    "Green": ((0.07, 0.3, 0.16), 0.5, 0.0, 0),
    "Blue": ((0.03, 0.22, 0.6), 0.45, 0.0, 0),
    "Rubber": ((0.03, 0.03, 0.032), 0.85, 0.0, 0),
    "Trim": ((0.07, 0.072, 0.078), 0.55, 0.0, 0),
    "Foliage": ((0.13, 0.3, 0.09), 0.9, 0.0, 0),
    "Foliage2": ((0.2, 0.4, 0.12), 0.9, 0.0, 0),
    "Bark": ((0.27, 0.2, 0.14), 0.95, 0.0, 0),
    "Fabric": ((0.86, 0.8, 0.68), 0.9, 0.0, 0),
    "LampRed": ((0.9, 0.05, 0.03), 0.3, 0.0, 0.8),
    "LampAmber": ((1.0, 0.6, 0.02), 0.3, 0.0, 0.8),
    "LampGreen": ((0.05, 0.85, 0.3), 0.3, 0.0, 0.8),
}


# ---------------------------------------------------------------- small maths
def lerp(a, b, t):
    return a + (b - a) * t


def pchip(points):
    """Monotone cubic through (x, y) points sorted by x: smooth, never overshoots."""
    xs = [p[0] for p in points]
    ys = [p[1] for p in points]
    n = len(xs)
    if n == 1:
        return lambda x: ys[0]
    h = [xs[i + 1] - xs[i] for i in range(n - 1)]
    d = [(ys[i + 1] - ys[i]) / h[i] for i in range(n - 1)]
    m = [0.0] * n
    m[0], m[-1] = d[0], d[-1]
    for i in range(1, n - 1):
        if d[i - 1] * d[i] > 0:
            w1, w2 = 2 * h[i] + h[i - 1], h[i] + 2 * h[i - 1]
            m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i])

    def f(x):
        if x <= xs[0]:
            return ys[0]
        if x >= xs[-1]:
            return ys[-1]
        i = 0
        while x > xs[i + 1]:
            i += 1
        t = (x - xs[i]) / h[i]
        t2, t3 = t * t, t * t * t
        return ((2 * t3 - 3 * t2 + 1) * ys[i] + (t3 - 2 * t2 + t) * h[i] * m[i]
                + (-2 * t3 + 3 * t2) * ys[i + 1] + (t3 - t2) * h[i] * m[i + 1])
    return f


def newell(pts):
    n = Vector((0, 0, 0))
    for i, a in enumerate(pts):
        b = pts[(i + 1) % len(pts)]
        n.x += (a.y - b.y) * (a.z + b.z)
        n.y += (a.z - b.z) * (a.x + b.x)
        n.z += (a.x - b.x) * (a.y + b.y)
    return n


def frame(axis):
    """Two unit vectors perpendicular to axis (right-handed with it)."""
    a = Vector(axis).normalized()
    ref = Vector((0, 0, 1)) if abs(a.z) < 0.9 else Vector((1, 0, 0))
    e1 = a.cross(ref).normalized()
    return a, e1, a.cross(e1)


def rot(axis, degrees, about=(0, 0, 0)):
    c = Vector(about)
    return Matrix.Translation(c) @ Matrix.Rotation(math.radians(degrees), 4, axis) @ Matrix.Translation(-c)


def box_uv(p, n):
    ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
    if az >= ax and az >= ay:
        return (p.x, p.y)
    if ax >= ay:
        return (p.y, p.z)
    return (p.x, p.z)


# ---------------------------------------------------------------- mesh kit
class Kit:
    """Accumulates one mesh from closed primitives. Every primitive is welded and cleaned on its
    own, then wound so its normals point out of its volume (signed-volume test), so mirrored and
    rotated parts never come out inside-out."""

    def __init__(self, name="Body"):
        self.name = name
        self.v, self.f, self.m, self.s = [], [], [], []

    @property
    def triangles(self):
        return sum(len(f) - 2 for f in self.f)

    def commit(self, pts, faces, hint=None, M=None):
        """faces: [(indices, material or None, smooth)]. hint: outward direction for open panels."""
        if M is not None:
            pts = [M @ Vector(p) for p in pts]
        key, local, remap = {}, [], []
        for p in pts:
            k = (round(p[0], 5), round(p[1], 5), round(p[2], 5))
            i = key.get(k)
            if i is None:
                i = key[k] = len(local)
                local.append(Vector(p))
            remap.append(i)
        clean = []
        for idx, mat, smooth in faces:
            if mat is None:
                continue
            ids = []
            for i in idx:
                j = remap[i]
                if not ids or ids[-1] != j:
                    ids.append(j)
            while len(ids) > 1 and ids[0] == ids[-1]:
                ids.pop()
            if len(set(ids)) < 3:
                continue
            if len(set(ids)) != len(ids):  # pinched polygon: keep the first simple loop
                seen, out = set(), []
                for j in ids:
                    if j in seen:
                        break
                    seen.add(j)
                    out.append(j)
                ids = out
                if len(ids) < 3:
                    continue
            n = newell([local[i] for i in ids])
            if n.length < 1e-9:
                continue
            clean.append((ids, mat, smooth, n))
        if not clean:
            return
        if hint is not None:
            s = sum(n.dot(Vector(hint)) for _, _, _, n in clean)
        else:
            ref = sum(local, Vector((0, 0, 0))) / len(local)
            s = 0.0
            for ids, _, _, n in clean:
                c = sum((local[i] for i in ids), Vector((0, 0, 0))) / len(ids)
                s += n.dot(c - ref)
        base = len(self.v)
        self.v.extend(local)
        for ids, mat, smooth, _ in clean:
            if s < 0:
                ids = ids[::-1]
            self.f.append([base + i for i in ids])
            self.m.append(mat)
            self.s.append(smooth)

    # -- primitives
    def grid(self, rows, mat, wrap_u=False, wrap_v=False, cap0=None, cap1=None, smooth=True, hint=None, M=None):
        """rows[i][j] points. mat: name or f(i, j) -> name/None. Caps: ('ngon', name), or horizontal
        bands across a mirror-symmetric ring: ('strips', f(k)) when it starts at the bottom centre,
        ('pairs', f(k)) when it runs from one base corner over the top to the other."""
        nv, nu = len(rows), len(rows[0])
        pts = [p for r in rows for p in r]
        mf = mat if callable(mat) else (lambda i, j: mat)
        faces = []
        for i in range(nv if wrap_v else nv - 1):
            i2 = (i + 1) % nv
            for j in range(nu if wrap_u else nu - 1):
                j2 = (j + 1) % nu
                faces.append(([i * nu + j, i * nu + j2, i2 * nu + j2, i2 * nu + j], mf(i, j), smooth))
        for cap, i, reverse in ((cap0, 0, True), (cap1, nv - 1, False)):
            if cap is None:
                continue
            kind, cm = cap
            if kind == "ngon":
                ids = [i * nu + j for j in range(nu)]
                faces.append((ids[::-1] if reverse else ids, cm, False))
            elif kind == "strips":
                for k in range(nu // 2):
                    ids = [i * nu + k, i * nu + k + 1, i * nu + (nu - k - 1), i * nu + (nu - k) % nu]
                    faces.append((ids[::-1] if reverse else ids, cm(k) if callable(cm) else cm, False))
            else:  # "pairs": ring runs from one base corner over the top to the mirrored base corner
                for k in range((nu - 1) // 2):
                    ids = [i * nu + k, i * nu + k + 1, i * nu + (nu - 2 - k), i * nu + (nu - 1 - k)]
                    faces.append((ids[::-1] if reverse else ids, cm(k) if callable(cm) else cm, False))
        self.commit(pts, faces, hint, M)

    def box(self, centre, size, mat, M=None, smooth=False):
        cx, cy, cz = centre
        sx, sy, sz = size[0] / 2, size[1] / 2, size[2] / 2
        v = [(cx + x * sx, cy + y * sy, cz + z * sz) for z in (-1, 1) for y in (-1, 1) for x in (-1, 1)]
        faces = [(f, mat, smooth) for f in ([0, 2, 3, 1], [4, 5, 7, 6], [0, 1, 5, 4], [1, 3, 7, 5], [3, 2, 6, 7], [2, 0, 4, 6])]
        self.commit(v, faces, M=M)

    def wedge(self, pts8, mat, M=None, smooth=False):
        """Hexahedron from 8 points: bottom ring (4, counter-clockwise) then the top ring above it."""
        faces = [(f, mat, smooth) for f in ([3, 2, 1, 0], [4, 5, 6, 7], [0, 1, 5, 4], [1, 2, 6, 5], [2, 3, 7, 6], [3, 0, 4, 7])]
        self.commit(pts8, faces, M=M)

    def rbox(self, centre, size, r, mat, axis=1, segs=1, M=None, smooth=True, ends=True):
        """Box with rounded edges, lofted along `axis` (0 x, 1 y, 2 z). mat: name or f(i, j)."""
        a = axis
        b, c = [(1, 2), (2, 0), (0, 1)][a]
        hb, hc, ha = size[b] / 2, size[c] / 2, size[a] / 2
        r = min(r, hb * 0.98, hc * 0.98, ha * 0.98)
        steps = [(k / segs) * math.pi / 2 for k in range(segs + 1)]

        def ring(off, inset):
            pts = []
            for q, (sb, sc) in enumerate(((1, 1), (-1, 1), (-1, -1), (1, -1))):
                for t in steps:
                    ang = q * math.pi / 2 + t
                    p = [0.0, 0.0, 0.0]
                    p[a] = centre[a] + off
                    p[b] = centre[b] + sb * (hb - r) + (r - inset) * math.cos(ang)
                    p[c] = centre[c] + sc * (hc - r) + (r - inset) * math.sin(ang)
                    pts.append(tuple(p))
            return pts

        if ends:
            lo = [ring(-ha + r * (1 - math.sin(t)), r * (1 - math.cos(t))) for t in reversed(steps)]
            hi = [ring(ha - r * (1 - math.sin(t)), r * (1 - math.cos(t))) for t in steps]
            rows = lo + hi
        else:
            rows = [ring(-ha, 0), ring(ha, 0)]
        m0 = mat(0, 0) if callable(mat) else mat
        self.grid(rows, mat, wrap_u=True, cap0=("ngon", m0), cap1=("ngon", m0), smooth=smooth, M=M)

    def lathe(self, profile, mat, origin=(0, 0, 0), axis=(0, 0, 1), segs=12, M=None, smooth=True, phase=0.0):
        """profile: [(radius, distance along axis)]. mat: name or f(band, segment)."""
        a, e1, e2 = frame(axis)
        o = Vector(origin)
        rows = [[o + a * h + (e1 * math.cos(phase + 2 * math.pi * k / segs) + e2 * math.sin(phase + 2 * math.pi * k / segs)) * r
                 for k in range(segs)] for r, h in profile]
        m0 = mat(0, 0) if callable(mat) else mat
        m1 = mat(len(profile) - 2, 0) if callable(mat) else mat
        self.grid(rows, mat, wrap_u=True, cap0=("ngon", m0) if profile[0][0] > 1e-5 else None,
                  cap1=("ngon", m1) if profile[-1][0] > 1e-5 else None, smooth=smooth, M=M)

    def cyl(self, a, b, r, mat, segs=8, M=None, smooth=True):
        a, b = Vector(a), Vector(b)
        self.lathe([(r, 0), (r, (b - a).length)], mat, origin=a, axis=b - a, segs=segs, M=M, smooth=smooth)

    def ellipsoid(self, centre, radii, mat, segs=10, rings=6, M=None, jitter=None):
        c = Vector(centre)
        rows = []
        for i in range(rings + 1):
            t = -math.pi / 2 + math.pi * i / rings
            row = []
            for k in range(segs):
                ang = 2 * math.pi * k / segs
                d = Vector((math.cos(ang) * math.cos(t), math.sin(ang) * math.cos(t), math.sin(t)))
                s = jitter(i, k) if jitter and 0 < i < rings else 1.0
                row.append(c + Vector((d.x * radii[0], d.y * radii[1], d.z * radii[2])) * s)
            rows.append(row)
        self.grid(rows, mat, wrap_u=True, M=M)

    def tube(self, path, r, mat, segs=6, M=None, smooth=True, closed=False):
        """Round tube along a polyline. r: number or one radius per point."""
        pts = [Vector(p) for p in path]
        n = len(pts)
        radii = r if isinstance(r, (list, tuple)) else [r] * n
        tans = []
        for i in range(n):
            a = pts[i - 1] if (i > 0 or closed) else pts[i]
            b = pts[(i + 1) % n] if (i < n - 1 or closed) else pts[i]
            tans.append((b - a).normalized())
        _, e1, _ = frame(tans[0])
        rows = []
        for i in range(n):
            t = tans[i]
            e1 = (e1 - t * e1.dot(t)).normalized()
            e2 = t.cross(e1)
            rows.append([pts[i] + (e1 * math.cos(2 * math.pi * k / segs) + e2 * math.sin(2 * math.pi * k / segs)) * radii[i] for k in range(segs)])
        self.grid(rows, mat, wrap_u=True, wrap_v=closed, cap0=None if closed else ("ngon", mat),
                  cap1=None if closed else ("ngon", mat), smooth=smooth, M=M)

    def sweep(self, path, section, mat, up=(0, 0, 1), M=None, smooth=True, closed=False):
        """Sweep a closed 2D section [(side, up)] along a polyline, keeping `up` upright."""
        pts = [Vector(p) for p in path]
        n = len(pts)
        upv = Vector(up)
        rows = []
        for i in range(n):
            a = pts[i - 1] if (i > 0 or closed) else pts[i]
            b = pts[(i + 1) % n] if (i < n - 1 or closed) else pts[i]
            t = (b - a).normalized()
            side = t.cross(upv).normalized()
            # keep the section width constant through bends
            if 0 < i < n - 1 or closed:
                t0 = (pts[i] - a).normalized()
                s0 = t0.cross(upv).normalized()
                k = side.dot(s0)
                scale = 1 / max(k, 0.5)
            else:
                scale = 1.0
            rows.append([pts[i] + side * (s * scale) + upv * u for s, u in section])
        mf = mat if callable(mat) else (lambda i, j: mat)
        self.grid(rows, mf, wrap_u=True, wrap_v=closed, cap0=None if closed else ("ngon", mf(0, 0)),
                  cap1=None if closed else ("ngon", mf(n - 2, 0)), smooth=smooth, M=M)

    def prism(self, outline, depth, mat, side_mat=None, M=None):
        """Extrude a planar polygon (3D points) by the vector `depth`."""
        a = [Vector(p) for p in outline]
        d = Vector(depth)
        n = len(a)
        pts = a + [p + d for p in a]
        faces = [(list(range(n))[::-1], mat, False), (list(range(n, 2 * n)), mat, False)]
        for i in range(n):
            j = (i + 1) % n
            faces.append(([i, j, n + j, n + i], side_mat or mat, False))
        self.commit(pts, faces, M=M)

    def panel(self, pts, mat, hint, M=None, smooth=False):
        self.commit(pts, [(list(range(len(pts))), mat, smooth)], hint=hint, M=M)

    def add(self, other, M=None):
        """Copy another kit in, optionally transformed (mirrors are re-wound)."""
        det = M.to_3x3().determinant() if M is not None else 1
        base = len(self.v)
        self.v.extend((M @ v) if M is not None else v.copy() for v in other.v)
        for f, m, s in zip(other.f, other.m, other.s):
            ids = [base + i for i in f]
            self.f.append(ids[::-1] if det < 0 else ids)
            self.m.append(m)
            self.s.append(s)

    def shift(self, d):
        d = Vector(d)
        for v in self.v:
            v += d
        return self

    def bounds(self):
        lo = [min(v[i] for v in self.v) for i in range(3)]
        hi = [max(v[i] for v in self.v) for i in range(3)]
        return lo, hi


# ---------------------------------------------------------------- Blender objects
def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def material(name, palette):
    mat = bpy.data.materials.get(name)
    if mat:
        return mat
    colour, rough, metal, emit = palette[name]
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*colour, 1)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = metal
    if emit:
        bsdf.inputs["Emission Color"].default_value = (*colour, 1)
        bsdf.inputs["Emission Strength"].default_value = emit
    mat.diffuse_color = (*colour, 1)
    return mat


def tint(mat, colour):
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (*colour, 1)
    mat.diffuse_color = (*colour, 1)


def to_object(kit, palette, insets=(), sharp=38.0, name=None, location=(0, 0, 0)):
    """Kit -> mesh object. insets: [(material, rim material, thickness, depth)] recesses every
    region of that material (glass in its frame, lamps in their bezels). A face material may carry
    a tag ("Glass:rear"): it renders as Glass but is matched by insets only under its full name."""
    name = name or kit.name
    base = [m.split(":")[0] for m in kit.m]
    used = set(base) | {i[1] for i in insets if i[0] in set(kit.m)}
    unknown = used - set(palette)
    assert not unknown, f"{name}: materials outside the allowed list: {unknown}"
    names = [n for n in palette if n in used]
    index = {n: i for i, n in enumerate(names)}
    tags = {t: i + 1 for i, t in enumerate(sorted(set(kit.m)))}
    me = bpy.data.meshes.new(name)
    me.from_pydata([tuple(v) for v in kit.v], [], kit.f)
    for n in names:
        me.materials.append(material(n, palette))
    me.polygons.foreach_set("material_index", [index[m] for m in base])
    me.polygons.foreach_set("use_smooth", kit.s)
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    layer = bm.faces.layers.int.new("tag")
    bm.faces.ensure_lookup_table()
    for f, m in zip(bm.faces, kit.m):
        f[layer] = tags[m]
    for sel, rim, thick, depth in insets:
        if sel not in tags:
            continue
        faces = [f for f in bm.faces if f[layer] == tags[sel]]
        res = bmesh.ops.inset_region(bm, faces=faces, thickness=thick, depth=depth, use_even_offset=True, use_boundary=True)
        for f in res["faces"]:
            f.material_index = index[rim]
            f[layer] = 0
            f.smooth = False
    bm.faces.layers.int.remove(layer)
    ngons = [f for f in bm.faces if len(f.verts) > 4]
    if ngons:
        bmesh.ops.triangulate(bm, faces=ngons, quad_method="BEAUTY", ngon_method="BEAUTY")
    loose = [v for v in bm.verts if not v.link_faces]
    if loose:
        bmesh.ops.delete(bm, geom=loose, context="VERTS")
    bm.normal_update()
    limit = math.radians(sharp)
    for e in bm.edges:
        fs = e.link_faces
        if len(fs) != 2 or fs[0].material_index != fs[1].material_index or fs[0].normal.angle(fs[1].normal, 0) > limit:
            e.smooth = False
    bm.to_mesh(me)
    bm.free()
    uv = me.uv_layers.new(name="UVMap")
    flat = []
    for poly in me.polygons:
        for li in poly.loop_indices:
            flat.extend(box_uv(me.vertices[me.loops[li].vertex_index].co, poly.normal))
    uv.data.foreach_set("uv", flat)
    me.validate(clean_customdata=False)
    me.update()
    ob = bpy.data.objects.new(name, me)
    ob.location = location
    bpy.context.scene.collection.objects.link(ob)
    return ob


def triangles(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def assemble(name, children):
    """Root empty named after the asset, with every part parented to it."""
    root = bpy.data.objects.new(name, None)
    root.empty_display_size = 0.3
    bpy.context.scene.collection.objects.link(root)
    for ob in children:
        ob.parent = root
    return root


def export(root, blend_path, fbx_path):
    blend_path.parent.mkdir(parents=True, exist_ok=True)
    fbx_path.parent.mkdir(parents=True, exist_ok=True)
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    for ob in [root] + list(root.children_recursive):
        ob.hide_set(False)
        ob.select_set(True)
    bpy.context.view_layer.objects.active = root
    # Same axes and unit handling as lmcommon.export, but with the axis change baked into the mesh
    # data (bake_space_transform) so vehicles need no +90 degree instance rotation in Unity.
    bpy.ops.export_scene.fbx(filepath=str(fbx_path), use_selection=True, apply_unit_scale=True,
                             apply_scale_options="FBX_SCALE_UNITS", axis_forward="-Z", axis_up="Y",
                             object_types={"MESH", "EMPTY"}, mesh_smooth_type="OFF", use_mesh_modifiers=False,
                             add_leaf_bones=False, bake_space_transform=True, bake_anim=False, path_mode="STRIP")
    if blend_path.exists():
        blend_path.unlink()
    bpy.ops.wm.save_as_mainfile(filepath=str(blend_path), compress=True)


# ---------------------------------------------------------------- preview renders
class Stage:
    """Neutral studio: soft sun, sky-gradient ambient, pale ground that catches the shadow."""

    def __init__(self, size=(900, 600)):
        scene = bpy.context.scene
        self.scene = scene
        ids = {e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items}
        scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in ids else "BLENDER_EEVEE_NEXT"
        scene.render.resolution_x, scene.render.resolution_y = size
        scene.render.image_settings.file_format = "PNG"
        scene.view_settings.view_transform = "AgX"
        for look in ("AgX - Medium High Contrast", "Medium High Contrast"):
            try:
                scene.view_settings.look = look
                break
            except TypeError:
                pass
        world = bpy.data.worlds.new("Studio")
        world.use_nodes = True
        nt = world.node_tree
        bg = nt.nodes["Background"]
        coord = nt.nodes.new("ShaderNodeTexCoord")
        sep = nt.nodes.new("ShaderNodeSeparateXYZ")
        ramp = nt.nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].position = 0.42
        ramp.color_ramp.elements[0].color = (0.72, 0.74, 0.76, 1)
        ramp.color_ramp.elements[1].position = 0.75
        ramp.color_ramp.elements[1].color = (0.86, 0.9, 0.96, 1)
        mapr = nt.nodes.new("ShaderNodeMapRange")
        mapr.inputs["From Min"].default_value = -1
        nt.links.new(coord.outputs["Generated"], sep.inputs[0])
        nt.links.new(sep.outputs["Z"], mapr.inputs["Value"])
        nt.links.new(mapr.outputs["Result"], ramp.inputs["Fac"])
        nt.links.new(ramp.outputs["Color"], bg.inputs["Color"])
        bg.inputs["Strength"].default_value = 0.85
        scene.world = world
        sun = bpy.data.objects.new("PreviewSun", bpy.data.lights.new("PreviewSun", "SUN"))
        sun.data.energy = 3.4
        sun.data.angle = math.radians(8)
        sun.rotation_euler = (math.radians(52), 0, math.radians(-32))
        scene.collection.objects.link(sun)
        ground = Kit("PreviewGround")
        ground.panel([(-400, -400, 0), (400, -400, 0), (400, 400, 0), (-400, 400, 0)], "Ground", (0, 0, 1))
        gm = bpy.data.materials.new("Ground")
        gm.use_nodes = True
        gb = gm.node_tree.nodes.get("Principled BSDF")
        gb.inputs["Base Color"].default_value = (0.62, 0.63, 0.64, 1)
        gb.inputs["Roughness"].default_value = 1.0
        me = bpy.data.meshes.new("PreviewGround")
        me.from_pydata([tuple(v) for v in ground.v], [], ground.f)
        me.materials.append(gm)
        self.ground = bpy.data.objects.new("PreviewGround", me)
        scene.collection.objects.link(self.ground)
        cam = bpy.data.objects.new("PreviewCamera", bpy.data.cameras.new("PreviewCamera"))
        cam.data.lens = 85
        cam.data.clip_end = 2000
        scene.collection.objects.link(cam)
        scene.camera = cam
        self.cam, self.sun = cam, sun
        self.extras = [sun, self.ground, cam]

    def shot(self, objects, path, azimuth, elevation, margin=1.08):
        """azimuth: degrees from the front (+Y) towards the right (+X)."""
        corners = []
        for ob in objects:
            if ob.type != "MESH":
                continue
            corners += [ob.matrix_world @ Vector(c) for c in ob.bound_box]
        lo = Vector([min(c[i] for c in corners) for i in range(3)])
        hi = Vector([max(c[i] for c in corners) for i in range(3)])
        centre = (lo + hi) / 2
        az, el = math.radians(azimuth), math.radians(elevation)
        d = Vector((math.sin(az) * math.cos(el), math.cos(az) * math.cos(el), math.sin(el)))
        self.cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        self.cam.location = centre + d * 10
        bpy.context.view_layer.update()
        deps = bpy.context.evaluated_depsgraph_get()
        co, _ = self.cam.camera_fit_coords(deps, [x for c in corners for x in c])
        self.cam.location = centre + (Vector(co) - centre) * margin
        self.scene.render.filepath = str(path)
        bpy.ops.render.render(write_still=True)

    def remove(self):
        for ob in self.extras:
            bpy.data.objects.remove(ob, do_unlink=True)


def _pixels(path):
    img = bpy.data.images.load(str(path))
    w, h = img.size
    a = np.empty(w * h * 4, np.float32)
    img.pixels.foreach_get(a)
    bpy.data.images.remove(img)
    return a.reshape(h, w, 4)


def sheet(paths, out, columns=None):
    """Tile rendered PNGs into one contact sheet (row-major, top-left first)."""
    tiles = [_pixels(p) for p in paths]
    columns = columns or len(tiles)
    h, w, _ = tiles[0].shape
    rows = math.ceil(len(tiles) / columns)
    canvas = np.ones((rows * h, columns * w, 4), np.float32)
    for i, t in enumerate(tiles):
        r, c = divmod(i, columns)
        y0 = (rows - 1 - r) * h
        canvas[y0:y0 + h, c * w:(c + 1) * w] = t
    img = bpy.data.images.new("sheet", columns * w, rows * h, alpha=False)
    img.pixels.foreach_set(canvas.ravel())
    img.filepath_raw = str(out)
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)
    for p in paths:
        Path(p).unlink()
