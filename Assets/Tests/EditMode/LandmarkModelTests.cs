using System.Linq;
using NUnit.Framework;
using Padova.Architecture;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PadovaOpenWorld.Tests
{
    /// <summary>Blender landmark models (Santo, Prato della Valle) stay on their survey evidence.</summary>
    public class LandmarkModelTests
    {
        WorldContext world;LandmarkManifest manifest;Transform models;Sources sources;
        // Frozen survey evidence for the hero models (Data/Landmarks/sources.json).
        [System.Serializable]class Sources{public Landmark santo;}
        [System.Serializable]class Landmark{public SourceUnit[] units;}
        [System.Serializable]class SourceUnit{public string id;public float top,@base,cx,cz;public float[] outline;}
        [OneTimeSetUp]public void Load()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/PadovaPlayable.unity");
            world=Object.FindFirstObjectByType<WorldContext>();
            manifest=JsonUtility.FromJson<LandmarkManifest>(AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Art/Landmarks/Landmarks.json").text);
            sources=JsonUtility.FromJson<Sources>(System.IO.File.ReadAllText("Data/Landmarks/sources.json"));
            models=GameObject.Find("Landmark models").transform;
            // Streamed ground and walls only exist near a viewpoint: load the tiles around the two models.
            world.RefreshVisibility(new Vector3(350,2,-850));Physics.SyncTransforms();
        }
        Transform Part(string model,string part)=>models.Find(model).GetComponentsInChildren<MeshFilter>(true).First(f=>f.name==part).transform;

        [Test]public void ReplacedSurveyUnitsAreDrawnOnlyByTheModels()
        {
            Assert.That(world.Landmarks,Is.Not.Null);
            Assert.That(manifest.replacedUnits.Length,Is.EqualTo(30));
            var replaced=manifest.replacedUnits.ToHashSet();var pack=world.Streamer.Pack;int checkedUnits=0;
            Assert.That(sources.santo.units.All(u=>replaced.Contains(u.id)),Is.True,"Frozen Santo evidence must be the replaced survey records");
            for(int tx=-1;tx<=3;tx++)for(int tz=-6;tz<=-2;tz++)
            {
                var tile=pack.Load(tx,tz);if(tile==null)continue;
                foreach(var u in tile.Units){checkedUnits++;Assert.That(replaced.Contains(u.SourceId),Is.False,"Replaced unit is also streamed: "+u.SourceId);}
            }
            Assert.That(checkedUnits,Is.GreaterThan(500));
            Assert.That(world.Data.landmarks.Select(p=>p.name),Does.Contain("Basilica di Sant'Antonio").And.Contain("Prato della Valle"));
        }

        [Test]public void EveryManifestModelIsPlacedWithHiddenCollisionAndDiscoveryPoint()
        {
            Assert.That(manifest.models,Is.Not.Empty);
            foreach(var m in manifest.models)
            {
                var root=models.Find(m.name);Assert.That(root,Is.Not.Null,"Model not placed: "+m.name);
                Assert.That(root.position,Is.EqualTo(new Vector3(m.anchor[0],m.anchor[1],m.anchor[2])),m.name);
                var collision=root.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f=>f.name.EndsWith("_Collision"));
                Assert.That(collision,Is.Not.Null,"Missing _Collision mesh: "+m.name);
                Assert.That(collision.GetComponent<MeshCollider>(),Is.Not.Null);Assert.That(collision.GetComponent<MeshRenderer>().enabled,Is.False);
                foreach(var r in root.GetComponentsInChildren<MeshRenderer>(true))
                    Assert.That(r.sharedMaterials.All(mat=>mat&&AssetDatabase.GetAssetPath(mat).EndsWith(".mat")),Is.True,"Unmapped FBX material on "+r.name);
            }
            foreach(var p in manifest.places)
                Assert.That(Physics.Raycast(new Vector3(p.x,300,p.z),Vector3.down,400,(1<<8)|(1<<9)),Is.True,"Discovery point without ground: "+p.name);
            Assert.That(manifest.replacedUnits.Distinct().Count(),Is.EqualTo(manifest.replacedUnits.Length));
        }

        [Test]public void SantoKeepsSurveyedFootprintsAndEaves()
        {
            var units=sources.santo.units;
            var collision=Part("Santo","Santo_Collision").GetComponent<MeshCollider>();
            Assert.That(collision,Is.Not.Null);Assert.That(collision.GetComponent<MeshRenderer>().enabled,Is.False);
            var b=collision.bounds;
            float minX=units.Min(u=>Enumerable.Range(0,u.outline.Length/2).Min(i=>u.outline[2*i])),maxX=units.Max(u=>Enumerable.Range(0,u.outline.Length/2).Max(i=>u.outline[2*i]));
            float minZ=units.Min(u=>Enumerable.Range(0,u.outline.Length/2).Min(i=>u.outline[2*i+1])),maxZ=units.Max(u=>Enumerable.Range(0,u.outline.Length/2).Max(i=>u.outline[2*i+1]));
            Assert.That(b.min.x,Is.EqualTo(minX).Within(.02f));Assert.That(b.max.x,Is.EqualTo(maxX).Within(.02f));
            Assert.That(b.min.z,Is.EqualTo(minZ).Within(.02f));Assert.That(b.max.z,Is.EqualTo(maxZ).Within(.02f));
            Assert.That(b.max.y,Is.EqualTo(units.Max(u=>u.top)).Within(.02f),"Campanile eave (DBT QGR) preserved");
            Assert.That(b.min.y,Is.EqualTo(units.Min(u=>u.@base)).Within(.02f));
            // Campanile spires: published total height 68 m above the surveyed base.
            var visual=Part("Santo","Santo").GetComponent<Renderer>().bounds;
            Assert.That(visual.max.y-units.First(u=>u.id=="UN_VOL:30830").@base,Is.EqualTo(68).Within(.1f));
            // Ray down onto the crossing drum lands on the model, not on empty ground.
            var drum=units.First(u=>u.id=="UN_VOL:30736");
            Assert.That(Physics.Raycast(new Vector3(drum.cx,200,drum.cz),Vector3.down,out var hit,300,1<<9),Is.True);
            Assert.That(hit.point.y,Is.EqualTo(drum.top).Within(.05f));
        }

        [Test]public void PratoCanalIsSunkAtTheSurveyedWaterLevelAndWalkable()
        {
            var water=Part("PratoDellaValle","Prato_Water").GetComponent<Renderer>();
            float fountainX=manifest.models.First(m=>m.name=="PratoDellaValle").anchor[0],fountainZ=manifest.models.First(m=>m.name=="PratoDellaValle").anchor[2];
            // A point inside the canal (centroid of a water triangle): no navigation ground above the water, only the hidden bed below it.
            var mesh=water.GetComponent<MeshFilter>().sharedMesh;var tri=mesh.triangles;var v=mesh.vertices;
            var probe=water.transform.TransformPoint((v[tri[0]]+v[tri[1]]+v[tri[2]])/3);probe.y=50;
            Assert.That(Physics.Raycast(probe,Vector3.down,out var ground,100,1<<8),Is.False,"Ground must be cut out over the canal");
            var hits=Physics.RaycastAll(probe,Vector3.down,100,1<<9);
            Assert.That(hits,Is.Not.Empty,"Hidden canal bed");
            Assert.That(hits.Min(h=>h.point.y),Is.LessThan(world.Data.GroundAt(probe.x,probe.z)-1));
            Assert.That(water.bounds.min.y,Is.LessThan(world.Data.GroundAt(probe.x,probe.z)-.5f));
            // The island and the outer walkway are walkable ground.
            Assert.That(Physics.Raycast(new Vector3(fountainX+30,50,fountainZ+20),Vector3.down,100,1<<8),Is.True);
            Assert.That(Physics.Raycast(new Vector3(fountainX-95,50,fountainZ),Vector3.down,100,1<<8),Is.True);
            Assert.That(Part("PratoDellaValle","Prato").GetComponent<MeshFilter>().sharedMesh.vertexCount,Is.GreaterThan(30000),"Statues, bridges and parapets present");
        }
    }
}
