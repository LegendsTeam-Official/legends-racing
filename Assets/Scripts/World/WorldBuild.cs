using System.Collections.Generic;
using UnityEngine;

namespace Legends
{
    public partial class WorldGenerator
    {
        const float RiverExtent = 1600f;

        void BuildRiver() { }

        // vertical face from a to b between y0..y1, facing toward 'toward'
        void VFace(string mat, Vector3 a, Vector3 b, float y0, float y1, Vector3 toward, float uvScale)
        {
            a.y = 0; b.y = 0;
            Vector3 n = Vector3.Cross(Vector3.up, (b - a).normalized);
            Vector3 mid = (a + b) * 0.5f;
            if (Vector3.Dot(toward - mid, n) < 0) { var t = a; a = b; b = t; n = -n; }
            At(mid);
            float len = Vector3.Distance(a, b);
            float u0 = (a.x + a.z) * uvScale, u1 = u0 + len * uvScale;
            B(mat).Quad(new Vector3(a.x, y0, a.z), new Vector3(a.x, y1, a.z), new Vector3(b.x, y1, b.z), new Vector3(b.x, y0, b.z), n,
                new Vector2(u0, y0 * uvScale), new Vector2(u0, y1 * uvScale), new Vector2(u1, y1 * uvScale), new Vector2(u1, y0 * uvScale));
        }

        // horizontal strip between two z-offsets from the river centre, from x0 to x1
        void RiverStrip(string mat, float x0, float x1, float d0, float d1, float y, float uvScale)
        {
            float z00 = RiverZ(x0) + d0, z01 = RiverZ(x0) + d1, z10 = RiverZ(x1) + d0, z11 = RiverZ(x1) + d1;
            Vector3 p0 = new Vector3(x0, y, z00), p1 = new Vector3(x0, y, z01), p2 = new Vector3(x1, y, z11), p3 = new Vector3(x1, y, z10);
            At(p0);
            B(mat).Quad(p0, p1, p2, p3, Vector3.up, new Vector2(p0.x, p0.z) * uvScale, new Vector2(p1.x, p1.z) * uvScale, new Vector2(p2.x, p2.z) * uvScale, new Vector2(p3.x, p3.z) * uvScale);
        }

        void BuildRiverGeometry()
        {
            const float step = 6f;
            const float walkOuter = 57.5f; // promenade road edge
            int i = 0;
            for (float x = -RiverExtent; x < RiverExtent; x += step, i++)
            {
                float x1 = x + step;
                RiverStrip("Water", x, x1, -RiverHalf - 1f, RiverHalf + 1f, WaterY, 1f / 20f);
                // walkways on both banks
                RiverStrip("Sidewalk", x, x1, RiverHalf, walkOuter, Curb, 0.25f);
                RiverStrip("Sidewalk", x, x1, -walkOuter, -RiverHalf, Curb, 0.25f);
                Vector3 n0 = new Vector3(x, 0, RiverZ(x)), n1 = new Vector3(x1, 0, RiverZ(x1));
                // embankment walls facing the water
                VFace("Embankment", n0 + Vector3.forward * RiverHalf, n1 + Vector3.forward * RiverHalf, WaterY - 0.6f, Curb, n0, 0.25f);
                VFace("Embankment", n0 - Vector3.forward * RiverHalf, n1 - Vector3.forward * RiverHalf, WaterY - 0.6f, Curb, n0, 0.25f);
                // curbs toward the promenade roads
                VFace("Sidewalk", n0 + Vector3.forward * walkOuter, n1 + Vector3.forward * walkOuter, 0, Curb, n0 + Vector3.forward * 80f, 0.25f);
                VFace("Sidewalk", n0 - Vector3.forward * walkOuter, n1 - Vector3.forward * walkOuter, 0, Curb, n0 - Vector3.forward * 80f, 0.25f);
                if (Mathf.Abs(x) > Half + 20f) continue;
                // railings
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 a = n0 + Vector3.forward * side * (RiverHalf + 0.3f) + Vector3.up * Curb;
                    Vector3 b = n1 + Vector3.forward * side * (RiverHalf + 0.3f) + Vector3.up * Curb;
                    Vector3 d = (b - a); float len = d.magnitude; d /= len;
                    float yaw = Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg;
                    At(a);
                    blockers.Add(((a + b) * 0.5f + Vector3.up * 0.8f, new Vector3(len + 0.1f, 1.6f, 0.3f), yaw));
                    B("PoleMetal").OrientedBox((a + b) * 0.5f + Vector3.up * 1.05f, new Vector3(len * 0.5f + 0.02f, 0.035f, 0.035f), yaw, 1f);
                    B("PoleMetal").OrientedBox((a + b) * 0.5f + Vector3.up * 0.55f, new Vector3(len * 0.5f + 0.02f, 0.02f, 0.02f), yaw, 1f);
                    B("PoleMetal").Box(a + new Vector3(-0.04f, 0, -0.04f), a + new Vector3(0.04f, 1.08f, 0.04f), 1f);
                    // lamps & trees on the walkway
                    if (i % 5 == 0)
                        Lamp(n0 + Vector3.forward * side * (RiverHalf + 1.2f) + Vector3.up * Curb, -Vector3.forward * side, 5.2f);
                    if (i % 3 == 1 && Chance(0.8f))
                        Tree(n0 + Vector3.forward * side * (RiverHalf + 8f + R(-1.5f, 1.5f)) + Vector3.up * Curb);
                }
            }
        }

        void BuildBridges()
        {
            foreach (var r in roads)
            {
                if (r.promenade) continue;
                // find the stretch over the river
                float sa = -1, sb = -1;
                for (float s = 0; s < r.Length; s += 1f)
                {
                    r.Sample(s, out var p, out _);
                    bool over = Mathf.Abs(p.z - RiverZ(p.x)) < RiverHalf + 0.5f;
                    if (over && sa < 0) sa = s;
                    if (over) sb = s;
                }
                if (sa < 0) continue;
                float edgeL = r.hw + r.walkL, edgeR = r.hw + r.walkR;
                float a = sa - 1.5f, b = sb + 1.5f;
                int n = Mathf.CeilToInt((b - a) / 3f);
                r.Sample(a, out var p0, out var t0);
                for (int k = 1; k <= n; k++)
                {
                    float s = Mathf.Lerp(a, b, k / (float)n);
                    r.Sample(s, out var p1, out var t1);
                    Vector3 rt0 = Road.Right(t0), rt1 = Road.Right(t1);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float e = side < 0 ? edgeL : edgeR;
                        Vector3 q0 = p0 + rt0 * side * e, q1 = p1 + rt1 * side * e;
                        Vector3 mid = (q0 + q1) * 0.5f; Vector3 d = (q1 - q0); float len = d.magnitude; d /= len;
                        float yaw = Mathf.Atan2(-d.z, d.x) * Mathf.Rad2Deg;
                        At(mid);
                        // parapet
                        B("Bridge").OrientedBox(mid + Vector3.up * (Curb + 0.55f) - rt0 * side * 0.18f, new Vector3(len * 0.5f + 0.01f, 0.55f, 0.18f), yaw, 0.5f);
                        // fascia below the deck
                        VFace("Bridge", q0, q1, -1.4f, Curb, mid + rt0 * side * 10f, 0.5f);
                    }
                    // underside
                    Vector3 l0 = p0 - rt0 * edgeL, l1 = p1 - rt1 * edgeL, r0 = p0 + rt0 * edgeR, r1 = p1 + rt1 * edgeR;
                    At(p0);
                    Vector3 dn = Vector3.up * -1.4f;
                    B("Bridge").Quad(r0 + dn, r1 + dn, l1 + dn, l0 + dn, Vector3.down, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
                    p0 = p1; t0 = t1;
                }
                // piers
                float mid2 = (sa + sb) * 0.5f;
                foreach (float off in new[] { -16f, 16f })
                {
                    r.Sample(mid2 + off, out var pp, out var tt);
                    Vector3 rt = Road.Right(tt);
                    foreach (float o in new[] { -(r.hw - 1.5f), r.hw - 1.5f })
                    {
                        At(pp);
                        B("Bridge").Cylinder(pp + rt * o + Vector3.up * (WaterY - 0.6f), 1.2f, -1.4f - (WaterY - 0.6f), 12, 0.5f, false);
                    }
                }
            }
        }

        // ---------- park ----------
        float parkMid(float x, float zs) => (PromN(x) + 12f + zs) * 0.5f;

        void BuildPark()
        {
            const float cell = 8f;
            var southRing = new Dictionary<int, float>();
            float RingS(float x)
            {
                int k = Mathf.RoundToInt(x);
                if (!southRing.TryGetValue(k, out float v)) { var c = RingCross(true, x); v = c.Count > 0 ? c[0] : -250f; southRing[k] = v; }
                return v;
            }
            // lawn
            for (float x = -464; x < 464; x += cell)
                for (float z = -560; z < -200; z += cell)
                {
                    float cx = x + cell * 0.5f, cz = z + cell * 0.5f;
                    if (cz < PromN(cx) + 10f || cz > RingS(cx) - 13f) continue;
                    if (occ.Get(cx, cz) != 0) continue;
                    At(new Vector3(cx, 0, cz));
                    B("ParkLawn").Top(x, z, x + cell, z + cell, 0.02f, 1f / 6f);
                    for (float sx = x + 2; sx < x + cell; sx += 4) for (float sz = z + 2; sz < z + cell; sz += 4) occ.Set(sx, sz, 3);
                }
            // paths, fountains
            foreach (float fx in new[] { -235f, 235f })
            {
                float fz = parkMid(fx, RingS(fx));
                Vector3 f = new Vector3(fx, 0, fz);
                Disc("Path", f + Vector3.up * 0.03f, 14f, 24);
                At(f);
                B("Bridge").Cylinder(f, 6f, 0.7f, 24, 0.5f, true);
                Disc("Water", f + Vector3.up * 0.72f, 5.6f, 24);
                B("Bridge").Cylinder(f, 0.6f, 2.6f, 12, 0.5f, true);
                B("Bridge").Cylinder(f + Vector3.up * 2.6f, 1.6f, 0.25f, 16, 0.5f, true);
                MarkDisc(f, 15f, 7);
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.PI / 3f + 0.3f;
                    Lamp(f + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 13f + Vector3.up * 0.03f, -new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)), 4.4f);
                }
                // paths radiating from the fountain
                float side = Mathf.Sign(fx);
                ParkPath(Func(fx + side * 14f, side * 452f, 3f, x => new Vector2(x, parkMid(x, RingS(x)) + 18f * Mathf.Sin(x / 45f))));
                ParkPath(Func(fx - side * 14f, side * 14f, 3f, x => new Vector2(x, parkMid(x, RingS(x)) - 10f * Mathf.Sin(x / 30f))));
                ParkPath(Func(fz + 14f, RingS(fx) - 11f, 3f, z => new Vector2(fx + 12f * Mathf.Sin(z / 25f), z)));
                ParkPath(Func(fz - 14f, PromN(fx) + 8f, 3f, z => new Vector2(fx - 10f * Mathf.Sin(z / 22f), z)));
            }
            // trees
            for (int k = 0; k < 900; k++)
            {
                float x = R(-455, 455), z = R(-560, -210);
                if (occ.Get(x, z) != 3) continue;
                Tree(new Vector3(x, 0.02f, z));
                MarkDisc(new Vector3(x, 0, z), 4f, 6);
            }
        }

        void MarkDisc(Vector3 c, float rad, byte v)
        {
            for (float x = -rad; x <= rad; x += 2f) for (float z = -rad; z <= rad; z += 2f)
                    if (x * x + z * z <= rad * rad) occ.Set(c.x + x, c.z + z, v);
        }

        void ParkPath(List<Vector3> pts)
        {
            var r = new Road { pts = pts, hw = 1.6f };
            r.Finish();
            float L = r.Length;
            int n = Mathf.CeilToInt(L / 3f);
            r.Sample(0, out var p0, out var t0);
            for (int i = 1; i <= n; i++)
            {
                float s = L * i / n;
                r.Sample(s, out var p1, out var t1);
                Vector3 a0 = Road.Right(t0) * 1.6f, a1 = Road.Right(t1) * 1.6f, y = Vector3.up * 0.035f;
                At(p0);
                B("Path").Quad(p0 - a0 + y, p1 - a1 + y, p1 + a1 + y, p0 + a0 + y, Vector3.up, UVw(p0 - a0), UVw(p1 - a1), UVw(p1 + a1), UVw(p0 + a0));
                occ.Set(p0.x, p0.z, 7); occ.Set(p0.x + 2, p0.z, 7); occ.Set(p0.x - 2, p0.z, 7); occ.Set(p0.x, p0.z + 2, 7); occ.Set(p0.x, p0.z - 2, 7);
                if (i % 9 == 0) Lamp(p0 + Road.Right(t0) * ((i / 9) % 2 == 0 ? 2.3f : -2.3f), -Road.Right(t0) * ((i / 9) % 2 == 0 ? 1 : -1), 4.4f);
                p0 = p1; t0 = t1;
            }
        }

        // ---------- downtown ----------
        void BuildDowntown()
        {
            for (int i = 0; i < GridX.Length - 1; i++)
                for (int j = 0; j < GridZ.Length - 1; j++)
                {
                    float bx0 = GridX[i] + 11f, bx1 = GridX[i + 1] - 11f, bz0 = GridZ[j] + 11f, bz1 = GridZ[j + 1] - 11f;
                    Vector2 c = new Vector2((bx0 + bx1) * 0.5f, (bz0 + bz1) * 0.5f - 204f);
                    float downtown = 1f - Mathf.Clamp01((c.magnitude - 60f) / 330f);
                    const float maxDepth = 24f;
                    Row(new Vector3(bx0, 0, bz0), new Vector3(bx1, 0, bz0), Vector3.forward, maxDepth, downtown);
                    Row(new Vector3(bx1, 0, bz1), new Vector3(bx0, 0, bz1), Vector3.back, maxDepth, downtown);
                    Row(new Vector3(bx0, 0, bz1 - maxDepth), new Vector3(bx0, 0, bz0 + maxDepth), Vector3.right, maxDepth, downtown);
                    Row(new Vector3(bx1, 0, bz0 + maxDepth), new Vector3(bx1, 0, bz1 - maxDepth), Vector3.left, maxDepth, downtown);
                    At(new Vector3(c.x, 0, c.y + 204f));
                    B("Roof").Top(bx0 + 10, bz0 + 10, bx1 - 10, bz1 - 10, 0.05f, 0.125f);
                }
        }

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
                Building(a + dir * s, a + dir * (s + w), inward, depth, floors, 0f, true);
                s += w;
            }
        }

        // ---------- lots along roads ----------
        void BuildLots()
        {
            foreach (var r in roads)
            {
                if (!r.lots) continue;
                foreach (var (a, b, ja, jb) in Keep(r))
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float walk = side < 0 ? r.walkL : r.walkR;
                        if (walk <= 0) continue;
                        float off = r.hw + walk + 0.3f;
                        float s = a + (ja != null ? 6f : 1f);
                        float end = b - (jb != null ? 6f : 1f);
                        while (s < end - 10f)
                        {
                            r.Sample(s, out var p0, out var t0);
                            bool mid = r.avenue || InsideRing(p0);
                            float w = mid ? R(12f, 24f) : R(16f, 22f);
                            float depth = mid ? R(14f, 22f) : R(28f, 34f);
                            if (s + w > end) break;
                            r.Sample(s + w, out var p1, out var t1);
                            if (Vector3.Angle(t0, t1) > 14f) { s += 4f; continue; }
                            Vector3 inward = (Road.Right(t0) + Road.Right(t1)).normalized * side;
                            Vector3 q0 = p0 + Road.Right(t0) * side * off, q1 = p1 + Road.Right(t1) * side * off;
                            Vector3 fa = side > 0 ? q1 : q0, fb = side > 0 ? q0 : q1;
                            Vector3 ba = fa + inward * depth, bb = fb + inward * depth;
                            // shrink the footprint test slightly so neighbours can touch
                            Vector3 c = (fa + fb + ba + bb) * 0.25f;
                            Vector3 sh(Vector3 v) => c + (v - c) * 0.94f;
                            if (!occ.QuadFree(sh(fa), sh(fb), sh(bb), sh(ba))) { s += 4f; continue; }
                            occ.MarkQuad(fa, fb, bb, ba, 4);
                            At(c);
                            if (mid)
                            {
                                bool nearCore = InsideRing(p0);
                                int floors = nearCore ? Mathf.RoundToInt(R(2f, 7f)) : Mathf.RoundToInt(R(1f, 4f));
                                Building(fa, fb, inward, depth, floors, 0f, true);
                            }
                            else
                            {
                                HouseBuilder.Build(m => B(m), rnd, fa, fb, inward, depth, 0f, PorchLights);
                            }
                            s += w + R(0f, 1.5f);
                        }
                    }
                }
            }
            // porch light halos
            foreach (var p in PorchLights) Halo(p, 0.7f, new Color(1f, 0.85f, 0.6f));
        }

        // ---------- ground ----------
        void BuildGround()
        {
            const float cell = 25f;
            for (float x = -RiverExtent; x < RiverExtent; x += cell)
                for (float z = -RiverExtent; z < RiverExtent; z += cell)
                {
                    bool nearRiver = false;
                    for (int k = 0; k < 4 && !nearRiver; k++)
                    {
                        float cx = x + (k % 2) * cell, cz = z + (k / 2) * cell;
                        if (Mathf.Abs(cz - RiverZ(cx)) < RiverHalf + 3f) nearRiver = true;
                    }
                    // also test the middle of each edge (river can bulge into a cell)
                    for (int k = 0; k < 3 && !nearRiver; k++)
                    {
                        float cx = x + cell * (0.25f + 0.25f * k);
                        if (Mathf.Abs(z - RiverZ(cx)) < RiverHalf + 3f || Mathf.Abs(z + cell - RiverZ(cx)) < RiverHalf + 3f) nearRiver = true;
                        if (RiverZ(cx) > z && RiverZ(cx) < z + cell) nearRiver = true;
                    }
                    if (nearRiver) continue;
                    At(new Vector3(x + cell * 0.5f, 0, z + cell * 0.5f));
                    B("Ground").Top(x, z, x + cell, z + cell, -0.01f, 1f / 8f);
                }
        }

        // invisible static colliders for thin props (railings, poles, trunks)
        readonly List<(Vector3 c, Vector3 size, float yaw)> blockers = new List<(Vector3, Vector3, float)>();

        void BuildBlockers()
        {
            var root = new GameObject("Blockers");
            root.transform.SetParent(transform, false);
            root.isStatic = true;
            foreach (var (c, size, yaw) in blockers)
            {
                var go = new GameObject("b");
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(c, Quaternion.Euler(0, yaw, 0));
                go.AddComponent<BoxCollider>().size = size;
            }
            blockers.Clear();
        }

        // ---------- buildings (any orientation) ----------
        static readonly string[] Facades = { "FacadeConcrete", "FacadeBrick", "FacadeStone" };
        static readonly string[] Awnings = { "AwningRed", "AwningGreen", "AwningBlue" };

        void QuadUp(string mat, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uvScale)
        {
            // order so that it faces up
            if (Vector3.Cross(b - a, c - a).y < 0) { var t = b; b = d; d = t; }
            B(mat).Quad(a, b, c, d, Vector3.up, new Vector2(a.x, a.z) * uvScale, new Vector2(b.x, b.z) * uvScale, new Vector2(c.x, c.z) * uvScale, new Vector2(d.x, d.z) * uvScale);
        }

        void Building(Vector3 fa, Vector3 fb, Vector3 inward, float depth, int floors, float y0, bool shopFront)
        {
            const float shopH = 4.5f, floorH = 3.2f;
            float H = shopH + floors * floorH + 0.6f;
            string fac = Facades[rnd.Next(Facades.Length)];
            var f = B(fac);
            Vector2 uvMul = new Vector2(1f / 24f, 1f / 19.2f);
            Vector2 off = new Vector2(R(0, 24) / 24f, rnd.Next(6) * floorH / 19.2f - (y0 + shopH) / 19.2f);
            Vector3 ba = fa + inward * depth, bb = fb + inward * depth;
            float w = (fb - fa).magnitude;
            Vector3 dir = (fb - fa) / w;
            float yaw = Mathf.Atan2(-dir.z, dir.x) * Mathf.Rad2Deg;

            if (shopFront)
            {
                B("Shop").Wall(fa, fb, y0, y0 + shopH, new Vector2(1f / 24f, 1f / shopH), new Vector2(R(0, 24) / 24f, -y0 / shopH));
                f.Wall(fa, fb, y0 + shopH, y0 + H, uvMul, off);
            }
            else f.Wall(fa, fb, y0, y0 + H, uvMul, off);
            f.Wall(fb, bb, y0, y0 + H, uvMul, off + new Vector2(0.37f, 0));
            f.Wall(bb, ba, y0, y0 + H, uvMul, off + new Vector2(0.71f, 0));
            f.Wall(ba, fa, y0, y0 + H, uvMul, off + new Vector2(0.13f, 0));
            Vector3 top = Vector3.up * (y0 + H);
            QuadUp("Roof", fa + top, fb + top, bb + top, ba + top, 0.125f);
            Vector3 centre = (fa + fb + ba + bb) * 0.25f;
            B("Roof").OrientedBox(new Vector3(centre.x, y0 + H - 0.12f, centre.z), new Vector3(w * 0.5f + 0.35f, 0.38f, depth * 0.5f + 0.35f), yaw, 0.25f);
            Vector3 outward = -inward;
            if (shopFront)
            {
                Vector3 fm = (fa + fb) * 0.5f + outward * 0.17f;
                B("Roof").OrientedBox(new Vector3(fm.x, y0 + shopH + 0.07f, fm.z), new Vector3(w * 0.5f, 0.17f, 0.17f), yaw, 0.25f);
                if (Chance(0.45f))
                {
                    string aw = Awnings[rnd.Next(Awnings.Length)];
                    float aw0 = R(0.5f, Mathf.Max(0.6f, w - 6.5f));
                    float awLen = Mathf.Min(6f, w - aw0 - 0.3f);
                    if (awLen > 2f)
                    {
                        Vector3 p = fa + dir * (aw0 + awLen * 0.5f) + outward * 0.7f;
                        var awb = B(aw);
                        int st = awb.Count;
                        awb.OrientedBox(new Vector3(p.x, y0 + 3.35f, p.z), new Vector3(awLen * 0.5f, 0.06f, 0.75f), yaw, 0.5f);
                        for (int k = st; k < awb.Count; k++)
                        {
                            float d = Vector3.Dot(awb.V[k] - p, outward);
                            awb.V[k] += Vector3.down * (d + 0.75f) * 0.35f;
                        }
                    }
                }
            }
            float topY = y0 + H + 0.26f;
            int ac = rnd.Next(0, 4);
            for (int k = 0; k < ac; k++)
            {
                Vector3 p = fa + dir * R(2f, Mathf.Max(2.1f, w - 2f)) + inward * R(2f, Mathf.Max(2.1f, depth - 2f));
                B("Metal").OrientedBox(new Vector3(p.x, topY + 0.55f, p.z), new Vector3(0.7f, 0.55f, 0.5f), yaw, 0.5f);
            }
            if (floors >= 4 && floors <= 12 && w > 10f && depth > 10f && Chance(0.16f))
            {
                Vector3 p = fa + dir * R(4f, w - 4f) + inward * R(4f, depth - 4f); p.y = topY;
                for (int k = 0; k < 4; k++)
                {
                    Vector3 lp = p + dir * (k % 2 == 0 ? -1.4f : 1.4f) + inward * (k < 2 ? -1.4f : 1.4f);
                    B("Metal").Box(lp - new Vector3(0.12f, 0, 0.12f), lp + new Vector3(0.12f, 3.2f, 0.12f), 0.5f);
                }
                B("Wood").Cylinder(p + Vector3.up * 3.2f, 2.1f, 3.6f, 12, 0.5f);
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

        // ---------- lamps, trees, halos ----------
        void Lamp(Vector3 basePos, Vector3 toRoad, float height)
        {
            At(basePos);
            var pole = B("PoleMetal");
            float armLen = height > 6f ? 1.9f : 1.0f;
            pole.Cylinder(basePos, height > 6f ? 0.11f : 0.08f, height + 0.1f, 8, 1f, false);
            pole.Cylinder(basePos, 0.18f, 0.5f, 8, 1f, true);
            blockers.Add((basePos + Vector3.up * 1.5f, new Vector3(0.3f, 3f, 0.3f), 0f));
            Vector3 top = basePos + Vector3.up * height;
            Vector3 armEnd = top + toRoad * armLen;
            float yaw = Mathf.Atan2(-toRoad.z, toRoad.x) * Mathf.Rad2Deg;
            pole.OrientedBox((top + armEnd) * 0.5f, new Vector3(armLen * 0.5f, 0.05f, 0.05f), yaw, 1f);
            Vector3 head = armEnd + Vector3.down * 0.08f;
            pole.OrientedBox(head + Vector3.up * 0.09f, new Vector3(0.36f, 0.04f, 0.18f), yaw, 1f);
            B("LampHead").OrientedBox(head, new Vector3(0.33f, 0.06f, 0.16f), yaw, 1f);
            Vector3 lightPos = head + Vector3.down * 0.25f;
            LampHeads.Add(lightPos);
            Halo(lightPos, height > 6f ? 1.4f : 1.0f, new Color(R(0.9f, 1.1f), 1f, 1f));
            var gg = B("GroundGlow");
            float r = height > 6f ? 8.5f : 6f;
            Vector3 g = new Vector3(lightPos.x, 0, lightPos.z);
            gg.Top(g.x - r, g.z - r, g.x + r, g.z + r, 0.02f, 0f); SetGlowUV(gg);
            gg.Top(g.x - r, g.z - r, g.x + r, g.z + r, Curb + 0.015f, 0f); SetGlowUV(gg);
        }

        void Halo(Vector3 pos, float hs, Color tint)
        {
            At(pos);
            var hb = B("Halo");
            int st = hb.Count;
            hb.Quad(pos, pos, pos, pos, Vector3.up, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
            hb.UV2[st] = new Vector2(-hs, -hs); hb.UV2[st + 1] = new Vector2(-hs, hs); hb.UV2[st + 2] = new Vector2(hs, hs); hb.UV2[st + 3] = new Vector2(hs, -hs);
            for (int k = st; k < st + 4; k++) hb.C[k] = tint;
        }

        static void SetGlowUV(MeshBuilder b)
        {
            int n = b.Count;
            b.UV[n - 4] = new Vector2(0, 0); b.UV[n - 3] = new Vector2(0, 1); b.UV[n - 2] = new Vector2(1, 1); b.UV[n - 1] = new Vector2(1, 0);
        }

        static readonly string[] Leaves = { "LeavesOrange", "LeavesOrange", "LeavesYellow", "LeavesGreen" };

        void Tree(Vector3 p)
        {
            At(p);
            blockers.Add((p + Vector3.up * 1.5f, new Vector3(0.35f, 3f, 0.35f), 0f));
            float h = R(3.2f, 4.8f);
            B("Bark").Cylinder(p, 0.16f, h + 1.5f, 6, 1f, false);
            string lm = Leaves[rnd.Next(Leaves.Length)];
            int blobs = rnd.Next(6, 10);
            float spread = R(1.2f, 1.8f);
            for (int k = 0; k < blobs; k++)
            {
                Vector3 c = p + new Vector3(R(-spread, spread), h + R(0.6f, 3.6f), R(-spread, spread));
                float r = R(0.8f, 1.35f);
                B(lm).Blob(c, new Vector3(r, r * R(0.8f, 1.1f), r), rnd.Next());
            }
        }

        void Skyline()
        {
            for (int k = 0; k < 70; k++)
            {
                float ang = R(0, Mathf.PI * 2), dist = R(1650f, 2300f);
                Vector3 c = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * dist;
                float w = R(25, 60), d = R(25, 60), h = R(30, 140);
                At(c);
                var f = B(Facades[rnd.Next(Facades.Length)]);
                Vector2 uvMul = new Vector2(1f / 24f, 1f / 19.2f), off = new Vector2(R(0, 1), R(0, 1));
                Vector3 a = c + new Vector3(-w / 2, 0, -d / 2), b = c + new Vector3(w / 2, 0, -d / 2), e = c + new Vector3(w / 2, 0, d / 2), g = c + new Vector3(-w / 2, 0, d / 2);
                f.Wall(a, b, 0, h, uvMul, off); f.Wall(b, e, 0, h, uvMul, off); f.Wall(e, g, 0, h, uvMul, off); f.Wall(g, a, 0, h, uvMul, off);
                B("Roof").Top(c.x - w / 2, c.z - d / 2, c.x + w / 2, c.z + d / 2, h, 0.05f);
            }
        }
    }

    // Slowly scrolls the water ripples.
    public class WaterAnimator : MonoBehaviour
    {
        Material water;
        void Start() { water = Resources.Load<Material>("Materials/Water"); }
        void Update()
        {
            if (water == null) return;
            water.SetTextureOffset("_BaseMap", new Vector2(Time.time * 0.004f, Time.time * 0.011f));
        }
    }
}
