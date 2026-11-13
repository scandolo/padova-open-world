using UnityEngine;
using UnityEngine.InputSystem;

namespace Padova.Gameplay
{
    /// <summary>
    /// Chase camera for walking and driving, modelled on the original browser prototype: it swings in behind the
    /// direction of travel while moving, holds a manual mouse look for a moment, and sweeps its boom against architecture.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class ThirdPersonCamera : MonoBehaviour
    {
        public ThirdPersonMotor Player;
        public float Yaw = 270;
        public float Pitch = 16;
        public float Distance = 5.4f;
        public float ActualDistance;
        public bool Obstructed;
        public bool CursorCaptured;
        /// <summary>Set while the player drives; the camera then follows this body instead of the character.</summary>
        public Transform Vehicle;public float VehicleSpeed;public bool VehicleReversing;public bool VehicleBoosting;
        /// <summary>Velocity of the driven vehicle: in a slide the camera looks where the car is going, not where it points.</summary>
        public Vector3 VehicleVelocity;
        /// <summary>Boom scale for the driven vehicle: below one for a scooter, above for a bus.</summary>
        public float VehicleSize = 1;
        /// <summary>0..1 sway for the SPRITZ easter egg.</summary>
        public float Wobble;
        /// <summary>Current crash shake, 0..1; decays in about half a second.</summary>
        public float Shake { get; private set; }
        /// <summary>0 chase, 1 close, 2 high.</summary>
        public int View;
        /// <summary>True while the mouse is steering the view; the motor re-reads its movement basis then.</summary>
        public bool Looking { get; private set; }
        Camera view;
        bool snap;
        float lastManualLook, zoom = 1;
        InputAction centerAction, releaseAction;

        void Awake()
        {
            view = GetComponent<Camera>();
            centerAction = new InputAction("Cycle camera", InputActionType.Button, "<Keyboard>/c");
            releaseAction = new InputAction("Release cursor", InputActionType.Button, "<Keyboard>/escape");
            centerAction.performed += _ => { View = (View + 1) % 3; Yaw = Heading(); lastManualLook = Time.unscaledTime; snap = true; };
            releaseAction.performed += _ => ReleaseCursor();
        }
        void OnEnable() { centerAction?.Enable(); releaseAction?.Enable(); }
        void Start() { ResetCamera(); }

        /// <summary>Crash feedback: <paramref name="amount"/> 0..1, never weaker than the shake already running.</summary>
        public void AddShake(float amount) { Shake = Mathf.Clamp01(Mathf.Max(Shake, amount)); }

        public void ResetCamera()
        {
            Yaw = Vehicle ? Vehicle.eulerAngles.y : Player && Player.Visual ? Player.Visual.eulerAngles.y : Player ? Player.SpawnYaw : 180;
            Pitch = Vehicle ? DrivePitch : 16;
            Distance = 5.4f;
            lastManualLook = Time.unscaledTime - 10;
            snap = true;
        }

        public void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            CursorCaptured = false;
        }

        void OnDisable() { centerAction?.Disable(); releaseAction?.Disable(); ReleaseCursor(); }
        void OnDestroy() { centerAction?.Dispose(); releaseAction?.Dispose(); }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public void SetTestHeading(float yaw)
        {
            Yaw = yaw;
            lastManualLook = Time.unscaledTime + 30;
            snap = true;
        }
#endif
        const float DrivePitch = 15, LookHold = 1.6f;
        float Heading()
        {
            if (Vehicle)
            {
                if (VehicleReversing) return Vehicle.eulerAngles.y + 180;
                var flat = new Vector3(VehicleVelocity.x, 0, VehicleVelocity.z);
                if (flat.sqrMagnitude < 25) return Vehicle.eulerAngles.y;
                // Halfway between the nose and the direction of travel, so a slide shows as a slide.
                return Quaternion.LookRotation(Vehicle.forward + flat.normalized).eulerAngles.y;
            }
            return Player && Player.Visual ? Player.Visual.eulerAngles.y : Yaw;
        }

        void LateUpdate()
        {
            if (!Player || Player.ControlsPaused) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            var mouse = Mouse.current;
            Looking = false;
            if (mouse != null)
            {
                if (mouse.leftButton.isPressed || mouse.rightButton.isPressed)
                {
                    var delta = mouse.delta.ReadValue();
                    if (delta.sqrMagnitude > 0.01f)
                    {
                        Yaw += delta.x * 0.14f;
                        Pitch = Mathf.Clamp(Pitch - delta.y * 0.10f, -8, 60);
                        lastManualLook = Time.unscaledTime;
                        Looking = true;
                    }
                }
                zoom = Mathf.Clamp(zoom - mouse.scroll.ReadValue().y * 0.0012f, 0.55f, 1.8f);
            }
            bool driving = Vehicle;
            float speed = driving ? VehicleSpeed : Player.CurrentSpeed;
            // Swing in behind the direction of travel unless the player looked around in the last 1.6 s.
            if (Time.unscaledTime - lastManualLook > LookHold)
            {
                // The faster the car, the quicker the camera gets behind it; reversing swings it round at once.
                float follow = driving ? (VehicleReversing ? 4.8f : Mathf.Clamp(1.8f + speed * 0.055f, 2.1f, 4.5f)) : 2.2f;
                if (speed > 0.3f) Yaw = Mathf.LerpAngle(Yaw, Heading(), 1 - Mathf.Exp(-follow * dt));
                float rest = driving ? DrivePitch : 16;
                Pitch = Mathf.Lerp(Pitch, rest, 1 - Mathf.Exp(-0.8f * dt));
            }
            float distance, lift, pitch = Pitch, size = driving ? Mathf.Clamp(VehicleSize, 0.6f, 2.4f) : 1;
            if (View == 1) { distance = driving ? 4.6f * size : 2.9f; lift = driving ? 1.25f : 1.55f; pitch = Mathf.Min(Pitch, 10); }
            else if (View == 2) { distance = driving ? 15 * size : 11; lift = driving ? 1.4f : 1.4f; pitch = Mathf.Max(Pitch, 34); }
            else { distance = driving ? (8 + speed * 0.08f) * size : Distance; lift = driving ? 1.3f + 0.3f * size : 1.45f; }
            distance *= zoom;
            var pivot = (driving ? Vehicle.position : Player.transform.position) + Vector3.up * lift;
            var back = Quaternion.Euler(pitch, Yaw, 0) * Vector3.back;
            var goal = pivot + back * Boom(pivot, back, distance);
            // Smooth, then sweep again: a safe endpoint alone does not make a safe path through a portico.
            var smoothed = snap ? goal : Vector3.Lerp(transform.position, goal, 1 - Mathf.Exp(-(driving ? 10f : 12f) * dt));
            var offset = smoothed - pivot;
            float length = offset.magnitude;
            RaycastHit hit = default;
            Obstructed = length > 0.01f && Physics.SphereCast(pivot, 0.22f, offset / length, out hit, length, ThirdPersonMotor.WorldMask, QueryTriggerInteraction.Ignore);
            transform.position = Obstructed ? pivot + offset / length * Mathf.Max(0.25f, hit.distance - 0.12f) : smoothed;
            if ((pivot - transform.position).sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(pivot + Vector3.up * (driving ? 0.2f : 0.15f) - transform.position);
            if (view)
            {
                float fov = driving ? 58 + Mathf.Min(9, speed * 0.18f) + (VehicleBoosting ? 5 : 0) : 60 + (Player.CurrentSpeed > 5 ? 3 : 0);
                view.fieldOfView = snap ? fov : Mathf.Lerp(view.fieldOfView, fov, 1 - Mathf.Exp(-3 * dt));
            }
            ActualDistance = Vector3.Distance(pivot, transform.position);
            if (Shake > 0.002f)
            {
                // Two out-of-phase noise samples: a jolt, not a wobble. Applied after the boom so it cannot push through a wall by more than a hand's width.
                float t = Time.unscaledTime * 31;
                transform.position += (transform.right * (Mathf.PerlinNoise(t, 0.3f) - 0.5f) + transform.up * (Mathf.PerlinNoise(0.7f, t) - 0.5f)) * (Shake * 0.5f);
                transform.rotation *= Quaternion.Euler(0, 0, (Mathf.PerlinNoise(t, t * 0.5f) - 0.5f) * Shake * 5);
                Shake *= Mathf.Exp(-5 * Time.unscaledDeltaTime);
            }
            else Shake = 0;
            if (Wobble > 0.001f)
            {
                float t = Time.unscaledTime;
                transform.rotation *= Quaternion.Euler(Mathf.Sin(t * 1.3f) * 2.5f * Wobble, Mathf.Sin(t * 0.9f + 1) * 4 * Wobble, Mathf.Sin(t * 1.7f) * 9 * Wobble);
            }
            snap = false;
        }
        float Boom(Vector3 pivot, Vector3 back, float distance)
        {
            if (!Physics.SphereCast(pivot, 0.22f, back, out var hit, distance, ThirdPersonMotor.WorldMask, QueryTriggerInteraction.Ignore)) return distance;
            return Mathf.Max(0.25f, hit.distance - 0.12f);
        }
    }
}
