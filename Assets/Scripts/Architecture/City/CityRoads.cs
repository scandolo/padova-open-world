using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Padova.City
{
    /// <summary>One surveyed street element between two junctions (Tools/city/roads.py).</summary>
    public sealed class RoadEdge
    {
        public int A, B, Name;
        /// <summary>0 alley, 1 local street, 2 main road, 3 fast road, 4 pedestrian or limited traffic.</summary>
        public byte Class;
        /// <summary>1 one-way A→B, 2 one-way B→A, 4 bridge, 8 underpass, 16 roundabout, 32 tram on the carriageway.</summary>
        public byte Flags;
        public byte Lanes, MaxSpeed;
        /// <summary>Carriageway width measured against the survey (m).</summary>
        public float Width;
        /// <summary>x,z pairs from node A to node B.</summary>
        public float[] Points;
        public bool OneWayForward => (Flags & 1) != 0;
        public bool OneWayBackward => (Flags & 2) != 0;
        public bool Bridge => (Flags & 4) != 0;
        public bool Underpass => (Flags & 8) != 0;
        public bool Roundabout => (Flags & 16) != 0;
        public bool Pedestrian => Class == 4;
    }

    /// <summary>The drivable road graph of the whole comune: surveyed centrelines with traffic attributes lent by OpenStreetMap.</summary>
    public sealed class CityRoads
    {
        public const int Alley = 0, Local = 1, Main = 2, Fast = 3, Pedestrian = 4;
        public string[] Names;
        public float[] NodeX, NodeZ;
        /// <summary>0 plain, 1 traffic signal, 2 mini-roundabout, 3 dead end.</summary>
        public byte[] NodeKind, NodeDegree;
        public RoadEdge[] Edges;
        /// <summary>Widest carriageway meeting at each node (m).</summary>
        public float[] NodeWidth;

        /// <summary>A place where one vehicle can stand at the kerb.</summary>
        public struct Slot
        {
            /// <summary>Distance along the edge from node A (m) and offset to the right of A→B from the surveyed centreline (m).</summary>
            public float S, Side;
            /// <summary>True when the vehicle faces A→B.</summary>
            public bool Forward;
            public uint Seed;
        }

        public const float BayWidth = 2.0f, BayLength = 5.6f;

        /// <summary>How much of an edge near a node is kept clear of paint, lamps and parked cars (m).</summary>
        public float Clear(int node) => NodeDegree[node] >= 3 ? Mathf.Max(5, NodeWidth[node] * .5f + 3) : 1.5f;

        /// <summary>Kerbside parking on an edge, decided from its measured width: which sides of A→B hold a row of bays, and how far
        /// the driving lanes move over (to the right of A→B, m) to make room when only one side is used. Typology, not survey.</summary>
        public void Parking(int index, out bool right, out bool left, out float shift)
        {
            right = left = false; shift = 0;
            var e = Edges[index];
            if (e.Class != Local && e.Class != Main || e.Bridge || e.Underpass || e.Roundabout || (e.Flags & 32) != 0) return;
            bool oneWay = e.OneWayForward || e.OneWayBackward;
            float moving = oneWay ? 3.3f : 5.6f, w = e.Width;
            if (e.Class == Main) moving += 1;
            uint hash = (uint)(e.A * 7919 + e.B * 104729) * 2654435761u;
            if (hash % 100 < 12) return;                                   // some streets simply have none
            if (w >= moving + 2 * BayWidth + .7f) right = left = true;
            else if (w >= moving + BayWidth)
            {
                right = (hash >> 8) % 2 == 0; left = !right;
                shift = right ? -BayWidth * .5f : BayWidth * .5f;
            }
        }

        /// <summary>Half the width traffic drives on once parking bays are taken out, and the lateral centre of that strip.</summary>
        public float Driving(int index, out float centre)
        {
            Parking(index, out bool right, out bool left, out centre);
            return (Edges[index].Width - ((right ? 1 : 0) + (left ? 1 : 0)) * BayWidth) * .5f;
        }

        /// <summary>The bays along an edge, in order from A. Positions are deterministic, so the painted bays and the parked vehicles agree.</summary>
        public void Slots(int index, System.Collections.Generic.List<Slot> result)
        {
            result.Clear();
            Parking(index, out bool right, out bool left, out _);
            if (!right && !left) return;
            var e = Edges[index];
            float length = 0;
            for (int k = 2; k + 1 < e.Points.Length; k += 2) length += Mathf.Sqrt((e.Points[k] - e.Points[k - 2]) * (e.Points[k] - e.Points[k - 2]) + (e.Points[k + 1] - e.Points[k - 1]) * (e.Points[k + 1] - e.Points[k - 1]));
            float from = Clear(e.A) + 5, to = length - Clear(e.B) - 5;
            int count = Mathf.FloorToInt((to - from) / BayLength);
            if (count < 1) return;
            bool oneWay = e.OneWayForward || e.OneWayBackward;
            float offset = e.Width * .5f - BayWidth * .5f;
            for (int k = 0; k < count; k++)
            {
                float s = from + (k + .5f) * BayLength;
                uint seed = (uint)(index * 73856093) ^ (uint)(k * 19349663);
                if (right) result.Add(new Slot { S = s, Side = offset, Forward = !e.OneWayBackward, Seed = seed });
                if (left) result.Add(new Slot { S = s, Side = -offset, Forward = oneWay && e.OneWayForward, Seed = seed ^ 0x9E3779B9u });
            }
        }

        /// <summary>Point on an edge at distance <paramref name="s"/> from A, and the direction of travel A→B there.</summary>
        public Vector2 At(int index, float s, out Vector2 direction)
        {
            var pts = Edges[index].Points;
            direction = Vector2.right;
            var a = new Vector2(pts[0], pts[1]);
            for (int k = 2; k + 1 < pts.Length; k += 2)
            {
                var b = new Vector2(pts[k], pts[k + 1]);
                float span = Vector2.Distance(a, b);
                if (span > 1e-4f) direction = (b - a) / span;
                if (s <= span || k + 3 >= pts.Length) return a + direction * Mathf.Min(s, span);
                s -= span; a = b;
            }
            return a;
        }

        public static CityRoads Load(string path)
        {
            if (!File.Exists(path)) return null;
            var b = File.ReadAllBytes(path);
            if (b.Length < 8 || b[0] != 'P' || b[1] != 'C' || b[2] != 'R' || b[3] != 'D' || BitConverter.ToUInt32(b, 4) != 1) return null;
            int p = 8;
            var roads = new CityRoads();
            int count = (int)BitConverter.ToUInt32(b, p); p += 4;
            roads.Names = new string[count];
            for (int i = 0; i < count; i++)
            {
                int n = BitConverter.ToUInt16(b, p); p += 2;
                roads.Names[i] = Encoding.UTF8.GetString(b, p, n); p += n;
            }
            count = (int)BitConverter.ToUInt32(b, p); p += 4;
            roads.NodeX = new float[count]; roads.NodeZ = new float[count]; roads.NodeKind = new byte[count]; roads.NodeDegree = new byte[count];
            for (int i = 0; i < count; i++, p += 10)
            {
                roads.NodeX[i] = BitConverter.ToSingle(b, p); roads.NodeZ[i] = BitConverter.ToSingle(b, p + 4);
                roads.NodeKind[i] = b[p + 8]; roads.NodeDegree[i] = b[p + 9];
            }
            count = (int)BitConverter.ToUInt32(b, p); p += 4;
            roads.Edges = new RoadEdge[count];
            for (int i = 0; i < count; i++)
            {
                var e = new RoadEdge
                {
                    A = (int)BitConverter.ToUInt32(b, p), B = (int)BitConverter.ToUInt32(b, p + 4), Name = BitConverter.ToUInt16(b, p + 8),
                    Class = b[p + 10], Flags = b[p + 11], Width = BitConverter.ToSingle(b, p + 12), Lanes = b[p + 16], MaxSpeed = b[p + 17]
                };
                int points = BitConverter.ToUInt16(b, p + 18);
                p += 20;
                e.Points = new float[points * 2];
                Buffer.BlockCopy(b, p, e.Points, 0, points * 8);
                p += points * 8;
                roads.Edges[i] = e;
            }
            roads.NodeWidth = new float[roads.NodeX.Length];
            foreach (var e in roads.Edges)
            {
                roads.NodeWidth[e.A] = Mathf.Max(roads.NodeWidth[e.A], e.Width);
                roads.NodeWidth[e.B] = Mathf.Max(roads.NodeWidth[e.B], e.Width);
            }
            return roads;
        }
    }

    /// <summary>House numbers of the comune with their street names (Tools/city/places.py): where a delivery can be sent.</summary>
    public sealed class CityAddresses
    {
        public string[] Streets;
        public float[] X, Z;
        public ushort[] Street, Number;
        public string[] Letters;
        public int Count => X.Length;

        public static CityAddresses Load(string path)
        {
            if (!File.Exists(path)) return null;
            var b = File.ReadAllBytes(path);
            if (b.Length < 8 || b[0] != 'P' || b[1] != 'C' || b[2] != 'A' || b[3] != 'D' || BitConverter.ToUInt32(b, 4) != 1) return null;
            int p = 8;
            var list = new CityAddresses();
            int count = (int)BitConverter.ToUInt32(b, p); p += 4;
            list.Streets = new string[count];
            for (int i = 0; i < count; i++)
            {
                int n = BitConverter.ToUInt16(b, p); p += 2;
                list.Streets[i] = Encoding.UTF8.GetString(b, p, n); p += n;
            }
            count = (int)BitConverter.ToUInt32(b, p); p += 4;
            list.X = new float[count]; list.Z = new float[count]; list.Street = new ushort[count]; list.Number = new ushort[count]; list.Letters = new string[count];
            for (int i = 0; i < count; i++)
            {
                list.X[i] = BitConverter.ToSingle(b, p); list.Z[i] = BitConverter.ToSingle(b, p + 4);
                list.Street[i] = BitConverter.ToUInt16(b, p + 8); list.Number[i] = BitConverter.ToUInt16(b, p + 10);
                int n = b[p + 12];
                list.Letters[i] = n == 0 ? "" : Encoding.ASCII.GetString(b, p + 13, n);
                p += 13 + n;
            }
            return list;
        }

        /// <summary>"Via Tiziano Aspetti 42": the street in ordinary capitals, then the number.</summary>
        public string Label(int index)
        {
            var words = Streets[Street[index]].ToLowerInvariant().Split(' ');
            for (int i = 0; i < words.Length; i++) if (words[i].Length > 0) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
            return string.Join(" ", words) + " " + Number[index] + Letters[index];
        }

        /// <summary>A house number between <paramref name="near"/> and <paramref name="far"/> metres from a point, or -1.</summary>
        public int Pick(float x, float z, float near, float far, System.Random random)
        {
            for (int tries = 0; tries < 4000; tries++)
            {
                int i = random.Next(Count);
                float dx = X[i] - x, dz = Z[i] - z, d = dx * dx + dz * dz;
                if (d >= near * near && d <= far * far && Number[i] > 0) return i;
            }
            return -1;
        }
    }

    [Serializable] public sealed class CityPlace { public string name, kind, source; public float x, z; public int rank; }
    [Serializable] public sealed class CityDistrict { public string name; public float x, z; }

    /// <summary>Named places, everyday points of interest and districts (Tools/city/places.py).</summary>
    [Serializable]
    public sealed class CityPlaces
    {
        public CityPlace[] places = new CityPlace[0], pois = new CityPlace[0];
        public CityDistrict[] districts = new CityDistrict[0];
        public string attribution;

        public static CityPlaces Load(string path) => File.Exists(path) ? JsonUtility.FromJson<CityPlaces>(File.ReadAllText(path)) : null;

        /// <summary>Name of the nearest district centre within <paramref name="reach"/> metres, or an empty string.</summary>
        public string DistrictAt(float x, float z, float reach = 1400)
        {
            string best = "";
            float bestD = reach * reach;
            foreach (var d in districts)
            {
                float dx = d.x - x, dz = d.z - z, sq = dx * dx + dz * dz;
                if (sq < bestD) { bestD = sq; best = d.name; }
            }
            return best;
        }
    }
}
