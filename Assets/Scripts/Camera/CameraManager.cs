using UnityEngine;
using UnityEngine.InputSystem;

namespace StorageLord.CameraSystem
{
    /// <summary>
    /// Drives the scene's pre-placed MainCamera-tagged Camera as a free-orbit rig: holding the
    /// middle mouse button and dragging orbits/tilts, WASD/arrow keys pan the focus point, and
    /// scroll zooms. Deliberately never destroys, recreates, or reparents the Camera itself —
    /// PlacementManager caches Camera.main once in its own Awake and never re-queries it, so the
    /// Camera GameObject must stay exactly as the scene places it (as a child of the CameraRig this
    /// manager repositions).
    ///
    /// Created and wired by Bootstrapper.
    /// </summary>
    public class CameraManager : MonoBehaviour
    {
        private CameraConfig _config;
        private Transform _rig;
        private Camera _camera;

        private float _yaw;
        private float _pitch;
        private float _distance;

        /// <summary>
        /// Injects this manager's data/scene references, caches the rig's child Camera, and snaps
        /// to the configured default session framing. Called once by Bootstrapper immediately after
        /// creation, since this manager is created in code (not from a prefab) and so has no
        /// Inspector to assign references through directly.
        /// </summary>
        public void Initialize(CameraConfig config, Transform rig)
        {
            _config = config;
            _rig = rig;
            _camera = rig != null ? rig.GetComponentInChildren<Camera>() : null;

            if (_config == null || _rig == null || _camera == null)
            {
                Debug.LogWarning("CameraManager: missing CameraConfig, rig, or rig Camera child — camera control disabled.");
                return;
            }

            _yaw = _config.defaultYaw;
            _pitch = _config.defaultPitch;
            _distance = _config.defaultDistance;
            _rig.position = Vector3.zero;
            ApplyCameraTransform();
        }

        /// <summary>
        /// Polls orbit/pan/zoom input each frame and re-applies the camera's spherical offset from
        /// the rig.
        /// </summary>
        private void Update()
        {
            if (_config == null || _rig == null || _camera == null || Mouse.current == null || Keyboard.current == null)
            {
                return;
            }

            HandleOrbitInput();
            HandlePanInput();
            HandleZoomInput();
            ApplyCameraTransform();
        }

        /// <summary>
        /// While the middle mouse button is held, adjusts yaw/pitch by mouse delta, clamping pitch
        /// so the camera can never flatten to the horizon or snap to a pure top-down angle.
        /// </summary>
        private void HandleOrbitInput()
        {
            if (!Mouse.current.middleButton.isPressed)
            {
                return;
            }

            Vector2 delta = Mouse.current.delta.ReadValue();
            _yaw += delta.x * _config.orbitSensitivity;
            _pitch = Mathf.Clamp(_pitch + delta.y * _config.orbitSensitivity, _config.tiltMin, _config.tiltMax);
        }

        /// <summary>
        /// Moves the rig's focus point via WASD/arrow keys, relative to the camera's current yaw so
        /// "forward" always pans further into view, clamped to panBoundsRadius from platform origin.
        /// </summary>
        private void HandlePanInput()
        {
            Vector2 input = Vector2.zero;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1f;

            if (input.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Quaternion yawRotation = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 move = yawRotation * new Vector3(input.x, 0f, input.y);
            Vector3 nextPosition = _rig.position + move.normalized * (_config.panSpeed * Time.deltaTime);

            Vector3 flatPosition = new Vector3(nextPosition.x, 0f, nextPosition.z);
            flatPosition = Vector3.ClampMagnitude(flatPosition, _config.panBoundsRadius);
            _rig.position = new Vector3(flatPosition.x, _rig.position.y, flatPosition.z);
        }

        /// <summary>
        /// Adjusts zoom distance via scroll wheel, clamped to [zoomMin, zoomMax].
        /// </summary>
        private void HandleZoomInput()
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Approximately(scroll, 0f))
            {
                return;
            }

            _distance = Mathf.Clamp(_distance - Mathf.Sign(scroll) * _config.zoomSpeed, _config.zoomMin, _config.zoomMax);
        }

        /// <summary>
        /// Positions the camera as a spherical offset from the rig based on the current yaw/pitch/
        /// distance, always facing the rig.
        /// </summary>
        private void ApplyCameraTransform()
        {
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 position = _rig.position - rotation * Vector3.forward * _distance;
            _camera.transform.SetPositionAndRotation(position, rotation);
        }
    }
}
