using System.Linq;
using NUnit.Framework;
using Padova.World;
using UnityEngine;

namespace PadovaOpenWorld.Tests
{
    /// <summary>Handling maths and the vehicle catalog: no scene, no physics step.</summary>
    public class DrivingTests
    {
        [Test]public void SteeringHasLotsOfLockWhenSlowAndStaysWithinGripWhenFast()
        {
            foreach(var spec in VehicleCatalog.All)
            {
                Assert.That(CityCar.SteerRate(spec,0,false),Is.EqualTo(0),"No yaw while standing still: "+spec.Name);
                float slow=CityCar.SteerRate(spec,5,false),fast=CityCar.SteerRate(spec,30,false);
                if(spec.Drivable)Assert.That(slow,Is.GreaterThan(fast*1.8f),spec.Name);
                // Turning circle at walking pace is tighter than a real car's: about three metres of radius for the hatchback.
                if(spec==VehicleCatalog.Berlina)Assert.That(5/slow,Is.InRange(2.4f,3.6f));
                for(float v=1;v<=spec.BoostSpeed;v+=1)
                    Assert.That(v*CityCar.SteerRate(spec,v,false),Is.LessThanOrEqualTo(spec.Grip*.9f+.001f),spec.Name+" asks for more cornering than its tyres hold at "+v+" m/s");
                Assert.That(CityCar.SteerRate(spec,20,true),Is.GreaterThan(CityCar.SteerRate(spec,20,false)),"Handbrake must let the tail come round: "+spec.Name);
                Assert.That(CityCar.SteerRate(spec,-4,false),Is.EqualTo(CityCar.SteerRate(spec,4,false)),"Same lock in reverse");
            }
        }
        [Test]public void DriveFadesToTopSpeedAndTurboAddsShove()
        {
            var s=VehicleCatalog.Berlina;
            Assert.That(CityCar.Drive(s,0,s.TopSpeed,false),Is.EqualTo(s.Accel).Within(.001f));
            Assert.That(CityCar.Drive(s,s.TopSpeed*.5f,s.TopSpeed,false),Is.EqualTo(s.Accel).Within(.001f));
            Assert.That(CityCar.Drive(s,s.TopSpeed*.9f,s.TopSpeed,false),Is.InRange(.1f,s.Accel*.5f));
            Assert.That(CityCar.Drive(s,s.TopSpeed+2,s.TopSpeed,false),Is.LessThan(0),"Above top speed the car eases back");
            Assert.That(CityCar.Drive(s,10,s.BoostSpeed,true),Is.GreaterThan(s.Accel*1.5f));
            // Closed form for the fade: full thrust to 75 % of top, then an exponential approach. 0-100 km/h must stay arcade-quick.
            float v=0,t=0;while(v<27.78f&&t<30){v+=CityCar.Drive(s,v,s.TopSpeed,false)*.02f;t+=.02f;}
            Assert.That(t,Is.InRange(3f,6f),"Hatchback 0-100 km/h");
        }
        [Test]public void CatalogCoversThePrefabContract()
        {
            foreach(string name in new[]{"Utilitaria","Berlina","Pantera","Taxi","Scooter","Ape","Furgone","Autobus","Tram","Bicicletta"})
                Assert.That(VehicleCatalog.Get(name),Is.Not.Null,name);
            Assert.That(VehicleCatalog.Get("scooter"),Is.SameAs(VehicleCatalog.Scooter));Assert.That(VehicleCatalog.Get("Carrozza"),Is.Null);
            Assert.That(VehicleCatalog.All.Select(s=>s.Name).Distinct().Count(),Is.EqualTo(VehicleCatalog.All.Length));
            Assert.That(VehicleCatalog.Scooter.TopKmh,Is.EqualTo(90).Within(.5f));Assert.That(VehicleCatalog.Scooter.Wheels,Is.EqualTo(2));Assert.That(VehicleCatalog.Scooter.Lean,Is.True);
            Assert.That(VehicleCatalog.Ape.Wheels,Is.EqualTo(3));Assert.That(VehicleCatalog.Ape.Tip,Is.GreaterThan(0),"The Ape is the tippy one");
            Assert.That(VehicleCatalog.Ape.TopSpeed,Is.LessThan(VehicleCatalog.Utilitaria.TopSpeed));
            Assert.That(VehicleCatalog.Pantera.TopSpeed,Is.GreaterThan(VehicleCatalog.Berlina.TopSpeed),"Police must be able to catch the hatchback");
            foreach(var s in VehicleCatalog.All)
            {
                Assert.That(s.Mass,Is.GreaterThan(0));Assert.That(s.BoostSpeed,Is.GreaterThanOrEqualTo(s.TopSpeed));Assert.That(s.Brake,Is.GreaterThan(s.Accel),s.Name+": brakes must beat the engine");
                Assert.That(s.Wheelbase,Is.LessThan(s.Length));Assert.That(s.Track,Is.LessThanOrEqualTo(s.Width));Assert.That(s.Label,Is.Not.Empty);
            }
        }
        [Test]public void WeightedPicksAreDeterministicAndRespectShares()
        {
            var counts=VehicleCatalog.All.ToDictionary(s=>s,_=>0);
            for(int i=0;i<2000;i++){float u=(i+.5f)/2000;var s=VehicleCatalog.PickTraffic(u);Assert.That(VehicleCatalog.PickTraffic(u),Is.SameAs(s));counts[s]++;}
            Assert.That(counts[VehicleCatalog.Pantera],Is.EqualTo(0),"Police cars are not ordinary traffic");Assert.That(counts[VehicleCatalog.Tram],Is.EqualTo(0));
            Assert.That(counts[VehicleCatalog.Berlina],Is.GreaterThan(counts[VehicleCatalog.Ape]));
            Assert.That(counts.Count(c=>c.Value>0),Is.GreaterThanOrEqualTo(5),"Traffic mixes at least five types");
            float total=VehicleCatalog.All.Sum(s=>s.TrafficShare);
            Assert.That(counts[VehicleCatalog.Scooter]/2000f,Is.EqualTo(VehicleCatalog.Scooter.TrafficShare/total).Within(.01f));
            for(int i=0;i<200;i++)Assert.That(VehicleCatalog.PickParked(i/200f).Drivable,Is.True);
            Assert.That(VehicleCatalog.Paint(VehicleCatalog.Pantera,7),Is.EqualTo(VehicleCatalog.Pantera.Livery));
            Assert.That(VehicleCatalog.Paint(VehicleCatalog.Berlina,-3),Is.EqualTo(VehicleCatalog.Paint(VehicleCatalog.Berlina,VehicleCatalog.Berlina.Paints.Length-3)));
        }
        [Test]public void SpringsCarryTheWeightAtRestAndStopTheBodyBottomingOut()
        {
            float load=300,stiffness=load*9.81f/CityCar.RestCompression,damping=1.2f*load*Mathf.Sqrt(9.81f/CityCar.RestCompression);
            Assert.That(CityCar.SpringForce(stiffness,damping,CityCar.RestCompression,0),Is.EqualTo(load*9.81f).Within(.5f),"Static load at the rest compression");
            Assert.That(CityCar.SpringForce(stiffness,damping,.02f,-3),Is.EqualTo(0),"A spring never pulls the car down");
            float atTravel=CityCar.SpringForce(stiffness,damping,CityCar.Travel,0),beyond=CityCar.SpringForce(stiffness,damping,CityCar.Travel+.05f,0);
            Assert.That(beyond-atTravel,Is.GreaterThan(stiffness*.05f*5),"Bump stop is much stiffer than the spring");
            Assert.That(CityCar.SpringForce(stiffness,damping,10,50),Is.EqualTo(stiffness*CityCar.Travel*8).Within(1),"Force is capped: no launch from a deep penetration");
        }
        [Test]public void EquipSizesSuspensionAndColliderFromTheSpec()
        {
            foreach(var spec in new[]{VehicleCatalog.Berlina,VehicleCatalog.Scooter,VehicleCatalog.Ape,VehicleCatalog.Autobus})
            {
                var go=new GameObject("Equip test "+spec.Name);
                try
                {
                    int visuals=spec.Wheels;var wheels=new Transform[visuals];
                    for(int i=0;i<visuals;i++){wheels[i]=new GameObject("Wheel "+i).transform;wheels[i].SetParent(go.transform,false);}
                    var car=CityCar.Equip(go,spec,wheels);
                    Assert.That(car.Spec,Is.SameAs(spec));Assert.That(go.layer,Is.EqualTo(11));Assert.That(car.TopSpeed,Is.EqualTo(spec.TopSpeed));
                    Assert.That(car.Anchors.Length,Is.EqualTo(4),"Every type rides on four suspension points");Assert.That(car.WheelVisuals.Length,Is.EqualTo(spec.Wheels));
                    var body=go.GetComponent<Rigidbody>();Assert.That(body.mass,Is.EqualTo(spec.Mass));
                    var box=go.GetComponent<BoxCollider>();Assert.That(box.size.x,Is.EqualTo(spec.Width).Within(.001f));Assert.That(box.size.z,Is.EqualTo(spec.Length).Within(.001f));
                    Assert.That(box.center.y-box.size.y*.5f,Is.GreaterThan(.15f),"Ground clearance under the body");
                    Assert.That(car.WheelRadius,Is.EqualTo(spec.WheelRadius));
                    // Standing still, the wheel centre is one radius above the origin: the origin rests on the road.
                    foreach(var anchor in car.Anchors)Assert.That(anchor.y-(CityCar.Travel-CityCar.RestCompression),Is.EqualTo(spec.WheelRadius).Within(.001f));
                    float frontTrack=Mathf.Abs(car.Anchors[0].x-car.Anchors[1].x),rearTrack=Mathf.Abs(car.Anchors[2].x-car.Anchors[3].x);
                    Assert.That(car.Anchors[0].z,Is.GreaterThan(0));Assert.That(car.Anchors[3].z,Is.LessThan(0));
                    if(spec.Wheels==3)Assert.That(frontTrack,Is.LessThan(rearTrack*.3f),"Ape: single front wheel");
                    if(spec.Wheels==4)Assert.That(frontTrack,Is.EqualTo(spec.Track).Within(.001f));
                }
                finally{Object.DestroyImmediate(go);}
            }
        }
    }
}
