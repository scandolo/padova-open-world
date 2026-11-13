using UnityEngine;
using UnityEngine.Rendering;

namespace Padova.World
{
    /// <summary>True when the test harness launched this Player (it always passes -logFile): such runs stay silent.</summary>
    public static class Harness
    {
        public static readonly bool Launched = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-logFile") >= 0;
    }

    /// <summary>Adds the systems that need no scene wiring when the world loads: clock, sound, night lights, the far ground.</summary>
    static class WorldBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var game = Object.FindFirstObjectByType<OpenWorld>();
            if (!game) return;
            T Add<T>(string name) where T : Component
            {
                var go = new GameObject(name);
                go.transform.SetParent(game.transform, false);
                return go.AddComponent<T>();
            }
            Add<DayCycle>("Day and night");
            Add<NightLights>("Night lights").Game = game;
            Add<FarGround>("Far ground").Game = game;
            // Sound is built with the game reference in place before its Awake runs.
            var audio = new GameObject("City audio");
            audio.SetActive(false);
            audio.transform.SetParent(game.transform, false);
            var sound = audio.AddComponent<CityAudio>();
            sound.Game = game;
            if (Harness.Launched) sound.Volume = 0;
            audio.SetActive(true);
        }
    }

    /// <summary>Beyond the streamed tiles the survey map lies flat on the ground, so from a height the city runs on to the horizon
    /// instead of ending at the loading distance. A plan, not geometry: fog covers the difference.</summary>
    public sealed class FarGround : MonoBehaviour
    {
        public OpenWorld Game;

        void Start()
        {
            if (!Game || !Game.MapTexture || Game.City == null || Game.City.Data == null) return;
            var e = Game.MapExtent;
            float low = float.MaxValue;
            for (int i = 0; i <= 24; i++)
                for (int j = 0; j <= 24; j++)
                    low = Mathf.Min(low, Game.City.Data.GroundAt(Mathf.Lerp(e.x, e.z, i / 24f), Mathf.Lerp(e.y, e.w, j / 24f)));
            float y = low - 3;
            var tint = new Color(.6f, .66f, .5f);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var plan = new Material(shader) { hideFlags = HideFlags.DontSave };
            plan.SetTexture("_BaseMap", Game.MapTexture);
            plan.SetColor("_BaseColor", tint);
            plan.SetFloat("_Smoothness", 0);
            var beyond = new Material(shader) { hideFlags = HideFlags.DontSave };
            beyond.SetColor("_BaseColor", new Color(.52f, .58f, .42f));
            beyond.SetFloat("_Smoothness", 0);
            Quad("Survey plan", plan, e.x, e.y, e.z, e.w, y);
            Quad("Beyond the survey", beyond, e.x - 30000, e.y - 30000, e.z + 30000, e.w + 30000, y - 1);
        }

        void Quad(string name, Material material, float x0, float z0, float x1, float z1, float y)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(new[] { new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1) });
            mesh.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            mesh.SetNormals(new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
        }
    }
}
