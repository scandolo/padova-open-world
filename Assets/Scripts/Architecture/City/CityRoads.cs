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
            return roads;
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
