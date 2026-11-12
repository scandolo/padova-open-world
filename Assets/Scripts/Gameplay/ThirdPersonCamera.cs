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

        public void ResetCamera()
        {
            Yaw = Vehicle ? Vehicle.eulerAngles.y : Player && Player.Visual ? Player.Visual.eulerAngles.y : Player ? Player.SpawnYaw : 180;
            Pitch = Vehicle ? 11 : 16;
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
        float Heading()
        {
            if (Vehicle) return Vehicle.eulerAngles.y + (VehicleReversing ? 180 : 0);
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
            // Swing in behind the direction of travel unless the player looked around in the last 1.25 s.
            if (Time.unscaledTime - lastManualLook > 1.25f)
            {
                if (speed > 0.3f) Yaw = Mathf.LerpAngle(Yaw, Heading(), 1 - Mathf.Exp(-(driving ? 3.2f : 2.2f) * dt));
                float rest = driving ? 11 : 16;
                Pitch = Mathf.Lerp(Pitch, rest, 1 - Mathf.Exp(-0.8f * dt));
            }
            float distance, lift, pitch = Pitch;
            if (View == 1) { distance = driving ? 4.6f : 2.9f; lift = driving ? 1.25f : 1.55f; pitch = Mathf.Min(Pitch, 10); }
            else if (View == 2) { distance = driving ? 15 : 11; lift = driving ? 1.4f : 1.4f; pitch = Mathf.Max(Pitch, 34); }
            else { distance = driving ? 7.4f + speed * 0.09f : Distance; lift = driving ? 1.5f : 1.45f; }
            distance *= zoom;
            var pivot = (driving ? Vehicle.position : Player.transform.position) + Vector3.up * lift;
            var back = Quaternion.Euler(pitch, Yaw, 0) * Vector3.back;
            var goal = pivot + back * Boom(pivot, back, distance);
            // Smooth, then sweep again: a safe endpoint alone does not make a safe path through a portico.
            var smoothed = snap ? goal : Vector3.Lerp(transform.position, goal, 1 - Mathf.Exp(-(driving ? 9f : 12f) * dt));
            var offset = smoothed - pivot;
            float length = offset.magnitude;
            RaycastHit hit = default;
            Obstructed = length > 0.01f && Physics.SphereCast(pivot, 0.22f, offset / length, out hit, length, ThirdPersonMotor.WorldMask, QueryTriggerInteraction.Ignore);
            transform.position = Obstructed ? pivot + offset / length * Mathf.Max(0.25f, hit.distance - 0.12f) : smoothed;
            if ((pivot - transform.position).sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(pivot + Vector3.up * (driving ? 0.2f : 0.15f) - transform.position);
            if (view)
            {
                float fov = 60 + (driving ? Mathf.Min(13, speed * 0.33f) + (VehicleBoosting ? 6 : 0) : (Player.CurrentSpeed > 5 ? 3 : 0));
                view.fieldOfView = snap ? fov : Mathf.Lerp(view.fieldOfView, fov, 1 - Mathf.Exp(-3 * dt));
            }
            ActualDistance = Vector3.Distance(pivot, transform.position);
            snap = false;
        }
        float Boom(Vector3 pivot, Vector3 back, float distance)
        {
            if (!Physics.SphereCast(pivot, 0.22f, back, out var hit, distance, ThirdPersonMotor.WorldMask, QueryTriggerInteraction.Ignore)) return distance;
            return Mathf.Max(0.25f, hit.distance - 0.12f);
        }
    }
}
