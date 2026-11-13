using System;
using System.IO;
using Padova.Architecture;
using Padova.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Replaces the survey blockout in PadovaPlayable with the generated architectural city.</summary>
public static class BuildPadovaCity
{
    const string Tex = "Assets/Art/Padova/Textures/";
    const string Mat = "Assets/Art/Padova/Materials/";
    const string ScenePath = "Assets/Scenes/PadovaPlayable.unity";
    const string PlanPath = "Assets/Geography/PadovaCentro/CityPlan.json";
    const string SkyPath = "Assets/Art/Padova/Sky/Kloofendal_PartlyCloudy_4k.hdr";

    // Documented wall colours of central Padova's lime-washed façades (ochres, Venetian reds, pinks, creams).
    static readonly Color[] Palette =
    {
        new Color(0.93f, 0.76f, 0.50f), new Color(0.96f, 0.88f, 0.72f), new Color(0.76f, 0.42f, 0.31f),
        new Color(0.92f, 0.66f, 0.56f), new Color(0.97f, 0.85f, 0.58f), new Color(0.88f, 0.58f, 0.38f),
        new Color(0.87f, 0.83f, 0.76f), new Color(0.84f, 0.60f, 0.55f), new Color(0.80f, 0.52f, 0.36f),
        new Color(0.95f, 0.92f, 0.86f)
    };

    static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        Folder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static Texture2D Texture(string file, bool normal = false, bool alpha = false)
    {
        var path = Tex + file;
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) throw new FileNotFoundException(path);
        bool dirty = false;
        if (normal && importer.textureType != TextureImporterType.NormalMap) { importer.textureType = TextureImporterType.NormalMap; dirty = true; }
        if (alpha && !importer.alphaIsTransparency) { importer.alphaIsTransparency = true; dirty = true; }
        if (importer.anisoLevel != 8) { importer.anisoLevel = 8; dirty = true; }
        if (importer.maxTextureSize != 2048) { importer.maxTextureSize = 2048; dirty = true; }
        if (dirty) importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material Lit(string name, Color color, string albedo = null, string normal = null, float tile = 1, float smooth = 0.15f,
                        float metal = 0, float bump = 1, bool cutout = false, bool twoSided = false, Vector2? tile2 = null)
    {
        var path = Mat + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        m.SetTexture("_BaseMap", albedo != null ? Texture(albedo, false, cutout) : null);
        var scale = tile2 ?? new Vector2(1 / tile, 1 / tile);
        m.SetTextureScale("_BaseMap", scale);
        if (normal != null)
        {
            m.SetTexture("_BumpMap", Texture(normal, true));
            m.SetFloat("_BumpScale", bump);
            m.EnableKeyword("_NORMALMAP");
        }
        else { m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
        m.SetFloat("_AlphaClip", cutout ? 1 : 0);
        m.SetFloat("_Cutoff", 0.5f);
        if (cutout) m.EnableKeyword("_ALPHATEST_ON"); else m.DisableKeyword("_ALPHATEST_ON");
        m.SetFloat("_Cull", twoSided ? 0 : 2);
        m.doubleSidedGI = twoSided;
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material[] Materials()
    {
        Folder("Assets/Art/Padova/Materials");
        var m = new Material[(int)Slot.Count];
        m[(int)Slot.ClockBlue]=Lit("Astronomical blue",new Color(.035f,.14f,.29f),smooth:.28f);
        m[(int)Slot.Gold]=Lit("Clock brass",new Color(.8f,.58f,.20f),smooth:.45f,metal:.6f);
        m[(int)Slot.Foliage]=Lit("Tree foliage",new Color(.16f,.28f,.10f),smooth:.06f,twoSided:true);
        m[(int)Slot.Bark]=Lit("Tree bark",new Color(.28f,.20f,.12f),smooth:.05f);
        m[(int)Slot.Asphalt]=Lit("Outer road",new Color(.15f,.16f,.17f),smooth:.08f);
        m[(int)Slot.VehicleBlue]=Lit("Vehicle blue",new Color(.10f,.27f,.36f),smooth:.72f,metal:.35f);
        m[(int)Slot.VehicleRed]=Lit("Vehicle red",new Color(.52f,.085f,.055f),smooth:.72f,metal:.35f);
        m[(int)Slot.White]=Lit("Painted ivory",new Color(.92f,.89f,.8f),smooth:.55f);
        m[(int)Slot.LampGlow]=Lit("Lantern glass",new Color(1,.78f,.4f),smooth:.55f);
        m[(int)Slot.LampGlow].EnableKeyword("_EMISSION");m[(int)Slot.LampGlow].SetColor("_EmissionColor",new Color(1,.55f,.16f)*1.4f);
        for (int i = 0; i < 10; i++)
            m[(int)Slot.Plaster0 + i] = Lit("Plaster " + i, Palette[i], "Plaster_Albedo.jpg", "Plaster_Normal.jpg", 3.2f, 0.1f, 0, 0.7f);
        m[(int)Slot.Stone] = Lit("Istrian stone", new Color(0.97f, 0.95f, 0.9f), "IstrianStone_Albedo.jpg", "IstrianStone_Normal.jpg", 1.6f, 0.28f);
        m[(int)Slot.Brick] = Lit("Brick", new Color(0.95f, 0.9f, 0.86f), "Brick_Albedo.jpg", "Brick_Normal.jpg", 2.2f, 0.1f);
        m[(int)Slot.Roof] = Lit("Coppi roof", new Color(1.0f, 0.93f, 0.88f), "RoofCoppi_Albedo.jpg", "RoofCoppi_Normal.jpg", 2.4f, 0.12f, 0, 1.2f);
        m[(int)Slot.Wood] = Lit("Eave wood", new Color(0.30f, 0.20f, 0.13f), null, null, 1, 0.2f);
        m[(int)Slot.Glass] = Lit("Window glass", new Color(0.09f, 0.12f, 0.15f), null, null, 1, 0.94f, 0.2f);
        m[(int)Slot.Frame] = Lit("Window frame", new Color(0.86f, 0.84f, 0.79f), null, null, 1, 0.35f);
        m[(int)Slot.ShutterGreen] = Lit("Shutter green", new Color(0.20f, 0.33f, 0.22f), "Shutter_Albedo.png", "Shutter_Normal.png", 0.56f, 0.3f);
        m[(int)Slot.ShutterBrown] = Lit("Shutter brown", new Color(0.40f, 0.25f, 0.15f), "Shutter_Albedo.png", "Shutter_Normal.png", 0.56f, 0.3f);
        m[(int)Slot.Iron] = Lit("Wrought iron", new Color(0.08f, 0.08f, 0.09f), null, null, 1, 0.45f, 0.6f);
        m[(int)Slot.ShopGlass] = Lit("Shop glass", new Color(0.06f, 0.08f, 0.1f), null, null, 1, 0.96f, 0.3f);
        m[(int)Slot.Door] = Lit("Door wood", new Color(0.24f, 0.14f, 0.08f), null, null, 1, 0.3f);
        m[(int)Slot.Lead] = Lit("Lead sheet", new Color(0.86f, 0.88f, 0.9f), "Lead_Albedo.jpg", "Lead_Normal.jpg", 2.4f, 0.45f, 0.35f);
        m[(int)Slot.Trachyte] = Lit("Trachyte paving", new Color(0.95f, 0.95f, 0.95f), "Trachyte_Albedo.jpg", "Trachyte_Normal.jpg", 4, 0.22f);
        m[(int)Slot.Sidewalk] = Lit("Sidewalk stone", Color.white, "Sidewalk_Albedo.jpg", "Sidewalk_Normal.jpg", 3, 0.2f);
        m[(int)Slot.PorticoFloor] = Lit("Portico floor", Color.white, "PorticoFloor_Albedo.jpg", "PorticoFloor_Normal.jpg", 1.6f, 0.45f);
        m[(int)Slot.Courtyard] = Lit("Courtyard brick", new Color(0.92f, 0.88f, 0.84f), "CourtyardBrick_Albedo.jpg", "CourtyardBrick_Normal.jpg", 2, 0.12f);
        m[(int)Slot.GreyStone] = Lit("Trachyte masonry", new Color(0.58f, 0.58f, 0.6f), "IstrianStone_Albedo.jpg", "IstrianStone_Normal.jpg", 1.6f, 0.18f);
        m[(int)Slot.Masonry] = Lit("Ragione banded masonry", Color.white, "RagioneMasonry_Albedo.jpg", "RagioneMasonry_Normal.jpg", 1, 0.12f, 0, 1, false, false, new Vector2(0.25f, 0.5f));
        m[(int)Slot.Ceiling] = Lit("Portico ceiling", new Color(0.93f, 0.9f, 0.84f), "Plaster_Albedo.jpg", null, 3.2f, 0.08f, 0, 1, false, true);
        m[(int)Slot.AwningGreen] = Lit("Awning green", new Color(0.13f, 0.30f, 0.21f), null, null, 1, 0.15f, 0, 1, false, true);
        m[(int)Slot.AwningRed] = Lit("Awning bordeaux", new Color(0.45f, 0.09f, 0.1f), null, null, 1, 0.15f, 0, 1, false, true);
        m[(int)Slot.Railing] = Lit("Railing", Color.white, "Railing.png", null, 1, 0.4f, 0.5f, 1, true, true);
        m[(int)Slot.Balustrade] = Lit("Balustrade", Color.white, "Balustrade.png", null, 1, 0.25f, 0, 1, true, true);
        m[(int)Slot.Dark] = Lit("Interior shadow", new Color(0.025f, 0.022f, 0.02f), null, null, 1, 0.05f);
        return m;
    }

    static void Lighting(Camera camera)
    {
        // HDRI sky (Poly Haven CC0) for sky, ambient and reflections.
        var sky = (TextureImporter)AssetImporter.GetAtPath(SkyPath);
        // No mip chain: the panoramic shader's longitude wraps once, and with mips that wrap draws a one-pixel seam up the sky.
        if (sky.maxTextureSize != 4096 || sky.mipmapEnabled) { sky.maxTextureSize = 4096; sky.mipmapEnabled = false; sky.SaveAndReimport(); }
        var skyMat = AssetDatabase.LoadAssetAtPath<Material>(Mat + "Sky.mat");
        if (!skyMat) { skyMat = new Material(Shader.Find("Skybox/Panoramic")); AssetDatabase.CreateAsset(skyMat, Mat + "Sky.mat"); }
        skyMat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture>(SkyPath));
        skyMat.SetFloat("_Exposure", 1.0f);
        skyMat.SetFloat("_Rotation", 110);
        skyMat.SetFloat("_Mapping", 1);
        skyMat.SetFloat("_ImageType", 0);
        EditorUtility.SetDirty(skyMat);
        RenderSettings.skybox = skyMat;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 0.85f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 1;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.0009f;
        RenderSettings.fogColor = new Color(0.66f, 0.74f, 0.83f);
        // Early-evening sun from the west-north-west (a June evening in Padova), warm and low.
        var sun = UnityEngine.Object.FindFirstObjectByType<Light>();
        float az = 292 * Mathf.Deg2Rad, alt = 27 * Mathf.Deg2Rad;
        var toSun = new Vector3(Mathf.Sin(az) * Mathf.Cos(alt), Mathf.Sin(alt), Mathf.Cos(az) * Mathf.Cos(alt));
        sun.transform.rotation = Quaternion.LookRotation(-toSun);
        sun.color = new Color(1.0f, 0.88f, 0.74f);
        sun.intensity = 3.1f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.92f;
        sun.shadowBias = 0.04f;
        sun.shadowNormalBias = 0.35f;
        RenderSettings.sun = sun;
        // Longer shadow range for streets and piazzas.
        foreach (var asset in new[] { "Assets/Settings/PC_RPAsset.asset" })
        {
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(asset);
            rp.shadowDistance = 170;
            rp.shadowCascadeCount = 4;
            rp.cascade4Split = new Vector3(0.05f, 0.16f, 0.42f);
            EditorUtility.SetDirty(rp);
        }
        camera.farClipPlane = 2500;
        camera.allowHDR = true;
        var data = camera.GetComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.antialiasingQuality = AntialiasingQuality.High;
        // Grading: filmic tonemapping, gentle bloom and vignette.
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Gameplay/Daylight.asset");
        T Get<T>() where T : VolumeComponent
        {
            if (!profile.TryGet<T>(out var c)) { c = profile.Add<T>(true); AssetDatabase.AddObjectToAsset(c, profile); }
            return c;
        }
        Get<Tonemapping>().mode.Override(TonemappingMode.ACES);
        var bloom = Get<Bloom>(); bloom.intensity.Override(0.25f); bloom.threshold.Override(1.1f); bloom.scatter.Override(0.6f);
        var grade = Get<ColorAdjustments>(); grade.postExposure.Override(0.1f); grade.contrast.Override(18); grade.saturation.Override(10);
        var vignette = Get<Vignette>(); vignette.intensity.Override(0.18f); vignette.smoothness.Override(0.5f);
        var wb = Get<WhiteBalance>(); wb.temperature.Override(6); wb.tint.Override(0);
        EditorUtility.SetDirty(profile);
    }

    public static object Build()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play Mode first.");
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var name in new[] { "Padova Centro | DBT 2007", "Continuous mapped paving", "Palazzo della Ragione | photographic elevations", "Padova city", "City reflections" })
        {
            var old = GameObject.Find(name);
            if (old) UnityEngine.Object.DestroyImmediate(old);
        }
        var mats = Materials();
        var cityGo = new GameObject("Padova city");
        var city = cityGo.AddComponent<PadovaCity>();
        city.Plan = AssetDatabase.LoadAssetAtPath<TextAsset>(PlanPath);
        city.Materials = mats;
        city.PreviewInEditor = true;
        city.Build();
        Physics.SyncTransforms();
        var probe = new GameObject("City reflections").AddComponent<ReflectionProbe>();
        probe.transform.position = new Vector3(60, 12, -30);
        probe.size = new Vector3(900, 200, 700);
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.resolution = 256;
        probe.boxProjection = false;
        probe.importance = 1;
        var old2 = GameObject.Find("City reflections");
        var player = UnityEngine.Object.FindFirstObjectByType<ThirdPersonMotor>();
        // Keep the tested spawn/goal route but place both on the new surface.
        float Ground(float x, float z)
        {
            if (!Physics.Raycast(new Vector3(x, 80, z), Vector3.down, out var hit, 160, ThirdPersonMotor.GroundMask))
                throw new InvalidOperationException($"No generated ground at {x},{z}");
            return hit.point.y;
        }
        player.Spawn = new Vector3(player.Spawn.x, Ground(player.Spawn.x, player.Spawn.z) + 0.06f, player.Spawn.z);
        player.Goal = new Vector3(player.Goal.x, Ground(player.Goal.x, player.Goal.z), player.Goal.z);
        player.Respawn();
        var marker = GameObject.Find("Piazza walk destination");
        if (marker) marker.transform.position = player.Goal + Vector3.up * 0.08f;
        Lighting(Camera.main);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return new { city.Units, city.Vertices, city.Triangles, city.Chunks, city.BuildMilliseconds, spawn = player.Spawn.ToString(), goal = player.Goal.ToString() };
    }
}
