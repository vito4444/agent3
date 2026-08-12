using System;
using UnityEngine;

namespace Starsoil.Presentation
{
    /// <summary>
    /// L1/L2 camera rig per docs/plan/05: wheel zoom 12–900m, Q/E 45° step rotation,
    /// RMB free rotate, WASD / MMB pan. The pitch band tightens from (40°,70°) in L1
    /// to (55°,75°) as height grows past 120m, fully applied by 300m. Crossing 300m
    /// height flips the far LOD band and raises FarBandChanged (M0-T5 acceptance).
    /// M0 uses the legacy Input API; migration to Input System action maps is scheduled
    /// with M8 rebinding (see SETUP.md).
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        private const float ZoomMin = 12f;
        private const float ZoomMax = 900f;
        private const float PitchTightenStart = 120f;
        private const float PitchTightenEnd = 300f;
        private const float FarBandHeight = 300f;
        private const float PitchMinNear = 40f;
        private const float PitchMaxNear = 70f;
        private const float PitchMinFar = 55f;
        private const float PitchMaxFar = 75f;
        private const float ZoomWheelStepFactor = 1.6f;
        private const float PanSpeedPerHeight = 1.1f;
        private const float RotateDegreesPerPixel = 0.25f;
        private const float PitchPerPixel = 0.003f;
        private const float StepRotationDegrees = 45f;
        private const float DefaultZoom = 60f;
        private const float DefaultYaw = 45f;
        private const float DefaultPitch01 = 0.6f;
        private const float MouseDragPanFactor = 0.0016f;

        private float _zoom = DefaultZoom;
        private float _yaw = DefaultYaw;
        private float _pitch01 = DefaultPitch01;
        private Vector3 _pivot;
        private bool _farBand;

        public int RegionSize { get; set; } = Starsoil.Core.GameConstants.DefaultRegionSize;

        /// <summary>Camera height above the ground plane, in meters.</summary>
        public float Height => transform.position.y;

        public bool IsFarBand => _farBand;

        public event Action<bool> FarBandChanged;

        public Camera Cam { get; private set; }

        private void Awake()
        {
            Cam = GetComponent<Camera>();
            _pivot = new Vector3(RegionSize * 0.5f, 0f, RegionSize * 0.5f);
        }

        public void CenterOn(Vector3 worldPos)
        {
            _pivot = worldPos;
        }

        private void LateUpdate()
        {
            HandleZoom();
            HandleRotation();
            HandlePan();
            Apply();
            UpdateBand();
        }

        private void HandleZoom()
        {
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > Mathf.Epsilon)
            {
                _zoom = Mathf.Clamp(_zoom * Mathf.Pow(ZoomWheelStepFactor, -scroll * 10f), ZoomMin, ZoomMax);
            }
        }

        private void HandleRotation()
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                _yaw -= StepRotationDegrees;
            }
            if (Input.GetKeyDown(KeyCode.E))
            {
                _yaw += StepRotationDegrees;
            }
            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxis("Mouse X") * RotateDegreesPerPixel * Screen.width * 0.05f;
                _pitch01 = Mathf.Clamp01(_pitch01 + Input.GetAxis("Mouse Y") * -PitchPerPixel * Screen.height * 0.05f);
            }
        }

        private void HandlePan()
        {
            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move.z += 1f;
            if (Input.GetKey(KeyCode.S)) move.z -= 1f;
            if (Input.GetKey(KeyCode.D)) move.x += 1f;
            if (Input.GetKey(KeyCode.A)) move.x -= 1f;
            if (move.sqrMagnitude > 0f)
            {
                Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
                _pivot += yawRot * move.normalized * (_zoom * PanSpeedPerHeight * Time.unscaledDeltaTime);
            }
            if (Input.GetMouseButton(2))
            {
                Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
                Vector3 drag = new Vector3(-Input.GetAxis("Mouse X"), 0f, -Input.GetAxis("Mouse Y"));
                _pivot += yawRot * drag * (_zoom * MouseDragPanFactor * Screen.height * 0.05f);
            }
            _pivot.x = Mathf.Clamp(_pivot.x, 0f, RegionSize);
            _pivot.z = Mathf.Clamp(_pivot.z, 0f, RegionSize);
            _pivot.y = 0f;
        }

        private void Apply()
        {
            float tighten = Mathf.InverseLerp(PitchTightenStart, PitchTightenEnd, _zoom);
            float pitchMin = Mathf.Lerp(PitchMinNear, PitchMinFar, tighten);
            float pitchMax = Mathf.Lerp(PitchMaxNear, PitchMaxFar, tighten);
            float pitch = Mathf.Lerp(pitchMin, pitchMax, _pitch01);

            Quaternion rot = Quaternion.Euler(pitch, _yaw, 0f);
            transform.rotation = rot;
            transform.position = _pivot - rot * Vector3.forward * _zoom;
        }

        private void UpdateBand()
        {
            bool far = Height >= FarBandHeight;
            if (far != _farBand)
            {
                _farBand = far;
                Debug.Log("[CameraRig] LOD band -> " + (far ? "Far (L2 blocks)" : "Near (L1 full)"));
                FarBandChanged?.Invoke(far);
            }
        }
    }
}
