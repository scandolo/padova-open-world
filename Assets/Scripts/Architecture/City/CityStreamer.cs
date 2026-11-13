using System.Collections.Generic;
using System.Threading.Tasks;
using Padova.Architecture;
using UnityEngine;
using UnityEngine.Rendering;

namespace Padova.City
{
    /// <summary>Streams the compiled city around the camera: every tile gets a one-material distant version
    /// (ground, roofs, walls with procedural windows, trees), nearby tiles swap their walls for modelled façades,
    /// and colliders exist only where something can touch them. Meshes are built on worker threads.</summary>
    [ExecuteAlways, DefaultExecutionOrder(-440)]
    public sealed class CityStreamer : MonoBehaviour
    {
        public PadovaCity Core;
        public Material CityMaterial;
        [Tooltip("Distance to which the distant city is kept loaded (m).")] public float ShellRadius = 1500;
        [Tooltip("Distance inside which walls become modelled façades (m).")] public float DetailRadius = 150;
        [Tooltip("Distance inside which ground and walls collide (m).")] public float ColliderRadius = 430;
        public int GroundLayer = 8, BuildingLayer = 9, MaxJobs = 3;
        public bool PreviewInEditor = true;

        [Header("State")]
        public int Tiles;
        public int DetailTiles, ColliderTiles, Pending;
        /// <summary>Tiles inside the collider radius whose ground and walls are not solid yet.</summary>
        public int MissingColliders;
        public long Vertices;
        public float LastApplyMilliseconds, SlowestApplyMilliseconds;
        /// <summary>Accumulated cost by stage (ms): worker meshing, worker facades, collider cooking, main-thread uploads.</summary>
        public double ShellMs, DetailMs, BakeMs, UploadMs;
        public int ShellJobs, DetailJobs, BakeJobs;
        long shellTicks, detailTicks, bakeTicks;

        public CityPack Pack { get; private set; }
        Texture2DArray layers;
        Material runtimeMaterial;
        Transform root;
        float nextScan;
        Vector3 lastFocus = new Vector3(float.MaxValue, 0, 0);
        readonly Dictionary<long, Tile> tiles = new Dictionary<long, Tile>();
        readonly List<Tile> scratch = new List<Tile>();

        sealed class Tile
        {
            public int Tx, Tz;
            public float Distance;
            public GameObject Root, ShellGo, DetailGo;
            public Mesh Ground, Always, Shell, Detail, Collider;
            public MeshCollider GroundCollider, WallCollider;
            public TileData Data;
            public Task<TileBuild> ShellJob, DetailJob;
            public Task BakeJob;
            public List<Vector3> ColliderP;
            public List<int> ColliderI;
            public bool Loaded, Empty, Baked, NoDetail;
            public long Vertices;
        }

        void OnEnable()
        {
            if (Application.isPlaying || PreviewInEditor) Open();
        }

        void OnDisable() => Close();

        public bool Open()
        {
            if (Pack != null) return true;
            string folder = CityPack.DefaultFolder;
            if (!System.IO.File.Exists(System.IO.Path.Combine(folder, "city.json"))) return false;
            if (!Core) Core = FindFirstObjectByType<PadovaCity>();
            Pack = new CityPack(folder);
            root = new GameObject("Streamed city") { hideFlags = HideFlags.DontSave }.transform;
            root.SetParent(transform, false);
            layers = CityStyle.BuildLayers(Core ? Core.Materials : null);
            var source = CityMaterial ? CityMaterial : null;
            runtimeMaterial = source ? new Material(source) : new Material(Shader.Find("Padova/City"));
            runtimeMaterial.hideFlags = HideFlags.DontSave;
            runtimeMaterial.SetTexture("_Layers", layers);
            runtimeMaterial.SetFloat("_WaterLayer", (float)Layer.Water);
            return true;
        }

        public void Close()
        {
            foreach (var tile in tiles.Values) Unload(tile);
            tiles.Clear();
            Kill(root ? root.gameObject : null); Kill(layers); Kill(runtimeMaterial);
            root = null; layers = null; runtimeMaterial = null; Pack = null;
            Tiles = DetailTiles = ColliderTiles = Pending = 0; Vertices = 0;
            lastFocus = new Vector3(float.MaxValue, 0, 0);
        }

        static void Kill(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        void Start()
        {
            // Stand on real ground from the first frame; the rest of the city streams in behind the fog.
            if (Application.isPlaying && Camera.main) Prepare(Camera.main.transform.position);
        }

        void Update()
        {
            if (!Application.isPlaying || Pack == null) return;
            var camera = Camera.main;
            if (!camera) return;
            var focus = camera.transform.position;
            if (Time.unscaledTime >= nextScan || (focus - lastFocus).sqrMagnitude > 32 * 32)
            {
                nextScan = Time.unscaledTime + .25f;
                lastFocus = focus;
                Scan(focus);
            }
            Pump(focus, false);
        }

        /// <summary>Editor previews and captures: load everything the eye needs, synchronously.</summary>
        public void RefreshVisibility(Vector3 eye)
        {
            if (Application.isPlaying) return;
            if (Pack == null && !Open()) return;
            Sync(eye, ShellRadius, ColliderRadius, DetailRadius);
        }

        float TileDistance(int tx, int tz, Vector3 p)
        {
            float t = Pack.Tile, dx = Mathf.Max(tx * t - p.x, 0, p.x - (tx + 1) * t), dz = Mathf.Max(tz * t - p.z, 0, p.z - (tz + 1) * t);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        void Scan(Vector3 focus)
        {
            float t = Pack.Tile;
            int x0 = Mathf.FloorToInt((focus.x - ShellRadius) / t), x1 = Mathf.FloorToInt((focus.x + ShellRadius) / t);
            int z0 = Mathf.FloorToInt((focus.z - ShellRadius) / t), z1 = Mathf.FloorToInt((focus.z + ShellRadius) / t);
            for (int tx = x0; tx <= x1; tx++)
                for (int tz = z0; tz <= z1; tz++)
                {
                    if (TileDistance(tx, tz, focus) > ShellRadius) continue;
                    long key = CityPack.Key(tx, tz);
                    if (!tiles.ContainsKey(key)) tiles[key] = new Tile { Tx = tx, Tz = tz };
                }
            scratch.Clear();
            foreach (var tile in tiles.Values)
            {
                tile.Distance = TileDistance(tile.Tx, tile.Tz, focus);
                if (tile.Distance > ShellRadius + t && tile.ShellJob == null && tile.DetailJob == null && tile.BakeJob == null) scratch.Add(tile);
            }
            foreach (var tile in scratch) { Unload(tile); tiles.Remove(CityPack.Key(tile.Tx, tile.Tz)); }
        }

        /// <summary>Starts and finishes background work. With <paramref name="wait"/> every job runs to completion now.</summary>
        void Pump(Vector3 focus, bool wait, float shell = -1, float collide = -1, float detail = -1)
        {
            if (shell < 0) shell = ShellRadius;
            if (collide < 0) collide = ColliderRadius;
            if (detail < 0) detail = DetailRadius;
            float height = Mathf.Max(0, focus.y - Pack.Terrain.At(focus.x, focus.z) - 35);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int running = 0, applied = 0, baking = 0;
            scratch.Clear();
            foreach (var tile in tiles.Values) scratch.Add(tile);
            scratch.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            Tiles = DetailTiles = ColliderTiles = Pending = 0;
            int missing = 0;
            foreach (var tile in scratch)
            {
                if (tile.Distance <= collide && !tile.Empty && !tile.Baked) missing++;
                if (tile.ShellJob != null && !tile.ShellJob.IsCompleted) running++;
                if (tile.DetailJob != null && !tile.DetailJob.IsCompleted) running++;
                if (tile.BakeJob != null && !tile.BakeJob.IsCompleted) baking++;
            }
            var terrain = Pack.Terrain;
            var levels = Pack.Index.levels;
            var pack = Pack;
            foreach (var tile in scratch)
            {
                bool budget = wait || (applied < 2 && watch.Elapsed.TotalMilliseconds < 3);
                // ---- distant version
                if (!tile.Loaded && !tile.Empty && tile.Distance <= shell)
                {
                    if (tile.ShellJob == null && (wait || running < MaxJobs))
                    {
                        int tx = tile.Tx, tz = tile.Tz;
                        System.Func<TileBuild> work = () =>
                        {
                            long start = System.Diagnostics.Stopwatch.GetTimestamp();
                            var data = pack.Load(tx, tz);
                            var built = data == null ? null : TileMesher.Shell(data, terrain, levels);
                            System.Threading.Interlocked.Add(ref shellTicks, System.Diagnostics.Stopwatch.GetTimestamp() - start);
                            return built;
                        };
                        // A blocking caller does the work itself: pool threads of a hidden or busy app can be starved.
                        tile.ShellJob = wait ? Task.FromResult(work()) : Task.Run(work);
                        running++; ShellJobs++;
                    }
                    if (tile.ShellJob != null && (wait || tile.ShellJob.IsCompleted) && budget)
                    {
                        double before = watch.Elapsed.TotalMilliseconds;
                        ApplyShell(tile, tile.ShellJob.Result);
                        UploadMs += watch.Elapsed.TotalMilliseconds - before;
                        tile.ShellJob = null; applied++;
                    }
                }
                if (tile.ShellJob != null) Pending++;
                if (!tile.Loaded) continue;
                Tiles++;
                // ---- colliders
                bool wantCollider = tile.Distance <= collide;
                if (wantCollider && !tile.Baked && tile.BakeJob == null && tile.Collider)
                {
                    int ground = tile.Ground && tile.Ground.vertexCount > 0 ? tile.Ground.GetInstanceID() : 0, walls = tile.Collider.GetInstanceID();
                    bool hasWalls = tile.Collider.vertexCount > 0;
                    System.Action bake = () =>
                    {
                        long start = System.Diagnostics.Stopwatch.GetTimestamp();
                        if (ground != 0) Physics.BakeMesh(ground, false);
                        if (hasWalls) Physics.BakeMesh(walls, false);
                        System.Threading.Interlocked.Add(ref bakeTicks, System.Diagnostics.Stopwatch.GetTimestamp() - start);
                    };
                    if (wait) { bake(); tile.BakeJob = Task.CompletedTask; } else if (baking < 2) { tile.BakeJob = Task.Run(bake); baking++; }
                    if (tile.BakeJob != null) BakeJobs++;
                }
                if (tile.BakeJob != null && (wait || tile.BakeJob.IsCompleted))
                {
                    tile.BakeJob.Wait();
                    tile.BakeJob = null; tile.Baked = true;
                    if (tile.Ground && tile.Ground.vertexCount > 0) { tile.GroundCollider = tile.Root.transform.GetChild(0).gameObject.AddComponent<MeshCollider>(); tile.GroundCollider.sharedMesh = tile.Ground; }
                    if (tile.Collider.vertexCount > 0)
                    {
                        var go = new GameObject("Walls") { hideFlags = HideFlags.DontSave, layer = BuildingLayer };
                        go.transform.SetParent(tile.Root.transform, false);
                        tile.WallCollider = go.AddComponent<MeshCollider>(); tile.WallCollider.sharedMesh = tile.Collider;
                    }
                }
                if (tile.Baked)
                {
                    bool on = tile.Distance <= collide + 48;
                    if (tile.GroundCollider && tile.GroundCollider.enabled != on) tile.GroundCollider.enabled = on;
                    if (tile.WallCollider && tile.WallCollider.enabled != on) tile.WallCollider.enabled = on;
                    if (on) ColliderTiles++;
                }
                // ---- modelled façades
                float near = Mathf.Sqrt(tile.Distance * tile.Distance + height * height);
                if (near <= detail && !tile.Detail && !tile.NoDetail && tile.Data.Units.Length > 0)
                {
                    if (tile.DetailJob == null && (wait || running < MaxJobs))
                    {
                        var build = new TileBuild { Data = tile.Data };
                        System.Func<TileBuild> work = () =>
                        {
                            long start = System.Diagnostics.Stopwatch.GetTimestamp();
                            TileMesher.Detail(build);
                            System.Threading.Interlocked.Add(ref detailTicks, System.Diagnostics.Stopwatch.GetTimestamp() - start);
                            return build;
                        };
                        tile.DetailJob = wait ? Task.FromResult(work()) : Task.Run(work);
                        running++; DetailJobs++;
                    }
                    if (tile.DetailJob != null && (wait || tile.DetailJob.IsCompleted) && budget)
                    {
                        double before = watch.Elapsed.TotalMilliseconds;
                        ApplyDetail(tile, tile.DetailJob.Result);
                        UploadMs += watch.Elapsed.TotalMilliseconds - before;
                        tile.DetailJob = null; applied++;
                    }
                }
                else if (near > detail + 48 && tile.Detail && tile.DetailJob == null)
                {
                    Kill(tile.DetailGo); Kill(tile.Detail);
                    tile.DetailGo = null; tile.Detail = null;
                    if (tile.ShellGo) tile.ShellGo.SetActive(true);
                }
                if (tile.Detail) DetailTiles++;
            }
            MissingColliders = missing;
            double scale = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            ShellMs = shellTicks * scale; DetailMs = detailTicks * scale; BakeMs = bakeTicks * scale;
            LastApplyMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
            if (!wait) SlowestApplyMilliseconds = Mathf.Max(SlowestApplyMilliseconds, LastApplyMilliseconds);
        }

        /// <summary>Blocks until ground, walls and nearby facades around a point exist: for spawns, teleports and fast travel.</summary>
        public void Prepare(Vector3 focus)
        {
            if (Pack != null) Sync(focus, 260, 260, DetailRadius);
        }

        void Sync(Vector3 focus, float shell, float collide, float detail)
        {
            float keep = ShellRadius;
            ShellRadius = shell;
            Scan(focus);
            ShellRadius = keep;
            Pump(focus, true, shell, collide, detail);
        }

        GameObject Part(Tile tile, string name, Mesh mesh, int layer, bool shadows)
        {
            var go = new GameObject(name) { hideFlags = HideFlags.DontSave, layer = layer };
            go.transform.SetParent(tile.Root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = runtimeMaterial;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        void ApplyShell(Tile tile, TileBuild build)
        {
            if (build == null) { tile.Empty = true; return; }
            tile.Data = build.Data;
            tile.Root = new GameObject("Tile " + tile.Tx + "," + tile.Tz) { hideFlags = HideFlags.DontSave };
            tile.Root.transform.SetParent(root, false);
            tile.Ground = build.Ground.ToMesh("Ground " + tile.Tx + "," + tile.Tz);
            Part(tile, "Ground", tile.Ground, GroundLayer, false);        // child 0: also the ground collider
            tile.Always = build.Always.ToMesh("Roofs and gardens");
            Part(tile, "Roofs and gardens", tile.Always, 0, true);
            tile.Shell = build.Shell.ToMesh("Distant walls");
            tile.ShellGo = Part(tile, "Distant walls", tile.Shell, 0, true);
            tile.Collider = new Mesh { name = "Wall collider", indexFormat = build.ColliderP.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16, hideFlags = HideFlags.DontSave };
            tile.Collider.SetVertices(build.ColliderP);
            tile.Collider.SetTriangles(build.ColliderI, 0, true);
            tile.Vertices = build.Ground.Count + build.Always.Count + build.Shell.Count;
            Vertices += tile.Vertices;
            tile.Loaded = true;
        }

        void ApplyDetail(Tile tile, TileBuild build)
        {
            if (build.Detail == null || build.Detail.VertexCount == 0 || !Core) { tile.NoDetail = true; return; }
            tile.Detail = build.Detail.ToMesh("Facades " + tile.Tx + "," + tile.Tz, Core.Materials, out var materials, build.DetailTangents);
            tile.Detail.hideFlags = HideFlags.DontSave;
            tile.DetailGo = new GameObject("Facades") { hideFlags = HideFlags.DontSave };
            tile.DetailGo.transform.SetParent(tile.Root.transform, false);
            tile.DetailGo.AddComponent<MeshFilter>().sharedMesh = tile.Detail;
            var renderer = tile.DetailGo.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            if (tile.ShellGo) tile.ShellGo.SetActive(false);
        }

        void Unload(Tile tile)
        {
            Vertices -= tile.Vertices;
            Kill(tile.Root); Kill(tile.Ground); Kill(tile.Always); Kill(tile.Shell); Kill(tile.Detail); Kill(tile.Collider);
            tile.Loaded = false;
        }
    }
}
