using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Padova.World
{
    /// <summary>The game's sound: an engine with gears for whatever the player drives, tyres, wind, horn, crashes,
    /// passing traffic, police sirens, church bells on the hour, and the city in the background. All from <see cref="SoundBank"/>.</summary>
    [DefaultExecutionOrder(210)]
    public sealed class CityAudio : MonoBehaviour
    {
        public OpenWorld Game;
        [Range(0, 1)] public float Volume = .8f;
        /// <summary>Engine speed 0..1 and the gear it is in; exposed for tests.</summary>
        public float Rpm { get; private set; }
        public int Gear { get; private set; }
        public int Honks { get; private set; }
        public int Crashes { get; private set; }
        public int Tolls { get; private set; }
        public bool HornHeld { get; private set; }
        /// <summary>Raised when the player sounds the horn, with the vehicle's position.</summary>
        public static event Action<Vector3> Honked;

        AudioSource engine, skid, wind, horn, ui, hum, birds, crickets, steps, bell;
        AudioLowPassFilter engineFilter;
        readonly List<AudioSource> passing = new List<AudioSource>(), sirens = new List<AudioSource>();
        readonly List<CityTraffic.Vehicle> nearest = new List<CityTraffic.Vehicle>();
        InputAction hornAction;
        CityCar car;
        int impactSeen, money, discoveries, lastHour = -1, tollsLeft;
        float nextPassing, stride, nextToll, shift, honkAnswer;
        string notice = "";
        Vector3 bellAt;

        void Awake()
        {
            SoundBank.Build();
            var listener = FindFirstObjectByType<AudioListener>();
            var ear = listener ? listener.transform : transform;
            AudioSource Flat(string name, AudioClip clip, bool loop, float volume)
            {
                var go = new GameObject(name);
                go.transform.SetParent(ear, false);
                var s = go.AddComponent<AudioSource>();
                s.clip = clip; s.loop = loop; s.volume = volume; s.playOnAwake = false; s.spatialBlend = 0; s.dopplerLevel = 0;
                if (loop) { s.volume = 0; s.Play(); }
                return s;
            }
            engine = Flat("Engine", SoundBank.Engine, true, 0);
            engineFilter = engine.gameObject.AddComponent<AudioLowPassFilter>();
            skid = Flat("Tyres", SoundBank.Skid, true, 0);
            wind = Flat("Wind", SoundBank.Wind, true, 0);
            horn = Flat("Horn", SoundBank.Horn, true, 0);
            hum = Flat("City", SoundBank.Hum, true, 0);
            birds = Flat("Birds", SoundBank.Birds, true, 0);
            crickets = Flat("Crickets", SoundBank.Crickets, true, 0);
            steps = Flat("Steps", SoundBank.Step, false, .35f);
            ui = Flat("Interface", null, false, 1);
            ui.ignoreListenerPause = true;
            for (int i = 0; i < 4; i++) passing.Add(World("Passing car " + i, SoundBank.Engine, 3, 42));
            for (int i = 0; i < 3; i++) sirens.Add(World("Siren " + i, SoundBank.Siren, 12, 320));
            bell = World("Bell", SoundBank.Bell, 30, 700);
            bell.loop = false; bell.Stop(); bell.volume = .9f;
            hornAction = new InputAction("Horn", InputActionType.Button, "<Keyboard>/q");
            hornAction.Enable();
            if (Game && Game.Activities) money = Game.Activities.Money;
        }

        AudioSource World(string name, AudioClip clip, float near, float far)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.volume = 0; s.playOnAwake = false; s.spatialBlend = 1; s.dopplerLevel = .6f;
            s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = near; s.maxDistance = far;
            s.Play();
            return s;
        }

        void OnDestroy() => hornAction?.Dispose();

        /// <summary>One-shot interface sound; plays even while the game is paused.</summary>
        public void Play(AudioClip clip, float volume = 1, float pitch = 1)
        {
            if (!clip) return;
            ui.pitch = pitch;
            ui.PlayOneShot(clip, volume);
        }

        static float Ease(float current, float target, float rate, float dt) => Mathf.Lerp(current, target, 1 - Mathf.Exp(-rate * dt));

        void Update()
        {
            if (!Game) return;
            AudioListener.volume = Volume;
            AudioListener.pause = Game.Halted;
            float dt = Time.deltaTime;
            float night = DayCycle.Instance ? DayCycle.Instance.Night : 0;
            Drive(dt);
            Walk(dt);
            Fly(dt);
            Traffic(dt);
            Police();
            Bells(night);
            // Background: the city's hum, birds by day and crickets after dark, quieter at speed.
            float calm = Game.Mode == "drive" && car ? Mathf.Clamp01(1 - car.Speed / 18) : Game.Mode == "fly" ? .2f : 1;
            hum.volume = Ease(hum.volume, .22f, 2, dt);
            birds.volume = Ease(birds.volume, .2f * (1 - night) * calm, 1.5f, dt);
            crickets.volume = Ease(crickets.volume, .13f * night * calm, 1.5f, dt);
            Interface();
        }

        void Drive(float dt)
        {
            var driven = Game.Mode == "drive" ? Game.Driving : null;
            if (driven != car)
            {
                car = driven;
                Gear = 0; Rpm = .2f;
                if (car)
                {
                    impactSeen = car.ImpactSerial;
                    engine.clip = car.Spec.Wheels < 4 ? SoundBank.Buzz : car.Spec.Mass > 1700 ? SoundBank.Diesel : SoundBank.Engine;
                    if (car.Spec.Name == "Bicicletta") engine.clip = null;
                    horn.clip = car.Spec.Wheels == 2 ? SoundBank.Meep : SoundBank.Horn;
                    horn.pitch = car.Spec.Mass > 5000 ? .62f : car.Spec.Wheels == 3 ? 1.5f : car.Spec.Mass > 1700 ? .84f : 1;
                    if (engine.clip) engine.Play();
                    horn.Play();
                }
            }
            bool honk = car && !Game.Halted && hornAction.IsPressed();
            if (honk && !HornHeld) { Honks++; honkAnswer = Time.time + .8f; Honked?.Invoke(car.transform.position); }
            HornHeld = honk;
            horn.volume = Ease(horn.volume, honk ? .75f : 0, honk ? 60 : 30, dt);
            if (!car)
            {
                engine.volume = Ease(engine.volume, 0, 8, dt);
                skid.volume = Ease(skid.volume, 0, 10, dt);
                if (Game.Mode != "fly") wind.volume = Ease(wind.volume, 0, 4, dt);
                return;
            }
            var spec = car.Spec;
            float speed = Mathf.Abs(car.ForwardSpeed), top = Mathf.Max(spec.BoostSpeed, 1), throttle = Mathf.Abs(car.Throttle);
            int gears = spec.Wheels == 4 ? (spec.Mass > 5000 ? 4 : 5) : 1;
            // Each gear covers a share of the top speed; revs rise through it and drop on the change.
            float Low(int g) => g == 0 ? 0 : top * Mathf.Pow(g / (float)gears, 1.15f) * .82f;
            float High(int g) => top * Mathf.Pow((g + 1) / (float)gears, 1.15f);
            float revs = Mathf.InverseLerp(Low(Gear), High(Gear), speed);
            if (revs > .93f && Gear < gears - 1) { Gear++; shift = .16f; }
            else if (speed < Low(Gear) * .9f && Gear > 0) Gear--;
            float target = gears == 1 ? Mathf.Clamp01(.25f + .55f * throttle + .35f * speed / top) : Mathf.Lerp(.2f, 1, Mathf.InverseLerp(Low(Gear), High(Gear), speed));
            if (!car.Grounded) target = Mathf.Max(target, .35f + .65f * throttle);
            shift = Mathf.Max(0, shift - dt);
            Rpm = Ease(Rpm, shift > 0 ? target * .8f : target, target > Rpm ? 9 : 5, dt);
            float voice = spec.Wheels == 2 ? 1.25f : spec.Wheels == 3 ? 1.05f : spec.Mass > 5000 ? .6f : spec.Mass > 1700 ? .85f : spec.Mass < 900 ? 1.18f : 1;
            engine.pitch = Mathf.Lerp(.55f, 2.35f, Rpm) * voice * (car.Boosting ? 1.06f : 1);
            engine.volume = Ease(engine.volume, Mathf.Lerp(.2f, .62f, throttle) * (.65f + .35f * Rpm), 10, dt);
            engineFilter.cutoffFrequency = Mathf.Lerp(1100, 11000, Mathf.Clamp01(throttle * .65f + Rpm * .4f));
            skid.volume = Ease(skid.volume, car.Grounded ? Mathf.Clamp01(car.Slip * 1.4f + (car.Handbrake && speed > 4 ? .5f : 0)) * .5f : 0, 14, dt);
            skid.pitch = Mathf.Lerp(.8f, 1.25f, speed / top);
            wind.volume = Ease(wind.volume, Mathf.Clamp01((speed - 9) / 30) * .4f, 3, dt);
            wind.pitch = Mathf.Lerp(.8f, 1.4f, speed / top);
            if (car.ImpactSerial != impactSeen)
            {
                impactSeen = car.ImpactSerial;
                float hit = Mathf.InverseLerp(4, 26, car.LastImpact);
                Play(hit > .25f ? SoundBank.Crash : SoundBank.Thud, .35f + hit * .65f, 1.15f - hit * .3f);
                Crashes++;
            }
        }

        void Walk(float dt)
        {
            if (Game.Mode != "walk" || !Game.Player.Grounded || Game.Player.CurrentSpeed < .8f) { stride = .2f; return; }
            stride -= dt * Mathf.Lerp(1.9f, 3.1f, Mathf.InverseLerp(Game.Player.WalkSpeed, Game.Player.RunSpeed, Game.Player.CurrentSpeed));
            if (stride > 0) return;
            stride += 1;
            steps.pitch = UnityEngine.Random.Range(.85f, 1.15f);
            steps.PlayOneShot(SoundBank.Step, UnityEngine.Random.Range(.5f, .8f));
        }

        void Fly(float dt)
        {
            if (Game.Mode != "fly") return;
            if (engine.clip != SoundBank.Engine) { engine.clip = SoundBank.Engine; engine.Play(); }
            engine.pitch = Mathf.Lerp(1.5f, 1.9f, Mathf.InverseLerp(30, 60, Game.Plane.Speed));
            engine.volume = Ease(engine.volume, .3f, 4, dt);
            engineFilter.cutoffFrequency = 5000;
            wind.volume = Ease(wind.volume, .35f, 2, dt);
            wind.pitch = 1.2f;
        }

        /// <summary>The closest few moving cars get an engine voice in the world.</summary>
        void Traffic(float dt)
        {
            if (!Game.Traffic) return;
            var ear = Game.Position;
            if (Time.time >= nextPassing)
            {
                nextPassing = Time.time + .3f;
                nearest.Clear();
                foreach (var v in Game.Traffic.Vehicles)
                {
                    if (!v.Go.activeSelf || (v.Go.transform.position - ear).sqrMagnitude > 45 * 45) continue;
                    nearest.Add(v);
                }
                nearest.Sort((a, b) => (a.Go.transform.position - ear).sqrMagnitude.CompareTo((b.Go.transform.position - ear).sqrMagnitude));
            }
            for (int i = 0; i < passing.Count; i++)
            {
                var source = passing[i];
                var v = i < nearest.Count ? nearest[i] : null;
                if (v == null || !v.Go) { source.volume = Ease(source.volume, 0, 6, dt); continue; }
                source.transform.position = v.Go.transform.position;
                var clip = v.Spec.Wheels < 4 ? SoundBank.Buzz : v.Spec.Mass > 1700 ? SoundBank.Diesel : SoundBank.Engine;
                if (source.clip != clip) { source.clip = clip; source.Play(); }
                source.pitch = Mathf.Lerp(.6f, 1.5f, Mathf.Clamp01(v.Speed / 16)) * (v.Spec.Mass > 5000 ? .65f : 1);
                source.volume = Ease(source.volume, .3f + .2f * Mathf.Clamp01(v.Speed / 12), 6, dt);
            }
            // A driver stuck behind the player answers the horn, or loses patience.
            if (honkAnswer > 0 && Time.time > honkAnswer)
            {
                honkAnswer = 0;
                foreach (var v in nearest)
                    if (v.Blocked > 1 && v.Go) { AudioSource.PlayClipAtPoint(SoundBank.Horn, v.Go.transform.position, .6f); break; }
            }
        }

        void Police()
        {
            int i = 0;
            if (Game.Police)
                foreach (var p in Game.Police.Positions)
                {
                    if (i >= sirens.Count) break;
                    sirens[i].transform.position = p;
                    sirens[i].volume = .55f;
                    i++;
                }
            for (; i < sirens.Count; i++) sirens[i].volume = 0;
        }

        /// <summary>On the hour, the nearest church within earshot tolls: once at one, up to four strokes later in the day.</summary>
        void Bells(float night)
        {
            var day = DayCycle.Instance;
            if (!day) return;
            int hour = Mathf.FloorToInt(day.Hour);
            if (hour != lastHour)
            {
                bool first = lastHour < 0;
                lastHour = hour;
                if (!first && hour >= 7 && hour <= 22 && Game.City && Game.City.Data != null)
                {
                    float best = 650 * 650;
                    bool found = false;
                    var ear = Game.Position;
                    foreach (var place in Game.City.Data.landmarks)
                    {
                        if (place.kind != "church") continue;
                        float d = (place.x - ear.x) * (place.x - ear.x) + (place.z - ear.z) * (place.z - ear.z);
                        if (d < best) { best = d; bellAt = new Vector3(place.x, Game.City.Data.GroundAt(place.x, place.z) + 30, place.z); found = true; }
                    }
                    if (found) { tollsLeft = Mathf.Clamp((hour + 11) % 12 + 1, 1, 4); nextToll = 0; bell.pitch = .8f + Mathf.Repeat(bellAt.x * .013f, .35f); }
                }
            }
            if (tollsLeft > 0 && Time.time >= nextToll)
            {
                bell.transform.position = bellAt;
                bell.PlayOneShot(SoundBank.Bell);
                tollsLeft--; Tolls++;
                nextToll = Time.time + 1.9f;
            }
        }

        void Interface()
        {
            if (Game.Activities)
            {
                int now = Game.Activities.Money;
                if (now > money) Play(SoundBank.Coin, .7f);
                else if (now < money) Play(SoundBank.Deny, .6f);
                money = now;
            }
            if (Game.Discoveries > discoveries) Play(SoundBank.Chime, .6f);
            discoveries = Game.Discoveries;
            if (Game.Notice != notice) { notice = Game.Notice; if (Time.timeSinceLevelLoad > 2) Play(SoundBank.Blip, .25f); }
        }
    }
}
