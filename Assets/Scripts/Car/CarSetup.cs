using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Legends
{
    // Builds a drivable car from the glTF model imported by glTFast (Assets/Resources/Cars/CarConcept.glb).
    // Usage: var car = CarSetup.Spawn(pos, rot);
    public static class CarSetup
    {
        public const string ModelPath = "Cars/CarConcept";
        public const int CarLayer = 8;

        // Extra yaw (degrees) applied to the model if it does not face +Z after auto-detection.
        public static float ModelYawOffset = 0f;
        public static bool CastShadows = false;

        // Wheel node names in CarConcept.glb (glTF +X = car left; glTFast mirrors X, so "L" ends up at Unity -X = left).
        static readonly string[] WheelNames = { "WheelFrontL", "WheelFrontR", "WheelRearL", "WheelRearR" };

        public class WheelInfo
        {
            public Transform visual;   // original wheel node (geometry)
            public Vector3 center;     // car-local
            public float radius;
            public bool front, left;
        }

        public static GameObject Spawn(Vector3 pos, Quaternion rot)
        {
            var root = new GameObject("PlayerCar");
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var prefab = Resources.Load<GameObject>(ModelPath);
            Transform model;
            if (prefab != null)
            {
                // Instantiate keeps the prefab root's local rotation (CarConcept root node carries a -90 deg X matrix).
                model = Object.Instantiate(prefab, root.transform).transform;
                model.name = "Model";
            }
            else
            {
                Debug.LogError("CarSetup: Resources.Load<GameObject>(\"" + ModelPath + "\") returned null. Is com.unity.cloud.gltfast installed?");
                model = BuildFallbackModel(root.transform);
            }

            // ---- find wheels ----
            var wheels = FindWheelsByName(model);
            if (wheels == null) wheels = FindWheelsByPosition(model, root.transform);
            if (wheels == null)
            {
                Debug.LogError("CarSetup: could not identify 4 wheels, using generic positions.");
                wheels = GenericWheels(model);
            }

            // ---- orientation: front wheels must be at +Z ----
            float frontZ = 0f, rearZ = 0f;
            foreach (var w in wheels) { if (w.front) frontZ += w.visual.position.z; else rearZ += w.visual.position.z; }
            float yaw = ModelYawOffset + (frontZ < rearZ ? 180f : 0f);
            if (Mathf.Abs(yaw) > 0.01f) model.RotateAround(Vector3.zero, Vector3.up, yaw);

            // Wheel centres (world == car-local here: root is at origin, identity).
            for (int i = 0; i < wheels.Count; i++)
                wheels[i].center = WheelCenterWorld(wheels[i]);
            // Re-evaluate left/right after a possible 180 deg turn.
            foreach (var w in wheels) w.left = w.center.x < 0f;

            // ---- put the tyres on the ground (root origin = ground contact) ----
            float bottom = 0f;
            foreach (var w in wheels) bottom += (w.center.y - w.radius);
            bottom /= wheels.Count;
            model.position -= new Vector3(0f, bottom, 0f);
            foreach (var w in wheels) w.center.y -= bottom;

            // ---- body bounds (everything except wheels) ----
            var wheelSet = new HashSet<Transform>();
            foreach (var w in wheels) wheelSet.Add(w.visual);
            Bounds body = new Bounds();
            bool hasBody = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (IsUnderAny(r.transform, wheelSet)) continue;
                if (!hasBody) { body = r.bounds; hasBody = true; } else body.Encapsulate(r.bounds);
            }
            if (!hasBody) body = new Bounds(new Vector3(0f, 0.7f, 0.2f), new Vector3(2f, 1.1f, 4.3f));

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }

            // ---- rigidbody (added before colliders so they attach to it) ----
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 1450f;
            rb.linearDamping = 0.01f;
            rb.angularDamping = 0.35f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // ---- wheel visual rigs: Hub (steer, no spin) -> Spin (rolls) -> original node ----
            var rigs = new CarController.WheelRig[4];
            var wcs = new WheelCollider[4];
            const float suspensionDistance = 0.18f;
            const float targetPos = 0.45f;
            for (int i = 0; i < wheels.Count; i++)
            {
                var w = wheels[i];
                var hub = new GameObject((w.front ? "Hub_F" : "Hub_R") + (w.left ? "L" : "R")).transform;
                hub.SetParent(root.transform, false);
                hub.localPosition = w.center;
                hub.localRotation = Quaternion.identity;

                // Remove any steering yaw baked into the model (CarConcept front wheels are posed at ~30 deg).
                float bakedYaw = BakedYaw(w.visual);
                if (Mathf.Abs(bakedYaw) > 0.5f) w.visual.RotateAround(w.center, Vector3.up, -bakedYaw);

                var spin = new GameObject("Spin").transform;
                spin.SetParent(hub, false);
                spin.localPosition = Vector3.zero;
                spin.localRotation = Quaternion.identity;

                // Brake calipers should steer but not spin: move them to the hub.
                var calipers = new List<Transform>();
                foreach (var t in w.visual.GetComponentsInChildren<Transform>(true))
                {
                    string n = t.name.ToLowerInvariant();
                    if (t != w.visual && (n.Contains("brakepad") || n.Contains("caliper"))) calipers.Add(t);
                }
                foreach (var c in calipers) c.SetParent(hub, true);
                w.visual.SetParent(spin, true);

                // WheelCollider sits above the visual centre so that at targetPosition it matches the model.
                var wcGo = new GameObject("WC_" + (w.front ? "F" : "R") + (w.left ? "L" : "R"));
                wcGo.transform.SetParent(root.transform, false);
                wcGo.transform.localPosition = w.center + Vector3.up * (suspensionDistance * targetPos);
                wcGo.transform.localRotation = Quaternion.identity;
                var wc = wcGo.AddComponent<WheelCollider>();
                wc.radius = w.radius;
                wc.mass = 22f;
                wc.wheelDampingRate = 0.3f;
                wc.suspensionDistance = suspensionDistance;
                wc.forceAppPointDistance = 0.12f;
                wc.center = Vector3.zero;
                var spring = new JointSpring();
                spring.spring = 42000f;
                spring.damper = 4200f;
                spring.targetPosition = targetPos;
                wc.suspensionSpring = spring;
                wc.forwardFriction = CarController.MakeCurve(0.38f, 1.0f, 0.85f, 0.62f, 1.35f);
                wc.sidewaysFriction = w.front
                    ? CarController.MakeCurve(0.24f, 1.0f, 0.60f, 0.78f, 1.55f)
                    : CarController.MakeCurve(0.26f, 1.0f, 0.65f, 0.70f, 1.40f);
                wcs[i] = wc;

                rigs[i] = new CarController.WheelRig
                {
                    collider = wc,
                    hub = hub,
                    spin = spin,
                    front = w.front,
                    left = w.left
                };
            }

            // ---- body colliders (lower hull + cabin) ----
            var bodyMat = new PhysicsMaterial("CarBody");
            bodyMat.dynamicFriction = 0.15f;
            bodyMat.staticFriction = 0.15f;
            bodyMat.bounciness = 0.05f;
            bodyMat.frictionCombine = PhysicsMaterialCombine.Minimum;
            bodyMat.bounceCombine = PhysicsMaterialCombine.Minimum;

            Vector3 bmin = body.min, bmax = body.max, bsize = body.size;
            float hullBottom = Mathf.Max(bmin.y, 0f) + 0.14f;              // keep clear of the road
            float hullTop = bmin.y + bsize.y * 0.62f;
            AddBox(root.transform, "ColHull", bodyMat,
                new Vector3(body.center.x, (hullBottom + hullTop) * 0.5f, body.center.z),
                new Vector3(bsize.x * 0.96f, Mathf.Max(0.2f, hullTop - hullBottom), bsize.z * 0.98f));
            float cabBottom = hullTop - 0.02f;
            float cabTop = bmax.y;
            float cabZ0 = bmin.z + bsize.z * 0.10f, cabZ1 = bmin.z + bsize.z * 0.68f;
            AddBox(root.transform, "ColCabin", bodyMat,
                new Vector3(body.center.x, (cabBottom + cabTop) * 0.5f, (cabZ0 + cabZ1) * 0.5f),
                new Vector3(bsize.x * 0.80f, Mathf.Max(0.1f, cabTop - cabBottom), cabZ1 - cabZ0));

            // centre of mass
            float axleF = 0f, axleR = 0f, wheelY = 0f;
            int nf = 0, nr = 0;
            foreach (var w in wheels) { if (w.front) { axleF += w.center.z; nf++; } else { axleR += w.center.z; nr++; } wheelY += w.center.y; }
            axleF /= Mathf.Max(1, nf); axleR /= Mathf.Max(1, nr); wheelY /= wheels.Count;
            // Slightly front-biased, low centre of mass (below wheel centre) for a planted arcade feel.
            rb.centerOfMass = new Vector3(0f, wheelY - 0.08f, Mathf.Lerp(axleR, axleF, 0.53f));

            // Vehicle sub-stepping (one call configures the whole vehicle).
            if (wcs[0] != null) wcs[0].ConfigureVehicleSubsteps(5f, 12, 15);

            // ---- layer ----
            SetLayerRecursive(root, CarLayer);

            // ---- behaviours ----
            var ctrl = root.AddComponent<CarController>();
            ctrl.Init(rb, rigs);
            root.AddComponent<CarInput>();
            root.AddComponent<EngineAudio>();
            root.AddComponent<SpeedHud>();

            root.transform.SetPositionAndRotation(pos, rot);
            Physics.SyncTransforms();
            return root;
        }

        // ------------------------------------------------------------------ helpers

        static List<WheelInfo> FindWheelsByName(Transform model)
        {
            var all = model.GetComponentsInChildren<Transform>(true);
            var list = new List<WheelInfo>();
            for (int i = 0; i < WheelNames.Length; i++)
            {
                Transform found = null;
                foreach (var t in all) if (t.name == WheelNames[i]) { found = t; break; }
                if (found == null)
                {
                    // looser match: "wheel" + front/rear + l/r suffix
                    bool f = i < 2, l = (i % 2) == 0;
                    foreach (var t in all)
                    {
                        string n = t.name.ToLowerInvariant();
                        if (!n.Contains("wheel") || n.Contains("steer")) continue;
                        bool isF = n.Contains("front") || n.Contains("_f") || n.Contains("fl") || n.Contains("fr");
                        bool isR = n.Contains("rear") || n.Contains("back") || n.Contains("_r") || n.Contains("rl") || n.Contains("rr");
                        if (f ? !isF : !isR) continue;
                        bool isL = n.EndsWith("l") || n.Contains("left") || n.Contains("fl") || n.Contains("rl");
                        if (l != isL) continue;
                        if (t.GetComponentsInChildren<Renderer>(true).Length == 0) continue;
                        found = t; break;
                    }
                }
                if (found == null) return null;
                var wi = new WheelInfo { visual = found, front = i < 2, left = (i % 2) == 0 };
                wi.radius = WheelRadius(found);
                list.Add(wi);
            }
            return list;
        }

        // Fallback: the 4 lowest-lying, roughly round, separate mesh nodes at the corners.
        static List<WheelInfo> FindWheelsByPosition(Transform model, Transform root)
        {
            var rends = model.GetComponentsInChildren<Renderer>(true);
            if (rends.Length < 5) return null;
            Bounds all = rends[0].bounds;
            foreach (var r in rends) all.Encapsulate(r.bounds);
            var cands = new List<Renderer>();
            foreach (var r in rends)
            {
                var b = r.bounds;
                if (b.center.y > all.min.y + all.size.y * 0.4f) continue;
                if (b.size.y < all.size.y * 0.25f) continue;
                if (Mathf.Abs(b.size.y - b.size.z) > 0.25f * b.size.y) continue;   // roughly round in side view
                if (b.size.x > b.size.y * 0.9f) continue;
                cands.Add(r);
            }
            var list = new List<WheelInfo>();
            for (int q = 0; q < 4; q++)
            {
                bool front = q < 2, left = (q % 2) == 0;
                Renderer best = null; float bestScore = float.MinValue;
                foreach (var r in cands)
                {
                    var c = r.bounds.center - all.center;
                    if ((c.z > 0f) != front || (c.x < 0f) != left) continue;
                    float score = Mathf.Abs(c.x) + Mathf.Abs(c.z) + r.bounds.size.y * 2f;
                    if (score > bestScore) { bestScore = score; best = r; }
                }
                if (best == null) return null;
                // Use an intermediate pivot so spinning happens around the bounds centre.
                var pivot = new GameObject("WheelPivot").transform;
                pivot.SetParent(best.transform.parent, true);
                pivot.position = best.bounds.center;
                pivot.rotation = root.rotation;
                best.transform.SetParent(pivot, true);
                list.Add(new WheelInfo { visual = pivot, front = front, left = left, radius = best.bounds.extents.y });
            }
            return list;
        }

        static List<WheelInfo> GenericWheels(Transform model)
        {
            var list = new List<WheelInfo>();
            Vector3[] p = { new Vector3(-0.95f, 0.38f, 1.45f), new Vector3(0.95f, 0.38f, 1.45f), new Vector3(-0.95f, 0.38f, -1.3f), new Vector3(0.95f, 0.38f, -1.3f) };
            for (int i = 0; i < 4; i++)
            {
                var t = new GameObject("WheelDummy").transform;
                t.SetParent(model, false);
                t.position = p[i];
                list.Add(new WheelInfo { visual = t, front = i < 2, left = (i % 2) == 0, radius = 0.38f });
            }
            return list;
        }

        static Vector3 WheelCenterWorld(WheelInfo w)
        {
            // CarConcept wheel nodes have their pivot exactly at the wheel centre; prefer it when it is inside the bounds.
            var rends = w.visual.GetComponentsInChildren<Renderer>(true);
            if (rends.Length == 0) return w.visual.position;
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            Vector3 p = w.visual.position;
            if ((p - b.center).magnitude < w.radius * 0.35f) return p;
            return b.center;
        }

        static float WheelRadius(Transform wheel)
        {
            // Largest (tyre) mesh: radius from its local bounds perpendicular to the axle (local X).
            float best = 0f;
            foreach (var mf in wheel.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var e = mf.sharedMesh.bounds.extents;
                Vector3 s = mf.transform.lossyScale;
                float r = Mathf.Max(e.y * Mathf.Abs(s.y), e.z * Mathf.Abs(s.z));
                if (r > best) best = r;
            }
            if (best < 0.1f || best > 1.0f) best = 0.38f;
            return best;
        }

        // Yaw (about world up) between the wheel's axle (local X) and the car's right axis, folded to [-90, 90].
        static float BakedYaw(Transform wheel)
        {
            Vector3 axle = wheel.rotation * Vector3.right;
            axle.y = 0f;
            if (axle.sqrMagnitude < 1e-4f) return 0f;
            float a = Vector3.SignedAngle(Vector3.right, axle.normalized, Vector3.up);
            if (a > 90f) a -= 180f;
            else if (a < -90f) a += 180f;
            return a;
        }

        static bool IsUnderAny(Transform t, HashSet<Transform> set)
        {
            while (t != null) { if (set.Contains(t)) return true; t = t.parent; }
            return false;
        }

        static void AddBox(Transform parent, string name, PhysicsMaterial mat, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var bc = go.AddComponent<BoxCollider>();
            bc.center = center;
            bc.size = size;
            bc.sharedMaterial = mat;
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        static Transform BuildFallbackModel(Transform parent)
        {
            var model = new GameObject("Model").transform;
            model.SetParent(parent, false);
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(body.GetComponent<Collider>());
            body.name = "Body";
            body.transform.SetParent(model, false);
            body.transform.localPosition = new Vector3(0f, 0.65f, 0.2f);
            body.transform.localScale = new Vector3(2f, 0.8f, 4.3f);
            string[] names = WheelNames;
            Vector3[] p = { new Vector3(-0.95f, 0.38f, 1.45f), new Vector3(0.95f, 0.38f, 1.45f), new Vector3(-0.95f, 0.38f, -1.3f), new Vector3(0.95f, 0.38f, -1.3f) };
            for (int i = 0; i < 4; i++)
            {
                var w = new GameObject(names[i]).transform;
                w.SetParent(model, false);
                w.localPosition = p[i];
                var cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Object.Destroy(cyl.GetComponent<Collider>());
                cyl.transform.SetParent(w, false);
                cyl.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                cyl.transform.localScale = new Vector3(0.76f, 0.14f, 0.76f);
            }
            return model;
        }
    }
}
