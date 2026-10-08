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
        [Test]public void ExpansionRetainsUniqueSurveyIdentitiesAndSeparateCore()
        {
            Assert.That(game,Is.Not.Null);var world=game.City.Data;var core=game.City.City.Data;
            Assert.That(world.units.Length,Is.GreaterThan(19000));Assert.That(world.units.Select(u=>u.id).Distinct().Count(),Is.EqualTo(world.units.Length));
            Assert.That(world.units.Select(u=>u.id).Intersect(core.units.Select(u=>u.id)),Is.Empty);
            foreach(var u in world.units)Assert.That(u.surveyBase+u.surveyHeight,Is.EqualTo(u.top).Within(.002f),"Surveyed eave/height identity: "+u.id);
            foreach(var u in world.units.Where(u=>u.suspended))Assert.That(u.under-u.surveyBase,Is.EqualTo(u.surveyClearance).Within(.002f),u.id);
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
            float highest=game.City.Data.units.Max(u=>u.roof.Where((value,index)=>index%5==1).Max());
            Assert.That(game.Plane.MinimumAltitude,Is.GreaterThan(highest+15),"Flight assist must clear every imported roof");
        }
    }
}
