using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Padova.City;
using Padova.World;
using UnityEngine;

namespace PadovaOpenWorld.Tests
{
    /// <summary>Kerbside parking is derived from the road graph: bays must fit the measured carriageway and leave the lanes clear.</summary>
    public class ParkingTests
    {
        static CityRoads Roads() => CityRoads.Load(Path.Combine(Application.streamingAssetsPath, "City/roads.bin"));

        [Test]
        public void BaysFitTheCarriagewayAndStayClearOfJunctions()
        {
            var roads = Roads();
            Assert.That(roads, Is.Not.Null);
            var slots = new List<CityRoads.Slot>();
            int streets = 0, bays = 0;
            for (int e = 0; e < roads.Edges.Length; e++)
            {
                roads.Parking(e, out bool right, out bool left, out float shift);
                roads.Slots(e, slots);
                if (!right && !left) { Assert.That(slots, Is.Empty); Assert.That(shift, Is.EqualTo(0)); continue; }
                var edge = roads.Edges[e];
                Assert.That(edge.Class == CityRoads.Local || edge.Class == CityRoads.Main, "Only ordinary streets: " + e);
                Assert.That(edge.Bridge || edge.Roundabout, Is.False, "No parking on bridges or roundabouts: " + e);
                // What is left to drive on is at least a lane each way, or one lane on a one-way street.
                bool oneWay = edge.OneWayForward || edge.OneWayBackward;
                float half = roads.Driving(e, out float centre);
                Assert.That(half * 2, Is.GreaterThanOrEqualTo(oneWay ? 3.2f : 5.5f), "Driving strip of edge " + e);
                Assert.That(Mathf.Abs(centre), Is.LessThanOrEqualTo(CityRoads.BayWidth * .5f + .001f));
                if (slots.Count == 0) continue;
                streets++; bays += slots.Count;
                float length = 0;
                for (int k = 2; k + 1 < edge.Points.Length; k += 2) length += Vector2.Distance(new Vector2(edge.Points[k - 2], edge.Points[k - 1]), new Vector2(edge.Points[k], edge.Points[k + 1]));
                foreach (var slot in slots)
                {
                    Assert.That(Mathf.Abs(slot.Side), Is.EqualTo(edge.Width * .5f - CityRoads.BayWidth * .5f).Within(.001f), "Bay sits at the kerb");
                    Assert.That(slot.Side > 0 ? right : left, "Bay on a side that has parking");
                    Assert.That(slot.S - CityRoads.BayLength * .5f, Is.GreaterThanOrEqualTo(roads.Clear(edge.A) + 4.9f), "Clear of the junction at A");
                    Assert.That(slot.S + CityRoads.BayLength * .5f, Is.LessThanOrEqualTo(length - roads.Clear(edge.B) - 4.9f), "Clear of the junction at B");
                    // The moving strip and the bay never overlap.
                    float inner = Mathf.Abs(slot.Side) - CityRoads.BayWidth * .5f, strip = Mathf.Sign(slot.Side) * centre + half;
                    Assert.That(strip, Is.LessThanOrEqualTo(inner + .001f), "Traffic keeps out of the bays on edge " + e);
                }
            }
            Assert.That(streets, Is.GreaterThan(800), "Streets with kerbside parking");
            Assert.That(bays, Is.GreaterThan(8000), "Bays in the comune");
        }

        [Test]
        public void LanesKeepACarsWidthFromParkedVehicles()
        {
            var roads = Roads();
            var net = RoadNetwork.Build(roads, (x, z) => 0);
            int checkedLanes = 0;
            for (int i = 0; i < net.Lanes.Count; i += 7)
            {
                var lane = net.Lanes[i];
                roads.Parking(lane.Edge, out bool right, out bool left, out _);
                if (!right && !left || lane.Length < 40) continue;
                var edge = roads.Edges[lane.Edge];
                // Mid-lane, away from the taper at the junctions: distance from the surveyed centreline.
                var p = net.Sample(i, lane.Length * .5f, out _);
                float best = float.MaxValue;
                for (int k = 2; k + 1 < edge.Points.Length; k += 2)
                {
                    Vector2 a = new Vector2(edge.Points[k - 2], edge.Points[k - 1]), b = new Vector2(edge.Points[k], edge.Points[k + 1]), q = new Vector2(p.x, p.z);
                    float t = Mathf.Clamp01(Vector2.Dot(q - a, b - a) / Mathf.Max((b - a).sqrMagnitude, 1e-6f));
                    best = Mathf.Min(best, Vector2.Distance(q, a + (b - a) * t));
                }
                // A car is about 1.8 m wide: its far side must stay inside the kerb line, or inside the bay line where there is one.
                Assert.That(best + .9f, Is.LessThanOrEqualTo(edge.Width * .5f + .05f), "Lane " + i + " runs outside its carriageway");
                checkedLanes++;
            }
            Assert.That(checkedLanes, Is.GreaterThan(100));
        }

        [Test]
        public void ParkedTypesAreDrivableAndVaried()
        {
            var seen = new HashSet<string>();
            for (int k = 0; k < 400; k++)
            {
                var spec = VehicleCatalog.PickParked(k / 400f);
                Assert.That(spec.Drivable && spec.ParkedShare > 0, spec.Name);
                seen.Add(spec.Name);
            }
            Assert.That(seen.Count, Is.GreaterThanOrEqualTo(5), "Cars, scooters, vans, Apes and bicycles all turn up at the kerb");
            Assert.That(seen.Contains("Pantera") || seen.Contains("Autobus") || seen.Contains("Tram"), Is.False);
        }
    }
}
