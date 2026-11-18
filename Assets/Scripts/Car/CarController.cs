using UnityEngine;

namespace Legends
{
    // Arcade-sim car: engine curve + 6-speed automatic, speed-sensitive steering, brakes/handbrake,
    // downforce, anti-roll bars, light traction control and drift assist. Inputs are written by CarInput.
    [DisallowMultipleComponent]
    public class CarController : MonoBehaviour
    {
        [System.Serializable]
        public class WheelRig
        {
            public WheelCollider collider;
            public Transform hub;    // steers, does not spin (calipers live here)
            public Transform spin;   // rolls with the wheel (tyre + rim + disc)
            public bool front, left;
            [System.NonSerialized] public Quaternion spinOffset = Quaternion.identity;
            [System.NonSerialized] public float slip;
        }

        public enum Drive { RearWheel, AllWheel }

        // ---------------- inputs (written by CarInput) ----------------
        [Header("Input")]
        [Range(0f, 1f)] public float throttle;
        [Range(0f, 1f)] public float brake;
        [Range(-1f, 1f)] public float steer;
        public bool handbrake;

        // ---------------- outputs ----------------
        public float SpeedKmh { get; private set; }
        public float Rpm { get; private set; }
        public int Gear { get; private set; } = 1;          // -1 = R, 0 = N, 1..6
        public float SlipAmount { get; private set; }        // 0..1, max over wheels
        public float ForwardSpeed { get; private set; }      // m/s, signed
        public bool IsShifting => shiftTimer > 0f;
        public float ThrottleApplied { get; private set; }   // effective drive input (0..1) for audio

        [Header("Engine")]
        public float peakTorque = 430f;      // Nm
        public float idleRpm = 900f;
        public float redlineRpm = 7200f;
        public float topSpeedKmh = 230f;
        public float[] gearRatios = { 3.20f, 2.10f, 1.55f, 1.20f, 0.97f, 0.80f };
        public float reverseRatio = 3.1f;
        public float finalDrive = 5.0f;
        public float drivetrainEfficiency = 0.85f;
        public float upshiftRpm = 6700f;
        public float shiftTime = 0.22f;
        public Drive drive = Drive.AllWheel;
        [Range(0f, 1f)] public float frontTorqueShare = 0.35f;   // AWD only
        public bool tractionControl = true;

        [Header("Brakes")]
        public float maxBrakeTorque = 3000f;
        [Range(0f, 1f)] public float frontBrakeBias = 0.62f;
        public float handbrakeTorque = 4500f;
        [Range(0.1f, 1f)] public float handbrakeRearGrip = 0.45f;

        [Header("Steering")]
        public float maxSteerLow = 34f;      // deg at standstill
        public float maxSteerHigh = 8f;      // deg at highSpeedKmh
        public float highSpeedKmh = 190f;
        public float steerSpeed = 140f;      // deg/s
        public float counterSteerAssist = 0.35f;

        [Header("Aero / stability")]
        public float downforce = 2.2f;       // N per (m/s)^2
        public float dragCoefficient = 0.42f;// N per (m/s)^2
        public float antiRollFront = 7000f;
        public float antiRollRear = 5000f;
        [Range(0.5f, 1.5f)] public float gripScale = 0.92f;   // wet road

        public WheelRig[] wheels;
        Rigidbody rb;
        AnimationCurve torqueCurve;
        float steerAngle;
        float shiftTimer;
        float reverseHold;
        float flipTimer;
        float[] baseSideStiffness;
        float[] baseFwdStiffness;
        float rearGripFactor = 1f;

        public Rigidbody Body => rb;

        public static WheelFrictionCurve MakeCurve(float extSlip, float extVal, float asSlip, float asVal, float stiffness)
        {
            var c = new WheelFrictionCurve();
            c.extremumSlip = extSlip;
            c.extremumValue = extVal;
            c.asymptoteSlip = asSlip;
            c.asymptoteValue = asVal;
            c.stiffness = stiffness;
            return c;
        }

        public void Init(Rigidbody body, WheelRig[] rigs)
        {
            rb = body;
            wheels = rigs;
            baseSideStiffness = new float[wheels.Length];
            baseFwdStiffness = new float[wheels.Length];
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                baseSideStiffness[i] = w.collider.sidewaysFriction.stiffness;
                baseFwdStiffness[i] = w.collider.forwardFriction.stiffness;
                // offset between collider frame and the visual's rest rotation
                w.spinOffset = Quaternion.Inverse(w.collider.transform.rotation) * w.spin.rotation;
            }
        }

        void Awake()
        {
            torqueCurve = new AnimationCurve(
                new Keyframe(0f, 0.55f),
                new Keyframe(1000f, 0.62f),
                new Keyframe(2500f, 0.84f),
                new Keyframe(4500f, 1.00f),
                new Keyframe(6000f, 0.95f),
                new Keyframe(7200f, 0.80f),
                new Keyframe(8000f, 0.60f));
            Rpm = idleRpm;
            if (rb == null) rb = GetComponent<Rigidbody>();
        }

        void FixedUpdate()
        {
            if (rb == null || wheels == null) return;
            float dt = Time.fixedDeltaTime;

            Vector3 vel = rb.linearVelocity;
            ForwardSpeed = Vector3.Dot(vel, transform.forward);
            SpeedKmh = vel.magnitude * 3.6f;

            // ---------- gear selection / reverse ----------
            float driveIn, brakeIn;
            if (Gear < 0)
            {
                driveIn = brake; brakeIn = throttle;
                if (throttle > 0.1f && ForwardSpeed > -1.0f) { Gear = 1; driveIn = throttle; brakeIn = brake; }
            }
            else
            {
                driveIn = throttle; brakeIn = brake;
                if (brake > 0.1f && throttle < 0.1f && ForwardSpeed < 0.6f)
                {
                    reverseHold += dt;
                    if (reverseHold > 0.25f) { Gear = -1; reverseHold = 0f; driveIn = brake; brakeIn = 0f; }
                }
                else reverseHold = 0f;
            }

            // ---------- engine rpm ----------
            float wheelRpm = 0f; int driven = 0;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (!IsDriven(wheels[i])) continue;
                wheelRpm += wheels[i].collider.rpm; driven++;
            }
            wheelRpm = driven > 0 ? Mathf.Abs(wheelRpm / driven) : 0f;
            float ratio = CurrentRatio();
            float rpmFromWheels = wheelRpm * ratio * finalDrive;
            float target;
            if (IsShifting) target = Mathf.Max(idleRpm, rpmFromWheels);
            else
            {
                // clutch slip at low speed lets the engine rev above the wheels
                float slipRpm = idleRpm + driveIn * 2600f;
                target = Mathf.Max(rpmFromWheels, (Mathf.Abs(Gear) == 1) ? slipRpm : idleRpm + driveIn * 600f);
            }
            target = Mathf.Clamp(target, idleRpm, redlineRpm + 200f);
            Rpm = Mathf.Lerp(Rpm, target, 1f - Mathf.Exp(-dt * 10f));

            // ---------- automatic gearbox ----------
            if (shiftTimer > 0f) shiftTimer -= dt;
            if (Gear > 0 && !IsShifting)
            {
                float downRpm = 2300f + 1800f * driveIn;
                if (Rpm > upshiftRpm && Gear < gearRatios.Length && driveIn > 0.05f && ForwardSpeed > 2f)
                { Gear++; shiftTimer = shiftTime; }
                else if (Gear > 1 && rpmFromWheels < downRpm)
                {
                    // only downshift if the lower gear does not over-rev
                    float lower = wheelRpm * gearRatios[Gear - 2] * finalDrive;
                    if (lower < upshiftRpm - 600f) { Gear--; shiftTimer = shiftTime * 0.7f; }
                }
            }

            // ---------- drive torque ----------
            float engineTorque = peakTorque * torqueCurve.Evaluate(Rpm) * driveIn;
            if (Rpm >= redlineRpm) engineTorque = 0f;                    // limiter
            if (IsShifting) engineTorque *= 0.15f;
            float speedCap = Mathf.InverseLerp(topSpeedKmh, topSpeedKmh - 12f, SpeedKmh);
            if (Gear > 0) engineTorque *= speedCap;
            else engineTorque *= Mathf.InverseLerp(45f, 35f, SpeedKmh);   // reverse top ~40 km/h
            float wheelTorqueTotal = engineTorque * ratio * finalDrive * drivetrainEfficiency * Mathf.Sign(Gear == 0 ? 0 : Gear);
            ThrottleApplied = driveIn;

            // ---------- steering ----------
            float t = Mathf.InverseLerp(0f, highSpeedKmh, SpeedKmh);
            float maxSteer = Mathf.Lerp(maxSteerLow, maxSteerHigh, t * (2f - t));
            float targetSteer = steer * maxSteer;
            Vector3 localVel = transform.InverseTransformDirection(vel);
            if (localVel.z > 3f && counterSteerAssist > 0f)
            {
                float slipAngle = Mathf.Atan2(localVel.x, localVel.z) * Mathf.Rad2Deg;
                targetSteer += Mathf.Clamp(slipAngle * counterSteerAssist, -20f, 20f);
            }
            targetSteer = Mathf.Clamp(targetSteer, -maxSteerLow, maxSteerLow);
            steerAngle = Mathf.MoveTowards(steerAngle, targetSteer, steerSpeed * dt);

            // ---------- handbrake grip ----------
            rearGripFactor = Mathf.MoveTowards(rearGripFactor, handbrake ? handbrakeRearGrip : 1f, dt * (handbrake ? 4f : 1.6f));

            // ---------- per wheel ----------
            float maxSlip = 0f;
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                var wc = w.collider;

                if (w.front) wc.steerAngle = steerAngle;

                // friction (wet grip + handbrake)
                var side = wc.sidewaysFriction;
                side.stiffness = baseSideStiffness[i] * gripScale * (w.front ? 1f : rearGripFactor);
                wc.sidewaysFriction = side;
                var fwd = wc.forwardFriction;
                fwd.stiffness = baseFwdStiffness[i] * gripScale;
                wc.forwardFriction = fwd;

                WheelHit hit;
                bool grounded = wc.GetGroundHit(out hit);

                // motor
                float share = 0f;
                if (drive == Drive.RearWheel) share = w.front ? 0f : 0.5f;
                else share = (w.front ? frontTorqueShare : 1f - frontTorqueShare) * 0.5f;
                float tq = wheelTorqueTotal * share;
                if (tractionControl && grounded && tq != 0f)
                {
                    float fs = Mathf.Abs(hit.forwardSlip);
                    tq *= Mathf.Lerp(1f, 0.35f, Mathf.InverseLerp(0.35f, 0.75f, fs));
                }
                wc.motorTorque = tq;

                // brakes
                float bt = brakeIn * maxBrakeTorque * (w.front ? frontBrakeBias : 1f - frontBrakeBias) * 2f;
                if (!w.front && handbrake) bt = Mathf.Max(bt, handbrakeTorque);
                if (driveIn < 0.02f && brakeIn < 0.02f && Mathf.Abs(ForwardSpeed) < 0.8f) bt = Mathf.Max(bt, 300f); // hold still
                wc.brakeTorque = bt;

                // slip for fx
                float s = 0f;
                if (grounded)
                {
                    s = Mathf.Max(Mathf.Abs(hit.sidewaysSlip) / 0.45f, Mathf.Abs(hit.forwardSlip) / 0.75f);
                    s = Mathf.Clamp01(s - 0.25f) / 0.75f;
                }
                w.slip = s;
                if (s > maxSlip) maxSlip = s;
            }
            SlipAmount = Mathf.Lerp(SlipAmount, maxSlip, 1f - Mathf.Exp(-dt * 12f));

            // ---------- anti-roll bars ----------
            AntiRoll(true, antiRollFront);
            AntiRoll(false, antiRollRear);

            // ---------- aero ----------
            float v2 = vel.sqrMagnitude;
            if (AnyGrounded()) rb.AddForce(-transform.up * downforce * v2);
            if (v2 > 0.01f) rb.AddForce(-vel.normalized * dragCoefficient * v2);

            // ---------- auto-righting ----------
            if (Vector3.Dot(transform.up, Vector3.up) < 0.25f && vel.magnitude < 4f)
            {
                flipTimer += dt;
                if (flipTimer > 3f) ResetCar();
            }
            else flipTimer = 0f;
        }

        void LateUpdate()
        {
            if (wheels == null) return;
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                Vector3 p; Quaternion q;
                w.collider.GetWorldPose(out p, out q);
                Transform ct = w.collider.transform;
                w.hub.SetPositionAndRotation(p, ct.rotation * Quaternion.Euler(0f, w.collider.steerAngle, 0f));
                w.spin.SetPositionAndRotation(p, q * w.spinOffset);
            }
        }

        bool IsDriven(WheelRig w)
        {
            if (drive == Drive.RearWheel) return !w.front;
            return true;
        }

        float CurrentRatio()
        {
            if (Gear < 0) return reverseRatio;
            if (Gear == 0) return 0f;
            return gearRatios[Mathf.Clamp(Gear - 1, 0, gearRatios.Length - 1)];
        }

        bool AnyGrounded()
        {
            for (int i = 0; i < wheels.Length; i++) if (wheels[i].collider.isGrounded) return true;
            return false;
        }

        void AntiRoll(bool front, float stiffness)
        {
            WheelCollider l = null, r = null;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i].front != front) continue;
                if (wheels[i].left) l = wheels[i].collider; else r = wheels[i].collider;
            }
            if (l == null || r == null) return;
            WheelHit hit;
            float travelL = 1f, travelR = 1f;
            bool gl = l.GetGroundHit(out hit);
            if (gl) travelL = (-l.transform.InverseTransformPoint(hit.point).y - l.radius) / l.suspensionDistance;
            bool gr = r.GetGroundHit(out hit);
            if (gr) travelR = (-r.transform.InverseTransformPoint(hit.point).y - r.radius) / r.suspensionDistance;
            float force = (travelL - travelR) * stiffness;
            if (gl) rb.AddForceAtPosition(l.transform.up * -force, l.transform.position);
            if (gr) rb.AddForceAtPosition(r.transform.up * force, r.transform.position);
        }

        // Puts the car back on its wheels at the current spot (yaw kept).
        public void ResetCar()
        {
            if (rb == null) return;
            Vector3 fwd = transform.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-3f) fwd = Vector3.forward;
            Vector3 pos = rb.position + Vector3.up * 1.5f;
            Quaternion rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = pos;
            rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
            Gear = 1; Rpm = idleRpm; flipTimer = 0f; steerAngle = 0f;
        }

        // Teleport (e.g. respawn at a given point).
        public void Teleport(Vector3 pos, Quaternion rot)
        {
            if (rb == null) return;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = pos;
            rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
            Gear = 1; Rpm = idleRpm; flipTimer = 0f; steerAngle = 0f;
        }
    }
}
