using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Padova.World
{
    /// <summary>Three presets that trade distance and shadows for frame rate. "Balanced" is tuned for a MacBook Air;
    /// machines with 8 GB start on "Battery". The choice is remembered.</summary>
    public static class Quality
    {
        public static readonly string[] Names = { "Battery", "Balanced", "Sharp" };
        public static int Level { get; private set; } = 1;
        static readonly float[] Shell = { 950, 1400, 1800 }, Detail = { 95, 150, 190 }, Collide = { 330, 430, 430 }, Shadows = { 70, 140, 190 }, Scale = { .78f, 1, 1 }, Parked = { 105, 150, 180 };
        static readonly int[] Cascades = { 2, 4, 4 }, Walkers = { 14, 26, 34 };

        public static void Load(OpenWorld game)
        {
            int fallback = SystemInfo.systemMemorySize > 0 && SystemInfo.systemMemorySize <= 8192 ? 0 : 1;
            Apply(game, Harness.Launched ? 1 : PlayerPrefs.GetInt("padova.quality", fallback), false);
        }

        public static void Apply(OpenWorld game, int level, bool remember = true)
        {
            Level = Mathf.Clamp(level, 0, Names.Length - 1);
            if (remember && !Harness.Launched) { PlayerPrefs.SetInt("padova.quality", Level); PlayerPrefs.Save(); }
            if (game && game.City && game.City.Streamer)
            {
                var streamer = game.City.Streamer;
                streamer.ShellRadius = Shell[Level]; streamer.DetailRadius = Detail[Level]; streamer.ColliderRadius = Collide[Level];
            }
            if (game && game.Parked) game.Parked.Radius = Parked[Level];
            var walkers = game ? game.GetComponentInChildren<StreetWalkers>() : null;
            if (walkers) walkers.Limit = Walkers[Level];
#if !UNITY_EDITOR
            // In a Player the pipeline asset is a private copy in memory; in the Editor it is the project's file, so it is left alone.
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)
            {
                pipeline.shadowDistance = Shadows[Level];
                pipeline.shadowCascadeCount = Cascades[Level];
                pipeline.renderScale = Scale[Level];
            }
#endif
        }
    }
}
