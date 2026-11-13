using System;
using UnityEngine;

namespace Padova.World
{
    /// <summary>Every sound in the game, synthesised once at start-up: no audio files ship.
    /// Loops hold whole cycles (or are cross-faded at the seam), so they repeat without a click.</summary>
    public static class SoundBank
    {
        public const int Rate = 44100;
        public static AudioClip Engine, Buzz, Diesel, Skid, Wind, Horn, Meep, Siren, Crash, Thud, Bell, Coin, Chime, Blip, Deny, Hum, Birds, Crickets, Step, Ding, Cluck, Fanfare;
        public static bool Ready { get; private set; }

        public static void Build()
        {
            if (Ready) return;
            Ready = true;
            Engine = Clip("Engine", Harmonics(2, 55, 16, k => (k % 2 == 0 ? 1f : .6f) / Mathf.Pow(k, .9f), new[] { .5f, 1.5f }, new[] { .35f, .22f }, .05f), true);
            Buzz = Clip("Two-stroke", Harmonics(2, 90, 20, k => 1f / Mathf.Pow(k, .6f), new[] { .5f }, new[] { .5f }, .08f), true);
            Diesel = Clip("Diesel", DieselLoop(), true);
            Skid = Clip("Tyres", Seam(Band(Noise(2f, 11), 800, 2100), .25f), true);
            Wind = Clip("Wind", Seam(Band(Noise(2.5f, 12), 60, 420), .3f), true);
            Horn = Clip("Horn", Tones(.5f, new[] { 420f, 524f }, 8), true);
            Meep = Clip("Small horn", Tones(.5f, new[] { 740f }, 9, true), true);
            Siren = Clip("Siren", SirenLoop(), true);
            Crash = Clip("Crash", CrashShot(), false);
            Thud = Clip("Thud", Shot(.35f, (t, i) => Mathf.Sin(6.2832f * (95 - 80 * t) * t) * Mathf.Exp(-t * 16)), false);
            Bell = Clip("Bell", BellShot(), false);
            Coin = Clip("Coin", Shot(.7f, (t, i) => t < .08f ? Mathf.Sin(6.2832f * 1319 * t) * Mathf.Exp(-t * 30)
                : (Mathf.Sin(6.2832f * 1976 * (t - .08f)) + .3f * Mathf.Sin(6.2832f * 5454 * (t - .08f))) * Mathf.Exp(-(t - .08f) * 8)), false);
            Chime = Clip("Chime", Shot(1.5f, (t, i) =>
            {
                float v = 0;
                float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
                for (int n = 0; n < notes.Length; n++)
                {
                    float s = t - n * .09f;
                    if (s > 0) v += (Mathf.Sin(6.2832f * notes[n] * s) + .3f * Mathf.Sin(12.5664f * notes[n] * s)) * Mathf.Exp(-s * 5);
                }
                return v * .5f;
            }), false);
            Blip = Clip("Blip", Shot(.14f, (t, i) => Mathf.Sin(6.2832f * 880 * t) * Mathf.Exp(-t * 28)), false);
            Deny = Clip("Deny", Shot(.34f, (t, i) => (Mathf.Sin(6.2832f * 130 * t) > 0 ? .5f : -.5f) * Mathf.Exp(-t * 6) * Mathf.Clamp01(t * 200)), false);
            Hum = Clip("City hum", Seam(Band(Noise(6, 13), 30, 150), .6f), true);
            Birds = Clip("Birds", BirdLoop(), true);
            Crickets = Clip("Crickets", Shot(4, (t, i) =>
            {
                float beat = Mathf.Repeat(t, .5f), pulse = beat < .09f ? Mathf.Pow(Mathf.Abs(Mathf.Sin(6.2832f * 28 * beat)), 3) : 0;
                return Mathf.Sin(6.2832f * 4300 * t) * pulse * .5f + Mathf.Sin(6.2832f * 3900 * t) * (Mathf.Repeat(t + .23f, .8f) < .07f ? .3f : 0);
            }, false), true);
            Step = Clip("Step", Shot(.1f, new Func<float, int, float>(Footfall(new System.Random(5)))), false);
            // A hen: two short falling squawks with a nasal edge.
            Cluck = Clip("Cluck", Shot(.34f, (t, i) =>
            {
                float v = 0;
                foreach (float start in new[] { 0f, .17f })
                {
                    float s = t - start;
                    if (s < 0 || s > .13f) continue;
                    float phase = 6.2832f * (880 * s - 1500 * s * s);
                    v += (Mathf.Sin(phase) + .5f * Mathf.Sin(2 * phase) + .35f * Mathf.Sin(3 * phase)) * Mathf.Sin(Mathf.PI * s / .13f);
                }
                return v;
            }), false);
            // Found something: a rising four-note flourish.
            Fanfare = Clip("Fanfare", Shot(1.3f, (t, i) =>
            {
                float v = 0;
                float[] notes = { 392, 523.25f, 659.25f, 783.99f };
                for (int n = 0; n < notes.Length; n++)
                {
                    float s = t - n * .13f;
                    if (s <= 0) continue;
                    float hold = n == notes.Length - 1 ? 2.2f : 7;
                    for (int k = 1; k <= 4; k++) v += Mathf.Sin(6.2832f * notes[n] * k * s) / (k * k) * Mathf.Exp(-s * hold) * Mathf.Clamp01(s * 60);
                }
                return v;
            }), false);
            Ding = Clip("Ding", Shot(1.2f, (t, i) => (Mathf.Sin(6.2832f * 1568 * t) + .5f * Mathf.Sin(6.2832f * 2349 * t) + .25f * Mathf.Sin(6.2832f * 4186 * t)) * Mathf.Exp(-t * 4.5f)), false);
        }

        static AudioClip Clip(string name, float[] data, bool loop)
        {
            float peak = 1e-6f;
            foreach (float v in data) peak = Mathf.Max(peak, Mathf.Abs(v));
            float gain = .85f / peak;
            for (int i = 0; i < data.Length; i++) data[i] *= gain;
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            clip.hideFlags = HideFlags.DontSave;
            return clip;
        }

        static float[] Shot(float seconds, Func<float, int, float> f, bool fade = true)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            for (int i = 0; i < n; i++) d[i] = f(i / (float)Rate, i);
            if (fade) for (int i = 0, tail = Mathf.Min(n, 600); i < tail; i++) d[n - 1 - i] *= i / (float)tail;
            return d;
        }

        static float[] Noise(float seconds, int seed)
        {
            var random = new System.Random(seed);
            var d = new float[(int)(seconds * Rate)];
            for (int i = 0; i < d.Length; i++) d[i] = (float)(random.NextDouble() * 2 - 1);
            return d;
        }

        /// <summary>One-pole high-pass and low-pass, run <paramref name="order"/> times: a single pass leaves noise hissy,
        /// three give wind, tyres and the city's rumble a firm ceiling.</summary>
        static float[] Band(float[] d, float low, float high, int order = 3)
        {
            float a = 1 - Mathf.Exp(-6.2832f * high / Rate), b = 1 - Mathf.Exp(-6.2832f * low / Rate);
            for (int pass = 0; pass < order; pass++)
            {
                float lp = 0, hp = 0;
                for (int i = 0; i < d.Length; i++) { lp += a * (d[i] - lp); hp += b * (lp - hp); d[i] = lp - hp; }
            }
            return d;
        }

        /// <summary>Cross-fades the tail into the head, so a noise bed loops without a seam.</summary>
        static float[] Seam(float[] d, float seconds)
        {
            int overlap = (int)(seconds * Rate), n = d.Length - overlap;
            var result = new float[n];
            Array.Copy(d, result, n);
            for (int i = 0; i < overlap; i++)
            {
                float w = i / (float)overlap;
                result[i] = d[i] * Mathf.Sqrt(w) + d[n + i] * Mathf.Sqrt(1 - w);
            }
            return result;
        }

        /// <summary>Firing pulses of a four-stroke: harmonics of the firing frequency plus half orders for the uneven burble.</summary>
        static float[] Harmonics(float seconds, float firing, int count, Func<int, float> amplitude, float[] orders, float[] orderAmplitude, float hiss)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            var random = new System.Random((int)firing);
            for (int k = 1; k <= count; k++)
            {
                float a = amplitude(k), phase = (float)random.NextDouble() * 6.2832f, w = 6.2832f * firing * k / Rate;
                for (int i = 0; i < n; i++) d[i] += a * Mathf.Sin(w * i + phase);
            }
            for (int o = 0; o < orders.Length; o++)
            {
                float w = 6.2832f * firing * orders[o] / Rate;
                for (int i = 0; i < n; i++) d[i] += orderAmplitude[o] * Mathf.Sin(w * i);
            }
            var air = Seam(Band(Noise(seconds + .2f, 3), 250, 1800, 2), .2f);
            float level = 0;
            foreach (float v in air) level = Mathf.Max(level, Mathf.Abs(v));
            for (int i = 0; i < n; i++) d[i] = (float)Math.Tanh(d[i] * .7f) + air[i] / level * hiss;
            return d;
        }

        static float[] DieselLoop()
        {
            int n = 2 * Rate;
            var clatter = Seam(Band(Noise(2.2f, 7), 500, 2400, 2), .2f);
            float level = 0;
            foreach (float v in clatter) level = Mathf.Max(level, Mathf.Abs(v));
            var d = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate, knock = Mathf.Pow(.5f + .5f * Mathf.Sin(6.2832f * 40 * t), 6);
                float tone = 0;
                for (int k = 1; k <= 8; k++) tone += Mathf.Sin(6.2832f * 40 * k * t + k) / k;
                d[i] = tone * .6f + Mathf.Sin(6.2832f * 20 * t) * .4f + clatter[i] / level * knock * .7f;
            }
            return d;
        }

        static float[] Tones(float seconds, float[] notes, int harmonics, bool square = false)
        {
            int n = (int)(seconds * Rate);
            var d = new float[n];
            foreach (float note in notes)
                for (int k = 1; k <= harmonics; k += square ? 2 : 1)
                {
                    float w = 6.2832f * note * k / Rate;
                    for (int i = 0; i < n; i++) d[i] += Mathf.Sin(w * i) / k;
                }
            return d;
        }

        /// <summary>Italian two-tone: a fourth apart, a little over half a second each.</summary>
        static float[] SirenLoop()
        {
            const float half = .55f;
            return Shot(half * 2, (t, i) =>
            {
                float s = t < half ? t : t - half, note = t < half ? 466 : 622, v = 0;
                for (int k = 1; k <= 5; k++) v += Mathf.Sin(6.2832f * note * k * s) / k;
                return v * Mathf.Clamp01(s * 120) * Mathf.Clamp01((half - s) * 120);
            }, false);
        }

        static float[] CrashShot()
        {
            var noise = Band(Noise(.9f, 21), 80, 3200, 2);
            float level = 0;
            foreach (float v in noise) level = Mathf.Max(level, Mathf.Abs(v));
            float[] ring = { 523, 1187, 1873, 2637, 3411 }, decay = { 10, 12, 15, 18, 22 };
            return Shot(.9f, (t, i) =>
            {
                float v = noise[i] / level * 1.2f * Mathf.Exp(-t * 9) + Mathf.Sin(6.2832f * (90 - 50 * t) * t) * Mathf.Exp(-t * 14);
                for (int k = 0; k < ring.Length; k++) v += .12f * Mathf.Sin(6.2832f * ring[k] * t) * Mathf.Exp(-t * decay[k]);
                return v;
            });
        }

        /// <summary>A church bell: hum, prime, minor third, fifth and octave partials, the high ones dying first.</summary>
        static float[] BellShot()
        {
            float[] ratio = { .5f, 1, 1.19f, 1.5f, 2, 2.51f, 2.66f, 3.01f, 4.1f }, level = { .25f, 1, .6f, .35f, .8f, .3f, .25f, .2f, .15f };
            var strike = Band(Noise(5, 31), 1500, 6000, 1);
            return Shot(5, (t, i) =>
            {
                float v = strike[i] * 2 * Mathf.Exp(-t * 60);
                for (int k = 0; k < ratio.Length; k++) v += level[k] * Mathf.Sin(6.2832f * 262 * ratio[k] * t) * Mathf.Exp(-t * (.35f + .5f * ratio[k]));
                return v;
            });
        }

        static float[] BirdLoop()
        {
            const float seconds = 9;
            var d = new float[(int)(seconds * Rate)];
            var random = new System.Random(77);
            for (int phrase = 0; phrase < 9; phrase++)
            {
                float at = .3f + (float)random.NextDouble() * (seconds - 1.6f), pitch = 2400 + (float)random.NextDouble() * 2200, level = .25f + (float)random.NextDouble() * .5f;
                int notes = 2 + random.Next(5);
                for (int k = 0; k < notes; k++)
                {
                    float start = at + k * (.09f + (float)random.NextDouble() * .05f), length = .05f + (float)random.NextDouble() * .05f, sweep = (float)(random.NextDouble() * 2 - 1) * 1500;
                    int a = (int)(start * Rate), count = (int)(length * Rate);
                    float phase = 0;
                    for (int i = 0; i < count && a + i < d.Length; i++)
                    {
                        float u = i / (float)count;
                        phase += 6.2832f * (pitch + sweep * u) / Rate;
                        d[a + i] += Mathf.Sin(phase) * Mathf.Sin(u * Mathf.PI) * level;
                    }
                }
            }
            return d;
        }

        static Func<float, int, float> Footfall(System.Random random)
        {
            float lp = 0;
            return (t, i) =>
            {
                lp += .12f * ((float)(random.NextDouble() * 2 - 1) - lp);
                return lp * Mathf.Exp(-t * 45) + Mathf.Sin(6.2832f * 70 * t) * Mathf.Exp(-t * 60) * .5f;
            };
        }
    }
}
