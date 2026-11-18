using System.Collections.Generic;
using UnityEngine;

namespace Legends
{
    public class Road
    {
        public string name;
        public List<Vector3> pts = new List<Vector3>();
        public List<float> cum = new List<float>();
        public float hw, walkL, walkR;
        public string mat;
        public bool lots, lampsBoth, trees, avenue, promenade;
        public readonly List<(float s, Junction j)> joints = new List<(float, Junction)>();
        public float Length => cum[cum.Count - 1];

        public void Finish()
        {
            cum.Clear(); cum.Add(0);
            for (int i = 1; i < pts.Count; i++) cum.Add(cum[i - 1] + Vector3.Distance(pts[i - 1], pts[i]));
        }

        public void Sample(float s, out Vector3 p, out Vector3 t)
        {
            s = Mathf.Clamp(s, 0, Length);
            int lo = 0, hi = cum.Count - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (cum[mid] <= s) lo = mid; else hi = mid; }
            float seg = Mathf.Max(1e-4f, cum[hi] - cum[lo]);
            p = Vector3.Lerp(pts[lo], pts[hi], (s - cum[lo]) / seg);
            t = (pts[hi] - pts[lo]).normalized;
            // smooth tangent using neighbours
            if (lo > 0 && hi < pts.Count - 1) t = ((pts[hi] - pts[lo]).normalized * 2f + (pts[hi + 1] - pts[lo - 1]).normalized).normalized;
        }

        public static Vector3 Right(Vector3 t) => new Vector3(t.z, 0, -t.x);
    }

    public class Junction
    {
        public Vector3 p;
        public float r;
        public readonly List<Road> roads = new List<Road>();
        public readonly List<Arm> arms = new List<Arm>();
        public class Arm { public Vector3 d, roadL, roadR, walkInL, walkInR, walkOutL, walkOutR; public bool hasWalkL, hasWalkR; public float angle; }
    }

    public partial class WorldGenerator
    {
        Occupancy occ;
        readonly List<Road> roads = new List<Road>();
        readonly List<Junction> junctions = new List<Junction>();
        List<Vector3> ring;

        // ---------- river shape ----------
        public static float RiverZ(float x) => -560f + 70f * Mathf.Sin(x / 260f) + 30f * Mathf.Sin(x / 97f + 1f);
        static float PromN(float x) => RiverZ(x) + 62f;
        static float PromS(float x) => RiverZ(x) - 62f;

        // ---------- polyline helpers ----------
        static List<Vector3> CatmullRom(List<Vector2> ctrl, bool closed, float step)
        {
            var outPts = new List<Vector3>();
            int n = ctrl.Count;
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                Vector2 p0 = ctrl[closed ? (i - 1 + n) % n : Mathf.Max(i - 1, 0)];
                Vector2 p1 = ctrl[i];
                Vector2 p2 = ctrl[(i + 1) % n];
                Vector2 p3 = ctrl[closed ? (i + 2) % n : Mathf.Min(i + 2, n - 1)];
                int k = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / step));
                for (int j = 0; j < k; j++)
                {
                    float t = j / (float)k, t2 = t * t, t3 = t2 * t;
                    Vector2 q = 0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3);
                    outPts.Add(new Vector3(q.x, 0, q.y));
                }
            }
            if (closed) outPts.Add(outPts[0]);
            else outPts.Add(new Vector3(ctrl[n - 1].x, 0, ctrl[n - 1].y));
            return outPts;
        }

        static List<Vector3> Func(float a, float b, float step, System.Func<float, Vector2> f)
        {
            var l = new List<Vector3>();
            int n = Mathf.Max(2, Mathf.CeilToInt(Mathf.Abs(b - a) / step));
            for (int i = 0; i <= n; i++) { var v = f(Mathf.Lerp(a, b, i / (float)n)); l.Add(new Vector3(v.x, 0, v.y)); }
            return l;
        }

        Road Add(string name, List<Vector3> pts, bool avenue, bool lots)
        {
            var r = new Road { name = name, pts = pts, avenue = avenue, lots = lots };
            if (avenue) { r.hw = 7f; r.walkL = r.walkR = 4f; r.mat = "Road"; r.lampsBoth = true; r.trees = true; }
            else { r.hw = 4.5f; r.walkL = r.walkR = 2.5f; r.mat = "Road2"; r.lampsBoth = false; r.trees = true; }
            r.Finish();
            roads.Add(r);
            return r;
        }

        // Crossing of a vertical (x = c) or horizontal (z = c) line with the ring polyline.
        List<float> RingCross(bool vertical, float c)
        {
            var res = new List<float>();
            for (int i = 0; i < ring.Count - 1; i++)
            {
                Vector3 a = ring[i], b = ring[i + 1];
                float ua = vertical ? a.x : a.z, ub = vertical ? b.x : b.z;
                if ((ua - c) * (ub - c) > 0 || Mathf.Approximately(ua, ub)) continue;
                float t = (c - ua) / (ub - ua);
                res.Add(vertical ? Mathf.Lerp(a.z, b.z, t) : Mathf.Lerp(a.x, b.x, t));
            }
            res.Sort();
            return res;
        }

        bool InsideRing(Vector3 p)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 2; i < ring.Count - 1; j = i++)
            {
                Vector3 a = ring[i], b = ring[j];
                if (((a.z > p.z) != (b.z > p.z)) && (p.x < (b.x - a.x) * (p.z - a.z) / (b.z - a.z) + a.x)) inside = !inside;
            }
            return inside;
        }

        // ---------- road layout ----------
        public static readonly float[] GridX = { -264f, -132f, 0f, 132f, 264f };
        public static readonly float[] GridZ = { -60f, 72f, 204f, 336f, 468f };

        void DefineRoads()
        {
            var ringCtrl = new List<Vector2> {
                new Vector2(-470, -225), new Vector2(0, -255), new Vector2(470, -225), new Vector2(575, 150),
                new Vector2(505, 615), new Vector2(0, 700), new Vector2(-505, 615), new Vector2(-575, 150) };
            ring = CatmullRom(ringCtrl, true, 3f);
            var ringRoad = Add("Ring", new List<Vector3>(ring), true, true);

            const float over = 3f;
            // downtown grid, extended to the ring (x = 0 continues as the main N-S boulevard)
            foreach (float x in GridX)
            {
                var c = RingCross(true, x);
                float z0 = c[0] - over, z1 = c[c.Count - 1] + over;
                if (Mathf.Approximately(x, 0f)) { z0 = -Half - 3f; z1 = Half + 3f; }
                Add("GX" + x, Func(z0, z1, 3f, z => new Vector2(x, z)), true, true);
            }
            foreach (float z in GridZ)
            {
                var c = RingCross(false, z);
                Add("GZ" + z, Func(c[0] - over, c[c.Count - 1] + over, 3f, x => new Vector2(x, z)), true, true);
            }

            // promenades along both river banks
            var pn = Add("PromN", Func(-Half - 3, Half + 3, 3f, x => new Vector2(x, PromN(x))), false, true);
            pn.promenade = true; pn.walkR = 0f; // river side handled by the embankment walkway (road runs +x, right = -z)
            var ps = Add("PromS", Func(-Half - 3, Half + 3, 3f, x => new Vector2(x, PromS(x))), false, true);
            ps.promenade = true; ps.walkL = 0f; // left = +z = river side

            // west & east residential: N-S streets from the north promenade to the map edge
            foreach (float x0 in new[] { -690f, -820f, -945f, 690f, 820f, 945f })
            {
                float ph = x0 * 0.013f;
                System.Func<float, float> fx = z => x0 + 22f * Mathf.Sin(z / 95f + ph);
                float zs = PromN(fx(PromN(x0))) - over;
                Add("NS" + x0, Func(zs, Half + 3, 3f, z => new Vector2(fx(z), z)), false, true);
            }
            // E-W streets from the ring outwards
            foreach (float z0 in new[] { -200f, 60f, 330f, 590f })
            {
                var c = RingCross(false, z0);
                float ph = z0 * 0.01f;
                System.Func<float, float> fz = x => z0 + 18f * Mathf.Sin(x / 75f + ph) * Mathf.Clamp01((Mathf.Abs(x) - 580f) / 60f);
                Add("EWw" + z0, Func(c[0] + over * 0.5f, -Half - 3, 3f, x => new Vector2(x, fz(x))), false, true);
                Add("EWe" + z0, Func(c[c.Count - 1] - over * 0.5f, Half + 3, 3f, x => new Vector2(x, fz(x))), false, true);
            }
            // north district
            foreach (float z0 in new[] { 800f, 925f })
            {
                float ph = z0 * 0.02f;
                Add("N" + z0, Func(-Half - 3, Half + 3, 3f, x => new Vector2(x, z0 + 20f * Mathf.Sin(x / 110f + ph))), false, true);
            }
            foreach (float x0 in new[] { -330f, 330f })
            {
                var c = RingCross(true, x0);
                Add("NC" + x0, Func(c[c.Count - 1] - over * 0.5f, Half + 3, 3f, z => new Vector2(x0 + 15f * Mathf.Sin(z / 60f), z)), false, true);
            }
            // south district across the river
            foreach (float z0 in new[] { -775f, -905f })
            {
                float ph = z0 * 0.017f;
                Add("S" + z0, Func(-Half - 3, Half + 3, 3f, x => new Vector2(x, z0 + 22f * Mathf.Sin(x / 120f + ph))), false, true);
            }
            foreach (float x0 in new[] { -770f, -500f, -240f, 240f, 500f, 770f })
            {
                System.Func<float, float> fx = z => x0 + 18f * Mathf.Sin(z / 80f + x0);
                float zs = PromS(fx(PromS(x0))) + over;
                Add("SC" + x0, Func(zs, -Half - 3, 3f, z => new Vector2(fx(z), z)), false, true);
            }
            // extra bridges (2-lane) between the promenades
            foreach (float x0 in new[] { -600f, 580f })
            {
                Add("Br" + x0, Func(PromN(x0) + over, PromS(x0) - over, 3f, z => new Vector2(x0, z)), false, false);
            }
        }

        // ---------- junctions ----------
        void FindJunctions()
        {
            // spatial hash of segments
            const float cell = 24f;
            var hash = new Dictionary<long, List<(int r, int i)>>();
            long Key(int x, int z) => ((long)x << 32) ^ (uint)z;
            for (int ri = 0; ri < roads.Count; ri++)
            {
                var pts = roads[ri].pts;
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    Vector3 a = pts[i], b = pts[i + 1];
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / cell), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / cell);
                    int z0 = Mathf.FloorToInt(Mathf.Min(a.z, b.z) / cell), z1 = Mathf.FloorToInt(Mathf.Max(a.z, b.z) / cell);
                    for (int x = x0; x <= x1; x++)
                        for (int z = z0; z <= z1; z++)
                        {
                            long k = Key(x, z);
                            if (!hash.TryGetValue(k, out var l)) hash[k] = l = new List<(int, int)>();
                            l.Add((ri, i));
                        }
                }
            }
            var found = new List<(Vector3 p, int ra, float sa, int rb, float sb)>();
            var seen = new HashSet<long>();
            foreach (var l in hash.Values)
                for (int a = 0; a < l.Count; a++)
                    for (int b = a + 1; b < l.Count; b++)
                    {
                        var A = l[a]; var Bq = l[b];
                        if (A.r == Bq.r) continue;
                        long pairKey = ((long)(A.r * 100000 + A.i) << 32) ^ (uint)(Bq.r * 100000 + Bq.i);
                        if (seen.Contains(pairKey)) continue;
                        Road ra = roads[A.r], rb = roads[Bq.r];
                        if (SegX(ra.pts[A.i], ra.pts[A.i + 1], rb.pts[Bq.i], rb.pts[Bq.i + 1], out float ta, out float tb))
                        {
                            seen.Add(pairKey);
                            Vector3 p = Vector3.Lerp(ra.pts[A.i], ra.pts[A.i + 1], ta);
                            found.Add((p, A.r, Mathf.Lerp(ra.cum[A.i], ra.cum[A.i + 1], ta), Bq.r, Mathf.Lerp(rb.cum[Bq.i], rb.cum[Bq.i + 1], tb)));
                        }
                    }
            // merge nearby crossings into junctions
            foreach (var f in found)
            {
                Junction j = null;
                foreach (var e in junctions) if ((e.p - f.p).sqrMagnitude < 14f * 14f) { j = e; break; }
                if (j == null) { j = new Junction { p = f.p }; junctions.Add(j); }
                Attach(j, roads[f.ra], f.sa);
                Attach(j, roads[f.rb], f.sb);
            }
            foreach (var j in junctions)
            {
                float hw = 0; foreach (var r in j.roads) hw = Mathf.Max(hw, r.hw);
                j.r = hw + 3f;
            }
        }

        static void Attach(Junction j, Road r, float s)
        {
            foreach (var e in r.joints) if (e.j == j) return;
            r.joints.Add((s, j));
            j.roads.Add(r);
        }

        static bool SegX(Vector3 a, Vector3 b, Vector3 c, Vector3 d, out float t, out float u)
        {
            t = u = 0;
            float rx = b.x - a.x, rz = b.z - a.z, sx = d.x - c.x, sz = d.z - c.z;
            float den = rx * sz - rz * sx;
            if (Mathf.Abs(den) < 1e-6f) return false;
            float qx = c.x - a.x, qz = c.z - a.z;
            t = (qx * sz - qz * sx) / den;
            u = (qx * rz - qz * rx) / den;
            return t >= 0 && t <= 1 && u >= 0 && u <= 1;
        }

        // Intervals of a road that are not inside junctions
        List<(float a, float b, Junction ja, Junction jb)> Keep(Road r)
        {
            var cuts = new List<(float s0, float s1, Junction j)>();
            foreach (var (s, j) in r.joints) cuts.Add((s - j.r, s + j.r, j));
            cuts.Sort((x, y) => x.s0.CompareTo(y.s0));
            var res = new List<(float, float, Junction, Junction)>();
            float cur = 0; Junction prev = null;
            foreach (var c in cuts)
            {
                if (c.s0 > cur + 0.5f) res.Add((cur, c.s0, prev, c.j));
                if (c.s1 > cur) { cur = c.s1; prev = c.j; }
            }
            if (r.Length > cur + 0.5f) res.Add((cur, r.Length, prev, null));
            return res;
        }

        void MarkOccupancy()
        {
            foreach (var r in roads)
                for (int i = 0; i < r.pts.Count - 1; i++)
                {
                    Vector3 a = r.pts[i], b = r.pts[i + 1];
                    Vector3 t = (b - a).normalized, rt = Road.Right(t);
                    float wl = r.hw + Mathf.Max(r.walkL, 1f) + 0.5f, wr = r.hw + Mathf.Max(r.walkR, 1f) + 0.5f;
                    occ.MarkQuad(a - rt * wl - t, b - rt * wl + t, b + rt * wr + t, a + rt * wr - t, 1);
                }
            foreach (var j in junctions)
            {
                float rr = j.r + 6f;
                occ.MarkQuad(j.p + new Vector3(-rr, 0, -rr), j.p + new Vector3(-rr, 0, rr), j.p + new Vector3(rr, 0, rr), j.p + new Vector3(rr, 0, -rr), 1);
            }
            // river corridor incl. embankment walk
            for (float x = -1250; x < 1250; x += 4)
            {
                float z0 = RiverZ(x);
                for (float dz = -58f; dz <= 58f; dz += 4f) occ.Set(x, z0 + dz, 2);
            }
            // downtown blocks
            occ.MarkQuad(new Vector3(-270, 0, -66), new Vector3(-270, 0, 474), new Vector3(270, 0, 474), new Vector3(270, 0, -66), 5);
        }

        // ---------- road geometry ----------
        void BuildRoads()
        {
            foreach (var r in roads)
            {
                foreach (var (a, b, ja, jb) in Keep(r))
                {
                    Ribbon(r, a, b);
                    Sidewalks(r, a, b, ja, jb);
                    Furniture(r, a, b, ja != null, jb != null);
                    if (ja != null) { AddArm(ja, r, a, +1); if (ja.roads.Count >= 3 || r.avenue) Crosswalk(r, a + 2.2f); }
                    if (jb != null) { AddArm(jb, r, b, -1); if (jb.roads.Count >= 3 || r.avenue) Crosswalk(r, b - 2.2f); }
                }
            }
        }

        void Ribbon(Road r, float a, float b)
        {
            int n = Mathf.Max(1, Mathf.CeilToInt((b - a) / 3f));
            float texLen = r.avenue ? 32f : 24f;
            r.Sample(a, out var p0, out var t0);
            for (int i = 1; i <= n; i++)
            {
                float s = Mathf.Lerp(a, b, i / (float)n), sp = Mathf.Lerp(a, b, (i - 1) / (float)n);
                r.Sample(s, out var p1, out var t1);
                Vector3 r0 = Road.Right(t0) * r.hw, r1 = Road.Right(t1) * r.hw;
                At(p0);
                B(r.mat).Quad(p0 - r0, p1 - r1, p1 + r1, p0 + r0, Vector3.up,
                    new Vector2(0, sp / texLen), new Vector2(0, s / texLen), new Vector2(1, s / texLen), new Vector2(1, sp / texLen));
                // manholes & drains
                if (rnd.NextDouble() < 0.04)
                {
                    float off = (rnd.Next(2) == 0 ? -1 : 1) * (r.avenue ? R(1.5f, 5f) : R(0.8f, 2.5f));
                    Disc("Manhole", p0 + Road.Right(t0) * off + Vector3.up * 0.006f, 0.38f, 10);
                }
                if (rnd.NextDouble() < 0.07)
                {
                    float side = rnd.Next(2) == 0 ? -1 : 1;
                    Vector3 c = p0 + Road.Right(t0) * side * (r.hw - 0.35f) + Vector3.up * 0.006f;
                    FlatRect("Drain", c, t0, 0.45f, 0.25f);
                }
                p0 = p1; t0 = t1;
            }
        }

        void Disc(string mat, Vector3 c, float rad, int sides)
        {
            At(c);
            var b = B(mat);
            for (int k = 0; k < sides; k++)
            {
                float a0 = k * Mathf.PI * 2 / sides, a1 = (k + 1) * Mathf.PI * 2 / sides;
                Vector3 p0 = c + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * rad, p1 = c + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * rad;
                Vector2 u0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 0.5f + Vector2.one * 0.5f, u1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 0.5f + Vector2.one * 0.5f;
                b.Quad(c, p1, p1, p0, Vector3.up, new Vector2(0.5f, 0.5f), u1, u1, u0);
            }
        }

        void FlatRect(string mat, Vector3 c, Vector3 along, float halfAlong, float halfAcross)
        {
            At(c);
            Vector3 t = along * halfAlong, rr = Road.Right(along) * halfAcross;
            B(mat).Quad(c - t - rr, c + t - rr, c + t + rr, c - t + rr, Vector3.up, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
        }

        void Sidewalks(Road r, float a, float b, Junction ja, Junction jb)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float walk = side < 0 ? r.walkL : r.walkR;
                if (walk <= 0) continue;
                float sa = a + (ja != null ? walk + 1.5f : 0f), sb = b - (jb != null ? walk + 1.5f : 0f);
                if (sb - sa < 1f) continue;
                int n = Mathf.Max(1, Mathf.CeilToInt((sb - sa) / 3f));
                r.Sample(sa, out var p0, out var t0);
                for (int i = 1; i <= n; i++)
                {
                    float s = Mathf.Lerp(sa, sb, i / (float)n);
                    r.Sample(s, out var p1, out var t1);
                    Vector3 in0 = p0 + Road.Right(t0) * side * r.hw, in1 = p1 + Road.Right(t1) * side * r.hw;
                    Vector3 out0 = p0 + Road.Right(t0) * side * (r.hw + walk), out1 = p1 + Road.Right(t1) * side * (r.hw + walk);
                    At(p0);
                    var sw = B("Sidewalk");
                    Vector3 up = Vector3.up * Curb;
                    if (side > 0)
                    {
                        sw.Quad(in0 + up, in1 + up, out1 + up, out0 + up, Vector3.up, UVw(in0), UVw(in1), UVw(out1), UVw(out0));
                        sw.Quad(in1, in1 + up, in0 + up, in0, -Road.Right(t0), new Vector2(0, 0), new Vector2(0, 0.04f), new Vector2(1, 0.04f), new Vector2(1, 0));
                    }
                    else
                    {
                        sw.Quad(out0 + up, out1 + up, in1 + up, in0 + up, Vector3.up, UVw(out0), UVw(out1), UVw(in1), UVw(in0));
                        sw.Quad(in0, in0 + up, in1 + up, in1, Road.Right(t0), new Vector2(0, 0), new Vector2(0, 0.04f), new Vector2(1, 0.04f), new Vector2(1, 0));
                    }
                    p0 = p1; t0 = t1;
                }
            }
        }

        static Vector2 UVw(Vector3 p) => new Vector2(p.x, p.z) * 0.25f;

        void Crosswalk(Road r, float s)
        {
            if (s < 0 || s > r.Length) return;
            r.Sample(s, out var p, out var t);
            Vector3 rt = Road.Right(t);
            for (float o = -r.hw + 0.8f; o <= r.hw - 0.8f; o += 1.2f)
                FlatRect("Paint", p + rt * (o + 0.3f) + Vector3.up * 0.012f, t, 1.6f, 0.3f);
        }

        void AddArm(Junction j, Road r, float s, int dirSign)
        {
            r.Sample(s, out var p, out var t);
            Vector3 d = t * dirSign; // away from junction
            Vector3 left = new Vector3(-d.z, 0, d.x);
            float wLeftD = dirSign > 0 ? r.walkL : r.walkR;
            float wRightD = dirSign > 0 ? r.walkR : r.walkL;
            var arm = new Junction.Arm { d = d, angle = Mathf.Atan2(d.z, d.x) };
            arm.roadL = p + left * r.hw; arm.roadR = p - left * r.hw;
            float sw = 1.5f;
            arm.hasWalkL = wLeftD > 0; arm.hasWalkR = wRightD > 0;
            arm.walkInL = p + d * (wLeftD + sw) + left * r.hw; arm.walkOutL = p + d * (wLeftD + sw) + left * (r.hw + wLeftD);
            arm.walkInR = p + d * (wRightD + sw) - left * r.hw; arm.walkOutR = p + d * (wRightD + sw) - left * (r.hw + wRightD);
            j.arms.Add(arm);
        }

        void BuildJunctions()
        {
            foreach (var j in junctions)
            {
                if (j.arms.Count == 0) continue;
                At(j.p);
                // asphalt patch: convex hull of arm corners
                var pts = new List<Vector3>();
                foreach (var a in j.arms) { pts.Add(a.roadL); pts.Add(a.roadR); }
                var hull = Hull(pts);
                Vector3 c = Vector3.zero; foreach (var h in hull) c += h; c /= hull.Count;
                var b = B("Intersection");
                for (int i = 0; i < hull.Count; i++)
                {
                    Vector3 h0 = hull[i] + Vector3.up * 0.002f, h1 = hull[(i + 1) % hull.Count] + Vector3.up * 0.002f;
                    Vector3 cc = c + Vector3.up * 0.002f;
                    // hull is CCW seen from above (x right, z up) -> clockwise triangle is c, h1, h0
                    b.Quad(cc, h1, h1, h0, Vector3.up, UVi(cc), UVi(h1), UVi(h1), UVi(h0));
                }
                // sidewalk corner pads between neighbouring arms
                j.arms.Sort((x, y) => x.angle.CompareTo(y.angle));
                for (int i = 0; i < j.arms.Count; i++)
                {
                    var A = j.arms[i]; var Bn = j.arms[(i + 1) % j.arms.Count];
                    if (!A.hasWalkL || !Bn.hasWalkR) continue;
                    float ang = Mathf.DeltaAngle(A.angle * Mathf.Rad2Deg, Bn.angle * Mathf.Rad2Deg);
                    if (j.arms.Count > 1 && (ang <= 10f)) continue;
                    if (j.arms.Count == 1) continue;
                    var poly = new List<Vector3> { A.roadL, A.walkInL, A.walkOutL };
                    if (LineX(A.walkOutL, A.d, Bn.walkOutR, Bn.d, out Vector3 X) && (X - j.p).magnitude < j.r + 14f) poly.Add(X);
                    poly.Add(Bn.walkOutR); poly.Add(Bn.walkInR); poly.Add(Bn.roadR);
                    var sw = B("Sidewalk");
                    Vector3 up = Vector3.up * Curb;
                    // fan from first vertex; polygon goes CW or CCW depending on geometry -> fix per triangle
                    for (int k = 1; k < poly.Count - 1; k++)
                    {
                        Vector3 p0 = poly[0] + up, p1 = poly[k] + up, p2 = poly[k + 1] + up;
                        if (Vector3.Cross(p1 - p0, p2 - p0).y < 0) { var tmp = p1; p1 = p2; p2 = tmp; }
                        sw.Quad(p0, p1, p1, p2, Vector3.up, UVw(p0), UVw(p1), UVw(p1), UVw(p2));
                    }
                    // curb faces along the road-facing edges
                    CurbFace(sw, A.roadL, A.walkInL, j.p);
                    CurbFace(sw, Bn.walkInR, Bn.roadR, j.p);
                    CurbFace(sw, Bn.roadR, A.roadL, j.p);
                }
            }
        }

        // vertical curb face from a to b, facing toward 'toward'
        void CurbFace(MeshBuilder sw, Vector3 a, Vector3 b, Vector3 toward)
        {
            Vector3 n = Vector3.Cross(Vector3.up, (b - a).normalized);
            Vector3 mid = (a + b) * 0.5f;
            if (Vector3.Dot(toward - mid, n) < 0) { var t = a; a = b; b = t; n = -n; }
            Vector3 up = Vector3.up * Curb;
            sw.Quad(a, a + up, b + up, b, n, new Vector2(0, 0), new Vector2(0, 0.04f), new Vector2(1, 0.04f), new Vector2(1, 0));
        }

        static Vector2 UVi(Vector3 p) => new Vector2(p.x, p.z) / 14f;

        static bool LineX(Vector3 a, Vector3 da, Vector3 b, Vector3 db, out Vector3 x)
        {
            x = Vector3.zero;
            float den = da.x * db.z - da.z * db.x;
            if (Mathf.Abs(den) < 0.05f) return false;
            float t = ((b.x - a.x) * db.z - (b.z - a.z) * db.x) / den;
            x = a + da * t; x.y = a.y;
            return true;
        }

        // Andrew's monotone chain; returns CCW hull (x right, z up)
        static List<Vector3> Hull(List<Vector3> p)
        {
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.z.CompareTo(b.z));
            var h = new List<Vector3>();
            float Cross(Vector3 o, Vector3 a, Vector3 b) => (a.x - o.x) * (b.z - o.z) - (a.z - o.z) * (b.x - o.x);
            for (int i = 0; i < p.Count; i++) { while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], p[i]) <= 0) h.RemoveAt(h.Count - 1); h.Add(p[i]); }
            int lower = h.Count + 1;
            for (int i = p.Count - 2; i >= 0; i--) { while (h.Count >= lower && Cross(h[h.Count - 2], h[h.Count - 1], p[i]) <= 0) h.RemoveAt(h.Count - 1); h.Add(p[i]); }
            h.RemoveAt(h.Count - 1);
            return h;
        }

        // ---------- lamps / trees along roads ----------
        void Furniture(Road r, float a, float b, bool ja, bool jb)
        {
            float margin = 6f;
            float s0 = a + (ja ? margin : 2f), s1 = b - (jb ? margin : 2f);
            if (s1 <= s0) return;
            float spacing = r.avenue ? 34f : 32f;
            int idx = 0;
            for (float s = s0 + spacing * 0.3f; s < s1; s += spacing, idx++)
            {
                r.Sample(s, out var p, out var t);
                Vector3 rt = Road.Right(t);
                bool overRiver = Mathf.Abs(p.z - RiverZ(p.x)) < RiverHalf + 2f;
                for (int side = -1; side <= 1; side += 2)
                {
                    if (!r.lampsBoth && (idx % 2 == 0 ? side < 0 : side > 0)) continue;
                    float walk = side < 0 ? r.walkL : r.walkR;
                    if (walk <= 0) continue;
                    Vector3 basePos = p + rt * side * (r.hw + 0.6f) + Vector3.up * Curb;
                    Lamp(basePos, -rt * side, r.avenue ? 7.5f : 6.2f);
                }
                if (r.trees && !overRiver)
                {
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (!Chance(r.avenue ? 0.55f : 0.45f)) continue;
                        float walk = side < 0 ? r.walkL : r.walkR;
                        if (walk <= 0) continue;
                        r.Sample(s + spacing * 0.5f, out var tp, out var tt);
                        if (s + spacing * 0.5f > s1) continue;
                        float off = r.avenue ? r.hw + 2.4f : r.hw + walk + 1.4f;
                        Vector3 pos = tp + Road.Right(tt) * side * off;
                        if (!r.avenue && occ.Get(pos.x, pos.z) > 1 && occ.Get(pos.x, pos.z) != 4) continue;
                        Tree(pos + Vector3.up * (r.avenue ? Curb : 0.05f));
                    }
                }
            }
        }
    }
}
