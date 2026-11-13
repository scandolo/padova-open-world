using System.Collections.Generic;
using UnityEngine;

namespace Padova.World
{
    /// <summary>Builds vehicles from the Blender library (Resources/Vehicles/&lt;Name&gt;, see docs/CITY_ENGINE.md) with a plain
    /// stand-in when a model is missing. One place decides how a vehicle looks; CityCar decides how it drives.</summary>
    public static class VehicleFactory
    {
        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        static MaterialPropertyBlock block;
        static Material standIn;

        static GameObject Prefab(VehicleSpec spec)
        {
            if (!prefabs.TryGetValue(spec.Name, out var prefab)) prefabs[spec.Name] = prefab = Resources.Load<GameObject>("Vehicles/" + spec.Name);
            return prefab;
        }

        static Transform Find(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        /// <summary>Visual model in <paramref name="paint"/>. Wheels come back in CityCar order: front-left, front-right, rear-left,
        /// rear-right (front, rear-left, rear-right on three wheels; front, rear on two).</summary>
        public static GameObject Model(VehicleSpec spec, Color paint, out Transform[] wheels, out Transform steer)
        {
            var prefab = Prefab(spec);
            var go = prefab ? Object.Instantiate(prefab) : StandIn(spec);
            go.name = spec.Name;
            string[] names = spec.Wheels == 2 ? new[] { "Wheel_F", "Wheel_R" } : spec.Wheels == 3 ? new[] { "Wheel_F", "Wheel_RL", "Wheel_RR" } : new[] { "Wheel_FL", "Wheel_FR", "Wheel_RL", "Wheel_RR" };
            var found = new List<Transform>();
            foreach (string name in names) { var w = Find(go.transform, name); if (w) found.Add(w); }
            wheels = found.Count == names.Length ? found.ToArray() : new Transform[0];
            steer = Find(go.transform, "Steer");
            Tint(go, paint);
            return go;
        }

        /// <summary>Colours every "Paint" material slot of one vehicle without creating materials.</summary>
        public static void Tint(GameObject go, Color paint)
        {
            block ??= new MaterialPropertyBlock();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!materials[i] || materials[i].name != "Paint") continue;
                    r.GetPropertyBlock(block, i); block.SetColor("_BaseColor", paint); r.SetPropertyBlock(block, i);
                }
            }
        }

        /// <summary>Renderer slots that use the named material (police light bar, tail lights…).</summary>
        public static List<(Renderer renderer, int index)> Slots(GameObject go, string material)
        {
            var result = new List<(Renderer, int)>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var materials = r.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) if (materials[i] && materials[i].name == material) result.Add((r, i));
            }
            return result;
        }

        /// <summary>A vehicle the player or the police can drive.</summary>
        public static CityCar Drivable(VehicleSpec spec, Color paint, Vector3 position, Quaternion rotation, Transform parent)
        {
            var go = Model(spec, paint, out var wheels, out var steer);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            var car = CityCar.Equip(go, spec, wheels);
            if (steer) { car.SteerPivot = steer; car.Apply(spec); }
            return car;
        }

        /// <summary>Swaps a car saved in the scene for the catalog model at the same spot.</summary>
        public static CityCar Replace(CityCar old, VehicleSpec spec, Color paint)
        {
            var car = Drivable(spec, paint, old.transform.position, old.transform.rotation, old.transform.parent);
            car.name = old.name;
            Object.Destroy(old.gameObject);
            return car;
        }

        static GameObject StandIn(VehicleSpec spec)
        {
            if (!standIn) standIn = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Paint" };
            var go = new GameObject(spec.Name);
            void Part(string name, PrimitiveType type, Vector3 position, Vector3 scale, Quaternion rotation)
            {
                var part = GameObject.CreatePrimitive(type);
                Object.Destroy(part.GetComponent<Collider>());
                part.name = name; part.transform.SetParent(go.transform, false);
                part.transform.localPosition = position; part.transform.localRotation = rotation; part.transform.localScale = scale;
                part.GetComponent<Renderer>().sharedMaterial = standIn;
            }
            Part("Body", PrimitiveType.Cube, new Vector3(0, spec.Height * .5f + .1f, 0), new Vector3(spec.Width, spec.Height * .7f, spec.Length), Quaternion.identity);
            float half = spec.Wheels == 2 ? 0 : spec.Track * .5f, d = spec.WheelRadius * 2;
            var roll = Quaternion.Euler(0, 0, 90);
            if (spec.Wheels == 4) { Part("Wheel_FL", PrimitiveType.Cylinder, new Vector3(-half, spec.WheelRadius, spec.Wheelbase * .5f), new Vector3(d, .1f, d), roll); Part("Wheel_FR", PrimitiveType.Cylinder, new Vector3(half, spec.WheelRadius, spec.Wheelbase * .5f), new Vector3(d, .1f, d), roll); }
            else Part("Wheel_F", PrimitiveType.Cylinder, new Vector3(0, spec.WheelRadius, spec.Wheelbase * .5f), new Vector3(d, .06f, d), roll);
            if (spec.Wheels == 2) Part("Wheel_R", PrimitiveType.Cylinder, new Vector3(0, spec.WheelRadius, -spec.Wheelbase * .5f), new Vector3(d, .06f, d), roll);
            else { Part("Wheel_RL", PrimitiveType.Cylinder, new Vector3(-half, spec.WheelRadius, -spec.Wheelbase * .5f), new Vector3(d, .1f, d), roll); Part("Wheel_RR", PrimitiveType.Cylinder, new Vector3(half, spec.WheelRadius, -spec.Wheelbase * .5f), new Vector3(d, .1f, d), roll); }
            return go;
        }
    }
}
