using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Padova.Architecture
{
    /// <summary>Material slots shared by every generated mesh. PadovaCity.Materials is indexed by this.</summary>
    public enum Slot
    {
        Plaster0, Plaster1, Plaster2, Plaster3, Plaster4, Plaster5, Plaster6, Plaster7, Plaster8, Plaster9,
        Stone, Brick, Roof, Wood, Glass, Frame, ShutterGreen, ShutterBrown, Iron, ShopGlass, Door,
        Lead, Trachyte, Sidewalk, PorticoFloor, Courtyard, GreyStone, Masonry, Ceiling,
        AwningGreen, AwningRed, Railing, Balustrade, Dark,
        ClockBlue, Gold, Foliage, Bark, Asphalt, VehicleBlue, VehicleRed, White, LampGlow, Count
    }

    /// <summary>Accumulates positions/normals/UVs with one index list per material slot.</summary>
    public sealed class MeshSink
    {
        public readonly List<Vector3> Positions = new List<Vector3>(8192);
        public readonly List<Vector3> Normals = new List<Vector3>(8192);
        public readonly List<Vector2> Uvs = new List<Vector2>(8192);
        readonly List<int>[] indices = new List<int>[(int)Slot.Count];

        public int VertexCount => Positions.Count;
        List<int> I(Slot slot) => indices[(int)slot] ??= new List<int>(2048);

        int Add(Vector3 p, Vector3 n, Vector2 uv)
        {
            Positions.Add(p); Normals.Add(n); Uvs.Add(uv);
            return Positions.Count - 1;
        }

        /// <summary>Quad p0..p3 in perimeter order; winding is chosen so the face points along <paramref name="normal"/>.</summary>
        public void Quad(Slot slot, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 normal,
                         Vector2 t0, Vector2 t1, Vector2 t2, Vector2 t3)
        {
            int b = Add(p0, normal, t0); Add(p1, normal, t1); Add(p2, normal, t2); Add(p3, normal, t3);
            var list = I(slot);
            if (Vector3.Dot(Vector3.Cross(p3 - p0, p1 - p0), normal) >= 0)
            { list.Add(b); list.Add(b + 3); list.Add(b + 2); list.Add(b); list.Add(b + 2); list.Add(b + 1); }
            else
            { list.Add(b); list.Add(b + 1); list.Add(b + 2); list.Add(b); list.Add(b + 2); list.Add(b + 3); }
        }

        public void Tri(Slot slot, Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector2 ta, Vector2 tb, Vector2 tc)
        {
            int i = Add(a, normal, ta); Add(b, normal, tb); Add(c, normal, tc);
            var list = I(slot);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) >= 0) { list.Add(i); list.Add(i + 1); list.Add(i + 2); }
            else { list.Add(i); list.Add(i + 2); list.Add(i + 1); }
        }

        /// <summary>Indexed triangles from the plan; flips any triangle whose Unity normal disagrees with <paramref name="up"/>.</summary>
        public void Indexed(Slot slot, IList<Vector3> pts, IList<Vector2> uvs, IList<int> tris, int upSign)
        {
            int start = Positions.Count;
            var normals = new Vector3[pts.Count];
            var fixedTris = new int[tris.Count];
            for (int k = 0; k + 2 < tris.Count; k += 3)
            {
                int a = tris[k], b = tris[k + 1], c = tris[k + 2];
                var n = Vector3.Cross(pts[b] - pts[a], pts[c] - pts[a]);
                if (upSign != 0 && n.y * upSign < 0) { (b, c) = (c, b); n = -n; }
                fixedTris[k] = a; fixedTris[k + 1] = b; fixedTris[k + 2] = c;
                normals[a] += n; normals[b] += n; normals[c] += n;
            }
            for (int k = 0; k < pts.Count; k++)
                Add(pts[k], normals[k].sqrMagnitude > 1e-12f ? normals[k].normalized : Vector3.up, uvs[k]);
            var list = I(slot);
            for (int k = 0; k < fixedTris.Length; k++) list.Add(start + fixedTris[k]);
        }

        /// <summary>Regular grid with smooth normals (used for curved roofs and columns).</summary>
        public void Grid(Slot slot, Vector3[,] p, Vector2[,] uv, bool flip)
        {
            int nx = p.GetLength(0), ny = p.GetLength(1), start = Positions.Count;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    var du = p[Mathf.Min(i + 1, nx - 1), j] - p[Mathf.Max(i - 1, 0), j];
                    var dv = p[i, Mathf.Min(j + 1, ny - 1)] - p[i, Mathf.Max(j - 1, 0)];
                    var n = Vector3.Cross(du, dv).normalized;
                    Add(p[i, j], flip ? -n : n, uv[i, j]);
                }
            var list = I(slot);
            for (int i = 0; i < nx - 1; i++)
                for (int j = 0; j < ny - 1; j++)
                {
                    int a = start + i * ny + j, b = a + ny, c = b + 1, d = a + 1;
                    var n = Vector3.Cross(p[i + 1, j] - p[i, j], p[i, j + 1] - p[i, j]);
                    if (flip) n = -n;
                    // Front faces follow Unity's clockwise rule relative to the averaged normal.
                    if (Vector3.Dot(Vector3.Cross(Positions[d] - Positions[a], Positions[b] - Positions[a]), n) >= 0)
                    { list.Add(a); list.Add(d); list.Add(c); list.Add(a); list.Add(c); list.Add(b); }
                    else
                    { list.Add(a); list.Add(b); list.Add(c); list.Add(a); list.Add(c); list.Add(d); }
                }
        }

        /// <summary>Axis-aligned box in a local frame (origin + axes with half extents).</summary>
        public void Box(Slot slot, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, bool bottom = false)
        {
            Face(slot, c + az, ax, ay, az.normalized);
            Face(slot, c - az, -ax, ay, -az.normalized);
            Face(slot, c + ax, -az, ay, ax.normalized);
            Face(slot, c - ax, az, ay, -ax.normalized);
            Face(slot, c + ay, ax, -az, ay.normalized);
            if (bottom) Face(slot, c - ay, ax, az, -ay.normalized);
        }

        void Face(Slot slot, Vector3 centre, Vector3 u, Vector3 v, Vector3 n)
        {
            float lu = u.magnitude * 2, lv = v.magnitude * 2;
            Quad(slot, centre - u - v, centre + u - v, centre + u + v, centre - u + v, n,
                 new Vector2(0, 0), new Vector2(lu, 0), new Vector2(lu, lv), new Vector2(0, lv));
        }

        public void Cylinder(Slot slot, Vector3 bottom, float radius, float height, int sides, float taper = 1)
        {
            var p = new Vector3[sides + 1, 2];
            var uv = new Vector2[sides + 1, 2];
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                p[i, 0] = bottom + dir * radius;
                p[i, 1] = bottom + dir * radius * taper + Vector3.up * height;
                uv[i, 0] = new Vector2(a * radius, 0);
                uv[i, 1] = new Vector2(a * radius, height);
            }
            Grid(slot, p, uv, true);
        }

        public bool Has(Slot slot) => indices[(int)slot] != null && indices[(int)slot].Count > 0;

        /// <summary>Per-vertex tangents from UVs; safe to call on a worker thread, unlike Mesh.RecalculateTangents.</summary>
        public Vector4[] ComputeTangents()
        {
            var tan = new Vector3[Positions.Count];
            foreach (var list in indices)
            {
                if (list == null) continue;
                for (int k = 0; k + 2 < list.Count; k += 3)
                {
                    int a = list[k], b = list[k + 1], c = list[k + 2];
                    Vector3 e1 = Positions[b] - Positions[a], e2 = Positions[c] - Positions[a];
                    Vector2 d1 = Uvs[b] - Uvs[a], d2 = Uvs[c] - Uvs[a];
                    float det = d1.x * d2.y - d2.x * d1.y;
                    if (Mathf.Abs(det) < 1e-9f) continue;
                    var t = (e1 * d2.y - e2 * d1.y) / det;
                    tan[a] += t; tan[b] += t; tan[c] += t;
                }
            }
            var result = new Vector4[tan.Length];
            for (int k = 0; k < tan.Length; k++)
            {
                var n = Normals[k];
                var t = tan[k] - n * Vector3.Dot(n, tan[k]);
                if (t.sqrMagnitude < 1e-10f) t = Vector3.Cross(n, Mathf.Abs(n.y) < .9f ? Vector3.up : Vector3.right);
                t.Normalize();
                result[k] = new Vector4(t.x, t.y, t.z, 1);
            }
            return result;
        }

        public Mesh ToMesh(string name, Material[] palette, out Material[] materials, Vector4[] precomputedTangents)
        {
            var mesh = ToMesh(name, palette, out materials, false);
            if (precomputedTangents != null) mesh.SetTangents(precomputedTangents);
            return mesh;
        }

        public Mesh ToMesh(string name, Material[] palette, out Material[] materials, bool tangents = true)
        {
            var mesh = new Mesh { name = name, indexFormat = Positions.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(Positions);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uvs);
            var used = new List<int>();
            for (int s = 0; s < indices.Length; s++) if (indices[s] != null && indices[s].Count > 0) used.Add(s);
            mesh.subMeshCount = used.Count;
            materials = new Material[used.Count];
            for (int k = 0; k < used.Count; k++)
            {
                mesh.SetTriangles(indices[used[k]], k, false);
                materials[k] = palette != null && used[k] < palette.Length ? palette[used[k]] : null;
            }
            mesh.RecalculateBounds();
            if (tangents) mesh.RecalculateTangents();
            return mesh;
        }
    }

    /// <summary>A wall plane: origin at the segment start, E along the wall, N outward, Y up.</summary>
    public struct Frame
    {
        public Vector3 A, E, N;
        public float L, U;

        public Frame(Vector2 a, Vector2 b, float uOffset = 0)
        {
            var d = b - a;
            L = d.magnitude;
            var e = L > 1e-6f ? d / L : Vector2.right;
            A = new Vector3(a.x, 0, a.y);
            E = new Vector3(e.x, 0, e.y);
            N = new Vector3(e.y, 0, -e.x);
            U = uOffset;
        }

        public Vector3 P(float u, float y, float d) => new Vector3(A.x + E.x * u + N.x * d, y, A.z + E.z * u + N.z * d);
    }
}
