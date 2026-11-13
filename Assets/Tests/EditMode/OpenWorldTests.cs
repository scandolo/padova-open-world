using System.Linq;
using NUnit.Framework;
using Padova.Architecture;
using Padova.World;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

namespace PadovaOpenWorld.Tests
{
    public class OpenWorldTests
    {
        OpenWorld game;
        [OneTimeSetUp]public void Load(){EditorSceneManager.OpenScene("Assets/Scenes/PadovaPlayable.unity");game=Object.FindFirstObjectByType<OpenWorld>();Physics.SyncTransforms();}
        [Test]public void StreamedCityKeepsSurveyValuesAndLeavesTheCoreToItsOwnPlan()
        {
            Assert.That(game,Is.Not.Null);var pack=game.City.Streamer.Pack;var core=game.City.City.Data;
            Assert.That(pack,Is.Not.Null,"City packs missing: run Tools/city/compile_city.py");
            var coreIds=new System.Collections.Generic.HashSet<string>(core.units.Select(u=>u.id));var seen=new System.Collections.Generic.HashSet<string>();int units=0;
            for(int tx=-3;tx<=2;tx++)for(int tz=-3;tz<=2;tz++)
            {
                var tile=pack.Load(tx,tz);if(tile==null)continue;
                foreach(var u in tile.Units)
                {
                    units++;string id=u.SourceId;
                    Assert.That(seen.Add(id),Is.True,"Duplicate survey unit "+id);Assert.That(coreIds.Contains(id),Is.False,"Core unit also streamed: "+id);
                    Assert.That(u.SurveyTop-u.Base,Is.InRange(.5f,150f),"Surveyed height: "+id);
                    Assert.That(u.Top,Is.GreaterThanOrEqualTo(u.SurveyTop-.001f),id);Assert.That(u.Top-u.SurveyTop,Is.LessThan(.36f),"Roof merge tolerance exceeded: "+id);
                    if(u.Suspended)Assert.That(u.Under-u.Base,Is.InRange(2f,u.SurveyTop-u.Base-1.5f),"Surveyed portico clearance: "+id);
                    Assert.That(u.Outline.Length,Is.GreaterThanOrEqualTo(6),id);
                }
            }
            Assert.That(units,Is.GreaterThan(8000));Assert.That(game.City.Buildings,Is.GreaterThan(10000));
            Assert.That(game.City.City.IncludeHorizon,Is.False);Assert.That(game.Player.WorldHalfSize.x,Is.GreaterThan(1000));
        }
        [Test]public void CarsHaveClearMappedGroundAndWorkingSuspension()
        {
            foreach(var car in game.Cars)
            {
                Assert.That(car.Wheels.Length,Is.EqualTo(4));Assert.That(car.Wheels.All(w=>w.radius>.25f),Is.True);
                Assert.That(Physics.CheckBox(car.transform.position+Vector3.up*.85f,new Vector3(.9f,.5f,2.1f),car.transform.rotation,1<<9),Is.False,"Car intersects architecture: "+car.name);
                Assert.That(Physics.Raycast(car.transform.position+Vector3.up*2,Vector3.down,5,1<<8),Is.True,"Car has no ground");
            }
        }
        [Test]public void PedestrianDestinationsHaveConnectedWalkableRoutes()
        {
            Assert.That(game.Crowd.Character,Is.Not.Null);Assert.That(game.Crowd.Navigation,Is.Not.Null);
            var nav=NavMesh.AddNavMeshData(game.Crowd.Navigation);
            try
            {
                int found=0;foreach(var p in game.Crowd.Destinations)if(NavMesh.SamplePosition(p,out _,8,NavMesh.AllAreas))found++;
                Assert.That(found,Is.GreaterThanOrEqualTo(10));
                Assert.That(NavMesh.SamplePosition(game.Crowd.Destinations[0],out var a,8,NavMesh.AllAreas),Is.True);
                Assert.That(NavMesh.SamplePosition(game.Crowd.Destinations[1],out var b,8,NavMesh.AllAreas),Is.True);
                var path=new NavMeshPath();Assert.That(NavMesh.CalculatePath(a.position,b.position,NavMesh.AllAreas,path),Is.True);Assert.That(path.status,Is.EqualTo(NavMeshPathStatus.PathComplete));
            }
            finally{nav.Remove();}
        }
        [Test]public void MapCoordinatesStayNorthUpAcrossPanAndZoom()
        {
            game.MapCentre=new Vector2(-140,280);game.MapSpan=600;
            var position=new Vector3(52,0,-131);var point=game.WorldToMap(position);var back=game.MapToWorld(point);
            Assert.That(back.x,Is.EqualTo(position.x).Within(.001f));Assert.That(back.z,Is.EqualTo(position.z).Within(.001f));
            Assert.That(game.WorldToMap(Vector3.forward*500).y,Is.LessThan(game.WorldToMap(Vector3.zero).y));
        }
        [Test]public void ActivitiesHaveConnectedSurveyedRouteAndSceneReferences()
        {
            Assert.That(game.Traffic.Game,Is.EqualTo(game));Assert.That(game.Activities.Game,Is.EqualTo(game));
            Assert.That(game.Activities.Traffic,Is.EqualTo(game.Traffic));Assert.That(game.Activities.BeaconMaterial,Is.Not.Null);
            Assert.That(game.Crowd.Count,Is.EqualTo(64));
            var net=RoadNetwork.Build(game.City.Data.roads,game.City.Data.GroundAt);
            var route=net.Route(net.NearestNode(game.Activities.RaceStart),net.NearestNode(game.Activities.RaceFinish));
            Assert.That(net.Lanes.Count,Is.GreaterThan(1200));Assert.That(route,Is.Not.Empty);
            Assert.That(net.Length(route),Is.InRange(1500,3000));Assert.That(net.Along(route,140).Count,Is.GreaterThan(10));
        }
        [Test]public void LandmarkDetailsAndMarketAreModelledGeometry()
        {
            var city=game.City.City;var landmark=city.transform.Find("Generated city/Clock, Capitanio, Duomo and Bo");
            Assert.That(landmark,Is.Not.Null);Assert.That(landmark.GetComponent<MeshFilter>().sharedMesh.vertexCount,Is.GreaterThan(10000));
            Assert.That(landmark.GetComponent<Renderer>().bounds.max.y,Is.LessThanOrEqualTo(62.1f),"Landmark tops must stay within surveyed maximum");
            Assert.That(city.Materials[(int)Slot.ClockBlue].mainTexture,Is.Null,"Dial is geometry, not a projected picture");
            var props=Object.FindFirstObjectByType<Streetscape>();Assert.That(props.Stalls,Is.EqualTo(12));Assert.That(props.Lamps,Is.GreaterThan(5));
            float highest=0;var pack=game.City.Streamer.Pack;
            for(int tx=-6;tx<=5;tx++)for(int tz=-6;tz<=5;tz++){var tile=pack.Load(tx,tz);if(tile!=null)foreach(var u in tile.Units)highest=Mathf.Max(highest,u.Top);}
            Assert.That(highest,Is.GreaterThan(30));Assert.That(game.Plane.MinimumAltitude,Is.GreaterThan(highest+15),"Flight assist must clear every surveyed eave");
        }
    }
}
