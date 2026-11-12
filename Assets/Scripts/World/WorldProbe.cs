#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEngine;
using UnityEngine.AI;

namespace Padova.World
{
    public static class WorldProbe
    {
        static OpenWorld Game=>UnityEngine.Object.FindFirstObjectByType<OpenWorld>();
        static float[] XYZ(Vector3 p)=>new[]{p.x,p.y,p.z};
        [CliCommand("world_state","Read exploration mode, map, cars, flight and pedestrian navigation state.")]
        public static object Read()
        {
            var g=Game;if(!g)return new{ready=false};
            int invalid=0;foreach(var a in g.Crowd.GetComponentsInChildren<NavMeshAgent>())if(!a.isOnNavMesh)invalid++;
            return new{ready=true,mode=g.Mode,mapOpen=g.MapOpen,position=XYZ(g.Position),player=XYZ(g.Player.transform.position),frame=Time.frameCount,focused=Application.isFocused||Padova.Gameplay.GameplayProbe.FocusIgnored,
                g.HasWaypoint,waypoint=XYZ(g.Waypoint),mapCentre=new[]{g.MapCentre.x,g.MapCentre.y},mapSpan=g.MapSpan,screenWidth=Screen.width,screenHeight=Screen.height,
                traffic=g.Traffic.Active,trafficMoving=g.Traffic.Moving,trafficTravel=g.Traffic.TotalTravel,trafficRespawns=g.Traffic.Respawns,
                trafficCars=g.Traffic.Vehicles.Where(v=>v.Go.activeSelf).Select(v=>new{position=XYZ(v.Go.transform.position),v.Lane,v.S,v.Speed}).ToArray(),
                activity=g.Activities.Kind,phase=g.Activities.Phase,menu=g.Activities.MenuOpen,money=g.Activities.Money,savedMoney=PlayerPrefs.GetInt("padova.money",0),
                fares=g.Activities.Fares,clock=g.Activities.Clock,limit=g.Activities.Limit,fare=g.Activities.Fare,target=XYZ(g.Activities.Target),
                checkpoint=g.Activities.Checkpoint,checkpoints=g.Activities.Checkpoints.Count,bestRace=g.Activities.BestRace,routeLength=g.Activities.RouteLength,
                nearbyTraffic=g.NearbyTraffic!=null,notice=g.Notice,
                wanted=g.Police.Wanted,police=g.Police.Units.Count,policeNearest=g.Police.Nearest<9999?g.Police.Nearest:-1,evade=g.Police.Evade,bustTimer=g.Police.BustTimer,busts=g.Police.Busts,escapes=g.Police.Escapes,
                policeMoving=g.Police.Units.Count(u=>u.Car.Speed>2),boost=g.Driving?g.Driving.Boost:1,boosting=g.Driving&&g.Driving.Boosting,trafficHits=g.Driving?g.Driving.TrafficHits:0,
                cars=g.Cars.Length,nearby=g.Nearby?g.Nearby.name:"",carPosition=XYZ(g.Cars[0].transform.position),carSpeed=g.Driving?g.Driving.Speed:0,
                carTravel=g.Cars[0].DistanceTravelled,carHeading=g.Cars[0].transform.eulerAngles.y,planeSpeed=g.Plane.Speed,altitude=g.Plane.Altitude,planeHeading=g.Plane.transform.eulerAngles.y,
                planeTravel=g.Plane.DistanceTravelled,npcs=g.Crowd.ActiveCount,npcsMoved=g.Crowd.MovedCount,npcTravel=g.Crowd.TotalTravel,invalidNav=invalid,
                worldBuildings=g.City.Buildings,g.Discoveries,playerEnabled=g.Player.enabled,colliderEnabled=g.Player.GetComponent<CharacterController>().enabled};
        }
        static int savedMoney;static float savedBest;static bool hadMoney,hadBest,prefsSaved;
        [CliCommand("activity_fixture","Development-only setup for traffic and activity state-transition scenarios; does not complete objectives.")]
        public static object ActivityFixture([CliArg("name","save | restore | target | checkpoint | traffic | flee | clear_police",Required=true)]string name,
            [CliArg("distance","Metres before activity target")]float distance=0)
        {
            var g=Game;var a=g.Activities;
            if(name=="save")
            {
                hadMoney=PlayerPrefs.HasKey("padova.money");hadBest=PlayerPrefs.HasKey("padova.race.best");
                savedMoney=PlayerPrefs.GetInt("padova.money",0);savedBest=PlayerPrefs.GetFloat("padova.race.best",0);prefsSaved=true;
            }
            else if(name=="restore")
            {
                if(!prefsSaved)throw new InvalidOperationException("Save fixture first");a.End("");a.MenuOpen=false;
                if(hadMoney)PlayerPrefs.SetInt("padova.money",savedMoney);else PlayerPrefs.DeleteKey("padova.money");
                if(hadBest)PlayerPrefs.SetFloat("padova.race.best",savedBest);else PlayerPrefs.DeleteKey("padova.race.best");
                PlayerPrefs.Save();a.Money=savedMoney;a.BestRace=savedBest;prefsSaved=false;
            }
            else if(name=="flee")
            {
                // Move the driven car to a lane far from every patrol to exercise evasion, not driving skill.
                if(!g.Driving)throw new InvalidOperationException("Enter a car first");var net=g.Traffic.Network;Vector3 best=g.Position;float bestScore=-1;Vector3 bestTangent=Vector3.forward;
                for(int i=0;i<net.Lanes.Count;i+=7)
                {
                    var p=net.Sample(i,net.Lanes[i].Length*.5f,out var tangent);if(Mathf.Abs(p.x)>1100||Mathf.Abs(p.z)>950)continue;
                    float score=float.MaxValue;foreach(var q in g.Police.Positions)score=Mathf.Min(score,Vector3.Distance(p,q));
                    if(score>bestScore&&score<700){bestScore=score;best=p;bestTangent=tangent;}
                }
                var car=g.Driving;var pos=best+Vector3.up*.3f;car.Body.linearVelocity=Vector3.zero;car.Body.angularVelocity=Vector3.zero;
                car.Body.position=pos;car.Body.rotation=Quaternion.LookRotation(bestTangent.WithY(0));car.transform.SetPositionAndRotation(pos,car.Body.rotation);Physics.SyncTransforms();
            }
            else if(name=="clear_police"){g.Police.Clear();}
            else if(name=="traffic")
            {
                Fixture("reset");var v=g.Traffic.Vehicles.First(v=>v.Go.activeSelf);var p=v.Go.transform.position+v.Go.transform.forward*5;
                var c=g.Player.GetComponent<CharacterController>();c.enabled=false;g.Player.transform.position=p;c.enabled=true;g.Player.FollowCamera.SetTestHeading(v.Go.transform.eulerAngles.y);
            }
            else if(name=="target" || name=="checkpoint")
            {
                if(!g.Driving)throw new InvalidOperationException("Enter a car first");
                var target=name=="checkpoint"?a.Checkpoints[a.Checkpoint]:a.Target;
                var (lane,s,_)=g.Traffic.Network.Nearest(target);g.Traffic.Network.Sample(lane,s,out var tangent);
                var p=target-tangent.WithY(0).normalized*distance;
                p.y=g.City.Data.GroundAt(p.x,p.z)+.25f;
                if(Physics.Raycast(p+Vector3.up*30,Vector3.down,out var hit,60,1<<8))p.y=hit.point.y+.25f;
                var car=g.Driving;car.Body.linearVelocity=Vector3.zero;car.Body.angularVelocity=Vector3.zero;
                car.Body.position=p;car.Body.rotation=Quaternion.LookRotation(tangent.WithY(0));car.transform.SetPositionAndRotation(p,car.Body.rotation);Physics.SyncTransforms();
            }
            else throw new ArgumentException(name);
            return Read();
        }
        [CliCommand("world_fixture","Place the walking character beside a car, at a mapped point, or reset the exploration state for repeatable input tests.")]
        public static object Fixture([CliArg("name","car | reset | at",Required=true)]string name,
            [CliArg("x","Grid east for 'at'")]float x=0,[CliArg("z","Grid north for 'at'")]float z=0,[CliArg("yaw","Heading in degrees for 'at'")]float yaw=0)
        {
            var g=Game;g.SetMap(false);
            if(g.Mode=="fly")g.EndFlight();if(g.Mode=="drive"){g.Driving.ResetCar();g.ExitCar();}
            if(name=="reset"){g.Player.Respawn();g.HasWaypoint=false;System.Array.Clear(g.Visited,0,g.Visited.Length);g.Discoveries=0;}
            else if(name=="car")
            {
                var car=g.Cars[0];car.ResetCar();var p=car.transform.position+car.transform.right*2.2f;
                if(Physics.Raycast(p+Vector3.up*3,Vector3.down,out var hit,8,1<<8))p=hit.point+Vector3.up*.04f;
                var controller=g.Player.GetComponent<CharacterController>();controller.enabled=false;g.Player.transform.position=p;controller.enabled=true;
                g.Player.VerticalSpeed=-2;g.Player.FollowCamera.SetTestHeading(car.transform.eulerAngles.y);
            }
            else if(name=="at")
            {
                var p=new Vector3(x,200,z);
                if(!Physics.Raycast(p,Vector3.down,out var hit,400,(1<<8)|(1<<9)))throw new InvalidOperationException("No ground at "+x+","+z);
                var controller=g.Player.GetComponent<CharacterController>();controller.enabled=false;g.Player.transform.position=hit.point+Vector3.up*.04f;controller.enabled=true;
                g.Player.VerticalSpeed=-2;g.Player.FollowCamera.SetTestHeading(yaw);g.Player.Visual.rotation=Quaternion.Euler(0,yaw,0);
            }
            else throw new ArgumentException(name);
            return Read();
        }
    }
}
#endif
