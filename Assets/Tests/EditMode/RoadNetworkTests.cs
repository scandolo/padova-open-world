using NUnit.Framework;
using Padova.Architecture;
using Padova.World;
using UnityEngine;

namespace PadovaOpenWorld.Tests
{
    public class RoadNetworkTests
    {
        static WorldRoad Road(params float[] points)=>new WorldRoad{name="TEST ROAD",points=points};
        [Test]public void LanesKeepRightAndFollowGroundWithoutMovingSurveyPoints()
        {
            var road=Road(0,0,0,40);var original=(float[])road.points.Clone();
            var net=RoadNetwork.Build(new[]{road},(x,z)=>z*.1f);
            Assert.That(net.Lanes.Count,Is.EqualTo(2));
            var north=net.Sample(0,20,out var n);var south=net.Sample(1,20,out var s);
            Assert.That(north.x,Is.EqualTo(1.6f).Within(.01f));Assert.That(south.x,Is.EqualTo(-1.6f).Within(.01f));
            Assert.That(n.z,Is.GreaterThan(0));Assert.That(s.z,Is.LessThan(0));
            foreach(var lane in net.Lanes)foreach(var p in lane.Points)Assert.That(p.y,Is.EqualTo(p.z*.1f).Within(.001f));
            Assert.That(road.points,Is.EqualTo(original));
        }
        [Test]public void ShortestRouteAndCheckpointsStayConnected()
        {
            var net=RoadNetwork.Build(new[]{Road(0,0,0,40),Road(0,40,40,40),Road(0,0,100,0,40,40),Road(300,0,350,0)},(x,z)=>0);
            var route=net.Route(net.NearestNode(Vector3.zero),net.NearestNode(new Vector3(40,0,40)));
            Assert.That(route.Count,Is.EqualTo(2));Assert.That(net.Length(route),Is.InRange(79,84));
            Assert.That(net.Lanes[route[0]].To,Is.EqualTo(net.Lanes[route[1]].From));
            var points=net.Along(route,25);Assert.That(points.Count,Is.GreaterThanOrEqualTo(3));
            Assert.That(Vector3.Distance(points[points.Count-1],new Vector3(40,0,40)),Is.LessThan(.01f));
            Assert.That(net.Route(0,net.NearestNode(new Vector3(300,0,0))),Is.Empty);
        }
        [Test]public void TrafficContinuesThroughJunctionAndTurnsBackAtDeadEnd()
        {
            var net=RoadNetwork.Build(new[]{Road(0,0,0,40),Road(0,40,40,40)},(x,z)=>0);var random=new System.Random(1);
            Assert.That(net.Next(0,random),Is.EqualTo(2));Assert.That(net.Next(2,random),Is.EqualTo(3));
        }
    }
}
