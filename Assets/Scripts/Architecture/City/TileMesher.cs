using System.Collections.Generic;
using Padova.Architecture;
using UnityEngine;
using UnityEngine.Rendering;

namespace Padova.City
{
    /// <summary>Vertex stream for the Padova/City material: position, normal, metric UV, layer+tint colour, façade grid.</summary>
    public sealed class CitySink
    {
        public readonly List<Vector3> P = new List<Vector3>(4096), N = new List<Vector3>(4096);
        public readonly List<Vector2> Uv = new List<Vector2>(4096);
        public readonly List<Vector4> F = new List<Vector4>(4096);
        public readonly List<Color32> C = new List<Color32>(4096);
        public readonly List<int> I = new List<int>(8192);
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        public int Count => P.Count;
        public Bounds Bounds => P.Count == 0 ? new Bounds() : new Bounds((min + max) * .5f, max - min);

        public int V(Vector3 p, Vector3 n, Vector2 metres, in Surface s, Vector4 facade = default)
        {
            P.Add(p); N.Add(n); Uv.Add(metres / s.Metres); C.Add(s.Tint); F.Add(facade);
            min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            return P.Count - 1;
        }

        public void Tri(int a, int b, int c, Vector3 facing)
        {
            if (Vector3.Dot(Vector3.Cross(P[b] - P[a], P[c] - P[a]), facing) >= 0) { I.Add(a); I.Add(b); I.Add(c); }
            else { I.Add(a); I.Add(c); I.Add(b); }
        }

        public void Quad(in Surface s, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n, Vector2 t0, Vector2 t1, Vector2 t2, Vector2 t3,
                         Vector4 f0 = default, Vector4 f1 = default, Vector4 f2 = default, Vector4 f3 = default)
        {
            int a = V(p0, n, t0, s, f0), b = V(p1, n, t1, s, f1), c = V(p2, n, t2, s, f2), d = V(p3, n, t3, s, f3);
            Tri(a, b, c, n); Tri(a, c, d, n);
        }

        /// <summary>Vertical rectangle on a wall frame, with optional façade grid running u0..u1 by v0..v1.</summary>
        public void Wall(in Surface s, in Frame f, float u0, float y0, float u1, float y1, float d, Vector4 g0 = default, Vector4 g1 = default)
        {
            if (u1 - u0 < 1e-3f || y1 - y0 < 1e-3f) return;
            Quad(s, f.P(u0, y0, d), f.P(u1, y0, d), f.P(u1, y1, d), f.P(u0, y1, d), f.N,
                new Vector2(f.U + u0, y0), new Vector2(f.U + u1, y0), new Vector2(f.U + u1, y1), new Vector2(f.U + u0, y1),
                new Vector4(g0.x, g0.y, g0.z, g0.w), new Vector4(g1.x, g0.y, g0.z, g0.w), new Vector4(g1.x, g1.y, g0.z, g0.w), new Vector4(g0.x, g1.y, g0.z, g0.w));
        }

        public void Box(in Surface s, Vector3 centre, Vector3 ax, Vector3 ay, Vector3 az, bool top = true)
        {
            Face(s, centre + az, ax, ay, az.normalized); Face(s, centre - az, -ax, ay, -az.normalized);
            Face(s, centre + ax, -az, ay, ax.normalized); Face(s, centre - ax, az, ay, -ax.normalized);
            if (top) Face(s, centre + ay, ax, -az, ay.normalized);
        }

        void Face(in Surface s, Vector3 c, Vector3 u, Vector3 v, Vector3 n)
        {
            float lu = u.magnitude * 2, lv = v.magnitude * 2;
            Quad(s, c - u - v, c + u - v, c + u + v, c - u + v, n, Vector2.zero, new Vector2(lu, 0), new Vector2(lu, lv), new Vector2(0, lv));
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = P.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(P); mesh.SetNormals(N); mesh.SetUVs(0, Uv); mesh.SetUVs(1, F); mesh.SetColors(C);
            mesh.SetTriangles(I, 0, false);
            mesh.bounds = Bounds;
            return mesh;
        }
    }

    /// <summary>Everything one tile needs, built off the main thread.</summary>
    public sealed class TileBuild
    {
        public TileData Data;
        public CitySink Ground = new CitySink(), Always = new CitySink(), Shell = new CitySink();
        public List<Vector3> ColliderP = new List<Vector3>(2048);
        public List<int> ColliderI = new List<int>(4096);
        public MeshSink Detail;
        public Vector4[] DetailTangents;
    }

    /// <summary>Turns compiled survey tiles into meshes. Pure functions over tile data: safe on worker threads.</summary>
    public static class TileMesher
    {
        const float MaxEdge = 48;

        public static TileBuild Shell(TileData data, CityTerrain terrain, float[] levels)
        {
            var build = new TileBuild { Data = data };
            Ground(build, terrain, levels);
            foreach (var roof in data.Roofs) Roof(build.Always, roof);
            foreach (var unit in data.Units)
            {
                var style = CityStyle.For(unit.Seed, unit.Class);
                RoofOfUnit(build.Always, unit, style);
                ShellWalls(build.Shell, unit, style);
                UnitCollider(build, unit, style);
            }
            foreach (var tree in data.Trees) Tree(build, tree, terrain);
            foreach (var wall in data.Walls) GardenWall(build, wall, terrain);
            return build;
        }

        public static void Detail(TileBuild build)
        {
            var sink = new MeshSink();
            var facade = new FacadeBuilder(sink);
            foreach (var u in build.Data.Units)
            {
                var plan = new PlanUnit
                {
                    id = u.SourceId, name = "", use = "", suspended = u.Suspended, ground = u.Ground, top = u.Top, surveyTop = u.SurveyTop,
                    under = u.Under, area = u.Area, cx = u.Cx, cz = u.Cz, outline = u.Outline, ringSizes = u.RingSizes, segs = u.Segs,
                    ceiling = u.Suspended && u.Cap != null ? u.Outline : null, ceilingTris = u.Suspended && u.Cap != null ? System.Array.ConvertAll(u.Cap, i => (int)i) : null
                };
                facade.Unit(plan, CityStyle.For(u.Seed, u.Class));
            }
            build.Detail = sink;
            build.DetailTangents = sink.ComputeTangents();
        }

        // ------------------------------------------------------------------ ground
        static void Ground(TileBuild build, CityTerrain terrain, float[] levels)
        {
            var sink = build.Ground;
            foreach (var patch in build.Data.Patches)
            {
                int kind = (int)patch.Kind;
                var surface = CityStyle.Ground[Mathf.Min(kind, CityStyle.Ground.Length - 1)];
                float level = kind < levels.Length ? levels[kind] : 0;
                if (patch.Kind == GroundKind.Base) level = -.04f;
                if (patch.Kind == GroundKind.Field)
                {
                    // Crops differ field by field; the pattern is typology.
                    int pick = (int)(Mathf.Abs(patch.V[0] * 7.13f + patch.V[1] * 3.71f) % 3);
                    surface = pick == 0 ? surface : pick == 1 ? new Surface(Layer.Earth, .56f, .5f, .38f, 6f) : new Surface(Layer.Grass, .55f, .66f, .32f, 5f);
                }
                int n = patch.V.Length / 2;
                var map = new int[n];
                for (int i = 0; i < n; i++) map[i] = -1;
                Vector3 At(int i) => new Vector3(patch.V[2 * i], terrain.At(patch.V[2 * i], patch.V[2 * i + 1]) + level, patch.V[2 * i + 1]);
                int Index(int i)
                {
                    if (map[i] < 0) { var p = At(i); map[i] = sink.V(p, Vector3.up, new Vector2(p.x, p.z), surface); }
                    return map[i];
                }
                for (int t = 0; t + 2 < patch.T.Length; t += 3)
                {
                    int a = patch.T[t], b = patch.T[t + 1], c = patch.T[t + 2];
                    Vector3 pa = At(a), pb = At(b), pc = At(c);
                    float longest = Mathf.Max((pa - pb).sqrMagnitude, (pb - pc).sqrMagnitude, (pc - pa).sqrMagnitude);
                    if (longest <= MaxEdge * MaxEdge) sink.Tri(Index(a), Index(b), Index(c), Vector3.up);
                    else Split(sink, surface, terrain, level, pa, pb, pc, 0);
                }
            }
            var curbs = build.Data.Curbs;
            for (int k = 0; k + 5 < curbs.Length; k += 6)
            {
                var f = new Frame(new Vector2(curbs[k], curbs[k + 1]), new Vector2(curbs[k + 2], curbs[k + 3]));
                float g0 = terrain.At(curbs[k], curbs[k + 1]), g1 = terrain.At(curbs[k + 2], curbs[k + 3]), lo = curbs[k + 4], hi = curbs[k + 5];
                var surface = hi - lo > .6f ? CityStyle.Bank : CityStyle.Curb;
                sink.Quad(surface, new Vector3(curbs[k], g0 + lo, curbs[k + 1]), new Vector3(curbs[k + 2], g1 + lo, curbs[k + 3]),
                    new Vector3(curbs[k + 2], g1 + hi, curbs[k + 3]), new Vector3(curbs[k], g0 + hi, curbs[k + 1]), f.N,
                    Vector2.zero, new Vector2(f.L, 0), new Vector2(f.L, hi - lo), new Vector2(0, hi - lo));
            }
        }

        /// <summary>Long triangles are split so flat survey polygons follow the interpolated terrain.</summary>
        static void Split(CitySink sink, in Surface surface, CityTerrain terrain, float level, Vector3 a, Vector3 b, Vector3 c, int depth)
        {
            float ab = (a - b).sqrMagnitude, bc = (b - c).sqrMagnitude, ca = (c - a).sqrMagnitude;
            if (Mathf.Max(ab, bc, ca) > MaxEdge * MaxEdge && depth < 8)
            {
                if (bc > ab && bc >= ca) { Split(sink, surface, terrain, level, b, c, a, depth); return; }
                if (ca > ab && ca > bc) { Split(sink, surface, terrain, level, c, a, b, depth); return; }
                var m = (a + b) * .5f;
                m.y = terrain.At(m.x, m.z) + level;
                Split(sink, surface, terrain, level, a, m, c, depth + 1);
                Split(sink, surface, terrain, level, m, b, c, depth + 1);
                return;
            }
            int i = sink.V(a, Vector3.up, new Vector2(a.x, a.z), surface), j = sink.V(b, Vector3.up, new Vector2(b.x, b.z), surface), k = sink.V(c, Vector3.up, new Vector2(c.x, c.z), surface);
            sink.Tri(i, j, k, Vector3.up);
        }

        // ------------------------------------------------------------------ roofs
        static float Overhang(CityClass c)
        {
            switch (c)
            {
                case CityClass.Condo: case CityClass.Office: return .35f;
                case CityClass.Shed: case CityClass.Commercial: case CityClass.Sport: case CityClass.Station: return .15f;
                case CityClass.Minor: return .2f;
                case CityClass.Tower: return .3f;
                default: return .6f;
            }
        }

        static Surface RoofSurface(CityClass c, uint seed)
        {
            switch (c)
            {
                case CityClass.Shed: case CityClass.Commercial: case CityClass.Sport: case CityClass.Station:
                    return seed % 5 == 0 ? new Surface(Layer.Metal, .62f, .3f, .22f, 1.2f) : CityStyle.RoofMetal;
                case CityClass.Minor:
                    return seed % 3 == 0 ? CityStyle.RoofMetal : CityStyle.RoofTiles;
                default: return CityStyle.RoofTiles;
            }
        }

        static void Roof(CitySink sink, TileRoof roof)
        {
            uint seed = roof.Building != uint.MaxValue ? roof.Building * 2654435761u + 17 : (uint)(roof.Top * 977);
            var surface = RoofSurface(roof.Class, seed);
            float slope = roof.Slope, cos = 1 / Mathf.Sqrt(1 + slope * slope), overhang = Overhang(roof.Class);
            Surface? gable = null;
            var poly = new List<Vector2>(16);
            var tris = new List<int>(48);
            var nodes = roof.Nodes;
            Vector3 P(int id) => new Vector3(nodes[3 * id], roof.Top + nodes[3 * id + 2] * slope, nodes[3 * id + 1]);
            for (int f = 0; f < roof.Faces.Length; f++)
            {
                var ids = roof.Faces[f];
                if (ids.Length < 3) continue;
                Vector3 a = P(ids[0]), b = P(ids[1]);
                var edge = new Vector2(b.x - a.x, b.z - a.z);
                float length = edge.magnitude;
                if (length < 1e-4f) continue;
                var e = edge / length;
                var inward = new Vector2(-e.y, e.x);
                var outward3 = new Vector3(e.y, 0, -e.x);
                if ((roof.FaceFlags[f] & 2) != 0)
                {
                    // Gable end: a vertical wall in the building's plaster.
                    gable ??= CityStyle.Wall(CityStyle.For(seed, roof.Class).Wall);
                    var s = gable.Value;
                    var c = P(ids[2]);
                    int i0 = sink.V(a, outward3, new Vector2(0, a.y), s), i1 = sink.V(b, outward3, new Vector2(length, b.y), s), i2 = sink.V(c, outward3, new Vector2(length * .5f, c.y), s);
                    sink.Tri(i0, i1, i2, outward3);
                    continue;
                }
                poly.Clear();
                foreach (int id in ids) poly.Add(new Vector2(nodes[3 * id], nodes[3 * id + 1]));
                EarClip(poly, tris);
                if (tris.Count < 3) continue;
                var c0 = P(ids[tris[0]]); var c1 = P(ids[tris[1]]); var c2 = P(ids[tris[2]]);
                var normal = Vector3.Cross(c1 - c0, c2 - c0);
                if (normal.y < 0) normal = -normal;
                normal = normal.sqrMagnitude > 1e-10f ? normal.normalized : Vector3.up;
                int start = sink.Count;
                foreach (int id in ids)
                {
                    var p = P(id);
                    var rel = new Vector2(p.x - a.x, p.z - a.z);
                    sink.V(p, normal, new Vector2(Vector2.Dot(rel, e), Vector2.Dot(rel, inward) / cos), surface);
                }
                for (int t = 0; t + 2 < tris.Count; t += 3) sink.Tri(start + tris[t], start + tris[t + 1], start + tris[t + 2], Vector3.up);
                if ((roof.FaceFlags[f] & 1) == 0 || overhang <= 0 || slope < .02f) continue;
                // Eave: tiles run past the wall, with a fascia board and soffit below.
                float drop = overhang * slope, fy = roof.Top - drop;
                Vector3 qa = a + outward3 * overhang, qb = b + outward3 * overhang;
                qa.y = fy; qb.y = fy;
                sink.Quad(surface, a, b, qb, qa, normal, new Vector2(0, 0), new Vector2(length, 0), new Vector2(length, -overhang / cos), new Vector2(0, -overhang / cos));
                var e3 = new Vector3(e.x, 0, e.y);
                foreach (int sign in new[] { -1, 1 })
                {
                    var corner = sign < 0 ? a : b;
                    var qe = corner + outward3 * overhang; qe.y = fy;
                    var qc = qe + e3 * (overhang * sign);
                    int i0 = sink.V(corner, normal, Vector2.zero, surface), i1 = sink.V(qe, normal, new Vector2(0, -overhang), surface), i2 = sink.V(qc, normal, new Vector2(overhang * sign, -overhang), surface);
                    sink.Tri(i0, i1, i2, Vector3.up);
                }
                var wood = CityStyle.Wood;
                sink.Quad(wood, qa, qb, qb + Vector3.down * .2f, qa + Vector3.down * .2f, outward3, Vector2.zero, new Vector2(length, 0), new Vector2(length, .2f), new Vector2(0, .2f));
                Vector3 sa = a, sb = b; sa.y = roof.Top - .25f; sb.y = roof.Top - .25f;
                sink.Quad(wood, sa, sb, qb + Vector3.down * .2f, qa + Vector3.down * .2f, Vector3.down, Vector2.zero, new Vector2(length, 0), new Vector2(length, overhang), new Vector2(0, overhang));
            }
        }

        static void RoofOfUnit(CitySink sink, TileUnit u, UnitStyle style)
        {
            var o = u.Outline;
            int outer = u.RingSizes[0];
            switch (u.Roof)
            {
                case UnitRoof.Flat:
                {
                    if (u.Cap == null) return;
                    bool parapet = !u.Minor && u.Top - u.Ground > 4;
                    float y = parapet ? u.Top - .45f : u.Top;
                    var surface = u.Minor && u.Seed % 3 == 0 ? CityStyle.RoofMetal : CityStyle.RoofGravel;
                    int start = sink.Count;
                    for (int i = 0; i < o.Length / 2; i++) sink.V(new Vector3(o[2 * i], y, o[2 * i + 1]), Vector3.up, new Vector2(o[2 * i], o[2 * i + 1]), surface);
                    for (int t = 0; t + 2 < u.Cap.Length; t += 3) sink.Tri(start + u.Cap[t], start + u.Cap[t + 1], start + u.Cap[t + 2], Vector3.up);
                    if (!parapet) return;
                    var wall = CityStyle.Wall(style.Wall);
                    int ring = 0;
                    foreach (int n in u.RingSizes)
                    {
                        for (int k = 0; k < n; k++)
                        {
                            int a = ring + k, b = ring + (k + 1) % n;
                            var f = new Frame(new Vector2(o[2 * a], o[2 * a + 1]), new Vector2(o[2 * b], o[2 * b + 1]));
                            if (f.L < .05f) continue;
                            sink.Quad(wall, f.P(0, y, 0), f.P(f.L, y, 0), f.P(f.L, u.Top, 0), f.P(0, u.Top, 0), -f.N,
                                Vector2.zero, new Vector2(f.L, 0), new Vector2(f.L, .45f), new Vector2(0, .45f));
                        }
                        ring += n;
                    }
                    return;
                }
                case UnitRoof.Dome:
                {
                    // The survey maps drums as separate round volumes; the dome profile above the eave is typology.
                    float radius = Mathf.Sqrt(u.Area / Mathf.PI), height = radius * .92f;
                    var centre = new Vector3(u.Cx, u.Top, u.Cz);
                    var lead = CityStyle.RoofLead;
                    const int rings = 7;
                    int first = sink.Count;
                    for (int r = 0; r <= rings; r++)
                    {
                        float s = r / (float)rings * Mathf.PI * .5f, scale = Mathf.Cos(s), y = u.Top + Mathf.Sin(s) * height;
                        for (int k = 0; k < outer; k++)
                        {
                            var p = new Vector3(u.Cx + (o[2 * k] - u.Cx) * scale, y, u.Cz + (o[2 * k + 1] - u.Cz) * scale);
                            var n = p - centre; n.y *= radius / Mathf.Max(height, .01f);
                            sink.V(p, n.sqrMagnitude > 1e-6f ? n.normalized : Vector3.up, new Vector2(k * 1.2f, r * 1.2f), lead);
                        }
                    }
                    for (int r = 0; r < rings; r++)
                        for (int k = 0; k < outer; k++)
                        {
                            int a = first + r * outer + k, b = first + r * outer + (k + 1) % outer, c = b + outer, d = a + outer;
                            var facing = sink.P[a] - centre + Vector3.up;
                            sink.Tri(a, b, c, facing); sink.Tri(a, c, d, facing);
                        }
                    // Lantern.
                    float lr = Mathf.Max(.5f, radius * .13f), lh = Mathf.Max(1.2f, radius * .32f);
                    var top = new Vector3(u.Cx, u.Top + height - .15f, u.Cz);
                    Prism(sink, lead, top, lr, lh, 8);
                    Cone(sink, lead, top + Vector3.up * lh, lr * 1.25f, lh * .9f, 8);
                    return;
                }
                case UnitRoof.Spire:
                {
                    float width = Mathf.Sqrt(u.Area), height = width * (u.Seed % 3 == 0 ? .55f : 1.15f);
                    var surface = u.Seed % 4 == 0 ? CityStyle.RoofLead : CityStyle.RoofTiles;
                    var apex = new Vector3(u.Cx, u.Top + height, u.Cz);
                    for (int k = 0; k < outer; k++)
                    {
                        int j = (k + 1) % outer;
                        Vector3 a = new Vector3(o[2 * k], u.Top, o[2 * k + 1]), b = new Vector3(o[2 * j], u.Top, o[2 * j + 1]);
                        var n = Vector3.Cross(b - a, apex - a);
                        if (n.y < 0) n = -n;
                        n = n.sqrMagnitude > 1e-8f ? n.normalized : Vector3.up;
                        float len = (b - a).magnitude;
                        int i0 = sink.V(a, n, new Vector2(0, 0), surface), i1 = sink.V(b, n, new Vector2(len, 0), surface), i2 = sink.V(apex, n, new Vector2(len * .5f, height), surface);
                        sink.Tri(i0, i1, i2, n);
                    }
                    return;
                }
                default:
                    return;
            }
        }

        static void Prism(CitySink sink, in Surface s, Vector3 bottom, float radius, float height, int sides)
        {
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Mathf.PI * 2 / sides, a1 = (k + 1) * Mathf.PI * 2 / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                var n = (d0 + d1).normalized;
                sink.Quad(s, bottom + d0 * radius, bottom + d1 * radius, bottom + d1 * radius + Vector3.up * height, bottom + d0 * radius + Vector3.up * height, n,
                    new Vector2(a0 * radius, 0), new Vector2(a1 * radius, 0), new Vector2(a1 * radius, height), new Vector2(a0 * radius, height));
            }
        }

        static void Cone(CitySink sink, in Surface s, Vector3 bottom, float radius, float height, int sides)
        {
            var apex = bottom + Vector3.up * height;
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Mathf.PI * 2 / sides, a1 = (k + 1) * Mathf.PI * 2 / sides;
                Vector3 p0 = bottom + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius, p1 = bottom + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                var n = Vector3.Cross(p1 - p0, apex - p0);
                if (n.y < 0) n = -n;
                n.Normalize();
                int i0 = sink.V(p0, n, Vector2.zero, s), i1 = sink.V(p1, n, Vector2.right, s), i2 = sink.V(apex, n, Vector2.up, s);
                sink.Tri(i0, i1, i2, n);
            }
        }

        // ------------------------------------------------------------------ distant walls
        static int Bays(float length, float bay, float width) => length < width + .9f ? 0 : Mathf.Max(1, Mathf.FloorToInt((length + .35f) / bay));

        static void ShellWalls(CitySink sink, TileUnit u, UnitStyle st)
        {
            var wall = CityStyle.Wall(st.Wall);
            float seed = (u.Seed % 9973) / 9973f;
            var segs = u.Segs;
            float offset = 0;
            bool plain = st.Blind || st.Church || st.Tower;
            for (int i = 0; i + 7 < segs.Length; i += 8)
            {
                var f = new Frame(new Vector2(segs[i], segs[i + 1]), new Vector2(segs[i + 2], segs[i + 3]), offset);
                offset += f.L;
                if (f.L < .05f) continue;
                float y0 = segs[i + 4], y1 = segs[i + 5];
                var kind = (SegmentKind)(int)segs[i + 6];
                var flags = (SegmentFlags)(int)segs[i + 7];
                switch (kind)
                {
                    case SegmentKind.Blank:
                        sink.Wall(wall, f, 0, y0, f.L, y1, 0);
                        break;
                    case SegmentKind.Street:
                    case SegmentKind.Upper:
                    {
                        bool ground = kind == SegmentKind.Street, over = (flags & SegmentFlags.OverPortico) != 0, cornice = (flags & SegmentFlags.TopExposed) != 0;
                        float gridBase = !ground && over ? y0 : u.Ground;
                        int bays = plain ? 0 : Bays(f.L, st.Bay, st.WinW);
                        if (bays == 0) { sink.Wall(wall, f, 0, y0, f.L, y1, 0); break; }
                        float share = Mathf.Clamp(st.WinW / (f.L / bays), .05f, .8f);
                        float upperBase = gridBase;
                        if (ground)
                        {
                            float gf = Mathf.Min(st.GfH, y1 - gridBase - .6f);
                            if (y1 - gridBase < 6.2f) gf = Mathf.Max(2.6f, y1 - gridBase - .6f);
                            upperBase = gridBase + gf;
                            if (y0 < gridBase) sink.Wall(wall, f, 0, y0, f.L, gridBase, 0);
                            if (gf > 2.4f) sink.Wall(wall, f, 0, Mathf.Max(y0, gridBase), f.L, upperBase, 0, new Vector4(0, 0, share, seed), new Vector4(bays, 1, share, seed));
                            else sink.Wall(wall, f, 0, Mathf.Max(y0, gridBase), f.L, upperBase, 0);
                        }
                        float avail = y1 - upperBase - (cornice ? .45f : .15f);
                        int floors = avail < 2.4f ? 0 : Mathf.Max(1, Mathf.RoundToInt(avail / st.FloorH));
                        float from = Mathf.Max(y0, upperBase);
                        if (floors == 0) { sink.Wall(wall, f, 0, from, f.L, y1, 0); break; }
                        float fh = avail / floors, top = upperBase + avail;
                        sink.Wall(wall, f, 0, from, f.L, top, 0, new Vector4(0, 1 + (from - upperBase) / fh, share, seed), new Vector4(bays, 1 + floors, share, seed));
                        sink.Wall(wall, f, 0, top, f.L, y1, 0);
                        break;
                    }
                    case SegmentKind.PorticoBack:
                    {
                        float g = u.Ground, room = y1 - g;
                        int bays = Bays(f.L, Mathf.Max(3, st.Bay), 1.3f);
                        if (bays == 0 || room < 2.5f) { sink.Wall(wall, f, 0, y0, f.L, y1, 0); break; }
                        float open = Mathf.Min(room - .45f, 3.3f) / .72f;
                        if (y0 < g) sink.Wall(wall, f, 0, y0, f.L, g, 0);
                        sink.Wall(wall, f, 0, g, f.L, y1, 0, new Vector4(0, 0, .36f, seed), new Vector4(bays, Mathf.Min(.999f, room / open), .36f, seed));
                        break;
                    }
                    case SegmentKind.Arcade:
                    {
                        // Piers on the portico's open edge; spacing is typological, the opening and its clearance are surveyed.
                        float g = y0, under = y1, depth = .55f, col = .6f;
                        int n = Mathf.Max(1, Mathf.RoundToInt(f.L / st.ColumnSpacing));
                        float bay = f.L / n;
                        if (bay - col < .6f) { sink.Wall(wall, f, 0, g, f.L, under, 0); break; }
                        float band = Mathf.Min(.8f, (under - g) * .22f);
                        sink.Wall(wall, f, 0, under - band, f.L, under, 0);
                        var back = new Frame(new Vector2(segs[i + 2], segs[i + 3]) - new Vector2(f.N.x, f.N.z) * depth, new Vector2(segs[i], segs[i + 1]) - new Vector2(f.N.x, f.N.z) * depth);
                        sink.Wall(wall, back, 0, under - band, f.L, under, 0);
                        for (int k = 0; k <= n; k++)
                        {
                            float uc = Mathf.Clamp(k * bay, col / 2, f.L - col / 2);
                            sink.Box(wall, f.P(uc, (g + under - band) / 2, -depth / 2), f.E * (col / 2), Vector3.up * ((under - band - g) / 2), f.N * (depth / 2), false);
                        }
                        break;
                    }
                }
            }
            if (u.Suspended && u.Cap != null)
            {
                var o = u.Outline;
                int start = sink.Count;
                for (int k = 0; k < o.Length / 2; k++) sink.V(new Vector3(o[2 * k], u.Under, o[2 * k + 1]), Vector3.down, new Vector2(o[2 * k], o[2 * k + 1]), CityStyle.Ceiling);
                for (int t = 0; t + 2 < u.Cap.Length; t += 3) sink.Tri(start + u.Cap[t], start + u.Cap[t + 1], start + u.Cap[t + 2], Vector3.down);
            }
        }

        // ------------------------------------------------------------------ collision
        static void ColliderQuad(TileBuild b, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, bool twoSided = false)
        {
            int s = b.ColliderP.Count;
            b.ColliderP.Add(p0); b.ColliderP.Add(p1); b.ColliderP.Add(p2); b.ColliderP.Add(p3);
            b.ColliderI.Add(s); b.ColliderI.Add(s + 2); b.ColliderI.Add(s + 1); b.ColliderI.Add(s); b.ColliderI.Add(s + 3); b.ColliderI.Add(s + 2);
            if (!twoSided) return;
            b.ColliderI.Add(s); b.ColliderI.Add(s + 1); b.ColliderI.Add(s + 2); b.ColliderI.Add(s); b.ColliderI.Add(s + 2); b.ColliderI.Add(s + 3);
        }

        static void UnitCollider(TileBuild build, TileUnit u, UnitStyle st)
        {
            float lo = u.Suspended ? u.Under - .05f : Mathf.Min(u.Ground, u.Base) - 1.5f;
            float hi = u.Suspended ? Mathf.Min(u.Top, u.Under + 3) : Mathf.Min(u.Top, u.Ground + 14);
            var o = u.Outline;
            int ring = 0;
            foreach (int n in u.RingSizes)
            {
                for (int k = 0; k < n; k++)
                {
                    int a = ring + k, b = ring + (k + 1) % n;
                    var f = new Frame(new Vector2(o[2 * a], o[2 * a + 1]), new Vector2(o[2 * b], o[2 * b + 1]));
                    if (f.L < .01f) continue;
                    ColliderQuad(build, f.P(0, lo, 0), f.P(f.L, lo, 0), f.P(f.L, hi, 0), f.P(0, hi, 0));
                }
                ring += n;
            }
            if (!u.Suspended) return;
            if (u.Cap != null)
            {
                int start = build.ColliderP.Count;
                for (int k = 0; k < o.Length / 2; k++) build.ColliderP.Add(new Vector3(o[2 * k], u.Under, o[2 * k + 1]));
                for (int t = 0; t + 2 < u.Cap.Length; t += 3) { build.ColliderI.Add(start + u.Cap[t]); build.ColliderI.Add(start + u.Cap[t + 1]); build.ColliderI.Add(start + u.Cap[t + 2]); }
            }
            var segs = u.Segs;
            for (int i = 0; i + 7 < segs.Length; i += 8)
            {
                if ((SegmentKind)(int)segs[i + 6] != SegmentKind.Arcade) continue;
                var f = new Frame(new Vector2(segs[i], segs[i + 1]), new Vector2(segs[i + 2], segs[i + 3]));
                int n = Mathf.Max(1, Mathf.RoundToInt(f.L / st.ColumnSpacing));
                float bay = f.L / n, g = segs[i + 4] - .3f, top = segs[i + 5];
                if (bay - .6f < .6f) { ColliderQuad(build, f.P(0, g, 0), f.P(f.L, g, 0), f.P(f.L, top, 0), f.P(0, top, 0), true); continue; }
                for (int k = 0; k <= n; k++)
                {
                    float uc = Mathf.Clamp(k * bay, .3f, f.L - .3f);
                    Vector3 c = f.P(uc, 0, -.275f), x = f.E * .32f, z = f.N * .3f;
                    Vector3 A(Vector3 d, float y) => new Vector3(c.x + d.x, y, c.z + d.z);
                    ColliderQuad(build, A(-x + z, g), A(x + z, g), A(x + z, top), A(-x + z, top));
                    ColliderQuad(build, A(x - z, g), A(-x - z, g), A(-x - z, top), A(x - z, top));
                    ColliderQuad(build, A(x + z, g), A(x - z, g), A(x - z, top), A(x + z, top));
                    ColliderQuad(build, A(-x - z, g), A(-x + z, g), A(-x + z, top), A(-x - z, top));
                }
            }
        }

        // ------------------------------------------------------------------ props
        static void Tree(TileBuild build, TileTree tree, CityTerrain terrain)
        {
            var sink = build.Always;
            float h = Mathf.Max(3, tree.Height), ground = terrain.At(tree.X, tree.Z);
            var p = new Vector3(tree.X, ground, tree.Z);
            float hash = Mathf.Abs(Mathf.Sin(tree.X * 12.9898f + tree.Z * 78.233f) * 43758.5453f) % 1f;
            var leaf = new Surface(Layer.Foliage, .2f + hash * .1f, .36f + hash * .12f, .14f + hash * .05f, 2f);
            float trunk = tree.Kind == 1 ? h * .12f : tree.Kind == 2 ? h * .62f : h * .42f, radius = .1f + h * .014f;
            Prism(sink, CityStyle.Bark, p, radius, trunk + h * .15f, 5);
            if (tree.Kind == 1) Blob(sink, leaf, p + Vector3.up * (trunk + (h - trunk) * .5f), new Vector3(h * .09f, (h - trunk) * .52f, h * .09f), 6, 4);
            else if (tree.Kind == 2) Blob(sink, leaf, p + Vector3.up * (h * .82f), new Vector3(h * .34f, h * .17f, h * .34f), 7, 3);
            else
            {
                Blob(sink, leaf, p + Vector3.up * (h * .7f), new Vector3(h * .27f, h * .3f, h * .27f), 7, 4);
                Blob(sink, leaf.Shade(.9f), p + new Vector3(h * .13f * (hash - .5f) * 2, h * .56f, h * .1f), new Vector3(h * .2f, h * .2f, h * .22f), 6, 3);
            }
            Vector3 x = Vector3.right * .22f, z = Vector3.forward * .22f, up = Vector3.up * 3;
            Vector3 b = p + Vector3.down * .2f;
            ColliderQuad(build, b - x + z, b + x + z, b + x + z + up, b - x + z + up); ColliderQuad(build, b + x - z, b - x - z, b - x - z + up, b + x - z + up);
            ColliderQuad(build, b + x + z, b + x - z, b + x - z + up, b + x + z + up); ColliderQuad(build, b - x - z, b - x + z, b - x + z + up, b - x - z + up);
        }

        static void Blob(CitySink sink, in Surface s, Vector3 c, Vector3 size, int sides, int rings)
        {
            int first = sink.Count;
            for (int j = 0; j <= rings; j++)
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2 / sides, b = -Mathf.PI / 2 + j * Mathf.PI / rings;
                    var d = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(b), Mathf.Sin(a) * Mathf.Cos(b));
                    sink.V(c + Vector3.Scale(d, size), d, new Vector2(i, j), s);
                }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < sides; i++)
                {
                    int a = first + j * sides + i, b = first + j * sides + (i + 1) % sides, d = a + sides, e = b + sides;
                    var facing = sink.P[a] + sink.P[e] - c * 2;
                    sink.Tri(a, b, e, facing); sink.Tri(a, e, d, facing);
                }
        }

        static void GardenWall(TileBuild build, TileWall wall, CityTerrain terrain)
        {
            var sink = build.Always;
            var pts = wall.Points;
            bool hedge = wall.Kind == 1 && wall.Height < 1.7f;
            float hash = Mathf.Abs(Mathf.Sin(pts[0] * 3.17f + pts[1] * 5.31f) * 9973f) % 1f;
            var surface = hedge ? CityStyle.HedgeGreen : hash < .22f ? CityStyle.BrickWall : CityStyle.GardenWall.Shade(.86f + hash * .16f);
            float half = hedge ? .28f : .11f, h = wall.Height;
            for (int k = 0; k + 3 < pts.Length; k += 2)
            {
                var a = new Vector2(pts[k], pts[k + 1]); var b = new Vector2(pts[k + 2], pts[k + 3]);
                var f = new Frame(a, b);
                if (f.L < .05f) continue;
                float ga = terrain.At(a.x, a.y) - .1f, gb = terrain.At(b.x, b.y) - .1f;
                Vector3 n = f.N * half;
                Vector3 a0 = new Vector3(a.x, ga, a.y), b0 = new Vector3(b.x, gb, b.y), a1 = a0 + Vector3.up * (h + .1f), b1 = b0 + Vector3.up * (h + .1f);
                sink.Quad(surface, a0 + n, b0 + n, b1 + n, a1 + n, f.N, Vector2.zero, new Vector2(f.L, 0), new Vector2(f.L, h), new Vector2(0, h));
                sink.Quad(surface, b0 - n, a0 - n, a1 - n, b1 - n, -f.N, Vector2.zero, new Vector2(f.L, 0), new Vector2(f.L, h), new Vector2(0, h));
                sink.Quad(hedge ? surface : CityStyle.StoneTrim.Shade(.85f), a1 + n, b1 + n, b1 - n, a1 - n, Vector3.up, Vector2.zero, new Vector2(f.L, 0), new Vector2(f.L, half * 2), new Vector2(0, half * 2));
                ColliderQuad(build, a0, b0, b1, a1, true);
            }
        }

        // ------------------------------------------------------------------ triangulation
        /// <summary>Ear clipping for a simple polygon (roof faces from the straight skeleton); indices refer to the input order.</summary>
        public static void EarClip(List<Vector2> poly, List<int> output)
        {
            output.Clear();
            int n = poly.Count;
            if (n < 3) return;
            if (n == 3) { output.Add(0); output.Add(1); output.Add(2); return; }
            float area = 0;
            for (int i = 0; i < n; i++) { var p = poly[i]; var q = poly[(i + 1) % n]; area += p.x * q.y - q.x * p.y; }
            var order = new List<int>(n);
            if (area > 0) for (int i = 0; i < n; i++) order.Add(i); else for (int i = n - 1; i >= 0; i--) order.Add(i);
            int guard = n * n;
            while (order.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < order.Count; i++)
                {
                    int ia = order[(i + order.Count - 1) % order.Count], ib = order[i], ic = order[(i + 1) % order.Count];
                    Vector2 a = poly[ia], b = poly[ib], c = poly[ic];
                    float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                    if (cross < -1e-9f) continue;                       // reflex corner
                    bool empty = true;
                    for (int k = 0; k < order.Count && empty; k++)
                    {
                        int ip = order[k];
                        if (ip == ia || ip == ib || ip == ic) continue;
                        var p = poly[ip];
                        float d0 = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
                        float d1 = (c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x);
                        float d2 = (a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x);
                        if (d0 > 1e-9f && d1 > 1e-9f && d2 > 1e-9f) empty = false;
                    }
                    if (!empty) continue;
                    output.Add(ia); output.Add(ib); output.Add(ic);
                    order.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (order.Count == 3) { output.Add(order[0]); output.Add(order[1]); output.Add(order[2]); }
            else for (int i = 1; i + 1 < order.Count; i++) { output.Add(order[0]); output.Add(order[i]); output.Add(order[i + 1]); }   // degenerate remainder: fan
        }
    }
}
