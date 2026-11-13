using System;
using System.Collections.Generic;
using System.IO;
using Padova.Architecture;
using Padova.Gameplay;
using Padova.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

public static class BuildOpenWorld
{
    const string Folder="Assets/World";
    static void Import(string source,string target){if(File.Exists(source))File.Copy(source,target,true);else if(!File.Exists(target))throw new FileNotFoundException(source);AssetDatabase.ImportAsset(target,ImportAssetOptions.ForceSynchronousImport);}
    static GameObject MeshObject(string name,MeshSink sink,Material[] mats,Transform parent,bool asset=true)
    {
        var go=new GameObject(name);go.transform.SetParent(parent,false);
        var mesh=sink.ToMesh(name,mats,out var material);
        if(asset){string path=Folder+"/"+name+".asset";var old=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(old){EditorUtility.CopySerialized(mesh,old);UnityEngine.Object.DestroyImmediate(mesh);mesh=old;}else AssetDatabase.CreateAsset(mesh,path);}
        go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=material;return go;
    }
    static CityCar Car(string name,Vector3 pos,float yaw,Slot color,Material[] mats,Transform parent)
    {
        var car=new GameObject(name);car.transform.SetParent(parent);car.transform.SetPositionAndRotation(pos,Quaternion.Euler(0,yaw,0));
        var sink=new MeshSink();WorldShapes.CarBody(sink,color);MeshObject(name+" body",sink,mats,car.transform);
        var wheelSink=new MeshSink();WorldShapes.Wheel(wheelSink);
        var motor=CityCar.Equip(car,()=>MeshObject("Wheel",wheelSink,mats,car.transform).transform);
        return motor;
    }
    static void AttachActivities(OpenWorld game)
    {
        game.Traffic=game.GetComponent<CityTraffic>()??game.gameObject.AddComponent<CityTraffic>();game.Traffic.Game=game;
        game.Activities=game.GetComponent<Activities>()??game.gameObject.AddComponent<Activities>();game.Activities.Game=game;game.Activities.Traffic=game.Traffic;
        string beaconPath=Folder+"/Beacon.mat";var beacon=AssetDatabase.LoadAssetAtPath<Material>(beaconPath);
        if(!beacon){beacon=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(beacon,beaconPath);}
        beacon.SetFloat("_Surface",1);beacon.SetFloat("_SrcBlend",(float)UnityEngine.Rendering.BlendMode.SrcAlpha);beacon.SetFloat("_DstBlend",(float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        beacon.SetFloat("_ZWrite",0);beacon.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");beacon.SetOverrideTag("RenderType","Transparent");beacon.renderQueue=3000;
        EditorUtility.SetDirty(beacon);game.Activities.BeaconMaterial=beacon;
        AssetDatabase.ImportAsset("Assets/Shaders/Minimap.shader",ImportAssetOptions.ForceSynchronousImport);
        string minimapPath=Folder+"/Minimap.mat";var minimap=AssetDatabase.LoadAssetAtPath<Material>(minimapPath);
        if(!minimap){minimap=new Material(Shader.Find("Padova/Minimap"));AssetDatabase.CreateAsset(minimap,minimapPath);}
        game.MinimapMaterial=minimap;
        game.Police=game.GetComponent<Police>()??game.gameObject.AddComponent<Police>();game.Police.Game=game;game.Police.Traffic=game.Traffic;game.Police.Activities=game.Activities;game.Police.LightMaterial=beacon;
        game.Crowd.Count=64;
    }
    // Connect gameplay without regenerating existing architecture, navigation or vehicle assets.
    public static object Gameplay()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode first.");
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/PadovaPlayable.unity",OpenSceneMode.Single);
        var game=UnityEngine.Object.FindFirstObjectByType<OpenWorld>();AttachActivities(game);
        EditorUtility.SetDirty(game);EditorUtility.SetDirty(game.Crowd);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return new{traffic=game.Traffic.Count,pedestrians=game.Crowd.Count,activities=game.Activities!=null,police=game.Police!=null};
    }
    public static object Build()
    {
        if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play Mode first.");
        var scene=EditorSceneManager.OpenScene("Assets/Scenes/PadovaPlayable.unity",OpenSceneMode.Single);
        if(!AssetDatabase.IsValidFolder(Folder))AssetDatabase.CreateFolder("Assets","World");
        foreach(string name in new[]{"Wider Padova","Street life","Open world"}){var old=GameObject.Find(name);if(old)UnityEngine.Object.DestroyImmediate(old);}
        Import("Data/World/Map.png",Folder+"/Map.png");
        var ti=(TextureImporter)AssetImporter.GetAtPath(Folder+"/Map.png");ti.textureCompression=TextureImporterCompression.Uncompressed;ti.maxTextureSize=4096;ti.mipmapEnabled=false;ti.wrapMode=TextureWrapMode.Clamp;ti.SaveAndReimport();
        var city=UnityEngine.Object.FindFirstObjectByType<PadovaCity>();city.IncludeHorizon=false;city.Build();
        var world=new GameObject("Wider Padova").AddComponent<WorldContext>();world.City=city;world.Plan=AssetDatabase.LoadAssetAtPath<TextAsset>(Folder+"/WorldPlan.json");world.Landmarks=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Art/Landmarks/Landmarks.json");world.Build();
        var streets=new GameObject("Street life").AddComponent<Streetscape>();streets.City=city;streets.Build();
        var root=new GameObject("Open world");var game=root.AddComponent<OpenWorld>();game.Player=UnityEngine.Object.FindFirstObjectByType<ThirdPersonMotor>();game.City=world;game.MapTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/Map.png");
        game.Player.WorldHalfSize=new Vector2(1400,1200);
        // Real streets; vehicles and their parking locations are authored game content.
        Vector3 P(float x,float z)=>new Vector3(x,world.Data.GroundAt(x,z)+.25f,z);
        game.Cars=new[]{Car("blue compact",P(-106.19f,76.73f),13.7f,Slot.VehicleBlue,city.Materials,root.transform),Car("red compact",P(-179.91f,178.15f),-77.9f,Slot.VehicleRed,city.Materials,root.transform),Car("ivory compact",P(174.33f,-206.4f),-166.6f,Slot.White,city.Materials,root.transform)};
        var planeSink=new MeshSink();WorldShapes.Plane(planeSink);var plane=MeshObject("Tour aircraft",planeSink,city.Materials,root.transform);game.Plane=plane.AddComponent<TourPlane>();
        var propSink=new MeshSink();propSink.Box(Slot.Iron,Vector3.zero,Vector3.right*.10f,Vector3.up*1.1f,Vector3.forward*.025f);
        var prop=MeshObject("Propeller",propSink,city.Materials,plane.transform);prop.transform.localPosition=new Vector3(0,0,3.02f);game.Plane.Propeller=prop.transform;plane.SetActive(false);
        var crowdGo=new GameObject("Pedestrians");crowdGo.transform.SetParent(root.transform);var crowd=crowdGo.AddComponent<CityCrowd>();game.Crowd=crowd;
        var visual=UnityEngine.Object.Instantiate(game.Player.Visual.gameObject);visual.name="Pedestrian visual";visual.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);
        crowd.Character=PrefabUtility.SaveAsPrefabAsset(visual,Folder+"/Pedestrian.prefab");UnityEngine.Object.DestroyImmediate(visual);
        var skins=new List<Material>{game.Player.Visual.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterial};
        foreach(string name in new[]{"skaterFemaleA","criminalMaleA"})
        {
            Import(".context/playable/Kenney/Skins/"+name+".png",Folder+"/"+name+".png");
            string path=Folder+"/"+name+".mat";var mat=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!mat){mat=new Material(skins[0]);AssetDatabase.CreateAsset(mat,path);}mat.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(Folder+"/"+name+".png");EditorUtility.SetDirty(mat);skins.Add(mat);
        }
        crowd.Skins=skins.ToArray();
        crowd.Destinations=new[]{P(80,10),P(112,0),P(32,9),P(26,-86),P(90,-95),P(120,-71),P(-60,10),P(-97,15),P(-65,70),P(-165,-112),P(189,-57),P(195,-106)};
        AttachActivities(game);
        Physics.SyncTransforms();
        var sources=new List<NavMeshBuildSource>();
        var bounds=new Bounds(new Vector3(0,10,-20),new Vector3(680,100,540));
        NavMeshBuilder.CollectSources(bounds,(1<<8)|(1<<9),NavMeshCollectGeometry.PhysicsColliders,0,new List<NavMeshBuildMarkup>(),sources);
        var settings=NavMesh.GetSettingsByID(0);settings.agentRadius=.3f;settings.agentHeight=1.8f;settings.agentClimb=.35f;settings.agentSlope=45;settings.overrideVoxelSize=true;settings.voxelSize=.15f;
        var navigation=NavMeshBuilder.BuildNavMeshData(settings,sources,bounds,Vector3.zero,Quaternion.identity);
        if(!navigation)throw new InvalidOperationException("NavMesh bake failed.");navigation.name="Central Padova pedestrian navigation";
        string navPath=Folder+"/PedestrianNavMesh.asset";var existing=AssetDatabase.LoadAssetAtPath<NavMeshData>(navPath);
        if(existing){EditorUtility.CopySerialized(navigation,existing);UnityEngine.Object.DestroyImmediate(navigation);crowd.Navigation=existing;}else{AssetDatabase.CreateAsset(navigation,navPath);crowd.Navigation=navigation;}
        // The city has one sun. Forward avoids URP 17's stale realtime-probe atlas path during editor regeneration.
        var renderer=AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.Universal.UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
        var serialized=new SerializedObject(renderer);serialized.FindProperty("m_RenderingMode").intValue=0;serialized.ApplyModifiedProperties();renderer.SetDirty();
        var hud=UnityEngine.Object.FindFirstObjectByType<WalkHud>();if(hud){hud.enabled=false;if(hud.GoalMarker)hud.GoalMarker.gameObject.SetActive(false);}
        EditorUtility.SetDirty(city);EditorUtility.SetDirty(world);EditorUtility.SetDirty(streets);EditorUtility.SetDirty(game.Player);
        AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        return new{world.Buildings,world.Chunks,world.BuildMilliseconds,streets.Stalls,streets.Lamps,streets.Benches,navSources=sources.Count,cars=game.Cars.Length};
    }
}
