using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Imports the Blender vehicle library (Tools/blender/build_vehicles.py): maps the FBX material names onto
/// shared URP materials, checks the axis convention against the manifest and saves one prefab per vehicle under
/// Assets/Resources/Vehicles, where VehicleCatalog loads them by name.</summary>
public static class BuildVehicles
{
    const string Folder = "Assets/Art/Vehicles", Out = "Assets/Resources/Vehicles";
sealed class Entry { public string name, file; public List<(string name, Vector3 position)> wheels = new List<(string, Vector3)>(); }

    /// <summary>Reads names, files and wheel positions from Vehicles.json. JsonUtility cannot fill types that live in a run_script assembly.</summary>
    static List<Entry> ReadManifest()
    {
        string text = File.ReadAllText(Folder + "/Vehicles.json");
        var entries = new List<Entry>();
        var vehicle = new System.Text.RegularExpressions.Regex("\"name\":\\s*\"(\\w+)\",\\s*\"file\":\\s*\"([^\"]+)\"");
        var wheel = new System.Text.RegularExpressions.Regex("\"name\":\\s*\"(Wheel_\\w+)\",\\s*\"position\":\\s*\\[\\s*([-\\d.eE]+),\\s*([-\\d.eE]+),\\s*([-\\d.eE]+)\\s*\\]");
        var matches = vehicle.Matches(text);
        for (int i = 0; i < matches.Count; i++)
        {
            var entry = new Entry { name = matches[i].Groups[1].Value, file = matches[i].Groups[2].Value };
            int end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            foreach (System.Text.RegularExpressions.Match w in wheel.Matches(text.Substring(matches[i].Index, end - matches[i].Index)))
                entry.wheels.Add((w.Groups[1].Value, new Vector3(float.Parse(w.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(w.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture), float.Parse(w.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture))));
            entries.Add(entry);
        }
        return entries;
    }

    static Material Ensure(string name, Color colour, float smooth, float metal, Color? emission = null)
    {
        string dir = Folder + "/Materials", path = dir + "/" + name + ".mat";
        if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(Folder, "Materials");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name }; AssetDatabase.CreateAsset(mat, path); }
        mat.SetColor("_BaseColor", colour); mat.color = colour; mat.SetFloat("_Smoothness", smooth); mat.SetFloat("_Metallic", metal);
        if (emission.HasValue) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", emission.Value); mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        mat.enableInstancing = true;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Dictionary<string, Material> Materials() => new Dictionary<string, Material>
    {
        ["Paint"] = Ensure("Paint", Color.white, .78f, .25f),                      // tinted per instance
        ["Paint2"] = Ensure("Paint2", new Color(.93f, .93f, .9f), .7f, .2f),
        ["Glass"] = Ensure("Glass", new Color(.05f, .07f, .09f), .96f, .3f),
        ["Chrome"] = Ensure("Chrome", new Color(.82f, .84f, .86f), .9f, 1f),
        ["Rubber"] = Ensure("Rubber", new Color(.03f, .03f, .035f), .25f, 0f),
        ["Trim"] = Ensure("Trim", new Color(.07f, .07f, .08f), .4f, .1f),
        ["LightFront"] = Ensure("LightFront", new Color(1f, .96f, .85f), .9f, 0f, new Color(1f, .93f, .75f) * .6f),
        ["LightRear"] = Ensure("LightRear", new Color(.75f, .05f, .04f), .8f, 0f, new Color(.9f, .05f, .03f) * .5f),
        ["LightBlue"] = Ensure("LightBlue", new Color(.1f, .3f, 1f), .9f, 0f, new Color(.1f, .35f, 1f) * .4f),
        ["Interior"] = Ensure("Interior", new Color(.06f, .06f, .065f), .2f, 0f),
        ["Seat"] = Ensure("Seat", new Color(.36f, .22f, .13f), .35f, 0f),
        ["Plate"] = Ensure("Plate", new Color(.94f, .94f, .92f), .5f, 0f),
        ["Sign"] = Ensure("Sign", new Color(.98f, .86f, .25f), .5f, 0f, new Color(1f, .85f, .3f) * .5f),
    };

    public static object Import()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var manifest = ReadManifest();
        var materials = Materials();
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(Out)) AssetDatabase.CreateFolder("Assets/Resources", "Vehicles");
        var report = new List<object>();
        foreach (var entry in manifest)
        {
            string path = entry.file;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            mi.globalScale = 1; mi.useFileScale = true; mi.addCollider = false; mi.isReadable = false;
            mi.importAnimation = false; mi.importCameras = false; mi.importLights = false; mi.importNormals = ModelImporterNormals.Import;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; mi.materialLocation = ModelImporterMaterialLocation.InPrefab;
            var names = mi.GetExternalObjectMap().Keys.Where(k => k.type == typeof(Material)).Select(k => k.name)
                .Concat(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(m => m.name)).Distinct().ToList();
            foreach (var id in mi.GetExternalObjectMap().Keys.ToList()) mi.RemoveRemap(id);
            var missing = new List<string>();
            foreach (var name in names)
            {
                if (materials.TryGetValue(name.Split('.')[0], out var target)) mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), name), target);
                else missing.Add(name);
            }
            // The exporter bakes the axis change into the mesh data; keep whichever import setting puts the wheels where the manifest says.
            float error = float.MaxValue;
            foreach (bool bake in new[] { false, true })
            {
                mi.bakeAxisConversion = bake; mi.SaveAndReimport();
                error = WheelError(path, entry);
                if (error < .03f) break;
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = entry.name;
            var lod1 = instance.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Body_LOD1");
            if (lod1)
            {
                var near = instance.GetComponentsInChildren<Renderer>(true).Where(r => r.transform != lod1).ToArray();
                var group = instance.AddComponent<LODGroup>();
                group.SetLODs(new[] { new LOD(.12f, near), new LOD(.008f, new[] { lod1.GetComponent<Renderer>() }) });
                group.RecalculateBounds();
            }
            var bounds = new Bounds(instance.transform.position, Vector3.zero);
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true)) bounds.Encapsulate(r.bounds);
            PrefabUtility.SaveAsPrefabAsset(instance, Out + "/" + entry.name + ".prefab");
            UnityEngine.Object.DestroyImmediate(instance);
            report.Add(new { entry.name, wheelError = error, size = bounds.size.ToString("F2"), missing = string.Join(",", missing), children = string.Join(",", model.GetComponentsInChildren<Transform>(true).Select(t => t.name).Take(9)) });
        }
        AssetDatabase.SaveAssets();
        return report;
    }

    static float WheelError(string path, Entry entry)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        float worst = 0;
        foreach (var w in entry.wheels)
        {
            var t = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(c => c.name == w.name);
            if (!t) return 9;
            worst = Mathf.Max(worst, Vector3.Distance(model.transform.InverseTransformPoint(t.position), w.position));
        }
        return worst;
    }
}
