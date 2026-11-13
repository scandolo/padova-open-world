using System;
using UnityEngine;

namespace Padova.World
{
    /// <summary>Handling and shape numbers for one vehicle type. SI units: metres, seconds, kilograms.</summary>
    public sealed class VehicleSpec
    {
        /// <summary>Prefab name under Resources/Vehicles and the key used everywhere else.</summary>
        public string Name;
        /// <summary>What the HUD calls it.</summary>
        public string Label;
        public float Mass,Accel,Brake,TopSpeed,BoostSpeed,Reverse;
        /// <summary>Yaw rate scale (rad/s at parking speed) and the lateral acceleration the tyres can hold (m/s²).</summary>
        public float Steer,Grip;
        public float Length,Width,Height,Wheelbase,Track,WheelRadius;
        /// <summary>2 scooter and bicycle, 3 Ape, 4 everything else.</summary>
        public int Wheels=4;
        /// <summary>Height above the centre of mass where cornering force acts. Above zero the body really rolls (the Ape).</summary>
        public float Tip;
        /// <summary>Share of moving traffic and of parked vehicles; zero means never spawned that way.</summary>
        public float TrafficShare,ParkedShare;
        /// <summary>Two-wheelers bank into turns and are held upright.</summary>
        public bool Lean=>Wheels==2;
        public bool Drivable=true;
        /// <summary>Paint colours a spawned vehicle may get; empty means the fixed livery colour in <see cref="Livery"/>.</summary>
        public Color[] Paints=Array.Empty<Color>();
        public Color Livery=Color.white;
        public float TopKmh=>TopSpeed*3.6f;
    }

    /// <summary>Every vehicle type in the game. Models come from Resources/Vehicles/&lt;Name&gt; when present, otherwise from code.</summary>
    public static class VehicleCatalog
    {
        static readonly Color[] CarPaints=
        {
            new(.72f,.74f,.76f),new(.06f,.065f,.07f),new(.92f,.92f,.9f),new(.38f,.4f,.42f),new(.08f,.14f,.32f),
            new(.62f,.08f,.07f),new(.08f,.26f,.16f),new(.76f,.68f,.52f),new(.9f,.72f,.12f)
        };
        static readonly Color[] SmallCarPaints={new(.93f,.9f,.8f),new(.55f,.78f,.82f),new(.82f,.2f,.14f),new(.94f,.78f,.2f),new(.35f,.55f,.4f),new(.16f,.22f,.42f)};
        static readonly Color[] ScooterPaints={new(.86f,.16f,.12f),new(.94f,.92f,.84f),new(.55f,.8f,.78f),new(.13f,.15f,.18f),new(.95f,.74f,.2f),new(.3f,.5f,.8f)};
        static readonly Color[] WorkPaints={new(.93f,.93f,.9f),new(.3f,.5f,.36f),new(.2f,.35f,.6f),new(.78f,.74f,.66f)};

        /// <summary>Classic tiny city car: light, eager, runs out of breath early.</summary>
        public static readonly VehicleSpec Utilitaria=new()
        {
            Name="Utilitaria",Label="Utilitaria",Mass=720,Accel=5.6f,Brake=19,TopSpeed=27,BoostSpeed=34,Reverse=7,Steer=1.3f,Grip=13,
            Length=3.05f,Width=1.36f,Height=1.36f,Wheelbase=1.85f,Track=1.14f,WheelRadius=.25f,TrafficShare=.2f,ParkedShare=.22f,Paints=SmallCarPaints
        };
        /// <summary>Modern hatchback: the all-rounder.</summary>
        public static readonly VehicleSpec Berlina=new()
        {
            Name="Berlina",Label="Berlina",Mass=1200,Accel=6.6f,Brake=22,TopSpeed=36,BoostSpeed=45,Reverse=8,Steer=1.15f,Grip=15,
            Length=4.1f,Width=1.74f,Height=1.5f,Wheelbase=2.55f,Track=1.52f,WheelRadius=.31f,TrafficShare=.34f,ParkedShare=.28f,Paints=CarPaints
        };
        public static readonly VehicleSpec Furgone=new()
        {
            Name="Furgone",Label="Furgone",Mass=1900,Accel=4.6f,Brake=16,TopSpeed=27,BoostSpeed=32,Reverse=6,Steer=.92f,Grip=11,
            Length=5.2f,Width=1.98f,Height=2.35f,Wheelbase=3.2f,Track=1.7f,WheelRadius=.34f,TrafficShare=.1f,ParkedShare=.08f,Paints=WorkPaints
        };
        /// <summary>Police pursuit saloon ("pantera"): the quickest thing on the road, never parked for the taking.</summary>
        public static readonly VehicleSpec Pantera=new()
        {
            Name="Pantera",Label="Pantera",Mass=1350,Accel=9,Brake=24,TopSpeed=40,BoostSpeed=48,Reverse=9,Steer=1.15f,Grip=16,
            Length=4.5f,Width=1.8f,Height=1.46f,Wheelbase=2.7f,Track=1.56f,WheelRadius=.32f,Livery=new Color(.05f,.13f,.38f)
        };
        public static readonly VehicleSpec Taxi=new()
        {
            Name="Taxi",Label="Taxi",Mass=1300,Accel=7,Brake=21,TopSpeed=34,BoostSpeed=42,Reverse=8,Steer=1.1f,Grip=14.5f,
            Length=4.4f,Width=1.8f,Height=1.55f,Wheelbase=2.65f,Track=1.55f,WheelRadius=.31f,TrafficShare=.06f,ParkedShare=.05f,Livery=new Color(.95f,.95f,.93f)
        };
        /// <summary>Nimble two-wheeler, 90 km/h flat out.</summary>
        public static readonly VehicleSpec Scooter=new()
        {
            Name="Scooter",Label="Scooter",Mass=150,Accel=6.2f,Brake=16,TopSpeed=25,BoostSpeed=29,Reverse=2,Steer=1.85f,Grip=12,
            Length=1.8f,Width=.7f,Height=1.15f,Wheelbase=1.28f,Track=.3f,WheelRadius=.22f,Wheels=2,TrafficShare=.14f,ParkedShare=.2f,Paints=ScooterPaints
        };
        /// <summary>Three-wheeled Ape: slow, top-heavy and proud of it.</summary>
        public static readonly VehicleSpec Ape=new()
        {
            Name="Ape",Label="Ape",Mass=430,Accel=3.4f,Brake=12,TopSpeed=13,BoostSpeed=16.5f,Reverse=2,Steer=1.45f,Grip=7.5f,
            Length=2.6f,Width=1.28f,Height=1.6f,Wheelbase=1.7f,Track=1.1f,WheelRadius=.2f,Wheels=3,Tip=.42f,TrafficShare=.06f,ParkedShare=.1f,Paints=WorkPaints
        };
        public static readonly VehicleSpec Autobus=new()
        {
            Name="Autobus",Label="Autobus",Mass=9000,Accel=3,Brake=12,TopSpeed=20,BoostSpeed=23,Reverse=4,Steer=.62f,Grip=9.5f,
            Length=10.8f,Width=2.5f,Height=3.05f,Wheelbase=5.6f,Track=2.1f,WheelRadius=.48f,TrafficShare=.04f,Livery=new Color(.93f,.47f,.12f)
        };
        /// <summary>Runs on the lead's rails if it ever does; listed so the prefab contract is complete.</summary>
        public static readonly VehicleSpec Tram=new()
        {
            Name="Tram",Label="Tram",Mass=30000,Accel=1.2f,Brake=6,TopSpeed=14,BoostSpeed=14,Reverse=2,Steer=.2f,Grip=8,
            Length=24,Width=2.4f,Height=3.4f,Wheelbase=16,Track=1.6f,WheelRadius=.35f,Drivable=false,Livery=new Color(.2f,.45f,.75f)
        };
        public static readonly VehicleSpec Bicicletta=new()
        {
            Name="Bicicletta",Label="Bicicletta",Mass=95,Accel=3.2f,Brake=9,TopSpeed=8.5f,BoostSpeed=11.5f,Reverse=1.2f,Steer=2.1f,Grip=9,
            Length=1.75f,Width=.55f,Height=1.05f,Wheelbase=1.05f,Track=.3f,WheelRadius=.33f,Wheels=2,ParkedShare=.07f,Paints=ScooterPaints
        };
        public static readonly VehicleSpec[] All={Utilitaria,Berlina,Furgone,Pantera,Taxi,Scooter,Ape,Autobus,Tram,Bicicletta};

        public static VehicleSpec Get(string name)
        {
            foreach(var s in All)if(string.Equals(s.Name,name,StringComparison.OrdinalIgnoreCase))return s;
            return null;
        }
        /// <summary>Weighted choice for <paramref name="u"/> in [0,1): the same number always gives the same type.</summary>
        public static VehicleSpec Pick(float u,Func<VehicleSpec,float> share)
        {
            float total=0;foreach(var s in All)total+=Mathf.Max(0,share(s));
            if(total<=0)return Berlina;
            float at=Mathf.Repeat(u,1)*total;VehicleSpec last=Berlina;
            foreach(var s in All){float w=Mathf.Max(0,share(s));if(w<=0)continue;last=s;if(at<w)return s;at-=w;}
            return last;
        }
        public static VehicleSpec PickTraffic(float u)=>Pick(u,s=>s.TrafficShare);
        public static VehicleSpec PickParked(float u)=>Pick(u,s=>s.Drivable?s.ParkedShare:0);
        public static Color Paint(VehicleSpec spec,int seed)=>spec.Paints.Length>0?spec.Paints[(seed%spec.Paints.Length+spec.Paints.Length)%spec.Paints.Length]:spec.Livery;
    }
}
