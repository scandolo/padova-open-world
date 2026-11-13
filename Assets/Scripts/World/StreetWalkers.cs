using System.Collections.Generic;
using Padova.City;
using UnityEngine;

namespace Padova.World
{
    /// <summary>People on the pavements of the whole city. A small pool of walkers follows the player around: each one walks
    /// along the sidewalk of a surveyed street, crosses at junctions onto another, and steps aside for a car coming at them.
    /// No navigation mesh: the road graph and its measured widths are the path. The historic core keeps its own crowd.</summary>
    [DefaultExecutionOrder(126)]
    public sealed class StreetWalkers : MonoBehaviour
    {
        public OpenWorld Game;
        public int Count = 34;
        /// <summary>How many of the pool are out at once; the quality preset sets it.</summary>
        public int Limit = 26;
        [Tooltip("Walkers live inside this distance of the player (m).")] public float Radius = 120;
        /// <summary>Walkers on the street now, metres walked by all of them, and times one stepped out of a car's way.</summary>
        public int Walking, Dodges;
        public float Travel;

        sealed class Walker
        {
            public Transform Root;
            public Animator Animator;
            public int Edge;
            public float S, Side, Speed, Direction, Y, NextGround, NextLook, DodgeUntil;
            public Vector3 Dodge, Crossing;
            public bool Active, Cross;
        }

        readonly List<Walker> walkers = new List<Walker>();
        readonly List<int> lanes = new List<int>();
        List<int>[] atNode;
        float[] lengths;
        System.Random random = new System.Random(4127);
        float nextSpawn;

        void Build(CityRoads roads)
        {
            atNode = new List<int>[roads.NodeX.Length];
            lengths = new float[roads.Edges.Length];
            for (int e = 0; e < roads.Edges.Length; e++)
            {
                var edge = roads.Edges[e];
                for (int k = 2; k + 1 < edge.Points.Length; k += 2)
                    lengths[e] += Mathf.Sqrt((edge.Points[k] - edge.Points[k - 2]) * (edge.Points[k] - edge.Points[k - 2]) + (edge.Points[k + 1] - edge.Points[k - 1]) * (edge.Points[k + 1] - edge.Points[k - 1]));
                if (edge.Class == CityRoads.Fast || edge.Underpass || lengths[e] < 14) continue;
                (atNode[edge.A] ??= new List<int>()).Add(e);
                (atNode[edge.B] ??= new List<int>()).Add(e);
            }
        }

        bool Make()
        {
            var crowd = Game.Crowd;
            if (!crowd || !crowd.Character) return false;
            for (int i = 0; i < Count; i++)
            {
                var root = new GameObject("Walker " + (i + 1)).transform;
                root.SetParent(transform, false);
                var model = Instantiate(crowd.Character, root);
                model.SetActive(true);
                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one * (.93f + i % 7 * .02f);
                if (crowd.Skins != null && crowd.Skins.Length > 0)
                    foreach (var renderer in model.GetComponentsInChildren<SkinnedMeshRenderer>()) renderer.sharedMaterial = crowd.Skins[(i * 5 + 3) % crowd.Skins.Length];
                var animator = model.GetComponentInChildren<Animator>();
                if (animator) { animator.SetBool("Grounded", true); animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms; }
                root.gameObject.SetActive(false);
                walkers.Add(new Walker { Root = root, Animator = animator, Speed = 1.05f + i % 5 * .16f });
            }
            return true;
        }

        /// <summary>Where a walker stands: beside the carriageway on ordinary streets, anywhere in a pedestrian one.</summary>
        static Vector3 Spot(CityRoads roads, int edge, float s, float side, out Vector3 along)
        {
            var p = roads.At(edge, s, out var dir);
            p += new Vector2(dir.y, -dir.x) * side;
            along = new Vector3(dir.x, 0, dir.y);
            return new Vector3(p.x, 0, p.y);
        }

        float Margin(CityRoads roads, int edge, bool atB) => Mathf.Min(roads.Clear(atB ? roads.Edges[edge].B : roads.Edges[edge].A) * .6f, lengths[edge] * .3f);

        bool Free(Vector3 p) => !Physics.CheckSphere(p + Vector3.up * 1f, .4f, 1 << 9, QueryTriggerInteraction.Ignore);

        void Update()
        {
            if (!Game || !Game.City || Game.Halted) return;
            var roads = Game.City.Roads;
            var net = Game.Traffic ? Game.Traffic.Network : null;
            if (roads == null || net == null) return;
            if (atNode == null) Build(roads);
            if (walkers.Count == 0 && !Make()) { enabled = false; return; }
            var focus = Game.Position;
            float dt = Time.deltaTime;
            var car = Game.Mode == "drive" ? Game.Driving : null;
            int walking = 0;
            foreach (var w in walkers)
            {
                if (!w.Active) continue;
                var here = w.Root.position;
                if ((here - focus).sqrMagnitude > (Radius + 35) * (Radius + 35)) { w.Active = false; w.Root.gameObject.SetActive(false); continue; }
                walking++;
                Step(roads, w, dt, car);
            }
            Walking = walking;
            if (Time.time >= nextSpawn && walking < Mathf.Min(Limit, Count))
            {
                nextSpawn = Time.time + .15f;
                foreach (var w in walkers) if (!w.Active) { Spawn(roads, net, w, focus); break; }
            }
        }

        void Spawn(CityRoads roads, RoadNetwork net, Walker w, Vector3 focus)
        {
            net.LanesNear(focus, Radius, lanes);
            if (lanes.Count == 0) return;
            var camera = Camera.main;
            var core = Game.City.Streamer && Game.City.Streamer.Pack != null ? Game.City.Streamer.Pack.Index.coreExtent : null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int edge = net.Lanes[lanes[random.Next(lanes.Count)]].Edge;
                if (edge < 0 || lengths[edge] < 14) continue;
                var e = roads.Edges[edge];
                if (e.Class == CityRoads.Fast || e.Underpass || e.Bridge && e.Width < 6) continue;
                float sign = random.Next(2) == 0 ? 1 : -1;
                float side = e.Pedestrian ? sign * Mathf.Max(.4f, (float)random.NextDouble() * (e.Width * .5f - .8f)) : sign * (e.Width * .5f + 1.05f + (float)random.NextDouble() * .5f);
                float s = Margin(roads, edge, false) + (float)random.NextDouble() * Mathf.Max(1, lengths[edge] - Margin(roads, edge, false) - Margin(roads, edge, true));
                var p = Spot(roads, edge, s, side, out var along);
                float d = (p.x - focus.x) * (p.x - focus.x) + (p.z - focus.z) * (p.z - focus.z);
                if (d < 22 * 22 || d > Radius * Radius) continue;
                if (core != null && core.Length == 4 && p.x > core[0] && p.x < core[2] && p.z > core[1] && p.z < core[3]) continue;
                p.y = Game.SurfaceAt(p);
                if (!Free(p) || !Free(p + along * 3) || !Free(p - along * 3)) continue;
                // Not popping up in plain sight.
                if (camera && d < 70 * 70) { var v = camera.WorldToViewportPoint(p + Vector3.up); if (v.z > 0 && v.x > 0 && v.x < 1 && v.y > 0 && v.y < 1) continue; }
                w.Edge = edge; w.S = s; w.Side = side; w.Direction = random.Next(2) == 0 ? 1 : -1; w.Y = p.y; w.Cross = false; w.Dodge = Vector3.zero; w.DodgeUntil = 0;
                w.Root.SetPositionAndRotation(p, Quaternion.LookRotation(along * w.Direction));
                w.Root.gameObject.SetActive(true);
                w.Active = true;
                return;
            }
        }

        void Step(CityRoads roads, Walker w, float dt, CityCar car)
        {
            Vector3 target, along;
            float speed = w.Speed;
            if (w.Cross)
            {
                // Over the junction in a straight line to the pavement of the next street.
                target = w.Crossing;
                var to = target - w.Root.position; to.y = 0;
                if (to.sqrMagnitude < .09f) { w.Cross = false; return; }
                along = to.normalized;
                target = w.Root.position + along * (speed * dt);
            }
            else
            {
                w.S += w.Direction * speed * dt;
                float low = Margin(roads, w.Edge, false), high = lengths[w.Edge] - Margin(roads, w.Edge, true);
                if (w.S < low || w.S > high) { Turn(roads, w, w.S > high); return; }
                target = Spot(roads, w.Edge, w.S, w.Side, out along);
                along *= w.Direction;
                if (Time.time >= w.NextLook)
                {
                    w.NextLook = Time.time + .5f;
                    var ahead = target + along * 1.3f; ahead.y = w.Y;
                    if (!Free(ahead)) { w.Direction = -w.Direction; return; }
                }
            }
            // A car bearing down: step smartly out of its line.
            if (car && Time.time > w.DodgeUntil && car.Speed > 3.5f)
            {
                var from = w.Root.position - car.transform.position; from.y = 0;
                var heading = car.Body.linearVelocity; heading.y = 0;
                float distance = from.magnitude, closing = Vector3.Dot(heading.normalized, from.normalized);
                if (distance < 4 + car.Speed * .55f && closing > .82f)
                {
                    var away = Vector3.Cross(Vector3.up, heading.normalized);
                    if (Vector3.Dot(away, from) < 0) away = -away;
                    w.Dodge = away * 2.4f; w.DodgeUntil = Time.time + 1.6f; Dodges++;
                }
            }
            bool dodging = Time.time < w.DodgeUntil;
            if (Time.time >= w.NextGround) { w.NextGround = Time.time + .25f; w.Y = Game.SurfaceAt(target); }
            var want = target + (dodging ? w.Dodge : Vector3.zero);
            want.y = w.Y;
            var now = w.Root.position;
            var next = Vector3.MoveTowards(now, want, (dodging ? 5.5f : speed + 1.5f) * dt);
            next.y = Mathf.Lerp(now.y, w.Y, 1 - Mathf.Exp(-10 * dt));
            Travel += Vector3.Distance(now, next);
            w.Root.position = next;
            var move = next - now; move.y = 0;
            var face = move.sqrMagnitude > 1e-6f ? move.normalized : along;
            w.Root.rotation = Quaternion.Slerp(w.Root.rotation, Quaternion.LookRotation(face), 1 - Mathf.Exp(-8 * dt));
            if (w.Animator) w.Animator.SetFloat("Speed", dodging ? 5.5f : speed, .12f, dt);
        }

        /// <summary>At the end of a street: carry on along another one that meets it, or turn back at a dead end.</summary>
        void Turn(CityRoads roads, Walker w, bool atB)
        {
            int node = atB ? roads.Edges[w.Edge].B : roads.Edges[w.Edge].A;
            var options = atNode[node];
            int next = -1;
            if (options != null && options.Count > 1)
                for (int tries = 0; tries < 4 && next < 0; tries++)
                {
                    int pick = options[random.Next(options.Count)];
                    if (pick != w.Edge) next = pick;
                }
            if (next < 0)
            {
                w.Direction = -w.Direction;
                w.S = Mathf.Clamp(w.S, Margin(roads, w.Edge, false) + .05f, lengths[w.Edge] - Margin(roads, w.Edge, true) - .05f);
                return;
            }
            var e = roads.Edges[next];
            bool fromA = e.A == node;
            float sign = random.Next(2) == 0 ? 1 : -1;
            w.Edge = next;
            w.Direction = fromA ? 1 : -1;
            w.S = fromA ? Margin(roads, next, false) + .1f : lengths[next] - Margin(roads, next, true) - .1f;
            w.Side = e.Pedestrian ? sign * Mathf.Max(.4f, e.Width * .25f) : sign * (e.Width * .5f + 1.2f);
            var start = Spot(roads, next, w.S, w.Side, out _);
            start.y = w.Y;
            if (!Free(start)) { w.Side = -w.Side; start = Spot(roads, next, w.S, w.Side, out _); start.y = w.Y; }
            w.Crossing = start;
            w.Cross = true;
        }
    }
}
