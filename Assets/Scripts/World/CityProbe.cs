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

        [CliCommand("city_time", "Development-only: read the clock, sound and night-light state; --hour sets the time of day.")]
        public static object TimeOfDay([CliArg("hour", "Hour of the day, 0-24; omit to read")] float hour = -1, [CliArg("running", "0 stops the clock, 1 starts it")] int running = -1)
        {
            var day = DayCycle.Instance;
            if (!day) return new { ready = false };
            if (hour >= 0) day.Set(hour);
            if (running >= 0) day.Running = running > 0;
            var audio = Object.FindFirstObjectByType<CityAudio>();
            var lights = Object.FindFirstObjectByType<NightLights>();
            var sun = RenderSettings.sun;
            return new
            {
                ready = true, hour = day.Hour, night = day.Night, elevation = day.Elevation, clock = day.Clock, skips = day.Skips,
                sun = sun ? sun.intensity : 0, sunPitch = sun ? sun.transform.eulerAngles.x : 0, headlights = lights && lights.On,
                sound = audio != null, rpm = audio ? audio.Rpm : 0, gear = audio ? audio.Gear : 0, honks = audio ? audio.Honks : 0, hornHeld = audio && audio.HornHeld,
                crashes = audio ? audio.Crashes : 0, tolls = audio ? audio.Tolls : 0, volume = AudioListener.volume, paused = AudioListener.pause
            };
        }

        [CliCommand("city_parked", "Kerbside vehicles: how many stand around the player, were taken, knocked loose or blocked; --near 1 walks the player beside the nearest.")]
        public static object Parked([CliArg("near", "1 puts the walking player next to the nearest parked vehicle")] int near = 0)
        {
            var game = Object.FindFirstObjectByType<OpenWorld>();
            var parked = game ? game.Parked : null;
            if (!parked) return new { ready = false };
            var nearest = parked.NearestTo(game.Position, 400);
            if (near > 0 && nearest && game.Mode == "walk")
            {
                var p = nearest.transform.position + nearest.transform.right * (nearest.Spec.Width * .5f + 1.1f);
                if (Physics.Raycast(p + Vector3.up * 3, Vector3.down, out var hit, 8, 1 << 8)) p = hit.point + Vector3.up * .05f;
                var controller = game.Player.GetComponent<CharacterController>();
                controller.enabled = false; game.Player.transform.position = p; controller.enabled = true;
                game.Player.VerticalSpeed = -1; game.Player.FollowCamera.ResetCamera();
            }
            return new
            {
                ready = true, standing = parked.Standing, taken = parked.Taken, knocked = parked.Knocked, blocked = parked.Blocked, cars = game.Cars.Length,
                nearest = nearest ? nearest.Spec.Name : "", distance = nearest ? Vector3.Distance(nearest.transform.position, game.Position) : -1,
                within = game.NearbyParked ? game.NearbyParked.Spec.Name : "", vehicle = game.Driving ? game.Driving.Spec.Name : "",
                at = nearest ? new[] { nearest.transform.position.x, nearest.transform.position.y, nearest.transform.position.z } : new float[0],
                facing = nearest ? new[] { nearest.transform.forward.x, nearest.transform.forward.z } : new float[0]
            };
        }

        [CliCommand("city_drive", "Development-only: put the car being driven on the lane nearest a point anywhere in the comune, facing along it.")]
        public static object Drive([CliArg("x", "East of the origin (m)", Required = true)] float x, [CliArg("z", "North of the origin (m)", Required = true)] float z)
        {
            var game = Object.FindFirstObjectByType<OpenWorld>();
            if (!game || game.Mode != "drive" || !game.Driving) return new { success = false, reason = "Drive first" };
            var net = game.Traffic ? game.Traffic.Network : null;
            var p = new Vector3(x, 0, z);
            var rotation = game.Driving.transform.rotation;
            if (net != null)
            {
                var (lane, s, _) = net.Nearest(p, true);
                if (lane >= 0)
                {
                    p = net.Sample(lane, s, out var tangent);
                    tangent.y = 0;
                    if (tangent.sqrMagnitude > .001f) rotation = Quaternion.LookRotation(tangent);
                }
            }
            game.City.Streamer.Prepare(p);
            Physics.SyncTransforms();
            p.y = game.SurfaceAt(p) + .3f;
            game.Driving.Place(p, rotation);
            game.Player.FollowCamera.ResetCamera();
            return new { success = true, position = new[] { p.x, p.y, p.z } };
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
