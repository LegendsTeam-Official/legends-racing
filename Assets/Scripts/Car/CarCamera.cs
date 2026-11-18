using UnityEngine;

namespace Legends
{
    // Smooth mobile-racer chase camera: behind and above the car, velocity look-ahead,
    // speed FOV (60 -> 72), light high-speed shake, and collision-aware pull-in (ignores the car layer 8).
    [RequireComponent(typeof(Camera))]
    public class CarCamera : MonoBehaviour
    {
        public Transform target;

        public float distance = 5.5f;
        public float height = 1.9f;
        public float lookHeight = 1.05f;        // look point above the car origin
        public float lookAhead = 0.12f;         // seconds of velocity added to the look point
        public float maxLookAhead = 4f;
        public float positionSharpness = 9f;
        public float yawSharpness = 5f;
        public float fovMin = 60f, fovMax = 72f;
        public float fovFullSpeed = 55f;        // m/s for max FOV
        public float shakeStartSpeed = 33f;     // m/s (~120 km/h)
        public float shakeAmount = 0.035f;      // metres at top speed
        public float collisionRadius = 0.25f;
        public int ignoreLayer = 8;

        Camera cam;
        Rigidbody targetBody;
        Transform lastTarget;
        float yaw;
        Vector3 smoothPos;
        float currentDist;

        void Awake()
        {
            cam = GetComponent<Camera>();
        }

        void OnEnable()
        {
            if (target != null) Snap();
        }

        // Jump to the ideal position immediately (call after teleporting the car).
        public void Snap()
        {
            if (target == null) return;
            targetBody = target.GetComponent<Rigidbody>();
            lastTarget = target;
            Vector3 f = target.forward; f.y = 0f;
            if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
            yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            currentDist = distance;
            smoothPos = IdealPosition(yaw, distance);
            transform.position = smoothPos;
            transform.rotation = Quaternion.LookRotation(LookPoint(Vector3.zero) - smoothPos, Vector3.up);
        }

        Vector3 IdealPosition(float yawDeg, float dist)
        {
            Vector3 back = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.back;
            return target.position + back * dist + Vector3.up * height;
        }

        Vector3 LookPoint(Vector3 vel)
        {
            Vector3 ahead = Vector3.ClampMagnitude(new Vector3(vel.x, 0f, vel.z) * lookAhead, maxLookAhead);
            return target.position + Vector3.up * lookHeight + ahead;
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (target != lastTarget) Snap();
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 vel = targetBody != null ? targetBody.linearVelocity : Vector3.zero;
            float speed = vel.magnitude;

            // heading: blend car forward with velocity direction when moving forward fast
            Vector3 fwd = target.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 flatVel = new Vector3(vel.x, 0f, vel.z);
            Vector3 dir = fwd;
            if (flatVel.sqrMagnitude > 4f && Vector3.Dot(flatVel, fwd) > 0f)
                dir = Vector3.Slerp(fwd, flatVel.normalized, Mathf.InverseLerp(2f, 20f, flatVel.magnitude) * 0.5f);
            float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            yaw = Mathf.LerpAngle(yaw, targetYaw, 1f - Mathf.Exp(-yawSharpness * dt));

            float dist = distance + Mathf.Clamp01(speed / 60f) * 0.6f;   // pull back a bit at speed
            Vector3 ideal = IdealPosition(yaw, dist);
            smoothPos = Vector3.Lerp(smoothPos, ideal, 1f - Mathf.Exp(-positionSharpness * dt));
            // keep height tight so bumps don't drop the camera into the car
            smoothPos.y = Mathf.Lerp(smoothPos.y, ideal.y, 1f - Mathf.Exp(-positionSharpness * 1.5f * dt));

            // collision-aware pull-in
            Vector3 pivot = target.position + Vector3.up * (lookHeight + 0.3f);
            Vector3 toCam = smoothPos - pivot;
            float want = toCam.magnitude;
            Vector3 finalPos = smoothPos;
            if (want > 0.01f)
            {
                int mask = ~(1 << ignoreLayer);
                RaycastHit hit;
                float allowed = want;
                if (Physics.SphereCast(pivot, collisionRadius, toCam / want, out hit, want, mask, QueryTriggerInteraction.Ignore))
                    allowed = Mathf.Max(0.8f, hit.distance - 0.05f);
                // pull in fast, ease back out
                currentDist = allowed < currentDist ? allowed : Mathf.Lerp(currentDist, allowed, 1f - Mathf.Exp(-3f * dt));
                finalPos = pivot + toCam / want * Mathf.Min(currentDist, want);
            }

            // high-speed shake
            float shake = Mathf.Clamp01((speed - shakeStartSpeed) / 30f) * shakeAmount;
            Vector3 lookPt = LookPoint(vel);
            if (shake > 0f)
            {
                float tt = Time.time * 22f;
                finalPos += transform.right * (Mathf.PerlinNoise(tt, 0.3f) - 0.5f) * 2f * shake
                          + Vector3.up * (Mathf.PerlinNoise(0.7f, tt) - 0.5f) * 2f * shake;
            }

            transform.position = finalPos;
            Vector3 look = lookPt - finalPos;
            if (look.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(look, Vector3.up);

            if (cam != null)
            {
                float f = Mathf.Lerp(fovMin, fovMax, Mathf.SmoothStep(0f, 1f, speed / fovFullSpeed));
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, f, 1f - Mathf.Exp(-3f * dt));
            }
        }
    }
}
