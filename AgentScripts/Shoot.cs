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

    static Vector3 Parse(string s)
    {
        var v = s.Split(',');
        return new Vector3(float.Parse(v[0], CultureInfo.InvariantCulture), float.Parse(v[1], CultureInfo.InvariantCulture), float.Parse(v[2], CultureInfo.InvariantCulture));
    }
}
