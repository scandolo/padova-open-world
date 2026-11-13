using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Ad-hoc offscreen captures for agents: Shoot.Views("name:ex,ey,ez:tx,ty,tz;...", "outDir").
/// Unlike CaptureViews this takes viewpoints as an argument, so a new angle needs no code change.</summary>
public static class Shoot
{
    public static object Views(string spec, string outDir = ".context/views", int width = 1600, int height = 900, float fov = 60)
    {
        outDir = Path.GetFullPath(outDir);
        Directory.CreateDirectory(outDir);
        var go = new GameObject("Shoot camera") { hideFlags = HideFlags.HideAndDontSave };
        var cam = go.AddComponent<Camera>();
        if (Camera.main) cam.CopyFrom(Camera.main);
        cam.fieldOfView = fov;
        cam.farClipPlane = 6000;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var written = new List<string>();
        foreach (var view in spec.Split(';'))
        {
            var parts = view.Trim().Split(':');
            if (parts.Length != 3) continue;
            Vector3 eye = Parse(parts[1]), target = Parse(parts[2]);
            go.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                mb.SendMessage("RefreshVisibility", eye, SendMessageOptions.DontRequireReceiver);
            cam.targetTexture = rt;
            RenderPipeline.SubmitRenderRequest(cam, new UniversalRenderPipeline.SingleCameraRequest { destination = rt });
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            var path = Path.Combine(outDir, parts[0] + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            written.Add(path);
        }
        RenderTexture.active = null;
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(go);
        return written;
    }

    /// <summary>Street-level shots that cannot end up inside a building: "via roma@0.3;corso milano@0.6" picks the named
    /// road centreline at that fraction of its length and looks along it from eye height.</summary>
    public static object Streets(string spec, string outDir = ".context/views", float height = 1.7f, float side = 0)
    {
        var world = Object.FindFirstObjectByType<Padova.Architecture.WorldContext>();
        var views = new List<string>();
        foreach (var item in spec.Split(';'))
        {
            var parts = item.Trim().Split('@');
            float t = parts.Length > 1 ? float.Parse(parts[1], CultureInfo.InvariantCulture) : .5f;
            Padova.Architecture.WorldRoad best = null;
            foreach (var road in world.Data.roads)
                if (road.name != null && road.name.ToLowerInvariant().Contains(parts[0].ToLowerInvariant()) && (best == null || road.points.Length > best.points.Length)) best = road;
            if (best == null) continue;
            int n = best.points.Length / 2, i = Mathf.Clamp((int)(t * (n - 1)), 0, n - 2);
            var a = new Vector3(best.points[2 * i], 0, best.points[2 * i + 1]);
            var b = new Vector3(best.points[2 * i + 2], 0, best.points[2 * i + 3]);
            var dir = (b - a).normalized;
            var eye = a + new Vector3(dir.z, 0, -dir.x) * side;
            eye.y = world.Data.GroundAt(eye.x, eye.z) + height;
            var target = eye + dir * 40 + Vector3.up * 3;
            string label = parts[0].Replace(' ', '-') + "-" + Mathf.RoundToInt(t * 100);
            views.Add(string.Format(CultureInfo.InvariantCulture, "{0}:{1},{2},{3}:{4},{5},{6}", label, eye.x, eye.y, eye.z, target.x, target.y, target.z));
        }
        var files = (List<string>)Views(string.Join(";", views), outDir);
        files.AddRange(views);   // the poses, so a detail can be re-shot from the same spot
        return files;
    }

    static Vector3 Parse(string s)
    {
        var v = s.Split(',');
        return new Vector3(float.Parse(v[0], CultureInfo.InvariantCulture), float.Parse(v[1], CultureInfo.InvariantCulture), float.Parse(v[2], CultureInfo.InvariantCulture));
    }
}
