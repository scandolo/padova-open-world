using System.Collections.Generic;
using UnityEngine;

namespace Padova.Architecture
{
    public enum Arch { None, Round, Pointed }
    internal enum OpeningKind { Window, Shop, Door, Portal, Blind, Tall }

    internal struct Opening
    {
        public float U0, U1, Yb, Ys, Yt;
        public Arch Arch;
        public OpeningKind Kind;
        public bool Nobile;
        public float W => U1 - U0;
        public float Uc => (U0 + U1) * 0.5f;
    }

    /// <summary>Per-building architectural vocabulary. Survey data fixes footprint, heights and porticoes;
    /// these parameters choose among Padova's recurring façade types (documented in docs/ARCHITECTURE.md).</summary>
    public sealed class UnitStyle
    {
        public Slot Wall, Shutter;
        public bool Brick, Surround, StringCourse, Balcony, Plinth, Shutters, Blind, Church, Tower, Rusticated;
        public float Bay, WinW, WinH, GfH, FloorH, ColumnSpacing;
        public Arch UpperArch, GroundArch, ArcadeArch;
        public bool RoundColumns;
        public System.Random Rng;

        static readonly Slot[] Palette =
        {
            Slot.Plaster0, Slot.Plaster1, Slot.Plaster2, Slot.Plaster3, Slot.Plaster4,
            Slot.Plaster5, Slot.Plaster6, Slot.Plaster7, Slot.Plaster8, Slot.Plaster9
        };

        public static uint Hash(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h;
        }

        /// <summary>The historic-centre vocabulary drawn deterministically from a seed (one seed per building keeps its volumes coherent).</summary>
        public static UnitStyle Seeded(int seed)
        {
            var r = new System.Random(seed);
            float F(float a, float b) => a + (float)r.NextDouble() * (b - a);
            var s = new UnitStyle { Rng = r };
            s.Wall = Palette[r.Next(Palette.Length)];
            s.Bay = F(2.9f, 3.7f);
            s.WinW = F(1.0f, 1.22f);
            s.WinH = F(1.85f, 2.2f);
            s.GfH = F(3.9f, 4.6f);
            s.FloorH = F(3.1f, 3.5f);
            float a = F(0, 1);
            s.UpperArch = a < 0.12f ? Arch.Pointed : a < 0.22f ? Arch.Round : Arch.None;
            s.GroundArch = F(0, 1) < 0.4f ? Arch.Round : Arch.None;
            s.Surround = F(0, 1) < 0.8f;
            s.StringCourse = F(0, 1) < 0.72f;
            s.Balcony = F(0, 1) < 0.38f;
            s.Plinth = F(0, 1) < 0.85f;
            s.Shutters = s.UpperArch == Arch.Pointed ? F(0, 1) < 0.3f : F(0, 1) < 0.9f;
            s.Shutter = F(0, 1) < 0.68f ? Slot.ShutterGreen : Slot.ShutterBrown;
            s.Rusticated = F(0, 1) < 0.12f;
            s.RoundColumns = F(0, 1) < 0.55f;
            s.ColumnSpacing = F(3.0f, 4.1f);
            s.ArcadeArch = F(0, 1) < 0.7f ? Arch.Round : Arch.Pointed;
            return s;
        }

        public static UnitStyle For(PlanUnit u)
        {
            var s = Seeded((int)(Hash(u.id) & 0x7fffffff));
            if (s.Rng.NextDouble() < 0.05) { s.Wall = Slot.Brick; s.Brick = true; }
            if (u.use == "church")
            {
                s.Church = true; s.Brick = true; s.Wall = Slot.Brick; s.Shutters = false; s.Balcony = false;
                s.Bay = 6.5f; s.WinW = 1.5f; s.UpperArch = Arch.Round; s.StringCourse = false; s.Surround = true;
            }
            else if (u.use == "tower")
            {
                s.Tower = true; s.Brick = true; s.Wall = Slot.Brick; s.Shutters = false; s.Balcony = false;
                s.Bay = 30; s.WinW = 0.9f; s.WinH = 1.6f; s.FloorH = 6f; s.UpperArch = Arch.Round; s.StringCourse = false;
            }
            if (u.name == "Palazzo della Ragione") { s.Wall = Slot.GreyStone; s.Blind = true; }
            return s;
        }
    }

    /// <summary>Expands plan segments into façade geometry.</summary>
    public sealed class FacadeBuilder
    {
        readonly MeshSink s;
        readonly List<Vector2> arch = new List<Vector2>(32);
        readonly List<Vector2> arch2 = new List<Vector2>(32);
        public readonly List<Bounds> Columns = new List<Bounds>();
        PlanUnit u;
        UnitStyle st;
        internal UnitStyle Style { get => st; set => st = value; }
        internal MeshSink Sink => s;

        public FacadeBuilder(MeshSink sink) { s = sink; }

        public void Unit(PlanUnit unit, UnitStyle style)
        {
            u = unit; st = style;
            var segs = unit.segs;
            float uOffset = 0;
            for (int i = 0; i + 7 < segs.Length; i += 8)
            {
                var f = new Frame(new Vector2(segs[i], segs[i + 1]), new Vector2(segs[i + 2], segs[i + 3]), uOffset);
                uOffset += f.L;
                if (f.L < 0.05f) continue;
                float y0 = segs[i + 4], y1 = segs[i + 5];
                var kind = (SegmentKind)(int)segs[i + 6];
                var flags = (SegmentFlags)(int)segs[i + 7];
                bool courtyard = (flags & SegmentFlags.Courtyard) != 0;
                if(unit.id=="UN_VOL:45916" && courtyard)continue; // Ancient Bo courtyard is built as a double colonnade.
                bool cornice = (flags & SegmentFlags.TopExposed) != 0;
                switch (kind)
                {
                    case SegmentKind.Street:
                        Facade(f, y0, y1, u.ground, true, courtyard, cornice, false);
                        break;
                    case SegmentKind.Upper:
                        bool over = (flags & SegmentFlags.OverPortico) != 0;
                        Facade(f, y0, y1, over ? y0 : u.ground, false, courtyard, cornice, over);
                        break;
                    case SegmentKind.PorticoBack:
                        PorticoBack(f, y0, y1);
                        break;
                    case SegmentKind.Blank:
                        Rect(st.Wall, f, 0, y0, f.L, y1, 0);
                        break;
                    case SegmentKind.Arcade:
                        Arcade(f, y0, y1, courtyard);
                        break;
                }
            }
            if (unit.suspended && unit.ceilingTris != null && unit.ceilingTris.Length > 0) Ceiling(unit);
        }

        // ------------------------------------------------------------------ primitives
        internal void Rect(Slot slot, in Frame f, float u0, float y0, float u1, float y1, float d)
        {
            if (u1 - u0 < 1e-3f || y1 - y0 < 1e-3f) return;
            s.Quad(slot, f.P(u0, y0, d), f.P(u1, y0, d), f.P(u1, y1, d), f.P(u0, y1, d), f.N,
                new Vector2(f.U + u0, y0), new Vector2(f.U + u1, y0), new Vector2(f.U + u1, y1), new Vector2(f.U + u0, y1));
        }

        internal void RectBack(Slot slot, in Frame f, float u0, float y0, float u1, float y1, float d)
        {
            if (u1 - u0 < 1e-3f || y1 - y0 < 1e-3f) return;
            s.Quad(slot, f.P(u0, y0, d), f.P(u1, y0, d), f.P(u1, y1, d), f.P(u0, y1, d), -f.N,
                new Vector2(u0, y0), new Vector2(u1, y0), new Vector2(u1, y1), new Vector2(u0, y1));
        }

        /// <summary>Box in wall coordinates: u along, y up, d outward.</summary>
        internal void Block(Slot slot, in Frame f, float u0, float u1, float y0, float y1, float d0, float d1, bool bottom = true)
        {
            var c = f.P((u0 + u1) / 2, (y0 + y1) / 2, (d0 + d1) / 2);
            s.Box(slot, c, f.E * ((u1 - u0) / 2), Vector3.up * ((y1 - y0) / 2), f.N * ((d1 - d0) / 2), bottom);
        }

        internal static float Rise(Arch a, float w) => a == Arch.Round ? w * 0.5f : a == Arch.Pointed ? w * 0.7416f : 0;

        internal void ArchPoints(List<Vector2> pts, Opening o, int n = 8)
        {
            pts.Clear();
            float w = o.W;
            if (o.Arch == Arch.Round)
            {
                float r = w / 2;
                for (int k = 0; k <= n * 2; k++)
                {
                    float t = Mathf.PI * (1 - k / (float)(n * 2));
                    pts.Add(new Vector2(o.Uc + r * Mathf.Cos(t), o.Ys + r * Mathf.Sin(t)));
                }
            }
            else
            {
                // Pointed (Venetian Gothic) arch: two arcs of radius R centred on the springing line.
                float R = w * 0.8f;
                float phi = Mathf.Acos((w / 2 - R) / R);
                for (int k = 0; k <= n; k++)
                {
                    float t = Mathf.Lerp(Mathf.PI, phi, k / (float)n);
                    pts.Add(new Vector2(o.U0 + R + R * Mathf.Cos(t), o.Ys + R * Mathf.Sin(t)));
                }
                for (int k = 1; k <= n; k++)
                {
                    float t = Mathf.Lerp(Mathf.PI - phi, 0, k / (float)n);
                    pts.Add(new Vector2(o.U1 - R + R * Mathf.Cos(t), o.Ys + R * Mathf.Sin(t)));
                }
            }
        }

        // ------------------------------------------------------------------ walls with openings
        internal struct Row { public float Yb, Yt; public List<Opening> Ops; }

        internal void WallWithRowsLocal(Slot wall, in Frame f, float y0, float y1, List<Row> rows) => WallWithRows(wall, f, y0, y1, rows);

        void WallWithRows(Slot wall, in Frame f, float y0, float y1, List<Row> rows, float d = 0, bool back = false)
        {
            rows.Sort((a, b) => a.Yb.CompareTo(b.Yb));
            float y = y0;
            foreach (var row in rows)
            {
                Strip(wall, f, y, row.Yb, d, back);
                float u = 0;
                foreach (var o in row.Ops)
                {
                    Piece(wall, f, u, row.Yb, o.U0, row.Yt, d, back);
                    if (o.Yt < row.Yt) Piece(wall, f, o.U0, o.Yt, o.U1, row.Yt, d, back);
                    if (o.Arch != Arch.None) Spandrel(wall, f, o, d, back);
                    u = o.U1;
                }
                Piece(wall, f, u, row.Yb, f.L, row.Yt, d, back);
                y = row.Yt;
            }
            Strip(wall, f, y, y1, d, back);
        }

        void Strip(Slot wall, in Frame f, float ya, float yb, float d, bool back) => Piece(wall, f, 0, ya, f.L, yb, d, back);

        internal void Piece(Slot wall, in Frame f, float u0, float y0, float u1, float y1, float d, bool back)
        {
            if (back) RectBack(wall, f, u0, y0, u1, y1, d); else Rect(wall, f, u0, y0, u1, y1, d);
        }

        internal void Spandrel(Slot wall, in Frame f, Opening o, float d, bool back)
        {
            ArchPoints(arch, o);
            int mid = arch.Count / 2;
            var n = back ? -f.N : f.N;
            var tl = new Vector2(o.U0, o.Yt);
            var tr = new Vector2(o.U1, o.Yt);
            for (int k = 0; k < arch.Count - 1; k++)
            {
                var corner = k < mid ? tl : tr;
                var a = arch[k]; var b = arch[k + 1];
                s.Tri(wall, f.P(corner.x, corner.y, d), f.P(a.x, a.y, d), f.P(b.x, b.y, d), n,
                    new Vector2(f.U + corner.x, corner.y), new Vector2(f.U + a.x, a.y), new Vector2(f.U + b.x, b.y));
            }
            // Fill the small triangle between the two fans at the apex.
            var ap = arch[mid];
            s.Tri(wall, f.P(tl.x, tl.y, d), f.P(ap.x, ap.y, d), f.P(tr.x, tr.y, d), n,
                new Vector2(f.U + tl.x, tl.y), new Vector2(f.U + ap.x, ap.y), new Vector2(f.U + tr.x, tr.y));
        }

        internal void Reveal(Slot slot, in Frame f, Opening o, float d0, float d1, bool sill = true)
        {
            // d0 outer plane, d1 inner plane (d1 < d0).
            s.Quad(slot, f.P(o.U0, o.Yb, d0), f.P(o.U0, o.Yb, d1), f.P(o.U0, o.Ys, d1), f.P(o.U0, o.Ys, d0), f.E,
                new Vector2(d0, o.Yb), new Vector2(d1, o.Yb), new Vector2(d1, o.Ys), new Vector2(d0, o.Ys));
            s.Quad(slot, f.P(o.U1, o.Yb, d1), f.P(o.U1, o.Yb, d0), f.P(o.U1, o.Ys, d0), f.P(o.U1, o.Ys, d1), -f.E,
                new Vector2(d1, o.Yb), new Vector2(d0, o.Yb), new Vector2(d0, o.Ys), new Vector2(d1, o.Ys));
            if (sill)
                s.Quad(slot, f.P(o.U0, o.Yb, d0), f.P(o.U1, o.Yb, d0), f.P(o.U1, o.Yb, d1), f.P(o.U0, o.Yb, d1), Vector3.up,
                    new Vector2(o.U0, d0), new Vector2(o.U1, d0), new Vector2(o.U1, d1), new Vector2(o.U0, d1));
            if (o.Arch == Arch.None)
            {
                s.Quad(slot, f.P(o.U0, o.Ys, d1), f.P(o.U1, o.Ys, d1), f.P(o.U1, o.Ys, d0), f.P(o.U0, o.Ys, d0), Vector3.down,
                    new Vector2(o.U0, d1), new Vector2(o.U1, d1), new Vector2(o.U1, d0), new Vector2(o.U0, d0));
                return;
            }
            ArchPoints(arch, o);
            float acc = 0;
            for (int k = 0; k < arch.Count - 1; k++)
            {
                var a = arch[k]; var b = arch[k + 1];
                var t = b - a;
                float len = t.magnitude;
                var nl = new Vector2(t.y, -t.x) / Mathf.Max(len, 1e-5f);
                var n = f.E * nl.x + Vector3.up * nl.y;
                s.Quad(slot, f.P(a.x, a.y, d0), f.P(b.x, b.y, d0), f.P(b.x, b.y, d1), f.P(a.x, a.y, d1), n,
                    new Vector2(acc, d0), new Vector2(acc + len, d0), new Vector2(acc + len, d1), new Vector2(acc, d1));
                acc += len;
            }
        }

        internal void Panel(Slot slot, in Frame f, Opening o, float d, bool includeArch = true, float yTop = float.NaN)
        {
            float ys = float.IsNaN(yTop) ? o.Ys : yTop;
            int first = s.VertexCount;
            Rect(slot, f, o.U0, o.Yb, o.U1, ys, d);
            if (includeArch && o.Arch != Arch.None)
            {
                ArchPoints(arch, o);
                var c = new Vector2(o.Uc, o.Ys);
                for (int k = 0; k < arch.Count - 1; k++)
                {
                    var a = arch[k]; var b = arch[k + 1];
                    s.Tri(slot, f.P(c.x, c.y, d), f.P(a.x, a.y, d), f.P(b.x, b.y, d), f.N,
                        new Vector2(c.x, c.y), new Vector2(a.x, a.y), new Vector2(b.x, b.y));
                }
            }
            if (slot != Slot.Glass && slot != Slot.ShopGlass) return;
            // Glass carries no texture: its UV holds 1000 + a random number per window (which panes light up at night) and the height up the pane.
            var at = f.P(o.Uc, o.Yb, 0);
            float tag = Mathf.Abs(Mathf.Sin(at.x * 12.9898f + at.y * 37.719f + at.z * 78.233f) * 43758.547f) % 1f;
            tag = Mathf.Clamp(tag, .001f, .999f);
            float height = Mathf.Max(.1f, o.Ys - o.Yb + (o.Arch != Arch.None ? (o.U1 - o.U0) * .5f : 0));
            for (int k = first; k < s.VertexCount; k++) s.Uvs[k] = new Vector2(1000 + tag, (s.Positions[k].y - o.Yb) / height);
        }

        internal void Surround(Slot slot, in Frame f, Opening o, float fw, float proud)
        {
            Rect(slot, f, o.U0 - fw, o.Yb, o.U0, o.Ys, proud);
            Rect(slot, f, o.U1, o.Yb, o.U1 + fw, o.Ys, proud);
            if (o.Arch == Arch.None)
            {
                Block(slot, f, o.U0 - fw - 0.05f, o.U1 + fw + 0.05f, o.Ys, o.Ys + fw * 1.4f, 0, proud + 0.02f, true);
                return;
            }
            ArchPoints(arch, o);
            arch2.Clear();
            var centre = new Vector2(o.Uc, o.Ys);
            for (int k = 0; k < arch.Count; k++)
            {
                Vector2 dir;
                if (k == 0) dir = Vector2.left;
                else if (k == arch.Count - 1) dir = Vector2.right;
                else { var t = arch[k + 1] - arch[k - 1]; dir = new Vector2(-t.y, t.x).normalized; }
                arch2.Add(arch[k] + dir * fw);
            }
            for (int k = 0; k < arch.Count - 1; k++)
            {
                var a = arch[k]; var b = arch[k + 1]; var c = arch2[k + 1]; var d = arch2[k];
                s.Quad(slot, f.P(a.x, a.y, proud), f.P(b.x, b.y, proud), f.P(c.x, c.y, proud), f.P(d.x, d.y, proud), f.N,
                    a, b, c, d);
            }
        }

        void Sill(in Frame f, Opening o, float proud)
        {
            Block(Slot.Stone, f, o.U0 - 0.1f, o.U1 + 0.1f, o.Yb - 0.09f, o.Yb, -0.02f, proud, true);
        }

        void Window(in Frame f, Opening o, bool courtyard, bool pianoNobile)
        {
            float depth = 0.24f;
            var r = st.Rng;
            Reveal(st.Wall == Slot.Brick ? Slot.Brick : Slot.Ceiling, f, o, 0, -depth, true);
            bool shutters = st.Shutters && o.Arch == Arch.None;
            double roll = r.NextDouble();
            bool closed = shutters && roll < 0.28;
            if (closed)
            {
                Panel(st.Shutter, f, o, -0.08f, false);
                Rect(Slot.Frame, f, o.Uc - 0.02f, o.Yb, o.Uc + 0.02f, o.Ys, -0.075f);
            }
            else
            {
                Panel(Slot.Glass, f, o, -depth);
                float fd = -depth + 0.03f, b = 0.06f;
                Rect(Slot.Frame, f, o.U0, o.Yb, o.U0 + b, o.Ys, fd);
                Rect(Slot.Frame, f, o.U1 - b, o.Yb, o.U1, o.Ys, fd);
                Rect(Slot.Frame, f, o.U0 + b, o.Yb, o.U1 - b, o.Yb + b, fd);
                Rect(Slot.Frame, f, o.U0 + b, o.Ys - b, o.U1 - b, o.Ys, fd);
                Rect(Slot.Frame, f, o.Uc - 0.03f, o.Yb + b, o.Uc + 0.03f, o.Ys - b, fd);
                if (o.Arch == Arch.None && o.Ys - o.Yb > 1.7f)
                    Rect(Slot.Frame, f, o.U0 + b, o.Ys - 0.55f, o.U1 - b, o.Ys - 0.5f, fd);
                if (shutters && roll < 0.86)
                {
                    float fw = st.Surround && !courtyard ? 0.13f : 0.02f;
                    float lw = o.W / 2 + 0.02f;
                    Rect(st.Shutter, f, o.U0 - fw - lw, o.Yb, o.U0 - fw, o.Ys, 0.05f);
                    Rect(st.Shutter, f, o.U1 + fw, o.Yb, o.U1 + fw + lw, o.Ys, 0.05f);
                }
            }
            if (st.Surround && !courtyard) Surround(st.Brick ? Slot.Stone : Slot.Stone, f, o, 0.13f, 0.035f);
            Sill(f, o, st.Surround && !courtyard ? 0.07f : 0.05f);
            if (pianoNobile && !courtyard && st.Balcony && o.Arch == Arch.None)
            {
                float y = o.Yb - 0.02f;
                Block(Slot.Stone, f, o.U0 - 0.5f, o.U1 + 0.5f, y - 0.16f, y, 0, 0.62f, true);
                s.Quad(Slot.Railing, f.P(o.U0 - 0.46f, y, 0.58f), f.P(o.U1 + 0.46f, y, 0.58f), f.P(o.U1 + 0.46f, y + 1.0f, 0.58f), f.P(o.U0 - 0.46f, y + 1.0f, 0.58f), f.N,
                    new Vector2(0, 0), new Vector2(o.W + 0.92f, 0), new Vector2(o.W + 0.92f, 1), new Vector2(0, 1));
                s.Quad(Slot.Railing, f.P(o.U0 - 0.46f, y, 0.02f), f.P(o.U0 - 0.46f, y, 0.58f), f.P(o.U0 - 0.46f, y + 1.0f, 0.58f), f.P(o.U0 - 0.46f, y + 1.0f, 0.02f), -f.E,
                    new Vector2(0, 0), new Vector2(0.56f, 0), new Vector2(0.56f, 1), new Vector2(0, 1));
                s.Quad(Slot.Railing, f.P(o.U1 + 0.46f, y, 0.58f), f.P(o.U1 + 0.46f, y, 0.02f), f.P(o.U1 + 0.46f, y + 1.0f, 0.02f), f.P(o.U1 + 0.46f, y + 1.0f, 0.58f), f.E,
                    new Vector2(0, 0), new Vector2(0.56f, 0), new Vector2(0.56f, 1), new Vector2(0, 1));
            }
        }

        void Shop(in Frame f, Opening o, bool allowAwning)
        {
            float depth = 0.32f;
            Reveal(st.Rusticated ? Slot.Stone : Slot.Ceiling, f, o, 0, -depth, false);
            var r = st.Rng;
            if (r.NextDouble() < 0.18)
            {
                Panel(Slot.Iron, f, o, -depth + 0.05f, true);
                return;
            }
            Panel(Slot.ShopGlass, f, o, -depth);
            float fd = -depth + 0.04f;
            Rect(Slot.Iron, f, o.U0, o.Yb, o.U0 + 0.08f, o.Ys, fd);
            Rect(Slot.Iron, f, o.U1 - 0.08f, o.Yb, o.U1, o.Ys, fd);
            Rect(Slot.Iron, f, o.U0, o.Yb, o.U1, o.Yb + 0.35f, fd);
            float door = o.U0 + o.W * 0.62f;
            if (o.W > 1.8f) Rect(Slot.Iron, f, door, o.Yb, door + 0.06f, o.Ys, fd);
            if (o.Arch == Arch.None && o.Ys - o.Yb > 2.9f)
                Rect(Slot.Iron, f, o.U0, o.Ys - 0.55f, o.U1, o.Ys - 0.48f, fd);
            if (allowAwning && o.Arch == Arch.None && r.NextDouble() < 0.3)
            {
                var slot = r.NextDouble() < 0.55 ? Slot.AwningGreen : Slot.AwningRed;
                float y = o.Ys + 0.25f;
                var n = (f.N * 0.55f + Vector3.up).normalized;
                s.Quad(slot, f.P(o.U0 - 0.15f, y, 0.02f), f.P(o.U1 + 0.15f, y, 0.02f), f.P(o.U1 + 0.15f, y - 0.6f, 1.15f), f.P(o.U0 - 0.15f, y - 0.6f, 1.15f), n,
                    new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
                Rect(slot, f, o.U0 - 0.15f, y - 0.85f, o.U1 + 0.15f, y - 0.6f, 1.15f);
            }
        }

        void Door(in Frame f, Opening o, bool portal)
        {
            float depth = portal ? 0.45f : 0.3f;
            Reveal(Slot.Stone, f, o, 0, -depth, false);
            Panel(Slot.Door, f, o, -depth, o.Arch == Arch.None);
            if (o.Arch != Arch.None)
            {
                Panel(Slot.Door, f, o, -depth, false);
                ArchPoints(arch, o);
                var c = new Vector2(o.Uc, o.Ys);
                for (int k = 0; k < arch.Count - 1; k++)
                    s.Tri(Slot.Glass, f.P(c.x, c.y, -depth + 0.01f), f.P(arch[k].x, arch[k].y, -depth + 0.01f), f.P(arch[k + 1].x, arch[k + 1].y, -depth + 0.01f), f.N,
                        c, arch[k], arch[k + 1]);
            }
            if (portal || st.Surround) Surround(Slot.Stone, f, o, portal ? 0.28f : 0.16f, 0.05f);
        }

        // ------------------------------------------------------------------ façade layouts
        int Bays(float L, float bay, float w)
        {
            if (L < w + 0.9f) return 0;
            return Mathf.Max(1, Mathf.FloorToInt((L + 0.35f) / bay));
        }

        void Facade(in Frame f, float y0, float y1, float gridBase, bool ground, bool courtyard, bool cornice, bool overPortico)
        {
            var rows = new List<Row>();
            float L = f.L;
            if (st.Blind) { Rect(st.Wall, f, 0, y0, L, y1, 0); if (cornice) Cornice(f, y1); return; }
            float winW = st.WinW;
            int nb = Bays(L, st.Bay, winW);
            float bw = nb > 0 ? L / nb : L;
            float upperBase = gridBase;
            float groundTop = gridBase;
            if (ground)
            {
                float gfh = Mathf.Min(st.GfH, y1 - gridBase - 0.6f);
                if (y1 - gridBase < 6.2f) gfh = Mathf.Max(2.6f, y1 - gridBase - 0.6f);
                upperBase = gridBase + gfh;
                groundTop = upperBase;
                if (gfh > 2.4f && nb > 0) rows.Add(GroundRow(f, gridBase, gfh, nb, bw, courtyard));
            }
            float avail = y1 - upperBase - (cornice ? 0.45f : 0.15f);
            int nf = avail < 2.4f ? 0 : Mathf.Max(1, Mathf.RoundToInt(avail / st.FloorH));
            float fh = nf > 0 ? avail / nf : 0;
            bool church = st.Church || st.Tower;
            if (church && nb > 0 && avail > 3)
            {
                // Churches and towers: a few tall round-arched openings rather than domestic storeys.
                float h = st.Tower ? 1.6f : Mathf.Min(5.5f, avail * 0.45f);
                int levels = st.Tower ? Mathf.Max(1, Mathf.FloorToInt(avail / 7f)) : 1;
                for (int k = 0; k < levels; k++)
                {
                    float yb = st.Tower ? upperBase + avail * (k + 0.6f) / levels - h : upperBase + avail * 0.35f;
                    if (yb < y0 + 0.5f || yb + h > y1 - 0.4f) continue;
                    rows.Add(MakeRow(nb, bw, winW, yb, h, Arch.Round, OpeningKind.Tall));
                }
                nf = 0;
            }
            for (int k = 0; k < nf && nb > 0; k++)
            {
                float fy = upperBase + k * fh;
                bool nobile = k == 0 && (ground || overPortico);
                bool attic = k == nf - 1 && nf >= 4;
                float h = Mathf.Min(st.WinH + (nobile ? 0.3f : 0) - (attic ? 0.7f : 0), fh - 1.15f);
                if (h < 0.8f) continue;
                float yb = fy + Mathf.Min(0.95f, (fh - h) * 0.55f);
                if (yb < y0 + 0.35f || yb + h + Rise(st.UpperArch, winW) > y1 - 0.35f) continue;
                var row = MakeRow(nb, bw, winW, yb, h, attic ? Arch.None : st.UpperArch, OpeningKind.Window, nobile);
                rows.Add(row);
            }
            WallWithRows(st.Wall, f, y0, y1, rows);
            foreach (var row in rows)
                foreach (var o in row.Ops)
                {
                    switch (o.Kind)
                    {
                        case OpeningKind.Window: Window(f, o, courtyard, o.Nobile); break;
                        case OpeningKind.Tall: TallWindow(f, o); break;
                        case OpeningKind.Shop: Shop(f, o, !courtyard); break;
                        case OpeningKind.Door: Door(f, o, false); break;
                        case OpeningKind.Portal: Door(f, o, true); break;
                    }
                }
            if (ground && st.Plinth && !st.Church)
                Block(st.Rusticated ? Slot.GreyStone : Slot.Stone, f, -0.03f, L + 0.03f, y0, gridBase + 0.45f, -0.01f, 0.03f, false);
            if (ground && st.Rusticated && !courtyard)
                RusticBands(f, gridBase + 0.45f, groundTop);
            if (((ground && nf > 0) || overPortico) && st.StringCourse && !courtyard)
                Block(Slot.Stone, f, -0.06f, L + 0.06f, (overPortico ? y0 : groundTop) - 0.05f, (overPortico ? y0 : groundTop) + 0.14f, 0, 0.07f, true);
            if (cornice) Cornice(f, y1);
        }

        Row MakeRow(int nb, float bw, float w, float yb, float h, Arch a, OpeningKind kind, bool nobile = false)
        {
            var row = new Row { Yb = yb, Ops = new List<Opening>(nb) };
            float rise = Rise(a, w);
            for (int i = 0; i < nb; i++)
            {
                float c = (i + 0.5f) * bw;
                row.Ops.Add(new Opening { U0 = c - w / 2, U1 = c + w / 2, Yb = yb, Ys = yb + h, Yt = yb + h + rise, Arch = a, Kind = kind, Nobile = nobile && (i % 2 == nb % 2 || nb == 1) });
            }
            row.Yt = yb + h + rise;
            return row;
        }

        Row GroundRow(in Frame f, float g, float gfh, int nb, float bw, bool courtyard)
        {
            var row = new Row { Yb = g, Ops = new List<Opening>(nb) };
            var r = st.Rng;
            if (st.Church)
            {
                // A single portal centred on the façade.
                float w = Mathf.Min(2.4f, f.L - 1.2f);
                if (w > 1.2f)
                {
                    float h = Mathf.Min(gfh + 0.8f, 4.2f);
                    var o = new Opening { U0 = f.L / 2 - w / 2, U1 = f.L / 2 + w / 2, Yb = g, Ys = g + h - w / 2, Yt = g + h, Arch = Arch.Round, Kind = OpeningKind.Portal };
                    row.Ops.Add(o);
                    row.Yt = o.Yt;
                }
                else row.Yt = g;
                return row;
            }
            float top = g + Mathf.Min(gfh - 0.65f, 3.3f);
            if (courtyard)
            {
                float sill = g + 1.0f;
                float h = Mathf.Min(1.7f, gfh - 1.6f);
                if (h < 0.8f) { row.Yt = g; row.Yb = g; return row; }
                row.Yb = sill;
                for (int i = 0; i < nb; i++)
                {
                    float c = (i + 0.5f) * bw, w = st.WinW;
                    row.Ops.Add(new Opening { U0 = c - w / 2, U1 = c + w / 2, Yb = sill, Ys = sill + h, Yt = sill + h, Arch = Arch.None, Kind = OpeningKind.Window });
                }
                row.Yt = sill + h;
                return row;
            }
            var arch = st.GroundArch;
            for (int i = 0; i < nb; i++)
            {
                float c = (i + 0.5f) * bw;
                bool door = r.NextDouble() < 0.2 || bw < 2.1f;
                float w = door ? Mathf.Min(1.35f, bw - 0.6f) : Mathf.Min(bw - 0.75f, 2.7f);
                if (w < 0.8f) continue;
                float rise = Rise(arch, w);
                float ys = top - rise;
                if (ys - g < 2.1f) { ys = top; rise = 0; }
                row.Ops.Add(new Opening { U0 = c - w / 2, U1 = c + w / 2, Yb = g, Ys = ys, Yt = ys + rise, Arch = rise > 0 ? arch : Arch.None, Kind = door ? OpeningKind.Door : OpeningKind.Shop });
            }
            row.Yt = top;
            foreach (var o in row.Ops) row.Yt = Mathf.Max(row.Yt, o.Yt);
            return row;
        }

        void TallWindow(in Frame f, Opening o)
        {
            Reveal(Slot.Brick, f, o, 0, -0.45f, true);
            Panel(Slot.Glass, f, o, -0.45f);
            Surround(Slot.Stone, f, o, 0.18f, 0.04f);
        }

        void RusticBands(in Frame f, float y0, float y1)
        {
            for (float y = y0 + 0.45f; y < y1 - 0.1f; y += 0.45f)
                Rect(Slot.GreyStone, f, 0, y - 0.03f, f.L, y, 0.012f);
        }

        void Cornice(in Frame f, float top)
        {
            float L = f.L;
            Block(Slot.Stone, f, -0.08f, L + 0.08f, top - 0.62f, top - 0.5f, 0, 0.08f);
            Block(Slot.Stone, f, -0.18f, L + 0.18f, top - 0.5f, top - 0.34f, 0, 0.18f);
            Block(Slot.Stone, f, -0.3f, L + 0.3f, top - 0.34f, top - 0.2f, 0, 0.3f);
        }

        void PorticoBack(in Frame f, float y0, float y1)
        {
            // Shopfronts and doors under a surveyed portico.
            float g = u.ground;
            var rows = new List<Row>();
            float room = y1 - g;
            int nb = Bays(f.L, Mathf.Max(3.0f, st.Bay), 1.3f);
            if (nb > 0 && room > 2.5f)
            {
                float bw = f.L / nb;
                float top = g + Mathf.Min(room - 0.45f, 3.3f);
                var row = new Row { Yb = g, Ops = new List<Opening>(nb), Yt = top };
                var r = st.Rng;
                for (int i = 0; i < nb; i++)
                {
                    float c = (i + 0.5f) * bw;
                    bool door = r.NextDouble() < 0.18;
                    float w = door ? Mathf.Min(1.3f, bw - 0.6f) : Mathf.Min(bw - 0.7f, 2.8f);
                    if (w < 0.8f) continue;
                    row.Ops.Add(new Opening { U0 = c - w / 2, U1 = c + w / 2, Yb = g, Ys = top, Yt = top, Arch = Arch.None, Kind = door ? OpeningKind.Door : OpeningKind.Shop });
                }
                rows.Add(row);
            }
            WallWithRows(st.Wall, f, y0, y1, rows);
            foreach (var row in rows)
                foreach (var o in row.Ops)
                    if (o.Kind == OpeningKind.Shop) Shop(f, o, false); else Door(f, o, false);
            Block(Slot.Stone, f, -0.02f, f.L + 0.02f, y0, g + 0.3f, -0.01f, 0.02f, false);
        }

        internal void Arcade(in Frame f, float g, float under, bool courtyard)
        {
            // Columns/piers on the portico's open edge; spacing is typological, position/clearance are surveyed.
            float L = f.L;
            float clearance = under - g;
            int n = Mathf.Max(1, Mathf.RoundToInt(L / st.ColumnSpacing));
            float bay = L / n;
            bool round = st.RoundColumns && clearance > 3.0f;
            float colW = round ? 0.46f : 0.62f;
            float thick = 0.55f;
            float span = bay - colW;
            if (span < 0.6f) { Rect(st.Wall, f, 0, g, L, under, 0); return; }
            var a = st.ArcadeArch;
            float rise = Rise(a, span);
            float spring = under - 0.35f - rise;
            bool lintel = spring - g < 2.3f;
            if (lintel) { a = Arch.None; rise = 0; spring = under - 0.45f; }
            var row = new Row { Yb = spring, Yt = spring + rise, Ops = new List<Opening>(n) };
            for (int i = 0; i < n; i++)
            {
                float c = (i + 0.5f) * bay;
                row.Ops.Add(new Opening { U0 = c - span / 2, U1 = c + span / 2, Yb = spring, Ys = spring, Yt = spring + rise, Arch = a, Kind = OpeningKind.Window });
            }
            // Spandrel wall on both faces plus the arch soffits.
            float wallTop = under;
            WallWithRowsBand(f, spring, wallTop, row, 0, false);
            WallWithRowsBand(f, spring, wallTop, row, -thick, true);
            foreach (var o in row.Ops)
            {
                if (a != Arch.None) ArchSoffit(f, o, 0, -thick);
                else s.Quad(Slot.Ceiling, f.P(o.U0, spring, -thick), f.P(o.U1, spring, -thick), f.P(o.U1, spring, 0), f.P(o.U0, spring, 0), Vector3.down,
                    new Vector2(o.U0, 0), new Vector2(o.U1, 0), new Vector2(o.U1, thick), new Vector2(o.U0, thick));
            }
            // Supports.
            for (int i = 0; i <= n; i++)
            {
                float uc = Mathf.Clamp(i * bay, colW / 2, L - colW / 2);
                var foot = f.P(uc, g, -thick / 2);
                if (round)
                {
                    s.Cylinder(Slot.Stone, foot + Vector3.up * 0.28f, 0.2f, spring - g - 0.62f, 12, 0.92f);
                    s.Box(Slot.Stone, foot + Vector3.up * 0.14f, f.E * 0.27f, Vector3.up * 0.14f, f.N * 0.27f);
                    s.Box(Slot.Stone, f.P(uc, spring - 0.17f, -thick / 2), f.E * 0.3f, Vector3.up * 0.17f, f.N * 0.3f, true);
                }
                else
                {
                    s.Box(st.Wall, f.P(uc, (g + spring) / 2, -thick / 2), f.E * (colW / 2), Vector3.up * ((spring - g) / 2), f.N * (thick / 2));
                    s.Box(Slot.Stone, f.P(uc, spring - 0.1f, -thick / 2), f.E * (colW / 2 + 0.05f), Vector3.up * 0.1f, f.N * (thick / 2 + 0.05f), true);
                    s.Box(Slot.Stone, f.P(uc, g + 0.2f, -thick / 2), f.E * (colW / 2 + 0.03f), Vector3.up * 0.2f, f.N * (thick / 2 + 0.03f));
                }
                Columns.Add(new Bounds(foot + Vector3.up * ((spring - g) / 2), new Vector3(colW + 0.1f, spring - g, colW + 0.1f)));
            }
        }

        internal void WallWithRowsBand(in Frame f, float y0, float y1, Row row, float d, bool back)
        {
            float u = 0;
            foreach (var o in row.Ops)
            {
                Piece(st.Wall, f, u, row.Yb, o.U0, row.Yt, d, back);
                if (o.Arch != Arch.None) Spandrel(st.Wall, f, o, d, back);
                u = o.U1;
            }
            Piece(st.Wall, f, u, row.Yb, f.L, row.Yt, d, back);
            Piece(st.Wall, f, 0, row.Yt, f.L, y1, d, back);
        }

        internal void ArchSoffit(in Frame f, Opening o, float d0, float d1)
        {
            ArchPoints(arch, o, 10);
            float acc = 0;
            for (int k = 0; k < arch.Count - 1; k++)
            {
                var a = arch[k]; var b = arch[k + 1];
                var t = b - a;
                float len = t.magnitude;
                var nl = new Vector2(t.y, -t.x) / Mathf.Max(len, 1e-5f);
                var n = f.E * nl.x + Vector3.up * nl.y;
                s.Quad(st.Wall, f.P(a.x, a.y, d0), f.P(b.x, b.y, d0), f.P(b.x, b.y, d1), f.P(a.x, a.y, d1), n,
                    new Vector2(acc, d0), new Vector2(acc + len, d0), new Vector2(acc + len, d1), new Vector2(acc, d1));
                acc += len;
            }
        }

        void Ceiling(PlanUnit unit)
        {
            var c = unit.ceiling;
            var pts = new Vector3[c.Length / 2];
            var uvs = new Vector2[pts.Length];
            for (int k = 0; k < pts.Length; k++)
            {
                pts[k] = new Vector3(c[2 * k], unit.under, c[2 * k + 1]);
                uvs[k] = new Vector2(c[2 * k], c[2 * k + 1]);
            }
            s.Indexed(Slot.Ceiling, pts, uvs, unit.ceilingTris, -1);
        }
    }
}
