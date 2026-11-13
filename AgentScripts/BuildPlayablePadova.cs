using System;
using System.IO;
using System.Linq;
using Padova.Geography;
using Padova.Gameplay;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class BuildPlayablePadova
{
    const string Folder = "Assets/Gameplay";
    const string ScenePath = "Assets/Scenes/PadovaPlayable.unity";
    static void MakeFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\','/');
        MakeFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
    static Material Material(string name, Color color, string texture = null, string normal = null)
    {
        var path = Folder+"/"+name+".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m,path); }
        m.SetColor("_BaseColor",color);
        m.SetFloat("_Smoothness",0.15f);
        m.SetFloat("_Cull",0);
        if (texture != null) m.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(texture));
        if (normal != null)
        {
            m.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
            m.SetFloat("_BumpScale",0.5f);
            m.EnableKeyword("_NORMALMAP");
        }
        EditorUtility.SetDirty(m);
        return m;
    }
    static AnimationClip Clip(string name)
    {
        var path=Folder+"/"+name+".anim";
        var existing=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(existing) return existing;
        var source=AssetDatabase.LoadAllAssetsAtPath("Assets/Art/Kenney/"+name+".fbx").OfType<AnimationClip>().Where(c=>!c.name.StartsWith("__preview")).OrderByDescending(c=>c.length).First();
        var clip=UnityEngine.Object.Instantiate(source);
        clip.name=name;
        // Animation FBXs expose the skeleton as their root; the model wraps it in a Root child.
        // Bind to that child, preserving the model's metre scale and orientation.
        foreach(var binding in AnimationUtility.GetCurveBindings(source))
        {
            if(binding.type!=typeof(Transform)) continue;
            var curve=AnimationUtility.GetEditorCurve(source,binding);
            AnimationUtility.SetEditorCurve(clip,binding,null);
            var mapped=binding;mapped.path="Root"+(binding.path.Length>0?"/"+binding.path:"");
            AnimationUtility.SetEditorCurve(clip,mapped,curve);
        }
        AssetDatabase.CreateAsset(clip,path);
        return clip;
    }

    static AnimatorController CreateController()
    {
        var path = Folder+"/Locomotion.controller";
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (existing)
        {
            foreach(var child in existing.layers[0].stateMachine.states)
            {
                if(child.state.motion is BlendTree tree)
                {
                    var entries=tree.children;
                    entries[0].motion=Clip("idle");entries[1].motion=Clip("run");
                    tree.children=entries;EditorUtility.SetDirty(tree);
                }
                else if(child.state.name=="Jump") child.state.motion=Clip("jump");
            }
            EditorUtility.SetDirty(existing);
            return existing;
        }
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Speed",AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded",AnimatorControllerParameterType.Bool);
        var machine = controller.layers[0].stateMachine;
        var movement = machine.AddState("Locomotion");
        var blend = new BlendTree { name="Idle and run", blendParameter="Speed", blendType=BlendTreeType.Simple1D, useAutomaticThresholds=false };
        blend.AddChild(Clip("idle"),0);
        blend.AddChild(Clip("run"),6.4f);
        AssetDatabase.AddObjectToAsset(blend,controller);
        movement.motion = blend;
        var jump = machine.AddState("Jump");
        jump.motion = Clip("jump");
        machine.defaultState=movement;
        var up = movement.AddTransition(jump);
        up.hasExitTime=false; up.duration=0.1f;
        up.AddCondition(AnimatorConditionMode.IfNot,0,"Grounded");
        var down=jump.AddTransition(movement);
        down.hasExitTime=false; down.duration=0.12f;
        down.AddCondition(AnimatorConditionMode.If,0,"Grounded");
        return controller;
    }

    static Vector3 Ground(float x,float z)
    {
        if (!Physics.Raycast(new Vector3(x,100,z),Vector3.down,out var hit,160,ThirdPersonMotor.GroundMask))
            throw new InvalidOperationException($"No mapped walkable surface at {x},{z}");
        return hit.point;
    }

    public static object Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first");
        for(int i=0;i<SceneManager.sceneCount;i++)
            if(SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save unsaved scene work first");
        MakeFolder(Folder);
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/PadovaCentroSurvey.unity",OpenSceneMode.Single);
        EditorSceneManager.SaveScene(scene,ScenePath);
        var libraryPath=Folder+"/PlayableMeshes.asset";
        var library=AssetDatabase.LoadAssetAtPath<SurveyMeshLibrary>(libraryPath);
        if(!library){library=ScriptableObject.CreateInstance<SurveyMeshLibrary>();AssetDatabase.CreateAsset(library,libraryPath);}
        foreach(var mesh in AssetDatabase.LoadAllAssetsAtPath(libraryPath).OfType<Mesh>()) UnityEngine.Object.DestroyImmediate(mesh,true);
        var paving=Material("PavingPreview", new Color(0.88f,0.88f,0.86f),"Assets/Art/Surfaces/paving_diff.jpg","Assets/Art/Surfaces/paving_nor_gl.jpg");
        var plaster=Material("PlasterPreview",new Color(1.2f,1.17f,1.12f),"Assets/Art/Surfaces/plaster_diff.jpg");
        foreach(var feature in UnityEngine.Object.FindObjectsByType<SurveyFeature>(FindObjectsSortMode.None))
        {
            bool building=feature.Layer=="UN_VOL";
            feature.gameObject.layer=building?9:8;
            var mf=feature.GetComponent<MeshFilter>();
            var mesh=UnityEngine.Object.Instantiate(mf.sharedMesh);
            mesh.name=feature.name;
            var v=mesh.vertices; var n=mesh.normals; var uv=new Vector2[v.Length];
            for(int i=0;i<v.Length;i++)
            {
                if(Mathf.Abs(n[i].y)>0.7f) uv[i]=new Vector2(v[i].x,v[i].z)*0.5f;
                else if(Mathf.Abs(n[i].x)>Mathf.Abs(n[i].z)) uv[i]=new Vector2(v[i].z,v[i].y)*0.5f;
                else uv[i]=new Vector2(v[i].x,v[i].y)*0.5f;
            }
            mesh.uv=uv;
            mesh.RecalculateTangents();
            AssetDatabase.AddObjectToAsset(mesh,library);
            mf.sharedMesh=mesh;
            feature.GetComponent<MeshCollider>().sharedMesh=mesh;
            feature.GetComponent<MeshRenderer>().sharedMaterial=building?plaster:paving;
            // Offset the pedestrian overlay by 1 cm to prevent coincident survey surfaces flickering.
            if(feature.Layer=="AC_PED") feature.transform.position+=Vector3.up*0.01f;
            if(feature.Layer=="AR_MARC") feature.transform.position+=Vector3.up*0.02f;
        }
        Physics.SyncTransforms();
        var spawn=Ground(112,-104)+Vector3.up*0.08f;
        var goal=Ground(82,-104);
        var player=new GameObject("Player");
        player.layer=10;
        player.transform.position=spawn;
        var cc=player.AddComponent<CharacterController>();
        cc.height=1.78f;cc.center=new Vector3(0,0.90f,0);cc.radius=0.28f;
        cc.stepOffset=0.32f;cc.skinWidth=0.03f;cc.slopeLimit=50;cc.minMoveDistance=0;
        var motor=player.AddComponent<ThirdPersonMotor>();
        motor.Spawn=spawn;motor.Goal=goal;
        var visual=new GameObject("Character visual").transform;
        visual.SetParent(player.transform,false);
        var model=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Kenney/characterMedium.fbx"));
        model.transform.SetParent(visual,false);
        var renderers=model.GetComponentsInChildren<Renderer>();
        var bounds=renderers[0].bounds;
        foreach(var r in renderers) bounds.Encapsulate(r.bounds);
        float scale=1.75f/bounds.size.y;
        model.transform.localScale*=scale;
        bounds=renderers[0].bounds;
        foreach(var r in renderers) bounds.Encapsulate(r.bounds);
        model.transform.position+=Vector3.up*(spawn.y-bounds.min.y);
        var skin=Material("Character",Color.white,"Assets/Art/Kenney/skaterMaleA.png");
        foreach(var r in renderers){r.sharedMaterial=skin;r.gameObject.layer=10;}
        var animator=model.GetComponent<Animator>()??model.AddComponent<Animator>();
        animator.runtimeAnimatorController=CreateController();
        animator.applyRootMotion=false;
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        motor.Animator=animator;motor.Visual=visual;
        var camera=Camera.main;
        var survey=camera.GetComponent<SurveyExplorer>();
        UnityEngine.Object.DestroyImmediate(survey);
        camera.name="Third Person Camera";
        camera.fieldOfView=58;camera.nearClipPlane=0.1f;
        camera.clearFlags=CameraClearFlags.Skybox;
        var follow=camera.gameObject.AddComponent<ThirdPersonCamera>();
        follow.Player=motor;motor.FollowCamera=follow;
        var camData=camera.GetComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing=true;
        camData.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var marker=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        marker.name="Piazza walk destination";marker.layer=11;
        marker.transform.position=goal+Vector3.up*0.08f;
        marker.transform.localScale=new Vector3(1.1f,0.035f,1.1f);
        UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
        var gold=Material("Destination",new Color(0.97f,0.63f,0.14f));
        gold.EnableKeyword("_EMISSION");gold.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;gold.SetColor("_EmissionColor",new Color(0.5f,0.22f,0.02f));
        marker.GetComponent<Renderer>().sharedMaterial=gold;
        var hud=player.AddComponent<WalkHud>();hud.Player=motor;hud.GoalMarker=marker.transform;
        var light=UnityEngine.Object.FindFirstObjectByType<Light>();
        light.transform.rotation=Quaternion.Euler(38,-35,0);light.intensity=2.1f;
        light.color=new Color(1,0.93f,0.82f);
        light.shadowBias=0.03f;light.shadowNormalBias=0.25f;
        var skyPath=Folder+"/DaySky.mat";
        var sky=AssetDatabase.LoadAssetAtPath<Material>(skyPath);
        if(!sky){sky=new Material(Shader.Find("Skybox/Procedural"));AssetDatabase.CreateAsset(sky,skyPath);}
        sky.SetColor("_SkyTint",new Color(0.48f,0.56f,0.64f));
        sky.SetColor("_GroundColor",new Color(0.35f,0.32f,0.28f));
        sky.SetFloat("_AtmosphereThickness",0.9f);sky.SetFloat("_Exposure",1.15f);
        RenderSettings.skybox=sky;
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(0.52f,0.61f,0.70f);
        RenderSettings.ambientEquatorColor=new Color(0.46f,0.44f,0.4f);
        RenderSettings.ambientGroundColor=new Color(0.24f,0.22f,0.2f);
        RenderSettings.fog=true;RenderSettings.fogMode=FogMode.ExponentialSquared;
        RenderSettings.fogDensity=0.0018f;RenderSettings.fogColor=new Color(0.65f,0.73f,0.78f);
        var profilePath=Folder+"/Daylight.asset";
        var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
        if(!profile){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,profilePath);}
        if(!profile.TryGet<Tonemapping>(out var tone)){tone=profile.Add<Tonemapping>(true);AssetDatabase.AddObjectToAsset(tone,profile);}
        tone.mode.Override(TonemappingMode.ACES);
        var volume=new GameObject("Daylight grading").AddComponent<Volume>();volume.isGlobal=true;volume.profile=profile;
        motor.Respawn();
        camera.transform.position=spawn+new Vector3(4.3f,2.5f,0);
        camera.transform.rotation=Quaternion.Euler(14,270,0);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene,ScenePath);
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
        AssetDatabase.SaveAssets();
        Selection.activeGameObject=player;
        return new{scene=ScenePath,spawn=spawn.ToString(),goal=goal.ToString(),modelScale=scale,avatar=animator.avatar?animator.avatar.name:"missing"};
    }
}
