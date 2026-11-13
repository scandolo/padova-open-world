using UnityEngine;
using UnityEngine.InputSystem;

namespace Padova.World
{
    /// <summary>Time of day: moves the sun, dims sky, ambient light, fog and reflections, and publishes
    /// <c>_PadovaNight</c> (0 day .. 1 night) for lit windows, street lamps and headlights. T skips to the next part of the day.</summary>
    [DefaultExecutionOrder(90)]
    public sealed class DayCycle : MonoBehaviour
    {
        public static DayCycle Instance { get; private set; }
        /// <summary>Hours since midnight, 0–24.</summary>
        public float Hour = 9.5f;
        /// <summary>Real minutes for a full day; the dark hours pass at <see cref="NightRate"/> times that pace.</summary>
        public float MinutesPerDay = 48, NightRate = 3;
        public bool Running = true;
        /// <summary>0 in daylight, 1 in full night.</summary>
        public float Night { get; private set; }
        /// <summary>Sun elevation in degrees; negative below the horizon.</summary>
        public float Elevation { get; private set; }
        public int Skips { get; private set; }
        public const float Sunrise = 6, Sunset = 20.5f;

        Light sun;
        Material sky;
        Color sunColour, fogColour;
        UnityEngine.Rendering.SphericalHarmonicsL2 skyLight;
        float sunIntensity, reflections, exposure, skipTo = -1;
        InputAction skip;
        static readonly int NightId = Shader.PropertyToID("_PadovaNight"), Exposure = Shader.PropertyToID("_Exposure"), Tint = Shader.PropertyToID("_Tint");

        void Awake()
        {
            Instance = this;
            sun = RenderSettings.sun ? RenderSettings.sun : FindFirstObjectByType<Light>();
            if (sun) { sunColour = sun.color; sunIntensity = sun.intensity; }
            fogColour = RenderSettings.fogColor;
            skyLight = RenderSettings.ambientProbe;
            reflections = RenderSettings.reflectionIntensity;
            if (RenderSettings.skybox)
            {
                // A private copy: the sky asset on disk never changes with the clock.
                sky = new Material(RenderSettings.skybox) { hideFlags = HideFlags.DontSave };
                RenderSettings.skybox = sky;
                exposure = sky.HasProperty(Exposure) ? sky.GetFloat(Exposure) : 1;
            }
            skip = new InputAction("Skip time", InputActionType.Button, "<Keyboard>/t");
            skip.performed += _ => { if (!OpenWorld.Current || !OpenWorld.Current.Halted) SkipAhead(); };
            skip.Enable();
            Apply();
        }

        void OnDestroy()
        {
            skip?.Dispose();
            Shader.SetGlobalFloat(NightId, 0);
            if (Instance == this) Instance = null;
        }

        /// <summary>Morning, afternoon, sunset, night: T fast-forwards to the next one.</summary>
        public void SkipAhead()
        {
            float[] stops = { 8.5f, 14, 19.6f, 22.5f };
            float from = skipTo >= 0 ? skipTo : Hour, next = stops[0] + 24;
            foreach (float stop in stops) if (stop > from + .25f) { next = stop; break; }
            skipTo = Mathf.Repeat(next, 24);
            Skips++;
        }

        public void Set(float hour) { Hour = Mathf.Repeat(hour, 24); skipTo = -1; Apply(); }

        public string Clock => Mathf.FloorToInt(Hour).ToString("00") + ":" + (Mathf.FloorToInt(Hour * 60) % 60 / 5 * 5).ToString("00");

        void Update()
        {
            float dt = Time.deltaTime;
            if (skipTo >= 0)
            {
                // The sun sweeps across in a couple of seconds.
                float left = Mathf.Repeat(skipTo - Hour, 24), step = Mathf.Max(2.5f, left * 2.5f) * Time.unscaledDeltaTime;
                if (step >= left) { Hour = skipTo; skipTo = -1; } else Hour = Mathf.Repeat(Hour + step, 24);
            }
            else if (Running)
            {
                float rate = 24f / (MinutesPerDay * 60) * Mathf.Lerp(1, NightRate, Night);
                Hour = Mathf.Repeat(Hour + dt * rate, 24);
            }
            Apply();
        }

        /// <summary>Sun elevation (degrees) for an hour of the day: a sine arc between sunrise and sunset, a shallower one below the horizon.</summary>
        public static float ElevationAt(float hour)
        {
            float length = Sunset - Sunrise;
            if (hour >= Sunrise && hour <= Sunset) return 62 * Mathf.Sin(Mathf.PI * (hour - Sunrise) / length);
            float night = Mathf.Repeat(hour - Sunset, 24) / (24 - length);
            return -28 * Mathf.Sin(Mathf.PI * night);
        }

        void Apply()
        {
            Elevation = ElevationAt(Hour);
            // Ambient light fades through twilight; the sky itself stays bright until the sun is well down; lamps come on at sunset.
            float day = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-6, 8, Elevation));
            float bright = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-9, 3, Elevation));
            float golden = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-6, 1, Elevation)) * (1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(5, 30, Elevation)));
            Night = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-4, 3, Elevation));
            Shader.SetGlobalFloat(NightId, Night);
            if (sun)
            {
                // East at sunrise, south at midday, west at sunset; after dark the same light stands in for the moon.
                float azimuth = Mathf.Lerp(78, 292, Mathf.InverseLerp(Sunrise, Sunset, Hour));
                float sunLight = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-3, 12, Elevation)), moonLight = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-9, -4, Elevation));
                if (Elevation > -4)
                {
                    sun.transform.rotation = Quaternion.Euler(Mathf.Max(Elevation, 2.5f), azimuth + 180, 0);
                    sun.color = Color.Lerp(sunColour, new Color(1, .56f, .3f), golden * .85f);
                    sun.intensity = sunIntensity * sunLight;
                }
                else
                {
                    sun.transform.rotation = Quaternion.Euler(48, 205 + 180, 0);
                    sun.color = new Color(.56f, .68f, 1);
                    sun.intensity = sunIntensity * .11f * moonLight;
                }
            }
            RenderSettings.ambientProbe = skyLight * Mathf.Lerp(.2f, 1, Mathf.Max(day, bright * .6f));
            RenderSettings.reflectionIntensity = reflections * Mathf.Lerp(.1f, 1, day);
            var warm = Color.Lerp(fogColour, new Color(.86f, .62f, .46f), golden * .6f);
            var twilight = new Color(.13f, .17f, .32f);
            RenderSettings.fogColor = bright < .5f ? Color.Lerp(new Color(.022f, .03f, .058f), twilight, bright * 2) : Color.Lerp(twilight, warm, bright * 2 - 1);
            if (sky)
            {
                if (sky.HasProperty(Exposure)) sky.SetFloat(Exposure, exposure * Mathf.Lerp(.11f, 1, bright));
                if (sky.HasProperty(Tint)) sky.SetColor(Tint, Color.Lerp(new Color(.2f, .3f, .62f), Color.Lerp(new Color(.5f, .5f, .5f), new Color(.64f, .46f, .36f), golden * .75f), bright));
            }
        }

        void OnGUI()
        {
            var game = OpenWorld.Current;
            if (!game || game.MapOpen || game.Menu) return;
            var old = GUI.matrix;
            Hud.Begin();
            Hud.Text(new Rect(1370, 84, 200, 28), Clock + (skipTo >= 0 ? "  »" : ""), 22, Hud.Muted, TextAnchor.UpperRight, true);
            GUI.matrix = old;
        }
    }
}
