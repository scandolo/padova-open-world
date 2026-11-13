using System.Collections.Generic;
using Padova.City;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Padova.World
{
    /// <summary>The city's small secrets. Crested Padovana hens loose in parks and squares to be caught on foot; a spritz, an
    /// espresso or a gelato at the real bars, cafés and gelaterie; the three "senza" of Padova; Galileo's tower after dark;
    /// a chorus for graduates at the Bo; bells for whoever honks at the Santo; and an old cheat code. Where they are comes
    /// from the place data, so nothing here is placed by hand.</summary>
    [DefaultExecutionOrder(500)]
    public sealed class EasterEggs : MonoBehaviour
    {
        public OpenWorld Game;
        public CityAudio Sound;
        public const int HenCount = 24;
        /// <summary>Hens caught so far, and which secrets have been found; exposed for tests.</summary>
        public int Caught { get; private set; }
        public bool TreSenza { get; private set; }
        public bool Galileo { get; private set; }
        public bool FlyingApe { get; private set; }
        public int Drinks { get; private set; }
        public int Cheers { get; private set; }
        public float Tipsy { get; private set; }
        /// <summary>The bar, café or gelateria within reach on foot, if any.</summary>
        public CityPlace Counter { get; private set; }

        sealed class Hen
        {
            public Vector3 Home;
            public string Place;
            public bool Done, Golden;
            public Transform Body, Head;
            public Vector3 Run;
            public float NextCluck, Bob;
        }

        readonly List<Hen> hens = new List<Hen>();
        readonly List<Key> typed = new List<Key>();
        static readonly Key[] Code = { Key.UpArrow, Key.UpArrow, Key.DownArrow, Key.DownArrow, Key.LeftArrow, Key.RightArrow, Key.LeftArrow, Key.RightArrow, Key.B, Key.A };
        Material white, black, gold, comb, beak;
        InputAction order;
        bool orderPressed;
        float nextNear, bannerAt = -10, espressoUntil, gelatoUntil, lastHonk, lastCheer = -100, walk, run, jump;
        int honks, tips;
        string bannerTitle = "", bannerText = "";
        Vector3 santo, bo, specola;

        void Start()
        {
            if (!Game || !Game.City || Game.City.Data == null) { enabled = false; return; }
            order = new InputAction("Order", InputActionType.Button, "<Keyboard>/b");
            order.performed += _ => orderPressed = true;
            order.Enable();
            CityAudio.Honked += OnHonk;
            walk = Game.Player.WalkSpeed; run = Game.Player.RunSpeed; jump = Game.Player.JumpHeight;
            // Test runs start with nothing found and leave the player's own progress alone.
            TreSenza = !Harness.Launched && PlayerPrefs.GetInt("padova.egg.senza", 0) == 1;
            Galileo = !Harness.Launched && PlayerPrefs.GetInt("padova.egg.galileo", 0) == 1;
            santo = Find("Santo", "Sant'Antonio"); bo = Find("Palazzo del Bo", "Palazzo Bo"); specola = Find("Specola");
            PlaceHens();
        }

        void OnDestroy()
        {
            CityAudio.Honked -= OnHonk;
            order?.Dispose();
        }

        Vector3 Find(params string[] names)
        {
            foreach (var place in Game.City.Data.landmarks)
                foreach (string name in names)
                    if (place.name != null && place.name.Contains(name)) return new Vector3(place.x, Game.City.Data.GroundAt(place.x, place.z), place.z);
            return new Vector3(0, -9999, 0);
        }

        static bool Near(Vector3 a, Vector3 b, float d) => b.y > -9000 && (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z) < d * d;

        void Banner(string title, string text, AudioClip clip)
        {
            bannerTitle = title; bannerText = text; bannerAt = Time.unscaledTime;
            Game.Notify(title + " · " + text);
            if (Sound) Sound.Play(clip, .7f);
        }

        // ------------------------------------------------------------------ hens
        /// <summary>One hen per park, garden or square, spread over the city, the same ones every run; the golden one keeps to Prato della Valle.</summary>
        void PlaceHens()
        {
            var places = Game.City.Places;
            if (places == null) return;
            var saved = Harness.Launched ? "" : PlayerPrefs.GetString("padova.hens", "");
            var spots = new List<CityPlace>();
            foreach (var p in places.places) if (p.kind == "park" || p.kind == "garden" || p.kind == "square") spots.Add(p);
            // A fixed shuffle, then keep those far enough from the ones already chosen.
            spots.Sort((a, b) => Hash(a).CompareTo(Hash(b)));
            var prato = Find("Prato della Valle");
            if (prato.y > -9000) hens.Add(new Hen { Home = prato + new Vector3(18, 0, 6), Place = "Prato della Valle", Golden = true });
            foreach (var spot in spots)
            {
                if (hens.Count >= HenCount) break;
                var at = new Vector3(spot.x, 0, spot.z);
                bool crowded = false;
                foreach (var hen in hens) if (Near(at, hen.Home, 420)) { crowded = true; break; }
                if (!crowded) hens.Add(new Hen { Home = at, Place = spot.name });
            }
            for (int i = 0; i < hens.Count; i++) if (i < saved.Length && saved[i] == '1') { hens[i].Done = true; Caught++; }
        }

        static uint Hash(CityPlace p) => (uint)(Mathf.RoundToInt(p.x * 7.3f) * 73856093) ^ (uint)(Mathf.RoundToInt(p.z * 5.1f) * 19349663);

        Material Paint(Color colour)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.DontSave };
            m.SetColor("_BaseColor", colour);
            m.SetFloat("_Smoothness", .15f);
            return m;
        }

        /// <summary>A Padovana: round body, upright tail, and the crest bigger than its head.</summary>
        void Build(Hen hen, int index)
        {
            white ??= Paint(new Color(.93f, .91f, .86f)); black ??= Paint(new Color(.1f, .1f, .11f)); gold ??= Paint(new Color(.86f, .62f, .2f));
            comb ??= Paint(new Color(.95f, .93f, .9f)); beak ??= Paint(new Color(.9f, .55f, .15f));
            var plumage = hen.Golden ? gold : index % 3 == 0 ? black : index % 3 == 1 ? white : gold;
            var crest = hen.Golden ? white : index % 3 == 0 ? comb : index % 3 == 1 ? comb : black;
            var root = new GameObject("Gallina padovana " + (index + 1)).transform;
            root.SetParent(transform, false);
            Transform Part(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
            {
                var go = GameObject.CreatePrimitive(type);
                Destroy(go.GetComponent<Collider>());
                go.name = name;
                go.transform.SetParent(parent, false);
                go.transform.localPosition = position; go.transform.localScale = scale;
                go.GetComponent<Renderer>().sharedMaterial = material;
                return go.transform;
            }
            Part("Body", PrimitiveType.Sphere, root, new Vector3(0, .3f, 0), new Vector3(.3f, .28f, .42f), plumage);
            Part("Tail", PrimitiveType.Sphere, root, new Vector3(0, .46f, -.2f), new Vector3(.1f, .26f, .16f), plumage);
            Part("Leg", PrimitiveType.Cube, root, new Vector3(-.06f, .08f, 0), new Vector3(.025f, .16f, .025f), beak);
            Part("Leg", PrimitiveType.Cube, root, new Vector3(.06f, .08f, 0), new Vector3(.025f, .16f, .025f), beak);
            hen.Head = new GameObject("Head").transform;
            hen.Head.SetParent(root, false);
            hen.Head.localPosition = new Vector3(0, .5f, .2f);
            Part("Skull", PrimitiveType.Sphere, hen.Head, Vector3.zero, Vector3.one * .13f, plumage);
            Part("Crest", PrimitiveType.Sphere, hen.Head, new Vector3(0, .11f, -.01f), new Vector3(.22f, .2f, .22f), crest);
            Part("Beak", PrimitiveType.Cube, hen.Head, new Vector3(0, -.01f, .08f), new Vector3(.03f, .025f, .07f), beak);
            hen.Body = root;
            // Somewhere free to stand near the place's point.
            var home = hen.Home;
            for (int k = 0; k < 12; k++)
            {
                var p = hen.Home + Quaternion.Euler(0, k * 67, 0) * Vector3.forward * (3 + k * 2.5f);
                p.y = Game.SurfaceAt(p);
                if (!Physics.CheckSphere(p + Vector3.up * .5f, .45f, 1 << 9)) { home = p; break; }
            }
            hen.Home = home;
            root.position = home;
            root.localScale = Vector3.one * (hen.Golden ? 1.25f : 1);
        }

        void Hens(float dt)
        {
            var player = Game.Position;
            for (int i = 0; i < hens.Count; i++)
            {
                var hen = hens[i];
                if (hen.Done) continue;
                bool near = Near(player, hen.Body ? hen.Body.position : hen.Home, 150);
                if (!near) { if (hen.Body) { Destroy(hen.Body.gameObject); hen.Body = null; } continue; }
                if (!hen.Body) Build(hen, i);
                var p = hen.Body.position;
                var away = p - player; away.y = 0;
                float distance = away.magnitude;
                // It clucks now and then, so it can be found by ear.
                if (Time.time >= hen.NextCluck) { hen.NextCluck = Time.time + Random.Range(3f, 7f); if (Sound) Sound.PlayAt(SoundBank.Cluck, p, .9f); }
                if (Game.Mode == "walk" && distance < 1.25f) { Catch(hen, i); continue; }
                Vector3 move = Vector3.zero;
                if (distance < 7 && distance > .01f)
                {
                    // Runs from whoever comes close, with a swerve, but never far from home.
                    var flee = (away / distance + Vector3.Cross(Vector3.up, away / distance) * Mathf.Sin(Time.time * 2.3f + i) * .7f).normalized;
                    var home = hen.Home - p; home.y = 0;
                    if (home.magnitude > 16) flee = (flee + home.normalized * 1.4f).normalized;
                    move = flee * 3.1f;
                }
                else
                {
                    var home = hen.Home - p; home.y = 0;
                    if (home.magnitude > 5) move = home.normalized * .8f;
                }
                if (move.sqrMagnitude > .01f)
                {
                    var next = p + move * dt;
                    next.y = Game.SurfaceAt(next);
                    if (!Physics.CheckSphere(next + Vector3.up * .45f, .3f, 1 << 9)) hen.Body.position = next;
                    hen.Body.rotation = Quaternion.Slerp(hen.Body.rotation, Quaternion.LookRotation(move.normalized), 1 - Mathf.Exp(-10 * dt));
                    hen.Bob += dt * 16;
                }
                else hen.Bob += dt * 3;
                // Pecking when calm, head bobbing on the run.
                float peck = move.sqrMagnitude > .01f ? Mathf.Sin(hen.Bob) * 10 : Mathf.Max(0, Mathf.Sin(hen.Bob)) * 55;
                hen.Head.localRotation = Quaternion.Euler(peck, 0, 0);
            }
        }

        void Catch(Hen hen, int index)
        {
            hen.Done = true; Caught++;
            if (hen.Body) Destroy(hen.Body.gameObject);
            hen.Body = null;
            var flags = new char[hens.Count];
            for (int i = 0; i < hens.Count; i++) flags[i] = hens[i].Done ? '1' : '0';
            if (!Harness.Launched) PlayerPrefs.SetString("padova.hens", new string(flags));
            int prize = hen.Golden ? 150 : 25;
            if (Game.Activities) Game.Activities.Reward(prize);
            if (Caught >= hens.Count) Banner("TUTTE LE GALLINE", "Every Padovana hen is home · +€" + prize, SoundBank.Fanfare);
            else Banner(hen.Golden ? "LA GALLINA D'ORO" : "GALLINA PADOVANA", Caught + " / " + hens.Count + " caught · " + hen.Place + " · +€" + prize, SoundBank.Fanfare);
        }

        // ------------------------------------------------------------------ bars, cafés, gelaterie
        void Counters()
        {
            if (Time.time >= nextNear)
            {
                nextNear = Time.time + .4f;
                Counter = null;
                var places = Game.City.Places;
                if (Game.Mode == "walk" && places != null)
                {
                    float best = 8 * 8;
                    var p = Game.Position;
                    foreach (var poi in places.pois)
                    {
                        if (poi.kind != "bar" && poi.kind != "pub" && poi.kind != "cafe" && poi.kind != "gelateria") continue;
                        float d = (poi.x - p.x) * (poi.x - p.x) + (poi.z - p.z) * (poi.z - p.z);
                        if (d < best) { best = d; Counter = poi; }
                    }
                }
            }
            bool pressed = orderPressed;
            orderPressed = false;
            if (!pressed || Counter == null) return;
            string kind = Counter.kind;
            int price = kind == "gelateria" ? 3 : kind == "cafe" ? 2 : 4;
            if (!Game.Activities || Game.Activities.Money < price) { Game.Notify("That is €" + price + ". Earn some first: J for jobs."); if (Sound) Sound.Play(SoundBank.Deny, .5f); return; }
            Game.Activities.Charge(price);
            Drinks++;
            string where = string.IsNullOrEmpty(Counter.name) ? "" : " at " + Counter.name;
            if (kind == "gelateria") { gelatoUntil = Time.time + 60; Game.Notify("Gelato" + where + " · a spring in your step for a minute"); }
            else if (kind == "cafe") { espressoUntil = Time.time + 60; Game.Notify("Espresso" + where + " · you run faster for a minute"); }
            else { Tipsy = Mathf.Min(3, Tipsy + 1); Game.Notify(Tipsy >= 3 ? "Third spritz" + where + " · forse è meglio andare a piedi" : "Spritz" + where + " · salute!"); }
        }

        // ------------------------------------------------------------------ places and codes
        void OnHonk(Vector3 at)
        {
            honks = Time.time - lastHonk < 4 ? honks + 1 : 1;
            lastHonk = Time.time;
            // Graduation day never ends outside the Bo.
            if (Near(at, bo, 90) && Time.time - lastCheer > 20)
            {
                lastCheer = Time.time; Cheers++;
                Banner("DOTTORE, DOTTORE!", "The graduates outside Palazzo del Bo heard you", SoundBank.Fanfare);
            }
            // Three blasts at the Santo and the friars answer.
            if (honks >= 3 && Near(at, santo, 140) && Sound) { honks = 0; Sound.Toll(santo + Vector3.up * 40, 7); Game.Notify("The bells of the Santo answer back"); }
        }

        void Places()
        {
            var p = Game.Position;
            if (!TreSenza)
            {
                bool cafe = false, saint = false, lawn = false;
                var marks = Game.City.Data.landmarks;
                for (int i = 0; i < marks.Length && i < Game.Visited.Length; i++)
                {
                    if (!Game.Visited[i] || marks[i].name == null) continue;
                    if (marks[i].name.Contains("Pedrocchi")) cafe = true;
                    if (marks[i].name.Contains("Santo") || marks[i].name.Contains("Sant'Antonio")) saint = true;
                    if (marks[i].name.Contains("Prato della Valle")) lawn = true;
                }
                if (cafe && saint && lawn)
                {
                    TreSenza = true; if (!Harness.Launched) PlayerPrefs.SetInt("padova.egg.senza", 1);
                    if (Game.Activities) Game.Activities.Reward(100);
                    Banner("I TRE SENZA", "The saint without a name, the meadow without grass, the café without doors · +€100", SoundBank.Fanfare);
                }
            }
            if (!Galileo && DayCycle.Instance && DayCycle.Instance.Night > .6f && Near(p, specola, 70))
            {
                Galileo = true; if (!Harness.Launched) PlayerPrefs.SetInt("padova.egg.galileo", 1);
                if (Game.Activities) Game.Activities.Reward(50);
                // The sky wheels round a whole day while you watch.
                for (int k = 0; k < 4; k++) DayCycle.Instance.SkipAhead();
                Banner("EPPUR SI MUOVE", "Galileo's tower at night · the heavens turn · +€50", SoundBank.Fanfare);
            }
        }

        void Typed()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            foreach (var key in Code)
            {
                if (!keyboard[key].wasPressedThisFrame) continue;
                typed.Add(key);
                if (typed.Count > Code.Length) typed.RemoveAt(0);
                break;
            }
            if (typed.Count < Code.Length) return;
            for (int i = 0; i < Code.Length; i++) if (typed[i] != Code[i]) return;
            typed.Clear();
            FlyingApe = !FlyingApe;
            Banner(FlyingApe ? "APE VOLANTE" : "APE A TERRA", FlyingApe ? "Turbo without end, in whatever you drive" : "Back to an honest engine", SoundBank.Fanfare);
        }

        void Update()
        {
            if (!Game || Game.Halted) { orderPressed = false; return; }
            float dt = Time.deltaTime;
            Hens(dt);
            Counters();
            Places();
            Typed();
            if (Game.Driving) Game.Driving.EndlessBoost = FlyingApe;
            Tipsy = Mathf.Max(0, Tipsy - dt / 45);
            Game.Player.RunSpeed = run * (Time.time < espressoUntil ? 1.3f : 1);
            Game.Player.WalkSpeed = walk * (Time.time < espressoUntil ? 1.15f : 1);
            Game.Player.JumpHeight = jump * (Time.time < gelatoUntil ? 1.7f : 1);
            Tips();
        }

        /// <summary>A few hints for a first-time player, each said once.</summary>
        void Tips()
        {
            if (tips == 0 && Time.timeSinceLevelLoad > 9 && Game.Mode == "walk") { tips = 1; Game.Notify("Every parked car is yours: walk up to one and press E"); }
            else if (tips <= 1 && Game.Mode == "drive" && Game.Driving.DistanceTravelled > 250 && !(Game.Activities && Game.Activities.Active)) { tips = 2; Game.Notify("Press J for paid jobs: taxi fares, deliveries, sprints"); }
            else if (tips == 2 && Caught == 0 && Time.timeSinceLevelLoad > 240) { tips = 3; Game.Notify("Hear clucking? " + hens.Count + " Padovana hens are loose in the parks. Catch them on foot."); }
        }

        void LateUpdate()
        {
            // One spritz too many: the horizon will not stay level.
            if (Tipsy <= .01f || !Game || Game.Halted || !Camera.main) return;
            float t = Time.time;
            Camera.main.transform.rotation *= Quaternion.Euler(Mathf.Sin(t * .9f) * .7f * Tipsy, Mathf.Sin(t * .6f + 1) * 1.1f * Tipsy, Mathf.Sin(t * 1.3f) * 2.2f * Tipsy);
        }

        void OnGUI()
        {
            if (!Game || Game.MapOpen || Game.Menu) return;
            var old = GUI.matrix;
            Hud.Begin();
            if (Caught > 0) Hud.Text(new Rect(560, 898, 300, 24), "Hens  " + Caught + " / " + hens.Count, 17, Hud.Muted);
            if (Counter != null && Game.Mode == "walk")
            {
                string what = Counter.kind == "gelateria" ? "Gelato · €3" : Counter.kind == "cafe" ? "Espresso · €2" : "Spritz · €4";
                Hud.Prompt(new Vector2(800, 740), "B", what + (string.IsNullOrEmpty(Counter.name) ? "" : "  ·  " + Counter.name));
            }
            float since = Time.unscaledTime - bannerAt;
            if (since < 4) Hud.Banner(bannerTitle, bannerText, Hud.Gold, since);
            GUI.matrix = old;
        }

        /// <summary>Development: where the nearest uncaught hen is, for tests.</summary>
        public Vector3 NearestHen(Vector3 from, out string place)
        {
            place = "";
            Vector3 best = new Vector3(0, -9999, 0);
            float bestD = float.MaxValue;
            foreach (var hen in hens)
            {
                if (hen.Done) continue;
                var p = hen.Body ? hen.Body.position : hen.Home;
                float d = (p.x - from.x) * (p.x - from.x) + (p.z - from.z) * (p.z - from.z);
                if (d < bestD) { bestD = d; best = p; place = hen.Place; }
            }
            return best;
        }

        public int Total => hens.Count;
    }
}
