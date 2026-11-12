using UnityEngine;
using UnityEngine.InputSystem;

namespace Padova.Gameplay
{
    [RequireComponent(typeof(CharacterController))]
    public sealed class ThirdPersonMotor : MonoBehaviour
    {
        public ThirdPersonCamera FollowCamera;
        public Animator Animator;
        public Transform Visual;
        public Vector3 Spawn;
        public Vector3 Goal;
        public float WalkSpeed = 3.4f;
        public float RunSpeed = 6.4f;
        public float JumpHeight = 1.05f;
        public float TravelledMetres;
        public float CurrentSpeed;
        public float VerticalSpeed;
        public bool Grounded;
        public bool ReachedGoal;
        public int JumpCount;
        public int WallContacts;
        public int ArchitectureContacts;
        public string LastWallSourceId;
        public int RecoveryCount;
        public bool ControlsPaused;
        public bool RunToggled;
        public Vector2 MoveInput;
        public float SpawnYaw = 180;
        public Vector2 WorldHalfSize = new Vector2(320,235);
        public const int GroundMask = 1 << 8;
        public const int WorldMask = (1 << 8) | (1 << 9);
        CharacterController controller;
        InputAction jumpAction, runToggleAction, resetAction;
        bool jumpRequested;
        // Movement basis is held while the same keys stay down, so a camera swinging in behind
        // the walker cannot feed back into the direction of travel (the original prototype's rule).
        Vector2 basisInput;
        float basisYaw;

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            jumpAction = new InputAction("Jump", InputActionType.Button, "<Keyboard>/space");
            runToggleAction = new InputAction("Toggle run", InputActionType.Button, "<Keyboard>/tab");
            resetAction = new InputAction("Return to start", InputActionType.Button, "<Keyboard>/r");
            jumpAction.performed += _ => jumpRequested = true;
            runToggleAction.performed += _ => {if(!ControlsPaused)RunToggled = !RunToggled;};
            resetAction.performed += _ => {if(!ControlsPaused)Respawn();};
            Application.targetFrameRate = 60;
            Respawn();
        }

        void OnEnable() { jumpAction?.Enable(); runToggleAction?.Enable(); resetAction?.Enable(); }
        void OnDisable() { jumpAction?.Disable(); runToggleAction?.Disable(); resetAction?.Disable(); }
        void OnDestroy() { jumpAction?.Dispose(); runToggleAction?.Dispose(); resetAction?.Dispose(); }

        public void Respawn()
        {
            if (!controller) controller = GetComponent<CharacterController>();
            controller.enabled = false;
            transform.position = Spawn;
            controller.enabled = true;
            VerticalSpeed = -2;
            CurrentSpeed = 0;
            jumpRequested = false;
            ControlsPaused = false;
            if (Visual) Visual.rotation = Quaternion.Euler(0, SpawnYaw, 0);
            if (FollowCamera) FollowCamera.ResetCamera();
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var dt = Mathf.Min(Time.deltaTime, 0.05f);
            Grounded = controller.isGrounded;
            if (Grounded && VerticalSpeed < 0) VerticalSpeed = -2;
            Vector2 input = Vector2.zero;
            bool sprint = false;
            if (keyboard != null && !ControlsPaused)
            {
                input = new Vector2((keyboard.rightArrowKey.isPressed || keyboard.dKey.isPressed ? 1 : 0) - (keyboard.leftArrowKey.isPressed || keyboard.aKey.isPressed ? 1 : 0),
                                    (keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed ? 1 : 0) - (keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed ? 1 : 0));
                sprint = RunToggled || keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                if (Grounded && jumpRequested)
                {
                    VerticalSpeed = Mathf.Sqrt(JumpHeight * 2 * 22);
                    JumpCount++;
                    Grounded = false;
                }
            }
            jumpRequested = false;
            MoveInput = input;
            float cameraYaw = FollowCamera ? FollowCamera.Yaw : SpawnYaw;
            if (input != basisInput || input == Vector2.zero || FollowCamera && FollowCamera.Looking) basisYaw = cameraYaw;
            basisInput = input;
            var facing = Quaternion.Euler(0, basisYaw, 0);
            var direction = facing * new Vector3(input.x, 0, input.y);
            direction = Vector3.ClampMagnitude(direction, 1);
            var desired = direction * (sprint ? RunSpeed : WalkSpeed);
            var before = transform.position;
            VerticalSpeed = Mathf.Max(VerticalSpeed - 22 * dt, -35);
            controller.Move((desired + Vector3.up * VerticalSpeed) * dt);
            Grounded = controller.isGrounded;
            if (Grounded && VerticalSpeed < 0) VerticalSpeed = -2;
            var horizontal = Vector3.ProjectOnPlane(transform.position - before, Vector3.up);
            CurrentSpeed = dt > 0 ? horizontal.magnitude / dt : 0;
            TravelledMetres += horizontal.magnitude;
            if (direction.sqrMagnitude > 0.01f && Visual)
                Visual.rotation = Quaternion.Slerp(Visual.rotation, Quaternion.LookRotation(direction), 1-Mathf.Exp(-14*dt));
            if (Animator)
            {
                Animator.SetFloat("Speed", CurrentSpeed, 0.1f, dt);
                Animator.SetBool("Grounded", Grounded);
            }
            if (Vector3.ProjectOnPlane(transform.position-Goal, Vector3.up).magnitude < 2.5f && Grounded)
                ReachedGoal = true;
            // Gaps in survey coverage and the district edge are handled as a game boundary.
            // No invisible plane is added to pretend that unsurveyed ground is known.
            if (transform.position.y < -6 || Mathf.Abs(transform.position.x) > WorldHalfSize.x || Mathf.Abs(transform.position.z) > WorldHalfSize.y)
            {
                RecoveryCount++;
                Respawn();
            }
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (Mathf.Abs(hit.normal.y) < 0.5f)
            {
                WallContacts++;
                if (hit.gameObject.layer == 9)
                {
                    ArchitectureContacts++;
                    var feature = hit.collider.GetComponent<Padova.Geography.SurveyFeature>();
                    LastWallSourceId = feature ? feature.SourceId : hit.gameObject.name;
                }
            }
        }

        void OnApplicationFocus(bool focused)
        {
            if (!focused && FollowCamera) FollowCamera.ReleaseCursor();
        }
    }
}
