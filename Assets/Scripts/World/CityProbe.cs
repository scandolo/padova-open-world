#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Padova.Architecture;
using Unity.Pipeline.Commands;
using UnityEngine;

namespace Padova.World
{
    /// <summary>Test commands for the streamed city: loading state and a walk-anywhere fixture.</summary>
    public static class CityProbe
    {
        [CliCommand("city_state", "Streamed city: loaded tiles, facades, colliders, pending jobs, vertices and frame timing.")]
        public static object Read()
        {
            var world = Object.FindFirstObjectByType<WorldContext>();
            var s = world ? world.Streamer : null;
            if (!s) return new { ready = false };
            float slowest = s.SlowestApplyMilliseconds;
            s.SlowestApplyMilliseconds = 0;
            return new
            {
                ready = true, tiles = s.Tiles, detail = s.DetailTiles, colliders = s.ColliderTiles, pending = s.Pending, vertices = s.Vertices,
                applyMs = s.LastApplyMilliseconds, slowestApplyMs = slowest, frame = Time.frameCount, time = Time.realtimeSinceStartup,
                dt = Time.unscaledDeltaTime, buildings = world.Buildings,
                shellMs = s.ShellMs, detailMs = s.DetailMs, bakeMs = s.BakeMs, uploadMs = s.UploadMs, shellJobs = s.ShellJobs, detailJobs = s.DetailJobs, bakeJobs = s.BakeJobs
            };
        }

        [CliCommand("city_teleport", "Development-only: put the walking player on the ground at a point anywhere in the comune.")]
        public static object Teleport([CliArg("x", "East of the origin (m)", Required = true)] float x, [CliArg("z", "North of the origin (m)", Required = true)] float z)
        {
            var game = Object.FindFirstObjectByType<OpenWorld>();
            if (!game || game.Mode != "walk") return new { success = false, reason = "Walk first" };
            var p = new Vector3(x, game.City.Data.GroundAt(x, z) + 1.2f, z);
            game.City.Streamer.Prepare(p);
            Physics.SyncTransforms();
            if (Physics.Raycast(p + Vector3.up * 80, Vector3.down, out var hit, 200, 1 << 8)) p.y = hit.point.y + .1f;
            var controller = game.Player.GetComponent<CharacterController>();
            controller.enabled = false; game.Player.transform.position = p; controller.enabled = true;
            game.Player.VerticalSpeed = -1; game.Player.FollowCamera.ResetCamera();
            return new { success = true, position = new[] { p.x, p.y, p.z }, ground = hit.collider != null };
        }
    }
}
#endif
