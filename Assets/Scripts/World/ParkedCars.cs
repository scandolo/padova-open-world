using System.Collections.Generic;
using Padova.City;
using UnityEngine;

namespace Padova.World
{
    /// <summary>A vehicle standing in a kerbside bay. A hard knock makes it a physical car.</summary>
    public sealed class ParkedCar : MonoBehaviour
    {
        public ParkedCars Owner;
        public long Key;
        public VehicleSpec Spec;
        public Color Paint;

        void OnCollisionEnter(Collision collision)
        {
            if (Owner && collision.rigidbody && !collision.rigidbody.isKinematic && collision.relativeVelocity.sqrMagnitude > 9) Owner.Knock(this, collision);
        }
    }

    /// <summary>Fills the kerbside bays around the player with parked vehicles. Which bays exist comes from the road graph
    /// (<see cref="CityRoads.Slots"/>); which are occupied, by what and in which colour is seeded per bay, so a street looks the
    /// same every time. Any of them can be taken with E; one that gets rammed becomes a loose car.</summary>
    [DefaultExecutionOrder(125)]
    public sealed class ParkedCars : MonoBehaviour
    {
        public OpenWorld Game;
        [Tooltip("Vehicles appear inside this distance (m).")] public float Radius = 150;
        [Tooltip("Share of bays that hold a vehicle on local streets and on main roads.")] public float Local = .74f, Main = .6f;
        /// <summary>Vehicles standing now, taken by the player, knocked loose, and bays found blocked by a wall or a tree.</summary>
        public int Standing, Taken, Knocked, Blocked;

        readonly Dictionary<long, ParkedCar> live = new Dictionary<long, ParkedCar>();
        readonly HashSet<long> gone = new HashSet<long>();
        readonly Dictionary<int, CityRoads.Slot[]> bays = new Dictionary<int, CityRoads.Slot[]>();
        readonly Dictionary<string, Stack<GameObject>> spare = new Dictionary<string, Stack<GameObject>>();
        readonly List<(long key, int edge, int slot, float distance)> queue = new List<(long, int, int, float)>();
        readonly List<int> lanes = new List<int>();
        readonly HashSet<int> edges = new HashSet<int>();
        readonly List<long> far = new List<long>();
        readonly List<CityRoads.Slot> scratch = new List<CityRoads.Slot>();
        readonly List<CityCar> loose = new List<CityCar>();
        float nextScan, nextSweep;
        int ownCars = -1;

        static float Unit(uint seed, int salt) => ((seed + (uint)salt * 0x9E3779B9u) * 2654435761u >> 8 & 0xFFFF) / 65536f;

        CityRoads.Slot[] Bays(CityRoads roads, int edge)
        {
            if (!bays.TryGetValue(edge, out var list)) { roads.Slots(edge, scratch); bays[edge] = list = scratch.ToArray(); }
            return list;
        }

        /// <summary>Where a bay's vehicle stands: position on the ground plane and the way it faces.</summary>
        static Vector3 Place(CityRoads roads, int edge, in CityRoads.Slot slot, VehicleSpec spec, out Vector3 heading)
        {
            var p = roads.At(edge, slot.S, out var dir);
            var right = new Vector2(dir.y, -dir.x);
            float side = slot.Side, sign = Mathf.Sign(side);
            // Two-wheelers stand close to the kerb, nose in at an angle.
            if (spec.Wheels == 2) side += sign * (CityRoads.BayWidth * .5f - .55f);
            p += right * side;
            var along = slot.Forward ? dir : -dir;
            heading = new Vector3(along.x, 0, along.y);
            if (spec.Wheels == 2) heading = Quaternion.Euler(0, (slot.Forward ? 1 : -1) * sign * 55, 0) * heading;
            return new Vector3(p.x, 0, p.y);
        }

        void Update()
        {
            if (!Game || !Game.City || Game.Halted) return;
            var roads = Game.City.Roads;
            var net = Game.Traffic ? Game.Traffic.Network : null;
            if (roads == null || net == null) return;
            if (ownCars < 0) ownCars = Game.Cars.Length;
            var focus = Game.Position;
            if (Time.time >= nextScan)
            {
                nextScan = Time.time + .4f;
                Scan(roads, net, focus);
            }
            // A few new vehicles a frame, nearest first.
            for (int n = 0; n < 3 && queue.Count > 0; n++)
            {
                var next = queue[queue.Count - 1];
                queue.RemoveAt(queue.Count - 1);
                if (!live.ContainsKey(next.key) && !gone.Contains(next.key)) Spawn(roads, next.key, next.edge, next.slot);
            }
            if (Time.time >= nextSweep) { nextSweep = Time.time + 2; Sweep(focus); }
            Standing = live.Count;
        }

        void Scan(CityRoads roads, RoadNetwork net, Vector3 focus)
        {
            net.LanesNear(focus, Radius, lanes);
            edges.Clear();
            queue.Clear();
            float reach = Radius * Radius;
            var core = Game.City.Streamer && Game.City.Streamer.Pack != null ? Game.City.Streamer.Pack.Index.coreExtent : null;
            foreach (int lane in lanes)
            {
                int edge = net.Lanes[lane].Edge;
                if (edge < 0 || !edges.Add(edge)) continue;
                var list = Bays(roads, edge);
                float share = roads.Edges[edge].Class == CityRoads.Main ? Main : Local;
                for (int i = 0; i < list.Length; i++)
                {
                    long key = ((long)edge << 12) | (uint)i;
                    if (live.ContainsKey(key) || gone.Contains(key) || Unit(list[i].Seed, 1) > share) continue;
                    var p = roads.At(edge, list[i].S, out _);
                    float d = (p.x - focus.x) * (p.x - focus.x) + (p.y - focus.z) * (p.y - focus.z);
                    if (d > reach) continue;
                    if (core != null && core.Length == 4 && p.x > core[0] && p.x < core[2] && p.y > core[1] && p.y < core[3]) continue;
                    queue.Add((key, edge, i, d));
                }
            }
            queue.Sort((a, b) => b.distance.CompareTo(a.distance));
            // Vehicles left well behind go back to the spares.
            far.Clear();
            float leave = (Radius + 45) * (Radius + 45);
            foreach (var pair in live)
                if (!pair.Value || (pair.Value.transform.position - focus).sqrMagnitude > leave) far.Add(pair.Key);
            foreach (long key in far)
            {
                var car = live[key];
                live.Remove(key);
                if (!car) continue;
                car.gameObject.SetActive(false);
                if (!spare.TryGetValue(car.Spec.Name, out var stack)) spare[car.Spec.Name] = stack = new Stack<GameObject>();
                stack.Push(car.gameObject);
            }
        }

        void Spawn(CityRoads roads, long key, int edge, int index)
        {
            var slot = Bays(roads, edge)[index];
            var spec = VehicleCatalog.PickParked(Unit(slot.Seed, 2));
            var p = Place(roads, edge, slot, spec, out var heading);
            p += heading * ((Unit(slot.Seed, 3) - .5f) * Mathf.Max(0, CityRoads.BayLength - spec.Length - .5f));
            p.y = Game.SurfaceAt(p);
            var rotation = Quaternion.LookRotation(heading) * Quaternion.Euler(0, (Unit(slot.Seed, 4) - .5f) * 5, 0);
            // A bay that a wall, a tree or a lamp stands in stays empty.
            if (Physics.CheckBox(p + Vector3.up * (spec.Height * .5f + .25f), new Vector3(spec.Width * .5f, spec.Height * .5f - .1f, spec.Length * .5f), rotation, 1 << 9, QueryTriggerInteraction.Ignore))
            {
                gone.Add(key); Blocked++;
                return;
            }
            var paint = VehicleCatalog.Paint(spec, (int)(slot.Seed >> 4 & 0xFFFF));
            GameObject go;
            if (spare.TryGetValue(spec.Name, out var stack) && stack.Count > 0)
            {
                go = stack.Pop();
                if (!go) return;
                go.SetActive(true);
                VehicleFactory.Tint(go, paint);
            }
            else
            {
                go = VehicleFactory.Model(spec, paint, out _, out _);
                go.transform.SetParent(transform, false);
                go.layer = 11;
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0, spec.Height * .52f, 0);
                box.size = new Vector3(spec.Width, spec.Height * .72f, spec.Length);
                go.AddComponent<ParkedCar>();
            }
            go.name = "Parked " + spec.Name;
            go.transform.SetPositionAndRotation(p, rotation);
            var parked = go.GetComponent<ParkedCar>();
            parked.Owner = this; parked.Key = key; parked.Spec = spec; parked.Paint = paint;
            live[key] = parked;
        }

        /// <summary>The parked vehicle closest to a point, within <paramref name="radius"/> metres.</summary>
        public ParkedCar NearestTo(Vector3 p, float radius)
        {
            ParkedCar best = null;
            float bestD = radius * radius;
            foreach (var car in live.Values)
            {
                if (!car) continue;
                float d = (car.transform.position - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = car; }
            }
            return best;
        }

        CityCar Release(ParkedCar parked)
        {
            gone.Add(parked.Key);
            live.Remove(parked.Key);
            var t = parked.transform;
            var car = VehicleFactory.Drivable(parked.Spec, parked.Paint, t.position + Vector3.up * .05f, t.rotation, Game.transform);
            car.name = parked.Spec.Label;
            Destroy(parked.gameObject);
            return car;
        }

        /// <summary>The player takes a parked vehicle: it becomes a car that drives.</summary>
        public CityCar Take(ParkedCar parked)
        {
            if (!parked) return null;
            Taken++;
            return Release(parked);
        }

        /// <summary>Rammed: the vehicle comes off its bay and is shoved along by the blow.</summary>
        public void Knock(ParkedCar parked, Collision collision)
        {
            if (!parked || !live.ContainsKey(parked.Key)) return;
            var push = parked.transform.position - collision.rigidbody.position;
            push.y = 0;
            float share = Mathf.Clamp01(collision.rigidbody.mass / (collision.rigidbody.mass + parked.Spec.Mass));
            var car = Release(parked);
            car.Body.linearVelocity = push.normalized * (collision.relativeVelocity.magnitude * share * .9f);
            loose.Add(car);
            Knocked++;
        }

        /// <summary>Cars the player took or knocked loose and then left far behind are cleared away, so they never pile up.</summary>
        void Sweep(Vector3 focus)
        {
            for (int i = loose.Count - 1; i >= 0; i--)
            {
                var car = loose[i];
                if (car && car != Game.Driving && (car.transform.position - focus).sqrMagnitude < 260 * 260) continue;
                if (car && car != Game.Driving) Destroy(car.gameObject);
                loose.RemoveAt(i);
            }
            var cars = Game.Cars;
            for (int i = cars.Length - 1; i >= ownCars && i >= 0; i--)
            {
                var car = cars[i];
                if (car && (car == Game.Driving || (car.transform.position - focus).sqrMagnitude < 320 * 320)) continue;
                Game.RemoveCar(car);
                if (car) Destroy(car.gameObject);
            }
        }
    }
}
