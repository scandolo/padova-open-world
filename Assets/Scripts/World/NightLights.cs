using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Padova.World
{
    /// <summary>Headlights after dark. The car the player drives gets one real spot light, carried above and behind the
    /// bonnet so the road is lit evenly instead of burning out at the bumper; traffic gets a soft pool on the road ahead.</summary>
    [DefaultExecutionOrder(220)]
    public sealed class NightLights : MonoBehaviour
    {
        public OpenWorld Game;
        public bool On { get; private set; }
        public int Beams { get; private set; }
        Light beam;
        CityCar car;
        Mesh pool;
        Material glow;
        readonly List<GameObject> pools = new List<GameObject>();
        float nextScan;
        bool dark;

        void Awake()
        {
            var go = new GameObject("Headlights");
            go.transform.SetParent(transform, false);
            beam = go.AddComponent<Light>();
            beam.type = LightType.Spot;
            beam.range = 80;
            beam.spotAngle = 62;
            beam.innerSpotAngle = 36;
            beam.color = new Color(1, .95f, .82f);
            beam.shadows = LightShadows.None;
            beam.cullingMask = ~(1 << 11);          // not the vehicles themselves: the lamp sits above the roof
            beam.enabled = false;
            var shader = Shader.Find("Padova/CityGlow");
            if (!shader) return;
            glow = new Material(shader) { hideFlags = HideFlags.DontSave };
            glow.SetFloat("_Gain", .2f);
            pool = new Mesh { name = "Headlight pool", hideFlags = HideFlags.DontSave };
            pool.SetVertices(new[] { new Vector3(-4.5f, .12f, 0), new Vector3(4.5f, .12f, 0), new Vector3(4.5f, .12f, 15), new Vector3(-4.5f, .12f, 15) });
            pool.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
            var warm = new Color32(255, 240, 205, 255);
            pool.SetColors(new[] { warm, warm, warm, warm });
            pool.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        }

        void LateUpdate()
        {
            if (!Game) return;
            var driven = Game.Mode == "drive" ? Game.Driving : null;
            if (driven != car)
            {
                car = driven;
                beam.transform.SetParent(car ? car.transform : transform, false);
                if (car)
                {
                    beam.transform.localPosition = new Vector3(0, 3.2f, -1);
                    beam.transform.localRotation = Quaternion.Euler(9.5f, 0, 0);
                }
            }
            float night = DayCycle.Instance ? DayCycle.Instance.Night : 0;
            On = car && night > .15f;
            beam.enabled = On;
            beam.intensity = 300 * Mathf.InverseLerp(.15f, .6f, night);
            Traffic(night > .15f);
        }

        /// <summary>Every traffic vehicle carries a pool of light on the road ahead, shown only after dark.</summary>
        void Traffic(bool night)
        {
            if (!pool || !Game.Traffic) return;
            if (night && Time.time >= nextScan)
            {
                nextScan = Time.time + 1;
                pools.RemoveAll(p => !p);
                foreach (var v in Game.Traffic.Vehicles)
                {
                    if (!v.Go || v.Go.transform.Find("Headlight pool")) continue;
                    var go = new GameObject("Headlight pool");
                    go.transform.SetParent(v.Go.transform, false);
                    go.transform.localPosition = new Vector3(0, 0, v.Spec.Length * .5f - .5f);
                    go.AddComponent<MeshFilter>().sharedMesh = pool;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = glow;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    go.SetActive(dark);
                    pools.Add(go);
                }
                Beams = pools.Count;
            }
            if (night == dark) return;
            dark = night;
            foreach (var p in pools) if (p) p.SetActive(night);
        }
    }
}
