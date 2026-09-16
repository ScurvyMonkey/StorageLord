using UnityEngine;

namespace StorageLord.CameraSystem
{
    /// <summary>
    /// Tunable definition of the free-orbit platform camera — orbit/tilt sensitivity, pan speed
    /// and bounds, zoom range, and the default framing a session starts with. Mirrors GridConfig's
    /// role as the single source of tuning for its system. Namespace is "CameraSystem" rather than
    /// "Camera" to avoid colliding with UnityEngine.Camera when referenced unqualified.
    /// </summary>
    [CreateAssetMenu(menuName = "Storage Lord/Camera Config", fileName = "CameraConfig")]
    public class CameraConfig : ScriptableObject
    {
        [Header("Orbit")]
        [Tooltip("Degrees of yaw/pitch rotation per pixel of mouse delta while orbiting (middle-mouse drag).")]
        [Min(0.01f)]
        public float orbitSensitivity = 0.3f;

        [Tooltip("Minimum camera pitch, in degrees above the horizon. Keeps the camera from flattening to the horizon.")]
        public float tiltMin = 15f;

        [Tooltip("Maximum camera pitch, in degrees above the horizon. Keeps the camera from snapping to a pure top-down angle.")]
        public float tiltMax = 80f;

        [Header("Pan")]
        [Tooltip("Focus point pan speed, in units per second, via WASD/arrow keys.")]
        [Min(0.01f)]
        public float panSpeed = 10f;

        [Tooltip("Maximum distance the focus point can pan from platform origin.")]
        [Min(0.01f)]
        public float panBoundsRadius = 40f;

        [Header("Zoom")]
        [Tooltip("Minimum camera distance from the focus point.")]
        [Min(0.01f)]
        public float zoomMin = 5f;

        [Tooltip("Maximum camera distance from the focus point.")]
        [Min(0.01f)]
        public float zoomMax = 60f;

        [Tooltip("Distance change per scroll tick.")]
        [Min(0.01f)]
        public float zoomSpeed = 4f;

        [Header("Default Framing")]
        [Tooltip("Camera yaw, in degrees, at the start of a session.")]
        public float defaultYaw = 0f;

        [Tooltip("Camera pitch, in degrees, at the start of a session.")]
        public float defaultPitch = 45f;

        [Tooltip("Camera distance from the focus point at the start of a session.")]
        public float defaultDistance = 20f;
    }
}
