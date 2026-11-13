using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Padova.Architecture;
using Padova.City;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PadovaOpenWorld.Tests
{
    /// <summary>The streamed city: compiled survey tiles, their meshes and loading around a viewpoint.</summary>
    public class CityEngineTests
    {
        WorldContext world; CityPack pack;
        [OneTimeSetUp] public void Load()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/PadovaPlayable.unity");
            world = Object.FindFirstObjectByType<WorldContext>();
            pack = world.Streamer.Pack;
        }

        static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        static void AssertSound(CitySink sink, string what)
        {
            Assert.That(sink.P.All(Finite), Is.True, what + ": non-finite vertex");
            Assert.That(sink.I.Count % 3, Is.EqualTo(0), what);
            Assert.That(sink.I.Count == 0 || sink.I.Max() < sink.Count, Is.True, what + ": index out of range");
            Assert.That(sink.C.Count, Is.EqualTo(sink.Count)); Assert.That(sink.F.Count, Is.EqualTo(sink.Count)); Assert.That(sink.Uv.Count, Is.EqualTo(sink.Count));
            Assert.That(sink.C.All(c => c.a < (int)Layer.Count), Is.True, what + ": unknown surface layer");
        }

        [Test] public void PackCoversTheWholeComuneWithOneTerrain()
        {
            Assert.That(pack, Is.Not.Null, "City packs missing: run Tools/city/compile_city.py");
            var e = pack.Index.extent;
            Assert.That(e[2] - e[0], Is.GreaterThan(10000), "East-west extent of the compiled survey (m)");
            Assert.That(e[3] - e[1], Is.GreaterThan(10000), "North-south extent of the compiled survey (m)");
            Assert.That(pack.Index.totals.units, Is.GreaterThan(90000));
            Assert.That(pack.Index.attribution, Does.Contain("IODL 2.0").And.Contain("OpenStreetMap"));
            // Gameplay and rendering share the same ground, and it meets the core district's fitted plane.
            foreach (var p in new[] { new Vector2(0, 0), new Vector2(900, -700), new Vector2(-3200, 2100), new Vector2(5200, -4100) })
                Assert.That(world.Data.GroundAt(p.x, p.y), Is.EqualTo(pack.Terrain.At(p.x, p.y)).Within(1e-4f));
            var core = world.City.Data;
            Assert.That(pack.Terrain.At(60, -40), Is.EqualTo(core.GroundAt(60, -40)).Within(1e-3f));
            Assert.That(Mathf.Abs(pack.Terrain.At(4000, 3000)), Is.LessThan(15), "Plain of Padova: gentle terrain");
        }

        [Test] public void TilesMeshIntoSoundGeometryInsideTheirBounds()
        {
            int meshed = 0;
            foreach (var (tx, tz) in new[] { (0, 0), (-2, 1), (3, -3), (-9, 6), (14, -11), (2, 9) })
            {
                var data = pack.Load(tx, tz);
                if (data == null) continue;
                var build = TileMesher.Shell(data, pack.Terrain, pack.Index.levels);
                AssertSound(build.Ground, "ground"); AssertSound(build.Always, "roofs"); AssertSound(build.Shell, "walls");
                Assert.That(build.Ground.Count, Is.GreaterThan(3), "Every tile has ground");
                var b = build.Ground.Bounds;
                float t = pack.Tile;
                Assert.That(b.min.x, Is.GreaterThanOrEqualTo(tx * t - .05f)); Assert.That(b.max.x, Is.LessThanOrEqualTo((tx + 1) * t + .05f));
                Assert.That(b.min.z, Is.GreaterThanOrEqualTo(tz * t - .05f)); Assert.That(b.max.z, Is.LessThanOrEqualTo((tz + 1) * t + .05f));
                Assert.That(build.ColliderP.All(Finite), Is.True);
                if (data.Units.Length > 0)
                {
                    Assert.That(build.Shell.Count, Is.GreaterThan(0)); Assert.That(build.ColliderI.Count, Is.GreaterThan(0));
                    float top = data.Units.Max(u => u.Top);
                    Assert.That(build.Shell.Bounds.max.y, Is.LessThanOrEqualTo(top + .01f), "Walls never rise above the surveyed eave");
                    TileMesher.Detail(build);
                    Assert.That(build.Detail.VertexCount, Is.GreaterThan(0));
                    Assert.That(build.Detail.Positions.All(Finite), Is.True);
                    Assert.That(build.DetailTangents.Length, Is.EqualTo(build.Detail.VertexCount));
                }
                meshed++;
            }
            Assert.That(meshed, Is.GreaterThanOrEqualTo(4));
        }

        [Test] public void RoofsStartAtTheEaveAndCoverTheirBuildings()
        {
            var data = pack.Load(-2, 1);
            Assert.That(data.Roofs.Length, Is.GreaterThan(20));
            foreach (var roof in data.Roofs)
            {
                Assert.That(roof.Slope, Is.InRange(.02f, 1.6f));
                for (int k = 2; k < roof.Nodes.Length; k += 3) Assert.That(roof.Nodes[k], Is.GreaterThanOrEqualTo(0), "Roof below its eave");
                foreach (var face in roof.Faces) Assert.That(face.All(i => i * 3 + 2 < roof.Nodes.Length), Is.True);
            }
            // Every unit is closed on top: by a roof record, a flat roof, a dome or a spire.
            Assert.That(data.Units.Where(u => u.Roof == UnitRoof.Flat).All(u => u.Cap != null && u.Cap.Length >= 3), Is.True);
        }

        [Test] public void EarClippingKeepsTheAreaOfConcaveFaces()
        {
            var poly = new List<Vector2> { new Vector2(0, 0), new Vector2(6, 0), new Vector2(6, 2), new Vector2(2, 2), new Vector2(2, 5), new Vector2(0, 5) };
            var tris = new List<int>();
            TileMesher.EarClip(poly, tris);
            float area = 0;
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector2 a = poly[tris[i]], b = poly[tris[i + 1]], c = poly[tris[i + 2]];
                area += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) / 2;
            }
            Assert.That(tris.Count, Is.EqualTo(12));
            Assert.That(area, Is.EqualTo(18f).Within(1e-4f));
            poly.Reverse();
            TileMesher.EarClip(poly, tris);
            Assert.That(tris.Count, Is.EqualTo(12), "Winding of the input must not matter");
        }

        [Test] public void StreamerLoadsGroundAndWallsAroundAViewpointAndReleasesThemFarAway()
        {
            var here = new Vector3(-820, 3, -640);
            world.RefreshVisibility(here); Physics.SyncTransforms();
            var streamer = world.Streamer;
            Assert.That(streamer.Tiles, Is.GreaterThan(20)); Assert.That(streamer.DetailTiles, Is.GreaterThan(0)); Assert.That(streamer.ColliderTiles, Is.GreaterThan(3));
            Assert.That(Physics.Raycast(here + Vector3.up * 200, Vector3.down, out var ground, 400, 1 << 8), Is.True, "Streamed ground under the viewpoint");
            Assert.That(ground.point.y, Is.EqualTo(world.Data.GroundAt(here.x, here.z)).Within(.3f));
            bool wall = false;
            for (int k = 0; k < 16 && !wall; k++) wall = Physics.Raycast(new Vector3(here.x, ground.point.y + 1.5f, here.z), Quaternion.Euler(0, k * 22.5f, 0) * Vector3.forward, 200, 1 << 9);
            Assert.That(wall, Is.True, "Streamed building colliders near the viewpoint");
            long loaded = streamer.Vertices;
            world.RefreshVisibility(new Vector3(4200, 3, 2600)); Physics.SyncTransforms();
            Assert.That(Physics.Raycast(here + Vector3.up * 200, Vector3.down, 400, 1 << 8), Is.False, "Tiles far from the viewpoint are released");
            Assert.That(streamer.Vertices, Is.LessThan(loaded * 2), "Memory follows the viewpoint instead of growing");
        }
    }
}
