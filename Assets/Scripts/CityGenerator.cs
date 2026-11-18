using System.Collections.Generic;
using UnityEngine;

namespace Legends
{
    // Procedural western downtown: road grid, sidewalks, buildings with lit windows,
    // shops, street lamps, trees, rooftop clutter and a distant skyline.
    public class CityGenerator : MonoBehaviour
    {
        public const int Lines = 8;          // road lines per axis
        public const float Pitch = 132f;     // distance between road centrelines
        public const float RoadHalf = 7f;    // 4 lanes x 3.5 m
        public const float Walk = 4f;        // sidewalk width
        public const float Curb = 0.15f;     // sidewalk height

        public readonly List<Vector3> LampHeads = new List<Vector3>();
        public Vector3 SpawnPoint, SpawnForward;

        System.Random rnd;
        readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        readonly Dictionary<string, MeshBuilder> builders = new Dictionary<string, MeshBuilder>();

        static float LineCoord(int i) => (i - (Lines - 1) * 0.5f) * Pitch;

        Material M(string name)
        {
            if (!mats.TryGetValue(name, out var m))
            {
                m = Resources.Load<Material>("Materials/" + name);
                if (m == null) Debug.LogError("Missing material " + name);
                mats[name] = m;
            }
            return m;
        }

        MeshBuilder B(string mat)
        {
            if (!builders.TryGetValue(mat, out var b))
            {
                b = new MeshBuilder();
                if (mat == "Halo") { b.useUV2 = true; b.useColor = true; }
                if (mat == "GroundGlow") b.useColor = true;
                builders[mat] = b;
            }
            return b;
        }

        // Flush builders into GameObjects (called per chunk to keep meshes reasonable)
        void Flush(string chunk, bool colliders)
        {
            foreach (var kv in builders)
            {
                if (kv.Value.Count == 0) continue;
                bool tangents = kv.Key != "Halo" && kv.Key != "GroundGlow";
                var go = new GameObject(chunk + "_" + kv.Key);
                go.transform.SetParent(transform, false);
                var mesh = kv.Value.ToMesh(go.name, tangents);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = M(kv.Key);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                if (kv.Key == "Halo" || kv.Key == "GroundGlow") mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                if (colliders && (kv.Key == "Road" || kv.Key == "Intersection" || kv.Key == "Sidewalk"))
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
                kv.Value.Clear();
            }
        }

        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        bool Chance(float p) => rnd.NextDouble() < p;

        public void Generate(int seed)
        {
            rnd = new System.Random(seed);
            Ground();
            Roads();
            Flush("Roads", true);
            for (int i = -1; i < Lines; i++)
                for (int j = -1; j < Lines; j++)
                {
                    Block(i, j);
                    Flush($"Block_{i}_{j}", true);
                }
            StreetFurniture();
            Flush("Street", false);
            Skyline();
            Flush("Skyline", false);

            // spawn: middle of a central avenue, looking along it
            float x = LineCoord(Lines / 2) - 1.75f;
            SpawnPoint = new Vector3(x, 2.6f, LineCoord(1) + 30f);
            SpawnForward = Vector3.forward;
        }

        void Ground()
        {
            var b = B("Ground");
            b.Top(-2500, -2500, 2500, 2500, -0.02f, 0.05f);
        }

        // ---------------- roads ----------------
        void Roads()
        {
            float lo = LineCoord(0), hi = LineCoord(Lines - 1);
            for (int i = 0; i < Lines; i++)
            {
                float c = LineCoord(i);
                for (int k = 0; k < Lines - 1; k++)
                {
                    float a = LineCoord(k) + RoadHalf, e = LineCoord(k + 1) - RoadHalf;
                    RoadSegmentZ(c, a, e);   // road running along z at x=c
                    RoadSegmentX(c, a, e);   // road running along x at z=c
                }
                for (int k = 0; k < Lines; k++)
                {
                    float z = LineCoord(k);
                    B("Intersection").Top(c - RoadHalf, z - RoadHalf, c + RoadHalf, z + RoadHalf, 0f, 1f / 14f,
                        new Vector2(-(c - RoadHalf) / 14f, -(z - RoadHalf) / 14f));
                }
            }
        }

        void RoadSegmentZ(float x, float z0, float z1)
        {
            float u0 = 0, u1 = 1;
            var p0 = new Vector3(x - RoadHalf, 0, z0); var p1 = new Vector3(x - RoadHalf, 0, z1);
            var p2 = new Vector3(x + RoadHalf, 0, z1); var p3 = new Vector3(x + RoadHalf, 0, z0);
            float v0 = z0 / 32f, v1 = z1 / 32f;
            B("Road").Quad(p0, p1, p2, p3, Vector3.up, new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0));
        }

        void RoadSegmentX(float z, float x0, float x1)
        {
            // u across (along z), v along x; quad seen from above, clockwise
            var p0 = new Vector3(x0, 0, z - RoadHalf); var p1 = new Vector3(x0, 0, z + RoadHalf);
            var p2 = new Vector3(x1, 0, z + RoadHalf); var p3 = new Vector3(x1, 0, z - RoadHalf);
            float v0 = x0 / 32f, v1 = x1 / 32f;
            B("Road").Quad(p0, p1, p2, p3, Vector3.up, new Vector2(1, v0), new Vector2(0, v0), new Vector2(0, v1), new Vector2(1, v1));
        }

        // ---------------- blocks ----------------
        struct Lot { public Vector3 a, b; public Vector3 inward; public float depth; }

        void Block(int i, int j)
        {
            // block spans between road lines i and i+1 (outer ring uses virtual lines)
            float x0 = LineCoord(i) + RoadHalf, x1 = LineCoord(i + 1) - RoadHalf;
            float z0 = LineCoord(j) + RoadHalf, z1 = LineCoord(j + 1) - RoadHalf;
            bool outer = i < 0 || j < 0 || i >= Lines - 1 || j >= Lines - 1;

            // sidewalk slab with curb faces
            var sw = B("Sidewalk");
            sw.Top(x0, z0, x1, z1, Curb, 0.25f);
            var m = new Vector2(0.25f, 0.25f);
            sw.Wall(new Vector3(x0, 0, z0), new Vector3(x1, 0, z0), 0, Curb, m, Vector2.zero);
            sw.Wall(new Vector3(x1, 0, z1), new Vector3(x0, 0, z1), 0, Curb, m, Vector2.zero);
            sw.Wall(new Vector3(x0, 0, z1), new Vector3(x0, 0, z0), 0, Curb, m, Vector2.zero);
            sw.Wall(new Vector3(x1, 0, z0), new Vector3(x1, 0, z1), 0, Curb, m, Vector2.zero);

            float bx0 = x0 + Walk, bx1 = x1 - Walk, bz0 = z0 + Walk, bz1 = z1 - Walk;
            Vector2 c = new Vector2((bx0 + bx1) * 0.5f, (bz0 + bz1) * 0.5f);
            float downtown = 1f - Mathf.Clamp01((c.magnitude - 120f) / 420f);
            if (outer) downtown *= 0.6f;

            float maxDepth = 24f;
            // south (-z) side, facing -z
            Row(new Vector3(bx0, 0, bz0), new Vector3(bx1, 0, bz0), Vector3.forward, maxDepth, downtown);
            // north (+z) side, facing +z (walk from +x to -x)
            Row(new Vector3(bx1, 0, bz1), new Vector3(bx0, 0, bz1), Vector3.back, maxDepth, downtown);
            // west (-x) side, between rows
            Row(new Vector3(bx0, 0, bz1 - maxDepth), new Vector3(bx0, 0, bz0 + maxDepth), Vector3.right, maxDepth, downtown);
            // east (+x) side
            Row(new Vector3(bx1, 0, bz0 + maxDepth), new Vector3(bx1, 0, bz1 - maxDepth), Vector3.left, maxDepth, downtown);
            // courtyard roof filler so gaps don't show sky from above
            B("Roof").Top(bx0 + 10, bz0 + 10, bx1 - 10, bz1 - 10, Curb + 0.05f, 0.125f);
        }

        // Place buildings along a frontage from a (left, seen from street) to b; inward = into the block
        void Row(Vector3 a, Vector3 b, Vector3 inward, float maxDepth, float downtown)
        {
            Vector3 dir = (b - a); float len = dir.magnitude; dir /= len;
            float s = 0;
            while (s < len - 4f)
            {
                float w = R(10f, 26f);
                if (len - s - w < 8f) w = len - s;
                float depth = Mathf.Min(maxDepth, R(14f, maxDepth));
                int floors = Mathf.RoundToInt(R(2f, 5f) + downtown * R(3f, 17f));
                if (Chance(0.08f)) floors += Mathf.RoundToInt(downtown * R(4f, 10f));
                Vector3 fa = a + dir * s, fb = a + dir * (s + w);
                Building(fa, fb, inward, depth, floors);
                s += w;
            }
        }

        static readonly string[] Facades = { "FacadeConcrete", "FacadeBrick", "FacadeStone" };
        static readonly string[] Awnings = { "AwningRed", "AwningGreen", "AwningBlue" };

        void Building(Vector3 fa, Vector3 fb, Vector3 inward, float depth, int floors)
        {
            const float shopH = 4.5f, floorH = 3.2f;
            float y0 = Curb;
            float H = shopH + floors * floorH + 0.6f;
            string fac = Facades[rnd.Next(Facades.Length)];
            var f = B(fac);
            Vector2 uvMul = new Vector2(1f / 24f, 1f / 19.2f);
            Vector2 off = new Vector2(R(0, 24) / 24f, rnd.Next(6) * floorH / 19.2f - (y0 + shopH) / 19.2f);

            Vector3 ba = fa + inward * depth, bb = fb + inward * depth; // back corners
            float w = (fb - fa).magnitude;
            // front: shop band + facade above
            B("Shop").Wall(fa, fb, y0, y0 + shopH, new Vector2(1f / 24f, 1f / shopH), new Vector2(R(0, 24) / 24f, -y0 / shopH));
            f.Wall(fa, fb, y0 + shopH, y0 + H, uvMul, off);
            // sides and back (full height facade)
            f.Wall(fb, bb, y0, y0 + H, uvMul, off + new Vector2(0.37f, 0));
            f.Wall(bb, ba, y0, y0 + H, uvMul, off + new Vector2(0.71f, 0));
            f.Wall(ba, fa, y0, y0 + H, uvMul, off + new Vector2(0.13f, 0));
            // roof + cornice
            Vector3 mn = Vector3.Min(Vector3.Min(fa, fb), Vector3.Min(ba, bb));
            Vector3 mx = Vector3.Max(Vector3.Max(fa, fb), Vector3.Max(ba, bb));
            B("Roof").Top(mn.x, mn.z, mx.x, mx.z, y0 + H, 0.125f);
            B("Roof").Box(new Vector3(mn.x - 0.35f, y0 + H - 0.5f, mn.z - 0.35f), new Vector3(mx.x + 0.35f, y0 + H + 0.25f, mx.z + 0.35f), 0.25f);
            // shop cornice ledge
            Vector3 outward = -inward;
            Vector3 la = fa + outward * 0.35f, lb = fb + outward * 0.35f;
            Vector3 lmn = Vector3.Min(Vector3.Min(fa, fb), Vector3.Min(la, lb)), lmx = Vector3.Max(Vector3.Max(fa, fb), Vector3.Max(la, lb));
            B("Roof").Box(new Vector3(lmn.x, y0 + shopH - 0.1f, lmn.z), new Vector3(lmx.x, y0 + shopH + 0.25f, lmx.z), 0.25f);

            // awnings over some storefronts
            if (Chance(0.45f))
            {
                string aw = Awnings[rnd.Next(Awnings.Length)];
                float aw0 = R(0.5f, Mathf.Max(0.6f, w - 6.5f));
                float awLen = Mathf.Min(6f, w - aw0 - 0.3f);
                if (awLen > 2f)
                {
                    Vector3 dir = (fb - fa) / w;
                    Vector3 p = fa + dir * (aw0 + awLen * 0.5f) + outward * 0.7f;
                    float yaw = Quaternion.LookRotation(outward).eulerAngles.y;
                    var awb = B(aw);
                    int st = awb.Count;
                    awb.OrientedBox(new Vector3(p.x, y0 + 3.35f, p.z), new Vector3(awLen * 0.5f, 0.06f, 0.75f), yaw, 0.5f);
                    // tilt: lower the outer edge
                    for (int k = st; k < awb.Count; k++)
                    {
                        float d = Vector3.Dot(awb.V[k] - p, outward);
                        awb.V[k] += Vector3.down * (d + 0.75f) * 0.35f;
                    }
                }
            }

            // rooftop clutter
            float top = y0 + H + 0.25f;
            int ac = rnd.Next(0, 4);
            for (int k = 0; k < ac; k++)
            {
                Vector3 p = new Vector3(R(mn.x + 2, mx.x - 2), top, R(mn.z + 2, mx.z - 2));
                B("Metal").Box(p + new Vector3(-0.7f, 0, -0.5f), p + new Vector3(0.7f, 1.1f, 0.5f), 0.5f);
            }
            if (floors >= 4 && floors <= 12 && Chance(0.16f))
            {
                Vector3 p = new Vector3(R(mn.x + 4, mx.x - 4), top, R(mn.z + 4, mx.z - 4));
                for (int k = 0; k < 4; k++)
                {
                    Vector3 lp = p + new Vector3(k % 2 == 0 ? -1.4f : 1.4f, 0, k < 2 ? -1.4f : 1.4f);
                    B("Metal").Box(lp - new Vector3(0.12f, 0, 0.12f), lp + new Vector3(0.12f, 3.2f, 0.12f), 0.5f);
                }
                B("Wood").Cylinder(p + Vector3.up * 3.2f, 2.1f, 3.6f, 12, 0.5f);
                // conical roof
                var wb = B("Roof"); int sides = 12; Vector3 apex = p + Vector3.up * 7.6f;
                for (int k = 0; k < sides; k++)
                {
                    float a0 = k * Mathf.PI * 2 / sides, a1 = (k + 1) * Mathf.PI * 2 / sides;
                    Vector3 r0 = p + Vector3.up * 6.8f + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * 2.3f;
                    Vector3 r1 = p + Vector3.up * 6.8f + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * 2.3f;
                    Vector3 n = Vector3.Cross(apex - r0, r1 - r0).normalized;
                    wb.Quad(r0, apex, apex, r1, n, Vector2.zero, Vector2.up, Vector2.up, Vector2.right);
                }
            }
        }

        // ---------------- lamps, trees ----------------
        void StreetFurniture()
        {
            for (int i = 0; i < Lines; i++)
            {
                float c = LineCoord(i);
                for (int k = 0; k < Lines - 1; k++)
                {
                    float a = LineCoord(k) + RoadHalf, e = LineCoord(k + 1) - RoadHalf;
                    bool trees = Chance(0.6f);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float start = a + 10f + (side > 0 ? 15f : 0f);
                        for (float s = start; s < e - 8f; s += 30f)
                        {
                            // along z at x=c, and along x at z=c
                            Lamp(new Vector3(c + side * (RoadHalf + 0.6f), Curb, s), new Vector3(-side, 0, 0));
                            Lamp(new Vector3(s, Curb, c + side * (RoadHalf + 0.6f)), new Vector3(0, 0, -side));
                        }
                        if (trees)
                            for (float s = a + 18f + (side > 0 ? 7f : 0f); s < e - 10f; s += 15f)
                            {
                                if (Chance(0.25f)) continue;
                                Tree(new Vector3(c + side * (RoadHalf + 2.4f), Curb, s));
                                Tree(new Vector3(s, Curb, c + side * (RoadHalf + 2.4f)));
                            }
                    }
                }
            }
        }

        void Lamp(Vector3 basePos, Vector3 toRoad)
        {
            var pole = B("PoleMetal");
            pole.Cylinder(basePos, 0.11f, 7.6f, 8, 1f, false);
            pole.Cylinder(basePos, 0.18f, 0.5f, 8, 1f, true);
            Vector3 armEnd = basePos + Vector3.up * 7.5f + toRoad * 1.9f;
            Vector3 mid = (basePos + Vector3.up * 7.5f + armEnd) * 0.5f;
            Vector3 half = toRoad.x != 0 ? new Vector3(1.0f, 0.05f, 0.05f) : new Vector3(0.05f, 0.05f, 1.0f);
            pole.Box(mid - half, mid + half, 1f);
            Vector3 head = armEnd + Vector3.down * 0.08f;
            Vector3 hh = toRoad.x != 0 ? new Vector3(0.35f, 0.07f, 0.17f) : new Vector3(0.17f, 0.07f, 0.35f);
            pole.Box(head - hh + Vector3.up * 0.06f, head + hh + Vector3.up * 0.12f, 1f);
            B("LampHead").Box(head - hh, head + hh * 0.9f + Vector3.up * 0.05f, 1f);
            Vector3 lightPos = head + Vector3.down * 0.25f;
            LampHeads.Add(lightPos);

            // halo billboard
            var hb = B("Halo");
            int st = hb.Count;
            hb.Quad(lightPos, lightPos, lightPos, lightPos, Vector3.up, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
            float hs = 1.4f;
            hb.UV2[st] = new Vector2(-hs, -hs); hb.UV2[st + 1] = new Vector2(-hs, hs); hb.UV2[st + 2] = new Vector2(hs, hs); hb.UV2[st + 3] = new Vector2(hs, -hs);
            float tint = R(0.9f, 1.1f);
            for (int k = st; k < st + 4; k++) hb.C[k] = new Color(tint, 1f, 2f - tint);

            // light pools on the road and on the sidewalk
            var gg = B("GroundGlow");
            float r = 8.5f;
            Vector3 g = new Vector3(lightPos.x, 0.02f, lightPos.z);
            gg.Top(g.x - r, g.z - r, g.x + r, g.z + r, 0.02f, 0f);
            SetGlowUV(gg);
            gg.Top(g.x - r, g.z - r, g.x + r, g.z + r, Curb + 0.015f, 0f);
            SetGlowUV(gg);
        }

        static void SetGlowUV(MeshBuilder b)
        {
            int n = b.Count;
            b.UV[n - 4] = new Vector2(0, 0); b.UV[n - 3] = new Vector2(0, 1); b.UV[n - 2] = new Vector2(1, 1); b.UV[n - 1] = new Vector2(1, 0);
        }

        static readonly string[] Leaves = { "LeavesOrange", "LeavesOrange", "LeavesYellow", "LeavesGreen" };

        void Tree(Vector3 p)
        {
            float h = R(3.2f, 4.5f);
            B("Bark").Cylinder(p, 0.16f, h + 1.5f, 6, 1f, false);
            string lm = Leaves[rnd.Next(Leaves.Length)];
            int blobs = rnd.Next(6, 10);
            for (int k = 0; k < blobs; k++)
            {
                Vector3 c = p + new Vector3(R(-1.6f, 1.6f), h + R(0.6f, 3.6f), R(-1.6f, 1.6f));
                float r = R(0.8f, 1.35f);
                B(lm).Blob(c, new Vector3(r, r * R(0.8f, 1.1f), r), rnd.Next());
            }
        }

        // ---------------- far skyline ----------------
        void Skyline()
        {
            for (int k = 0; k < 90; k++)
            {
                float ang = R(0, Mathf.PI * 2), dist = R(720f, 1300f);
                Vector3 c = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * dist;
                float w = R(25, 60), d = R(25, 60), h = R(30, 140);
                string fac = Facades[rnd.Next(Facades.Length)];
                var f = B(fac);
                Vector2 uvMul = new Vector2(1f / 24f, 1f / 19.2f);
                Vector2 off = new Vector2(R(0, 1), R(0, 1));
                Vector3 a = c + new Vector3(-w / 2, 0, -d / 2), b = c + new Vector3(w / 2, 0, -d / 2);
                Vector3 e = c + new Vector3(w / 2, 0, d / 2), g = c + new Vector3(-w / 2, 0, d / 2);
                f.Wall(a, b, 0, h, uvMul, off); f.Wall(b, e, 0, h, uvMul, off); f.Wall(e, g, 0, h, uvMul, off); f.Wall(g, a, 0, h, uvMul, off);
                B("Roof").Top(c.x - w / 2, c.z - d / 2, c.x + w / 2, c.z + d / 2, h, 0.05f);
            }
        }
    }
}
