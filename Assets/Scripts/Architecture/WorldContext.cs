using System;
using System.Collections.Generic;
using Padova.City;
using UnityEngine;

namespace Padova.Architecture
{
    /// <summary>Gameplay view of the wider city: terrain height, road centrelines and named places.
    /// Buildings, ground and trees come from the streamed city packs (CityStreamer).</summary>
    [Serializable] public sealed class WorldPlan
    {
        public int version;public float[] extent,plane,coreExtent,terrain;public int terrainNX,terrainNZ;public float terrainStep;
        public WorldRoad[] roads;public WorldPlace[] landmarks;public string attribution,detailStatus;
        public float GroundAt(float x,float z)
        {
            float original=plane[0]*x+plane[1]*z+plane[2];if(terrain==null||terrain.Length==0)return original;
            float fx=Mathf.Clamp((x-extent[0])/terrainStep,0,terrainNX-1.001f),fz=Mathf.Clamp((z-extent[1])/terrainStep,0,terrainNZ-1.001f);
            int i=(int)fx,j=(int)fz;float a=fx-i,b=fz-j;
            float h=Mathf.Lerp(Mathf.Lerp(terrain[j*terrainNX+i],terrain[j*terrainNX+i+1],a),Mathf.Lerp(terrain[(j+1)*terrainNX+i],terrain[(j+1)*terrainNX+i+1],a),b);
            float dx=Mathf.Max(coreExtent[0]-x,0,x-coreExtent[2]),dz=Mathf.Max(coreExtent[1]-z,0,z-coreExtent[3]);float blend=Mathf.Clamp01(Mathf.Sqrt(dx*dx+dz*dz)/120);blend=blend*blend*(3-2*blend);
            return Mathf.Lerp(original,h,blend);
        }
    }
    [Serializable] public sealed class WorldRoad {public string id,name;public float[] points;}
    [Serializable] public sealed class WorldPlace {public string name,source;public float x,z;}
    /// <summary>Survey records superseded by Blender landmark models (Assets/Art/Landmarks/Landmarks.json).
    /// The city compiler reads the same manifest, so those units and that ground are left out of the packs.</summary>
    [Serializable] public sealed class LandmarkManifest
    {
        public string[] replacedUnits=new string[0],replacedPatches=new string[0];public float[] ownGround=new float[0];public WorldPlace[] places=new WorldPlace[0];public LandmarkModel[] models=new LandmarkModel[0];
        public bool OwnsGround(float x0,float z0,float x1,float z1)
        {
            for(int i=0;i+3<ownGround.Length;i+=4)if(x0>=ownGround[i]-.01f&&z0>=ownGround[i+1]-.01f&&x1<=ownGround[i+2]+.01f&&z1<=ownGround[i+3]+.01f)return true;
            return false;
        }
    }
    [Serializable] public sealed class LandmarkModel {public string name,asset;public float[] anchor;}

    [ExecuteAlways,DefaultExecutionOrder(-450)]
    public sealed class WorldContext:MonoBehaviour
    {
        public TextAsset Plan,Landmarks;public PadovaCity City;public float ViewDistance=1500;public float CollisionDistance=430;
        /// <summary>Surveyed volume units in the compiled city (several units can form one building).</summary>
        public int Buildings;public int Chunks;public float BuildMilliseconds;
        public WorldPlan Data{get;private set;}
        public CityStreamer Streamer{get;private set;}
        void OnEnable(){if(Plan)Build();}
        void OnDisable(){if(Streamer)Streamer.Close();}
        public void Build()
        {
            if(!Plan)return;
            if(!City)City=UnityEngine.Object.FindFirstObjectByType<PadovaCity>();
            var watch=System.Diagnostics.Stopwatch.StartNew();Data=JsonUtility.FromJson<WorldPlan>(Plan.text);
            var manifest=Landmarks?JsonUtility.FromJson<LandmarkManifest>(Landmarks.text):new LandmarkManifest();
            if(manifest.places.Length>0){var places=new List<WorldPlace>(Data.landmarks);places.AddRange(manifest.places);Data.landmarks=places.ToArray();}
            Streamer=GetComponent<CityStreamer>();
            if(!Streamer){Streamer=gameObject.AddComponent<CityStreamer>();Streamer.hideFlags=HideFlags.DontSave;}
            Streamer.Core=City;Streamer.ShellRadius=ViewDistance;Streamer.ColliderRadius=CollisionDistance;
            if(Streamer.Open())
            {
                // One terrain for gameplay and rendering.
                var t=Streamer.Pack.Terrain;Data.terrain=t.Grid;Data.terrainStep=t.Step;Data.terrainNX=t.Nx;Data.terrainNZ=t.Nz;
                Data.extent=new[]{t.X0,t.Z0,t.X0+(t.Nx-1)*t.Step,t.Z0+(t.Nz-1)*t.Step};Data.plane=t.Plane;Data.coreExtent=t.Core;
                Buildings=Streamer.Pack.Index.totals!=null?Streamer.Pack.Index.totals.units:0;
            }
            BuildMilliseconds=(float)watch.Elapsed.TotalMilliseconds;
        }
        public void RefreshVisibility(Vector3 eye){if(Streamer)Streamer.RefreshVisibility(eye);}
    }
}
