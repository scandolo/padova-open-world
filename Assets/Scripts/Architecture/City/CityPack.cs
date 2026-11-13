using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Padova.City
{
    /// <summary>Building classes written by Tools/city/compile_city.py (CLASSES). Typology keys, not survey facts.</summary>
    public enum CityClass : byte
    {
        Townhouse, Palazzo, Condo, House, Shed, Church, Tower, Civic, Commercial, Minor, Rural, Station, Sport, Office
    }

    /// <summary>Ground surface kinds written by the compiler (KINDS), in the same order.</summary>
    public enum GroundKind : byte
    {
        Base, Asphalt, Sidewalk, Paving, Cycle, Tram, Rail, Parking, Roadside, Garden, Lawn, Grove, Flowerbed, Hedge,
        Courtyard, Wood, Field, Meadow, Water, Pitch, Clay, Pool, Portico, Bridge, Island, Count
    }

    public enum UnitRoof : byte { Record, Flat, Dome, Spire }

    public sealed class TileUnit
    {
        public uint Id, Building;
        public byte Part;
        public CityClass Class;
        public UnitRoof Roof;
        public bool Suspended, Minor;
        /// <summary>Terrain level, wall top (shared roofs may raise it by at most 0.35 m), surveyed eave, surveyed base, portico underside.</summary>
        public float Ground, Top, SurveyTop, Base, Under;
        public int[] RingSizes;
        /// <summary>x,z pairs in local metres, rings concatenated (outer CCW, holes CW).</summary>
        public float[] Outline;
        /// <summary>Eight values per exposed wall piece: x0, z0, x1, z1, y0, y1, kind, flags (see SegmentKind / SegmentFlags).</summary>
        public float[] Segs;
        /// <summary>Triangulated footprint (indices into Outline) for flat roofs and portico ceilings; may be null.</summary>
        public ushort[] Cap;
        public float Cx, Cz, Area;
        public uint Seed => Building != uint.MaxValue ? Building * 2654435761u + 17 : Id * 40503u + Part;
        public string SourceId => (Id & 0x40000000) != 0 ? "MN_UVOL:" + (Id & 0x3FFFFFFF) + (Part > 0 ? "#" + Part : "") : "UN_VOL:" + Id + (Part > 0 ? "#" + Part : "");
    }

    public sealed class TileRoof
    {
        public CityClass Class;
        public uint Building;
        public float Top, Slope;
        /// <summary>x, z, t per node; roof height is Top + t * Slope.</summary>
        public float[] Nodes;
        public ushort[][] Faces;
        /// <summary>Bit 0: the face's first edge is an exposed eave (overhang). Bit 1: vertical gable wall.</summary>
        public byte[] FaceFlags;
    }

    public sealed class TilePatch { public GroundKind Kind; public float[] V; public ushort[] T; }
    public struct TileTree { public float X, Z, Height; public byte Kind; }
    public sealed class TileWall { public byte Kind; public float Height; public float[] Points; }

    public sealed class TileData
    {
        public int Tx, Tz;
        public TileUnit[] Units = Array.Empty<TileUnit>();
        public TileRoof[] Roofs = Array.Empty<TileRoof>();
        public TilePatch[] Patches = Array.Empty<TilePatch>();
        /// <summary>Six values per curb or bank face: x0, z0, x1, z1, low level, high level (relative to terrain).</summary>
        public float[] Curbs = Array.Empty<float>();
        public TileTree[] Trees = Array.Empty<TileTree>();
        public TileWall[] Walls = Array.Empty<TileWall>();
    }

    [Serializable]
    public sealed class CityIndex
    {
        public int version, regionTiles;
        public float tile;
        public float[] extent, plane, coreExtent, levels;
        public string[] classes, kinds;
        public string attribution;
        public CityTotals totals;
    }

    [Serializable]
    public sealed class CityTotals { public int units, roofs, patches, trees, walls, segs; }

    /// <summary>Ground interpolated from surveyed base elevations, blended into the core district's fitted plane.</summary>
    public sealed class CityTerrain
    {
        public float X0, Z0, Step;
        public int Nx, Nz;
        public float[] Grid;
        public float[] Plane, Core;

        public float At(float x, float z)
        {
            float original = Plane[0] * x + Plane[1] * z + Plane[2];
            if (Grid == null) return original;
            float fx = Mathf.Clamp((x - X0) / Step, 0, Nx - 1.001f), fz = Mathf.Clamp((z - Z0) / Step, 0, Nz - 1.001f);
            int i = (int)fx, j = (int)fz;
            float a = fx - i, b = fz - j;
            float h = Mathf.Lerp(Mathf.Lerp(Grid[j * Nx + i], Grid[j * Nx + i + 1], a), Mathf.Lerp(Grid[(j + 1) * Nx + i], Grid[(j + 1) * Nx + i + 1], a), b);
            float dx = Mathf.Max(Core[0] - x, 0, x - Core[2]), dz = Mathf.Max(Core[1] - z, 0, z - Core[3]);
            float blend = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz) / 120);
            blend = blend * blend * (3 - 2 * blend);
            return Mathf.Lerp(original, h, blend);
        }
    }

    /// <summary>Reads the compiled city packs. Thread-safe: tiles are parsed on worker threads.</summary>
    public sealed class CityPack
    {
        public readonly string Folder;
        public readonly CityIndex Index;
        public readonly CityTerrain Terrain;
        public float Tile => Index.tile;
        readonly Dictionary<long, byte[]> regions = new Dictionary<long, byte[]>();
        readonly LinkedList<long> recent = new LinkedList<long>();
        readonly object gate = new object();
        const int MaxRegions = 24;

        public static string DefaultFolder => Path.Combine(Application.streamingAssetsPath, "City");

        public CityPack(string folder)
        {
            Folder = folder;
            Index = JsonUtility.FromJson<CityIndex>(File.ReadAllText(Path.Combine(folder, "city.json")));
            var t = File.ReadAllBytes(Path.Combine(folder, "terrain.bin"));
            Terrain = new CityTerrain
            {
                X0 = BitConverter.ToSingle(t, 4), Z0 = BitConverter.ToSingle(t, 8), Step = BitConverter.ToSingle(t, 12),
                Nx = (int)BitConverter.ToUInt32(t, 20), Nz = (int)BitConverter.ToUInt32(t, 24), Plane = Index.plane, Core = Index.coreExtent
            };
            Terrain.Grid = new float[Terrain.Nx * Terrain.Nz];
            Buffer.BlockCopy(t, 28, Terrain.Grid, 0, Terrain.Grid.Length * 4);
        }

        public static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
        static int Floor(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);

        byte[] Region(int rx, int rz)
        {
            long key = Key(rx, rz);
            lock (gate)
            {
                if (regions.TryGetValue(key, out var cached)) { recent.Remove(key); recent.AddFirst(key); return cached; }
            }
            string path = Path.Combine(Folder, "r_" + rx + "_" + rz + ".bin");
            var data = File.Exists(path) ? File.ReadAllBytes(path) : null;
            lock (gate)
            {
                regions[key] = data;
                recent.AddFirst(key);
                while (recent.Count > MaxRegions) { regions.Remove(recent.Last.Value); recent.RemoveLast(); }
            }
            return data;
        }

        /// <summary>Parsed tile, or null where the survey has nothing.</summary>
        public TileData Load(int tx, int tz)
        {
            int n = Index.regionTiles, rx = Floor(tx, n), rz = Floor(tz, n);
            var region = Region(rx, rz);
            if (region == null) return null;
            int slot = (tz - rz * n) * n + (tx - rx * n);
            int offset = (int)BitConverter.ToUInt32(region, 6 + slot * 8), length = (int)BitConverter.ToUInt32(region, 10 + slot * 8);
            if (offset == 0 || length == 0) return null;
            return Parse(region, offset);
        }

        static TileData Parse(byte[] b, int p)
        {
            var tile = new TileData();
            p += 4;
            tile.Tx = BitConverter.ToInt16(b, p); tile.Tz = BitConverter.ToInt16(b, p + 2); p += 4;
            int count = BitConverter.ToUInt16(b, p); p += 2;
            tile.Units = new TileUnit[count];
            for (int i = 0; i < count; i++)
            {
                var u = new TileUnit { Id = BitConverter.ToUInt32(b, p), Part = b[p + 4], Class = (CityClass)b[p + 5], Roof = (UnitRoof)b[p + 7], Building = BitConverter.ToUInt32(b, p + 8) };
                byte flags = b[p + 6];
                u.Suspended = (flags & 1) != 0; u.Minor = (flags & 2) != 0;
                u.Ground = BitConverter.ToSingle(b, p + 12); u.Top = BitConverter.ToSingle(b, p + 16); u.SurveyTop = BitConverter.ToSingle(b, p + 20);
                u.Base = BitConverter.ToSingle(b, p + 24); u.Under = BitConverter.ToSingle(b, p + 28);
                p += 32;
                int rings = b[p++];
                u.RingSizes = new int[rings];
                int vertices = 0;
                for (int r = 0; r < rings; r++) { u.RingSizes[r] = BitConverter.ToUInt16(b, p); vertices += u.RingSizes[r]; p += 2; }
                u.Outline = Floats(b, ref p, vertices * 2);
                if ((flags & 4) != 0) { u.Segs = SimpleSegments(u, b, p); p += vertices; }
                else
                {
                    int segs = BitConverter.ToUInt16(b, p); p += 2;
                    u.Segs = new float[segs * 8];
                    for (int s = 0; s < segs; s++)
                    {
                        Buffer.BlockCopy(b, p, u.Segs, s * 32, 24);
                        u.Segs[s * 8 + 6] = b[p + 24]; u.Segs[s * 8 + 7] = b[p + 25];
                        p += 26;
                    }
                }
                if ((flags & 8) != 0) { int n = BitConverter.ToUInt16(b, p); p += 2; u.Cap = Shorts(b, ref p, n); }
                Centre(u);
                tile.Units[i] = u;
            }
            count = BitConverter.ToUInt16(b, p); p += 2;
            tile.Roofs = new TileRoof[count];
            for (int i = 0; i < count; i++)
            {
                var r = new TileRoof { Class = (CityClass)b[p], Building = BitConverter.ToUInt32(b, p + 1), Top = BitConverter.ToSingle(b, p + 5), Slope = BitConverter.ToSingle(b, p + 9) };
                int nodes = BitConverter.ToUInt16(b, p + 13), faces = BitConverter.ToUInt16(b, p + 15);
                p += 17;
                r.Nodes = Floats(b, ref p, nodes * 3);
                r.Faces = new ushort[faces][]; r.FaceFlags = new byte[faces];
                for (int f = 0; f < faces; f++) { int n = b[p]; r.FaceFlags[f] = b[p + 1]; p += 2; r.Faces[f] = Shorts(b, ref p, n); }
                tile.Roofs[i] = r;
            }
            count = BitConverter.ToUInt16(b, p); p += 2;
            tile.Patches = new TilePatch[count];
            for (int i = 0; i < count; i++)
            {
                var patch = new TilePatch { Kind = (GroundKind)b[p] };
                int vertices = BitConverter.ToUInt16(b, p + 1), indices = (int)BitConverter.ToUInt32(b, p + 3);
                p += 7;
                patch.V = Floats(b, ref p, vertices * 2);
                patch.T = Shorts(b, ref p, indices);
                tile.Patches[i] = patch;
            }
            count = (int)BitConverter.ToUInt32(b, p); p += 4;
            tile.Curbs = Floats(b, ref p, count);
            count = BitConverter.ToUInt16(b, p); p += 2;
            tile.Trees = new TileTree[count];
            for (int i = 0; i < count; i++, p += 10)
                tile.Trees[i] = new TileTree { X = BitConverter.ToSingle(b, p), Z = BitConverter.ToSingle(b, p + 4), Height = b[p + 8] * .1f, Kind = b[p + 9] };
            count = BitConverter.ToUInt16(b, p); p += 2;
            tile.Walls = new TileWall[count];
            for (int i = 0; i < count; i++)
            {
                var w = new TileWall { Kind = b[p], Height = BitConverter.ToSingle(b, p + 1) };
                int points = BitConverter.ToUInt16(b, p + 5);
                p += 7;
                w.Points = Floats(b, ref p, points * 2);
                tile.Walls[i] = w;
            }
            return tile;
        }

        /// <summary>Free-standing units store one flag byte per outline edge; every edge is one street wall from ground to eave.</summary>
        static float[] SimpleSegments(TileUnit u, byte[] b, int p)
        {
            var segs = new List<float>(u.Outline.Length * 4);
            int start = 0;
            float y0 = Mathf.Min(u.Ground, u.Base) - .4f;
            foreach (int n in u.RingSizes)
            {
                for (int k = 0; k < n; k++)
                {
                    int a = start + k, c = start + (k + 1) % n;
                    float ax = u.Outline[2 * a], az = u.Outline[2 * a + 1], bx = u.Outline[2 * c], bz = u.Outline[2 * c + 1];
                    if ((ax - bx) * (ax - bx) + (az - bz) * (az - bz) < .0625f) continue;
                    segs.Add(ax); segs.Add(az); segs.Add(bx); segs.Add(bz); segs.Add(y0); segs.Add(u.Top); segs.Add(0); segs.Add(b[p + a]);
                }
                start += n;
            }
            return segs.ToArray();
        }

        static void Centre(TileUnit u)
        {
            int n = u.RingSizes[0];
            double a = 0, cx = 0, cz = 0;
            for (int k = 0; k < n; k++)
            {
                int j = (k + 1) % n;
                double x0 = u.Outline[2 * k], z0 = u.Outline[2 * k + 1], x1 = u.Outline[2 * j], z1 = u.Outline[2 * j + 1], cross = x0 * z1 - x1 * z0;
                a += cross; cx += (x0 + x1) * cross; cz += (z0 + z1) * cross;
            }
            if (Math.Abs(a) < 1e-6) { u.Cx = u.Outline[0]; u.Cz = u.Outline[1]; return; }
            u.Area = (float)Math.Abs(a / 2); u.Cx = (float)(cx / (3 * a)); u.Cz = (float)(cz / (3 * a));
        }

        static float[] Floats(byte[] b, ref int p, int count)
        {
            var result = new float[count];
            Buffer.BlockCopy(b, p, result, 0, count * 4);
            p += count * 4;
            return result;
        }

        static ushort[] Shorts(byte[] b, ref int p, int count)
        {
            var result = new ushort[count];
            Buffer.BlockCopy(b, p, result, 0, count * 2);
            p += count * 2;
            return result;
        }
    }
}
