using System.Reflection;
using NUnit.Framework;
using Padova.World;
using UnityEngine;

namespace PadovaOpenWorld.Tests
{
    /// <summary>Sound synthesis and the day clock: no scene needed.</summary>
    public class AtmosphereTests
    {
        [Test]
        public void EverySoundIsFiniteNormalisedAndLoopsWithoutAClick()
        {
            SoundBank.Build();
            int clips = 0;
            foreach (var field in typeof(SoundBank).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(AudioClip)) continue;
                var clip = (AudioClip)field.GetValue(null);
                Assert.That(clip, Is.Not.Null, field.Name);
                clips++;
                var data = new float[clip.samples];
                clip.GetData(data, 0);
                float peak = 0;
                double energy = 0;
                foreach (float v in data)
                {
                    Assert.That(float.IsNaN(v) || float.IsInfinity(v), Is.False, field.Name + " has a bad sample");
                    peak = Mathf.Max(peak, Mathf.Abs(v));
                    energy += v * v;
                }
                Assert.That(peak, Is.InRange(.5f, .9f), field.Name + " peak");
                Assert.That(System.Math.Sqrt(energy / data.Length), Is.GreaterThan(.01), field.Name + " is nearly silent");
            }
            Assert.That(clips, Is.GreaterThanOrEqualTo(18));
            // Loops meet themselves: the step across the seam is no larger than the steps inside the clip.
            foreach (var clip in new[] { SoundBank.Engine, SoundBank.Buzz, SoundBank.Diesel, SoundBank.Horn, SoundBank.Meep, SoundBank.Skid, SoundBank.Wind, SoundBank.Hum })
            {
                var data = new float[clip.samples];
                clip.GetData(data, 0);
                float largest = 0;
                for (int i = 1; i < data.Length; i++) largest = Mathf.Max(largest, Mathf.Abs(data[i] - data[i - 1]));
                Assert.That(Mathf.Abs(data[0] - data[data.Length - 1]), Is.LessThanOrEqualTo(largest * 1.05f + .01f), clip.name + " clicks at the loop point");
            }
        }

        [Test]
        public void TheSunRisesInTheMorningAndSetsInTheEvening()
        {
            Assert.That(DayCycle.ElevationAt(13.25f), Is.InRange(58f, 63f), "High at midday");
            Assert.That(DayCycle.ElevationAt(DayCycle.Sunrise), Is.EqualTo(0).Within(.01f));
            Assert.That(DayCycle.ElevationAt(DayCycle.Sunset), Is.EqualTo(0).Within(.01f));
            Assert.That(DayCycle.ElevationAt(2), Is.LessThan(-15f), "Well below the horizon at night");
            for (float h = DayCycle.Sunrise; h < 13; h += .5f) Assert.That(DayCycle.ElevationAt(h + .5f), Is.GreaterThan(DayCycle.ElevationAt(h)), "Climbing at " + h);
            for (float h = 14; h < DayCycle.Sunset - .5f; h += .5f) Assert.That(DayCycle.ElevationAt(h + .5f), Is.LessThan(DayCycle.ElevationAt(h)), "Sinking at " + h);
            // No jump at midnight.
            Assert.That(DayCycle.ElevationAt(23.99f), Is.EqualTo(DayCycle.ElevationAt(.01f)).Within(.2f));
        }
    }
}
