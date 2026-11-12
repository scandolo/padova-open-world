#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Padova.Gameplay
{
    // Compiled once with the game. Probes never invoke Roslyn while a scenario is moving.
    public static class GameplayProbe
    {
        [Serializable] public class State
        {
            public bool ready, playing, focused, grounded, reachedGoal, cameraBlocked, runToggled;
            public string scene, wallSource, clip;
            public float[] position, spawn, goal, leftFootRotation;
            public float speed, travel, cameraDistance, cameraYaw, modelHeight, modelOffset, animationTime;
            public int jumps, contacts, recoveries, frame;
        }
        static ThirdPersonMotor Player => UnityEngine.Object.FindFirstObjectByType<ThirdPersonMotor>();
        /// <summary>QA only: keyboard input is accepted while another app has focus, so a test run survives the user touching the Mac.</summary>
        public static bool FocusIgnored;
        [CliCommand("gameplay_focus_policy", "QA only: accept device input while the Player is unfocused (in memory, not saved).")]
        public static State FocusPolicy([CliArg("ignore", "true to ignore focus", Required=true)] bool ignore)
        {
            UnityEngine.InputSystem.InputSystem.settings.backgroundBehavior=ignore?UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus:UnityEngine.InputSystem.InputSettings.BackgroundBehavior.ResetAndDisableNonBackgroundDevices;
            FocusIgnored=ignore;return Read();
        }
        static float[] XYZ(Vector3 v) => new[] {v.x,v.y,v.z};
        [CliCommand("gameplay_state", "Read player, animation, camera and objective state without runtime compilation.")]
        public static State Read()
        {
            var p=Player;
            var s=new State {ready=p!=null,playing=Application.isPlaying,focused=Application.isFocused||FocusIgnored,scene=SceneManager.GetActiveScene().name,frame=Time.frameCount};
            if(!p)return s;
            s.position=XYZ(p.transform.position);s.spawn=XYZ(p.Spawn);s.goal=XYZ(p.Goal);
            s.grounded=p.Grounded;s.speed=p.CurrentSpeed;s.travel=p.TravelledMetres;s.jumps=p.JumpCount;
            s.contacts=p.ArchitectureContacts;s.wallSource=p.LastWallSourceId;s.recoveries=p.RecoveryCount;s.reachedGoal=p.ReachedGoal;
            s.runToggled=p.RunToggled;
            s.cameraBlocked=p.FollowCamera.Obstructed;s.cameraDistance=p.FollowCamera.ActualDistance;s.cameraYaw=p.FollowCamera.Yaw;
            var r=p.GetComponentInChildren<SkinnedMeshRenderer>();
            if(r){s.modelHeight=r.bounds.size.y;s.modelOffset=Vector3.Distance(r.bounds.center,p.transform.position);}
            if(p.Animator && p.Animator.isInitialized)
            {
                var state=p.Animator.GetCurrentAnimatorStateInfo(0);s.animationTime=state.normalizedTime;
                s.clip=state.IsName("Jump")?"Jump":"Locomotion";
                var foot=p.Visual.GetComponentsInChildren<Transform>().FirstOrDefault(t=>t.name=="LeftFoot");
                if(foot){var q=foot.localRotation;s.leftFootRotation=new[]{q.x,q.y,q.z,q.w};}
            }
            return s;
        }
        [CliCommand("gameplay_camera", "Set a camera heading for a repeatable test fixture.")]
        public static State CameraHeading([CliArg("yaw", "Heading in degrees", Required=true)] float yaw)
        {
            Player.FollowCamera.SetTestHeading(yaw);
            return Read();
        }
        [CliCommand("gameplay_fixture", "Reset a scenario, or place the player outside the district to test recovery.")]
        public static State Fixture([CliArg("name", "reset | outside", Required=true)] string name)
        {
            var p=Player;
            if(!p || !Application.isPlaying) throw new InvalidOperationException("Enter Play Mode or start the development Player.");
            if(name=="reset"){p.RunToggled=false;p.ReachedGoal=false;p.Respawn();}
            else if(name=="outside")
            {
                var c=p.GetComponent<CharacterController>();c.enabled=false;
                p.transform.position=new Vector3(500,-10,500);c.enabled=true;
            }
            else throw new ArgumentException("Unknown fixture: "+name);
            return Read();
        }
        [CliCommand("gameplay_collision_fixture", "Place the player on mapped ground just outside a real nearby wall for an input collision scenario.")]
        public static State CollisionFixture()
        {
            var p=Player;
            var start=p.Spawn+Vector3.up;
            foreach(var direction in new[]{Vector3.forward,Vector3.back,Vector3.left,Vector3.right})
            {
                if(!Physics.Raycast(start,direction,out var wall,100,1<<9))continue;
                var outside=wall.point+wall.normal*1.2f;
                if(!Physics.Raycast(outside+Vector3.up*30,Vector3.down,out var ground,60,ThirdPersonMotor.GroundMask))continue;
                if(Physics.CheckCapsule(ground.point+Vector3.up*.32f,ground.point+Vector3.up*1.5f,.3f,1<<9))continue;
                var c=p.GetComponent<CharacterController>();c.enabled=false;p.transform.position=ground.point+Vector3.up*.04f;c.enabled=true;
                p.VerticalSpeed=-2;p.RunToggled=false;
                float yaw=Mathf.Atan2(-wall.normal.x,-wall.normal.z)*Mathf.Rad2Deg;
                p.Visual.rotation=Quaternion.Euler(0,yaw,0);p.FollowCamera.SetTestHeading(yaw);
                return Read();
            }
            throw new InvalidOperationException("No wall fixture with valid mapped ground found.");
        }
        [CliCommand("gameplay_capture", "Capture the next game frame as a PNG.")]
        public static string Capture([CliArg("path", "Absolute PNG output path", Required=true)] string path)
        {
            if(!Path.IsPathRooted(path)||Path.GetExtension(path).ToLowerInvariant()!=".png")throw new ArgumentException("Use an absolute PNG path.");
            ScreenCapture.CaptureScreenshot(path);
            return path;
        }
        [CliCommand("gameplay_grounding", "Measure the animated mesh sole against the actual collision surface.")]
        public static object Grounding()
        {
            var p=Player;var skin=p.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh=new Mesh();skin.BakeMesh(mesh);
            float sole=mesh.vertices.Min(v=>skin.transform.TransformPoint(v).y);
            UnityEngine.Object.Destroy(mesh);
            bool hit=Physics.Raycast(p.transform.position+Vector3.up,Vector3.down,out var ground,3,ThirdPersonMotor.GroundMask);
            return new {hit,soleGap=hit?sole-ground.point.y:999};
        }
    }
}
#endif
