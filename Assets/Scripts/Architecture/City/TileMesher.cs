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

    /// <summary>Road edges that cross a tile, with what the mesher needs to dress them.</summary>
    public sealed class StreetInput
    {
        public RoadEdge[] Edges;
        public CityRoads Graph;
        /// <summary>Widest carriageway meeting at each node (m).</summary>
        public float[] NodeWidth;
        /// <summary>Extent of the hand-built core district, which keeps its own street dressing.</summary>
        public float[] Core;
        public float Tile;
    }

    /// <summary>Turns compiled survey tiles into meshes. Pure functions over tile data: safe on worker threads.</summary>
    public static class TileMesher
    {
        const float MaxEdge = 48;

        public static TileBuild Shell(TileData data, CityTerrain terrain, float[] levels, StreetInput streets = null)
        {
            var build = new TileBuild { Data = data };
            Ground(build, terrain, levels);
            if (streets != null) Streets(build, streets, terrain);
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

        // ------------------------------------------------------------------ streets
        static readonly Surface Paint = new Surface(Layer.Plain, .9f, .9f, .86f);
        static readonly Surface LampMetal = new Surface(Layer.Plain, .16f, .17f, .18f);
        static readonly Surface LampIron = new Surface(Layer.Plain, .07f, .08f, .08f);
        static readonly Surface LampGlass = new Surface(Layer.Plain, 1f, .86f, .6f);
        static readonly Vector4 Glow = new Vector4(0, 0, -1, 0);

        /// <summary>Lane markings, zebra crossings and street lamps along the surveyed centrelines that cross this tile.
        /// Positions follow the survey graph and its measured widths; the pattern (dash rhythm, lamp spacing) is typology.</summary>
        static void Streets(TileBuild build, StreetInput input, CityTerrain terrain)
        {
            var sink = build.Always;
            float x0 = build.Data.Tx * input.Tile, z0 = build.Data.Tz * input.Tile, x1 = x0 + input.Tile, z1 = z0 + input.Tile;
            bool Mine(float x, float z) => x >= x0 && x < x1 && z >= z0 && z < z1;
            bool Core(float x, float z) => input.Core != null && x > input.Core[0] && x < input.Core[2] && z > input.Core[1] && z < input.Core[3];
            // Bounding boxes of this tile's solid units: a lamp is never planted inside a building.
            var units = build.Data.Units;
            var boxes = new Vector4[units.Length];
            for (int u = 0; u < units.Length; u++)
            {
                var o = units[u].Outline; int n = units[u].RingSizes[0];
                float ax = float.MaxValue, az = float.MaxValue, bx = float.MinValue, bz = float.MinValue;
                for (int k = 0; k < n; k++) { ax = Mathf.Min(ax, o[2 * k]); bx = Mathf.Max(bx, o[2 * k]); az = Mathf.Min(az, o[2 * k + 1]); bz = Mathf.Max(bz, o[2 * k + 1]); }
                boxes[u] = new Vector4(ax - .4f, az - .4f, bx + .4f, bz + .4f);
            }
            bool Built(float x, float z)
            {
                for (int u = 0; u < units.Length; u++)
                {
                    var b = boxes[u];
                    if (x < b.x || x > b.z || z < b.y || z > b.w || units[u].Suspended) continue;
                    var o = units[u].Outline; int n = units[u].RingSizes[0]; bool inside = false;
                    for (int i = 0, j = n - 1; i < n; j = i++)
                        if ((o[2 * i + 1] > z) != (o[2 * j + 1] > z) && x < (o[2 * j] - o[2 * i]) * (z - o[2 * i + 1]) / (o[2 * j + 1] - o[2 * i + 1]) + o[2 * i]) inside = !inside;
                    if (inside) return true;
                }
                return false;
            }
            void Strip(Vector2 a, Vector2 b, float half, float lift)
            {
                var d = (b - a).normalized; var n = new Vector2(-d.y, d.x) * half;
                Vector3 P(Vector2 p) => new Vector3(p.x, terrain.At(p.x, p.y) + lift, p.y);
                sink.Quad(Paint, P(a - n), P(a + n), P(b + n), P(b - n), Vector3.up, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
            }
            foreach (var edge in input.Edges)
            {
                if (edge.Pedestrian || edge.Points.Length < 4) continue;
                int count = edge.Points.Length / 2;
                var along = new float[count];
                for (int k = 1; k < count; k++) along[k] = along[k - 1] + Mathf.Sqrt(Sq(edge.Points[2 * k] - edge.Points[2 * k - 2]) + Sq(edge.Points[2 * k + 1] - edge.Points[2 * k - 1]));
                float length = along[count - 1];
                if (length < 6) continue;
                Vector2 At(float s, out Vector2 dir)
                {
                    int k = 1; while (k < count - 1 && along[k] < s) k++;
                    var a = new Vector2(edge.Points[2 * k - 2], edge.Points[2 * k - 1]); var b = new Vector2(edge.Points[2 * k], edge.Points[2 * k + 1]);
                    float span = along[k] - along[k - 1];
                    dir = span > 1e-4f ? (b - a) / span : Vector2.right;
                    return Vector2.Lerp(a, b, span > 1e-4f ? Mathf.Clamp01((s - along[k - 1]) / span) : 0);
                }
                float lift = edge.Bridge ? .16f : .035f, width = edge.Width;
                bool oneWay = edge.OneWayForward || edge.OneWayBackward, big = edge.Class >= CityRoads.Main, fast = edge.Class == CityRoads.Fast;
                // Junctions keep clear of paint: the wider the crossing roads, the longer the gap.
                float clearA = input.Graph.NodeDegree[edge.A] >= 3 ? Mathf.Max(5, input.NodeWidth[edge.A] * .5f + 3) : 1.5f;
                float clearB = input.Graph.NodeDegree[edge.B] >= 3 ? Mathf.Max(5, input.NodeWidth[edge.B] * .5f + 3) : 1.5f;
                if (edge.Roundabout) { clearA = clearB = .5f; }
                uint seed = (uint)(edge.A * 7919 + edge.B * 104729);
                // ---- centre line or lane divider
                bool centre = edge.Class >= CityRoads.Local && !edge.Roundabout && (oneWay ? width >= 6.4f && edge.Lanes >= 2 : width >= 5.4f);
                if (centre)
                    for (float s = clearA; s + 3 <= length - clearB; s += fast ? 12 : 7.5f)
                    {
                        float dash = fast ? 4.5f : 3;
                        var a = At(s, out _); var b = At(s + dash, out _); var m = (a + b) * .5f;
                        if (Mine(m.x, m.y) && !Core(m.x, m.y)) Strip(a, b, .06f, lift);
                    }
                // ---- edge lines on main and fast roads
                if (big && !edge.Roundabout && width >= 6)
                    for (float s = clearA; s < length - clearB; s += 8)
                    {
                        float e = Mathf.Min(s + 8, length - clearB);
                        var a = At(s, out var da); var b = At(e, out var db); var m = (a + b) * .5f;
                        if (!Mine(m.x, m.y) || Core(m.x, m.y)) continue;
                        foreach (float side in new[] { -1f, 1f })
                        {
                            float off = side * (width * .5f - .35f);
                            Strip(a + new Vector2(-da.y, da.x) * off, b + new Vector2(-db.y, db.x) * off, .06f, lift);
                        }
                    }
                // ---- zebra crossings where streets meet
                if (edge.Class >= CityRoads.Local && !fast && !edge.Roundabout && width >= 4.6f && length > 22)
                    for (int end = 0; end < 2; end++)
                    {
                        int node = end == 0 ? edge.A : edge.B;
                        if (input.Graph.NodeDegree[node] < 3) continue;
                        if (!big && (seed + (uint)end * 31) % 100 > 55) continue;
                        float clear = end == 0 ? clearA : clearB, s = end == 0 ? clear - 2.6f : length - clear + 2.6f;
                        var c = At(s, out var dir);
                        if (!Mine(c.x, c.y) || Core(c.x, c.y)) continue;
                        var across = new Vector2(-dir.y, dir.x);
                        int stripes = Mathf.Max(3, Mathf.FloorToInt((width - .6f) / 1f));
                        for (int k = 0; k < stripes; k++)
                        {
                            var m = c + across * ((k - (stripes - 1) * .5f) * 1f);
                            Strip(m - dir * 1.4f, m + dir * 1.4f, .25f, lift);
                        }
                    }
                // ---- street lamps on alternating sides
                if (edge.Class >= CityRoads.Local && !edge.Roundabout && length > 18)
                {
                    float spacing = fast ? 44 : big ? 34 : 30;
                    int index = 0;
                    for (float s = spacing * .5f + (seed % 9); s < length - 6; s += spacing, index++)
                    {
                        var c = At(s, out var dir);
                        float side = fast ? 1 : (index + (int)(seed % 2)) % 2 == 0 ? 1 : -1;
                        var across = new Vector2(-dir.y, dir.x) * side;
                        var foot = c + across * (width * .5f + .7f);
                        if (!Mine(foot.x, foot.y) || Core(foot.x, foot.y) || Built(foot.x, foot.y)) continue;
                        Lamp(build, new Vector3(foot.x, terrain.At(foot.x, foot.y), foot.y), new Vector3(-across.x, 0, -across.y), big);
                    }
                }
            }
        }

        static float Sq(float v) => v * v;

        /// <summary>A lamp post: a tall arm lamp on main roads, a lantern on local streets. <paramref name="toRoad"/> points at the carriageway.</summary>
        static void Lamp(TileBuild build, Vector3 foot, Vector3 toRoad, bool tall)
        {
            var sink = build.Always;
            var right = Vector3.Cross(Vector3.up, toRoad);
            if (tall)
            {
                float h = 8.2f;
                sink.Box(LampMetal, foot + Vector3.up * (h * .5f), right * .07f, Vector3.up * (h * .5f), toRoad * .07f);
                sink.Box(LampMetal, foot + Vector3.up * h + toRoad * .75f, right * .05f, Vector3.up * .05f, toRoad * .8f);
                var head = foot + Vector3.up * (h - .08f) + toRoad * 1.45f;
                sink.Box(LampMetal, head + Vector3.up * .06f, right * .16f, Vector3.up * .05f, toRoad * .34f);
                sink.Quad(LampGlass, head - right * .13f - toRoad * .3f, head + right * .13f - toRoad * .3f, head + right * .13f + toRoad * .3f, head - right * .13f + toRoad * .3f, Vector3.down,
                    Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Glow, Glow, Glow, Glow);
            }
            else
            {
                float h = 3.7f;
                sink.Box(LampIron, foot + Vector3.up * .25f, right * .1f, Vector3.up * .25f, toRoad * .1f);
                sink.Box(LampIron, foot + Vector3.up * (h * .5f), right * .045f, Vector3.up * (h * .5f), toRoad * .045f);
                var c = foot + Vector3.up * (h + .28f);
                // Lantern: four glowing panes under a small cap.
                foreach (var (n, t) in new[] { (toRoad, right), (-toRoad, right), (right, toRoad), (-right, toRoad) })
                    sink.Quad(LampGlass, c + n * .15f - t * .13f - Vector3.up * .26f, c + n * .15f + t * .13f - Vector3.up * .26f, c + n * .19f + t * .17f + Vector3.up * .22f, c + n * .19f - t * .17f + Vector3.up * .22f, n,
                        Vector2.zero, Vector2.right, Vector2.one, Vector2.up, Glow, Glow, Glow, Glow);
                sink.Box(LampIron, c + Vector3.up * .28f, right * .23f, Vector3.up * .05f, toRoad * .23f);
            }
            Vector3 x = Vector3.right * .09f, z = Vector3.forward * .09f, up = Vector3.up * 3.2f, b = foot + Vector3.down * .2f;
            ColliderQuad(build, b - x + z, b + x + z, b + x + z + up, b - x + z + up); ColliderQuad(build, b + x - z, b - x - z, b - x - z + up, b + x - z + up);
            ColliderQuad(build, b + x + z, b + x - z, b + x - z + up, b + x + z + up); ColliderQuad(build, b - x - z, b - x + z, b - x + z + up, b - x - z + up);
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
            var raw = wall.Points;
            bool hedge = wall.Kind == 1 && wall.Height < 1.7f;
            float hash = Mathf.Abs(Mathf.Sin(raw[0] * 3.17f + raw[1] * 5.31f) * 9973f) % 1f;
            var surface = hedge ? CityStyle.HedgeGreen : hash < .22f ? CityStyle.BrickWall : CityStyle.GardenWall.Shade(.86f + hash * .16f);
            var coping = hedge ? surface : CityStyle.StoneTrim.Shade(.85f);
            float half = hedge ? .28f : .11f, h = wall.Height;
            var p = new List<Vector2>(raw.Length / 2);
            for (int k = 0; k + 1 < raw.Length; k += 2)
            {
                var q = new Vector2(raw[k], raw[k + 1]);
                if (p.Count == 0 || (q - p[p.Count - 1]).sqrMagnitude > .0025f) p.Add(q);
            }
            int count = p.Count;
            if (count < 2) return;
            bool closed = count > 3 && (p[0] - p[count - 1]).sqrMagnitude < .0026f;
            // Both faces are mitred at every bend, so consecutive stretches share their corners and no crack opens between them.
            var side = new Vector2[count];
            var ground = new float[count];
            for (int k = 0; k < count; k++)
            {
                Vector2 before = k > 0 ? (p[k] - p[k - 1]).normalized : closed ? (p[count - 1] - p[count - 2]).normalized : Vector2.zero;
                Vector2 after = k < count - 1 ? (p[k + 1] - p[k]).normalized : closed ? (p[1] - p[0]).normalized : Vector2.zero;
                if (before == Vector2.zero) before = after;
                if (after == Vector2.zero) after = before;
                Vector2 n0 = new Vector2(-before.y, before.x), n1 = new Vector2(-after.y, after.x), m = n0 + n1;
                float length = m.magnitude;
                side[k] = length < .3f ? n1 * half : m / length * (half / Mathf.Max(.4f, Vector2.Dot(m / length, n1)));
                ground[k] = terrain.At(p[k].x, p[k].y) - .1f;
            }
            Vector3 P(int k, float s, float lift) => new Vector3(p[k].x + side[k].x * s, ground[k] + lift, p[k].y + side[k].y * s);
            float run = 0, top = h + .1f;
            for (int k = 0; k + 1 < count; k++)
            {
                var d = p[k + 1] - p[k];
                float length = d.magnitude;
                var n = new Vector3(-d.y, 0, d.x) / length;
                sink.Quad(surface, P(k, 1, 0), P(k + 1, 1, 0), P(k + 1, 1, top), P(k, 1, top), n, new Vector2(run, 0), new Vector2(run + length, 0), new Vector2(run + length, top), new Vector2(run, top));
                sink.Quad(surface, P(k + 1, -1, 0), P(k, -1, 0), P(k, -1, top), P(k + 1, -1, top), -n, new Vector2(run, 0), new Vector2(run + length, 0), new Vector2(run + length, top), new Vector2(run, top));
                sink.Quad(coping, P(k, 1, top), P(k + 1, 1, top), P(k + 1, -1, top), P(k, -1, top), Vector3.up, new Vector2(run, 0), new Vector2(run + length, 0), new Vector2(run + length, half * 2), new Vector2(run, half * 2));
                ColliderQuad(build, P(k, 0, 0), P(k + 1, 0, 0), P(k + 1, 0, top), P(k, 0, top), true);
                run += length;
            }
            if (closed) return;
            foreach (int k in new[] { 0, count - 1 })
            {
                var d = (k == 0 ? p[0] - p[1] : p[count - 1] - p[count - 2]).normalized;
                sink.Quad(surface, P(k, 1, 0), P(k, -1, 0), P(k, -1, top), P(k, 1, top), new Vector3(d.x, 0, d.y), Vector2.zero, new Vector2(half * 2, 0), new Vector2(half * 2, top), new Vector2(0, top));
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
