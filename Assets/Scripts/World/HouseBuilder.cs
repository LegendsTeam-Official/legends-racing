using System.Collections.Generic;
using UnityEngine;

namespace Legends
{
    // American suburban detached house on one lot: lawn, driveway, walkway, 1-2.5 storey body with
    // gable / hip / front-gable roof, porch with lantern(s), optional attached garage, windows & doors as
    // atlas quads, chimney, fence or hedge along the front line, shrubs, mailbox.
    // Everything is emitted in a local lot frame (x along the street, z into the lot, y up from groundY)
    // and transformed to world space at the end, so lots can have any yaw.
    public static class HouseBuilder
    {
        // ---------- texture layout shared with Editor/HouseAssets ----------
        public const int SidingColumns = 8;          // 7 siding colours + 1 brick column (atlas columns)
        public const int BrickColumn = 7;
        public const float SidingSpan = 10.24f;      // metres covered by the siding texture vertically (v = y / span)
        public const float SidingFoundation = 0.46f; // concrete band + water table at the bottom of the siding
        public const float RoofTile = 4f, LawnTile = 4f, DriveTile = 4f, HedgeTile = 2f, FenceTile = 3.6f;

        public const int AtlasSize = 1024;           // openings atlas (windows, doors, garage doors)
        public const float AtlasPxPerMetre = 100f;
        public enum Opening { Window, Picture, Door, GarageSingle, GarageDouble }
        public const int WindowVariants = 16, PictureVariants = 4, DoorVariants = 4, GarageVariants = 2;

        // Window variants. State: 0 off, 1 warm, 2 TV, 3 cool white, 4 dim lamp.
        // Cover: 0 none, 1 side curtains, 2 blinds half down, 3 blinds closed, 4 curtains drawn.
        public static readonly byte[] WindowState = { 0, 0, 0, 0, 1, 1, 1, 1, 2, 4, 3, 1, 0, 2, 1, 0 };
        public static readonly byte[] WindowCover = { 0, 1, 3, 0, 1, 0, 2, 3, 0, 1, 0, 4, 1, 2, 0, 2 };
        public static readonly bool[] WindowGrille = { false, false, false, true, true, false, false, false, false, false, false, false, true, false, true, false };
        // Picture window variants: 0 off, 1 warm + curtains, 2 TV, 3 warm sheers.

        // Pixel rect of an opening variant in the atlas (origin bottom-left)
        public static RectInt OpeningRect(Opening kind, int v)
        {
            switch (kind)
            {
                case Opening.GarageDouble: return new RectInt(v * 512, 0, 512, 224);          // 5.12 x 2.24 m
                case Opening.Door: return new RectInt(v * 128, 224, 128, 256);                // 1.28 x 2.56 m
                case Opening.GarageSingle: return new RectInt(512 + v * 256, 224, 256, 256);  // 2.56 x 2.56 m
                case Opening.Picture: return new RectInt(v * 256, 480, 256, 192);             // 2.56 x 1.92 m
                default: return new RectInt((v % 8) * 128, 672 + (v / 8) * 176, 128, 176);   // 1.28 x 1.76 m
            }
        }

        const int RoofSideGable = 0, RoofFrontGable = 1, RoofHip = 2;
        const float Fascia = 0.18f;

        static readonly int[] LitWindows, DarkWindows;
        static HouseBuilder()
        {
            var lit = new List<int>(); var dark = new List<int>();
            for (int i = 0; i < WindowVariants; i++) (WindowState[i] > 0 ? lit : dark).Add(i);
            LitWindows = lit.ToArray(); DarkWindows = dark.ToArray();
        }

        // frontLeft/frontRight: street-side lot corners (left->right seen from the street); inward: unit vector into the lot.
        public static void Build(System.Func<string, MeshBuilder> B, System.Random rnd, Vector3 frontLeft, Vector3 frontRight,
                                 Vector3 inward, float lotDepth, float groundY, List<Vector3> porchLights)
        {
            Vector3 along = frontRight - frontLeft; along.y = 0f;
            float W = along.magnitude;
            if (W < 8f || lotDepth < 14f) return;
            Vector3 right = along / W;
            Vector3 fwd = Vector3.Cross(right, Vector3.up);
            Vector3 origin = frontLeft;
            if (Vector3.Dot(fwd, inward) < 0f) { right = -right; fwd = -fwd; origin = frontRight; }
            origin.y = groundY;

            var c = new Ctx(B, rnd);
            c.Lot(W, lotDepth);
            c.ToWorld(origin, right, fwd);
            if (porchLights != null)
                foreach (var p in c.lights) porchLights.Add(origin + right * p.x + Vector3.up * p.y + fwd * p.z);
        }

        static Vector3 P(float x, float y, float z) => new Vector3(x, y, z);
        static Vector3 G(float x, float z) => new Vector3(x, 0f, z);
        static Vector2 UV(float u, float v) => new Vector2(u, v);
        static readonly Vector2 U00 = new Vector2(0, 0), U01 = new Vector2(0, 1), U11 = new Vector2(1, 1), U10 = new Vector2(1, 0);

        sealed class Ctx
        {
            readonly System.Func<string, MeshBuilder> get;
            readonly System.Random rnd;
            readonly List<MeshBuilder> used = new List<MeshBuilder>(16);
            readonly List<int> first = new List<int>(16);
            public readonly List<Vector3> lights = new List<Vector3>(3);
            float lit;

            public Ctx(System.Func<string, MeshBuilder> get, System.Random rnd) { this.get = get; this.rnd = rnd; }

            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            bool Chance(float p) => rnd.NextDouble() < p;

            MeshBuilder M(string mat)
            {
                var b = get(mat);
                if (used.IndexOf(b) < 0) { used.Add(b); first.Add(b.Count); }
                return b;
            }

            int[] Mark()
            {
                var m = new int[used.Count];
                for (int i = 0; i < m.Length; i++) m[i] = used[i].Count;
                return m;
            }

            // rotate everything emitted since mark by +90 deg yaw about the local origin: (x,y,z) -> (z,y,-x)
            void Rot90Since(int[] mark)
            {
                for (int i = 0; i < used.Count; i++)
                {
                    var b = used[i];
                    for (int k = i < mark.Length ? mark[i] : first[i]; k < b.Count; k++)
                    {
                        Vector3 v = b.V[k], n = b.N[k];
                        b.V[k] = new Vector3(v.z, v.y, -v.x);
                        b.N[k] = new Vector3(n.z, n.y, -n.x);
                    }
                }
            }

            public void ToWorld(Vector3 o, Vector3 r, Vector3 f)
            {
                for (int i = 0; i < used.Count; i++)
                {
                    var b = used[i];
                    for (int k = first[i]; k < b.Count; k++)
                    {
                        Vector3 v = b.V[k], n = b.N[k];
                        b.V[k] = o + r * v.x + Vector3.up * v.y + f * v.z;
                        b.N[k] = r * n.x + Vector3.up * n.y + f * n.z;
                    }
                }
            }

            // ---------------- primitives (winding fixed from the wanted facing direction) ----------------
            static void Quad(MeshBuilder b, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 want,
                             Vector2 u0, Vector2 u1, Vector2 u2, Vector2 u3)
            {
                Vector3 n = Vector3.Cross(p1 - p0, p3 - p0);
                if (n.sqrMagnitude < 1e-12f) n = Vector3.Cross(p2 - p0, p3 - p0);
                if (n.sqrMagnitude < 1e-12f) n = Vector3.Cross(p1 - p0, p2 - p0);
                if (Vector3.Dot(n, want) < 0f) b.Quad(p0, p3, p2, p1, (-n).normalized, u0, u3, u2, u1);
                else b.Quad(p0, p1, p2, p3, n.normalized, u0, u1, u2, u3);
            }

            static void Quad(MeshBuilder b, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 want)
                => Quad(b, p0, p1, p2, p3, want, U00, U01, U11, U10);

            static void Tri(MeshBuilder b, Vector3 a, Vector3 c, Vector3 d, Vector3 want, Vector2 ua, Vector2 uc, Vector2 ud)
            {
                Vector3 n = Vector3.Cross(c - a, d - a);
                if (Vector3.Dot(n, want) < 0f) { var t = c; c = d; d = t; var tu = uc; uc = ud; ud = tu; n = -n; }
                n.Normalize();
                int i = b.V.Count;
                b.V.Add(a); b.V.Add(c); b.V.Add(d);
                b.N.Add(n); b.N.Add(n); b.N.Add(n);
                b.UV.Add(ua); b.UV.Add(uc); b.UV.Add(ud);
                if (b.useUV2) { b.UV2.Add(Vector2.zero); b.UV2.Add(Vector2.zero); b.UV2.Add(Vector2.zero); }
                if (b.useColor) { b.C.Add(Color.white); b.C.Add(Color.white); b.C.Add(Color.white); }
                b.T.Add(i); b.T.Add(i + 1); b.T.Add(i + 2);
            }

            // siding wall a->b (left->right seen from outside), u squeezed into the colour column of the atlas
            void Siding(Vector3 a, Vector3 b, float y0, float y1, int col)
            {
                float len = Vector3.Distance(a, b);
                if (len < 0.01f) return;
                float u0 = (col + 0.2f + R(0f, 0.08f)) / SidingColumns;
                M("HouseSiding").Wall(a, b, y0, y1, new Vector2(0.5f / SidingColumns / len, 1f / SidingSpan), new Vector2(u0, 0f));
            }

            // three visible faces of a post (front -z, right +x, left -x)
            static void Post(MeshBuilder b, float x0, float z0, float x1, float z1, float y0, float y1)
            {
                var m = Vector2.one;
                b.Wall(G(x0, z0), G(x1, z0), y0, y1, m, Vector2.zero);
                b.Wall(G(x1, z0), G(x1, z1), y0, y1, m, Vector2.zero);
                b.Wall(G(x0, z1), G(x0, z0), y0, y1, m, Vector2.zero);
            }

            // concrete slab: top, front and both sides (back is against the house)
            void Slab(float x0, float x1, float za, float zb, float h)
            {
                var cb = M("HouseDriveway");
                float k = 1f / DriveTile; var mul = new Vector2(k, k);
                cb.Top(x0, za, x1, zb, h, k);
                cb.Wall(G(x0, za), G(x1, za), 0f, h, mul, Vector2.zero, x0);
                cb.Wall(G(x1, za), G(x1, zb), 0f, h, mul, Vector2.zero, za);
                cb.Wall(G(x0, zb), G(x0, za), 0f, h, mul, Vector2.zero, za);
            }

            // window / door / garage door as a thin quad in front of a wall; a = wall start at y=0, dir = wall direction
            void AddOpening(Vector3 a, Vector3 dir, float s, float y, Opening kind, int v, float scale)
            {
                RectInt r = OpeningRect(kind, v);
                float w = r.width / AtlasPxPerMetre * scale, h = r.height / AtlasPxPerMetre * scale;
                Vector3 n = Vector3.Cross(Vector3.up, dir);
                Vector3 p0 = a + dir * (s - w * 0.5f) + n * 0.035f + Vector3.up * y;
                Vector3 up = Vector3.up * h, rt = dir * w;
                float u0 = (r.x + 0.5f) / AtlasSize, u1 = (r.xMax - 0.5f) / AtlasSize;
                float v0 = (r.y + 0.5f) / AtlasSize, v1 = (r.yMax - 0.5f) / AtlasSize;
                Quad(M("HouseOpenings"), p0, p0 + up, p0 + up + rt, p0 + rt, n, UV(u0, v0), UV(u0, v1), UV(u1, v1), UV(u1, v0));
            }

            int PickWindow(float litMul) => Chance(lit * litMul) ? LitWindows[rnd.Next(LitWindows.Length)] : DarkWindows[rnd.Next(DarkWindows.Length)];
            int PickPicture(float litMul) => Chance(lit * litMul) ? 1 + rnd.Next(PictureVariants - 1) : 0;

            // evenly spaced windows on the wall interval [s0, s1]
            void FillWindows(Vector3 a, Vector3 dir, float s0, float s1, float y, bool picture, int maxN, float litMul)
            {
                float L = s1 - s0;
                if (L < 1.45f) return;
                if (picture && L >= 3.0f && Chance(0.45f))
                {
                    AddOpening(a, dir, (s0 + s1) * 0.5f, y - 0.1f, Opening.Picture, PickPicture(litMul), 1f);
                    return;
                }
                int n = Mathf.Clamp(Mathf.FloorToInt((L + 0.8f) / 2.4f), 1, maxN);
                for (int i = 0; i < n; i++)
                    AddOpening(a, dir, s0 + L * (i + 0.5f) / n, y, Opening.Window, PickWindow(litMul), 1f);
            }

            void Lantern(float x, float y, float zWall)
            {
                Post(M("HousePorchLight"), x - 0.1f, zWall - 0.17f, x + 0.1f, zWall - 0.005f, y - 0.16f, y + 0.16f);
                lights.Add(P(x, y, zWall - 0.45f));
            }

            // ---------------- the lot ----------------
            public void Lot(float W, float D)
            {
                // type
                double r = rnd.NextDouble();
                int style = r < 0.34 ? 0 : (r < 0.72 ? 1 : 2);               // 0 ranch, 1 two-storey, 2 front-gable (1.5 / 2.5)
                int floors = style == 0 ? 1 : style == 1 ? 2 : (Chance(0.5f) ? 1 : 2);
                float wallH = floors == 2 ? 6.0f : (style == 2 ? 3.6f : 3.2f);
                int roof = style == 2 ? RoofFrontGable : (Chance(style == 0 ? 0.3f : 0.35f) ? RoofHip : RoofSideGable);
                float pitch = style == 0 ? R(20f, 27f) : style == 1 ? R(24f, 33f) : (floors == 1 ? R(38f, 45f) : R(32f, 40f));
                float tanP = Mathf.Tan(pitch * Mathf.Deg2Rad);
                int col = rnd.Next(BrickColumn);
                string roofMat = Chance(0.6f) ? "HouseRoof" : "HouseRoofBrown";
                lit = Chance(0.15f) ? 0.05f : R(0.25f, 0.8f);

                // footprint
                bool garage = W >= 15f && Chance(0.65f);
                bool dbl = garage && W >= 19f && Chance(0.6f);
                float gW = garage ? (dbl ? 6.6f : 3.9f) : 0f;
                float drvW = garage ? (dbl ? 5.6f : 3.2f) : 3.0f;
                const float side = 1.3f;
                float extra = garage ? gW : drvW + 0.6f;
                float hw = style == 0 ? R(9.5f, 14f) : style == 1 ? R(8.5f, 12f) : R(7f, 9.5f);
                hw = Mathf.Max(5.5f, Mathf.Min(hw, W - 2f * side - extra));
                float setback = R(6f, 9f);
                float hd = style == 2 ? R(10f, 13f) : R(8f, 11f);
                hd = Mathf.Clamp(hd, 6f, Mathf.Max(6f, D - setback - 4f));
                float start = side + R(0f, Mathf.Max(0f, W - 2f * side - extra - hw));
                bool leftSide = Chance(0.5f);                                  // garage / driveway side
                float hx0, hx1, gx0 = 0f, gx1 = 0f, dx0, dx1;
                if (garage)
                {
                    if (leftSide) { gx0 = start; gx1 = gx0 + gW; hx0 = gx1; }
                    else { hx0 = start; gx0 = hx0 + hw; gx1 = gx0 + gW; }
                    hx1 = hx0 + hw;
                    float gc = (gx0 + gx1) * 0.5f;
                    dx0 = gc - drvW * 0.5f; dx1 = gc + drvW * 0.5f;
                }
                else
                {
                    if (leftSide) { dx0 = start; dx1 = dx0 + drvW; hx0 = dx1 + 0.6f; }
                    else { hx0 = start; dx0 = hx0 + hw + 0.6f; dx1 = dx0 + drvW; }
                    hx1 = hx0 + hw;
                }
                float z0 = setback, z1 = setback + hd;
                float gz0 = z0 + (garage ? R(-1.6f, 0.4f) : 0f);
                float gz1 = gz0 + Mathf.Min(6.8f, hd + 1.2f);
                float dEnd = garage ? gz0 : Mathf.Min(D - 1f, z0 + R(4f, 7.5f));

                // door + porch
                float doorF = style == 1 && Chance(0.6f) ? 0.5f : (Chance(0.5f) ? R(0.22f, 0.35f) : R(0.65f, 0.78f));
                float doorX = Mathf.Clamp(hx0 + hw * doorF, hx0 + 1.4f, hx1 - 1.4f);
                int porch = style == 0 ? (roof == RoofSideGable && Chance(0.75f) ? 2 : 0) : (Chance(0.85f) ? 1 : 0); // 0 stoop, 1 shed roof, 2 under main eave
                float pd = porch == 0 ? 1.3f : R(1.8f, 2.4f);
                float pw = porch == 2 ? hw : porch == 1 ? (Chance(style == 2 ? 0.45f : 0.2f) ? hw : R(2.6f, 3.8f)) : 2.0f;
                pw = Mathf.Min(pw, hw);
                float px0 = Mathf.Clamp(doorX - pw * 0.5f, hx0, hx1 - pw), px1 = px0 + pw;
                const float porchY = 0.5f;
                float stepZ = z0 - pd - 0.38f;

                // ground: lawn around the driveway, driveway, walkway
                var lawn = M("HouseLawn");
                float lk = 1f / LawnTile; Vector2 lo = UV(R(0f, 1f), R(0f, 1f));
                lawn.Top(0f, 0f, dx0, D, 0.03f, lk, lo);
                lawn.Top(dx1, 0f, W, D, 0.03f, lk, lo);
                if (dEnd < D - 0.05f) lawn.Top(dx0, dEnd, dx1, D, 0.03f, lk, lo);
                var conc = M("HouseDriveway");
                float ck = 1f / DriveTile; Vector2 co = UV(R(0f, 1f), R(0f, 1f));
                conc.Top(dx0, 0f, dx1, dEnd, 0.05f, ck, co);
                float wx0 = doorX - 0.6f, wx1 = doorX + 0.6f;
                bool straightWalk = true;
                if (Chance(0.4f))
                {
                    float za = stepZ - 1.2f;
                    if (dx1 <= wx0) { conc.Top(dx1, za, wx1, stepZ, 0.06f, ck, co); straightWalk = false; }
                    else if (dx0 >= wx1) { conc.Top(wx0, za, dx0, stepZ, 0.06f, ck, co); straightWalk = false; }
                }
                if (straightWalk) conc.Top(wx0, 0f, wx1, stepZ, 0.06f, ck, co);

                // body
                Siding(G(hx0, z0), G(hx1, z0), 0f, wallH, col);
                Siding(G(hx1, z0), G(hx1, z1), 0f, wallH, col);
                Siding(G(hx1, z1), G(hx0, z1), 0f, wallH, col);
                Siding(G(hx0, z1), G(hx0, z0), 0f, wallH, col);
                float yr = Roof(roof, hx0, hx1, z0, z1, wallH, tanP, 0.45f, porch == 2 ? pd + 0.3f : 0.45f, roofMat, col, true, out float tMain);

                // porch, steps, lanterns, door
                Slab(px0, px1, z0 - pd, z0, porchY);
                Slab(Mathf.Max(px0, doorX - 0.75f), Mathf.Min(px1, doorX + 0.75f), stepZ, z0 - pd, 0.25f);
                if (porch == 1) ShedPorch(px0, px1, z0, pd, wallH, roofMat, porchY, doorX);
                else if (porch == 2)
                {
                    float zp = z0 - pd + 0.15f;
                    Posts(px0, px1, zp, porchY, wallH - Fascia - (z0 - zp) * tMain, doorX);
                }
                int ls = Chance(0.5f) ? -1 : 1;
                Lantern(doorX + ls * 0.88f, porchY + 1.8f, z0);
                if (Chance(0.3f)) Lantern(doorX - ls * 0.88f, porchY + 1.8f, z0);
                AddOpening(G(hx0, z0), Vector3.right, doorX - hx0, porchY + 0.01f, Opening.Door, rnd.Next(DoorVariants), 0.92f);

                // windows
                const float sill1 = 1.05f, sill2 = 3.85f;
                Vector3 fa = G(hx0, z0);
                FillWindows(fa, Vector3.right, 0.4f, doorX - hx0 - 1.05f, sill1, true, 3, 1f);
                FillWindows(fa, Vector3.right, doorX - hx0 + 1.05f, hw - 0.4f, sill1, true, 3, 1f);
                if (floors == 2) FillWindows(fa, Vector3.right, 0.5f, hw - 0.5f, sill2, false, 4, 0.85f);
                if (roof == RoofFrontGable)
                {
                    // attic window in the street-facing gable
                    float half = hw * 0.5f, rise = yr - wallH, sc = 0.8f, wh = 1.76f * sc, ww = 1.28f * sc;
                    if (rise > wh + 0.6f && half * (1f - (wh + 0.45f) / rise) > ww * 0.5f + 0.15f)
                        AddOpening(fa, Vector3.right, half, wallH + 0.3f, Opening.Window, PickWindow(0.6f), sc);
                }
                bool gL = garage && leftSide, gR = garage && !leftSide;
                if (!gL)
                {
                    FillWindows(G(hx0, z1), Vector3.back, 1f, hd - 1f, sill1, false, 2, 0.9f);
                    if (floors == 2) FillWindows(G(hx0, z1), Vector3.back, 1f, hd - 1f, sill2, false, 2, 0.7f);
                }
                if (!gR)
                {
                    FillWindows(G(hx1, z0), Vector3.forward, 1f, hd - 1f, sill1, false, 2, 0.9f);
                    if (floors == 2) FillWindows(G(hx1, z0), Vector3.forward, 1f, hd - 1f, sill2, false, 2, 0.7f);
                }
                FillWindows(G(hx1, z1), Vector3.left, 1f, hw - 1f, sill1, false, 2, 0.8f);

                // chimney
                if (Chance(0.55f))
                {
                    if (roof == RoofSideGable && Chance(0.6f))
                    {
                        bool atLeft = garage ? !leftSide : Chance(0.5f);
                        float zm = (z0 + z1) * 0.5f, cx0 = atLeft ? hx0 - 0.55f : hx1 - 0.05f;
                        Chimney(cx0, cx0 + 0.6f, zm - 0.5f, zm + 0.5f, 0f, yr + R(0.4f, 0.8f), atLeft ? 1 : 3);
                    }
                    else
                    {
                        float cx, cz;
                        if (roof == RoofFrontGable) { cx = (hx0 + hx1) * 0.5f + (Chance(0.5f) ? 1f : -1f) * R(0.5f, hw * 0.25f) - 0.35f; cz = R(z0 + 2f, z1 - 2.8f); }
                        else { cx = R(hx0 + 1.5f, hx1 - 2.2f); cz = (z0 + z1) * 0.5f + R(0.2f, 1.2f); }
                        Chimney(cx, cx + 0.7f, cz, cz + 0.8f, wallH - 0.3f, yr + R(0.5f, 0.9f), -1);
                    }
                }

                // garage
                if (garage)
                {
                    const float gh = 2.95f;
                    Siding(G(gx0, gz0), G(gx1, gz0), 0f, gh, col);
                    Siding(G(gx1, gz1), G(gx0, gz1), 0f, gh, col);
                    if (leftSide) Siding(G(gx0, gz1), G(gx0, gz0), 0f, gh, col);
                    else Siding(G(gx1, gz0), G(gx1, gz1), 0f, gh, col);
                    // inner wall only where the garage sticks out past the house
                    float ix = leftSide ? gx1 : gx0;
                    if (gz0 < z0 - 0.01f) InnerWall(ix, gz0, z0, leftSide, gh, col);
                    if (gz1 > z1 + 0.01f) InnerWall(ix, z1, gz1, leftSide, gh, col);
                    int gRoof = roof == RoofFrontGable || Chance(0.4f) ? RoofFrontGable : roof;
                    Roof(gRoof, gx0, gx1, gz0, gz1, gh, Mathf.Min(tanP, 0.65f), 0.35f, 0.35f, roofMat, col, false, out _);
                    AddOpening(G(gx0, gz0), Vector3.right, gW * 0.5f, 0.02f, dbl ? Opening.GarageDouble : Opening.GarageSingle, rnd.Next(GarageVariants), 1f);
                    if (Chance(0.45f)) Lantern(leftSide ? gx0 + 0.35f : gx1 - 0.35f, 2.15f, gz0);
                }

                // front edge: none / picket fence / metal fence / hedge, with gaps for driveway and walkway
                double e = rnd.NextDouble();
                int edge = e < 0.5 ? 0 : e < 0.68 ? 1 : e < 0.78 ? 2 : 3;
                if (edge != 0)
                {
                    var gaps = new List<Vector2> { UV(dx0 - 0.25f, dx1 + 0.25f) };
                    if (straightWalk) gaps.Add(UV(wx0 - 0.3f, wx1 + 0.3f));
                    gaps.Sort((p, q) => p.x.CompareTo(q.x));
                    float s = 0.15f;
                    foreach (var g in gaps)
                    {
                        if (g.x - s > 0.6f) Edge(edge, s, g.x);
                        s = Mathf.Max(s, g.y);
                    }
                    if (W - 0.15f - s > 0.6f) Edge(edge, s, W - 0.15f);
                }

                // foundation shrubs beside the porch
                if (porch != 2 && edge == 0)
                {
                    float[] xs = { hx0 + 0.9f, hx1 - 0.9f };
                    foreach (float x in xs)
                        if ((x < px0 - 0.9f || x > px1 + 0.9f) && Chance(0.55f))
                            M("HouseHedge").Blob(P(x, 0.4f, z0 - 0.6f), P(R(0.6f, 0.9f), R(0.45f, 0.7f), 0.55f), rnd.Next());
                }

                // mailbox at the street, next to the driveway
                float mx = dx0 > 1.8f ? dx0 - 0.6f : dx1 + 0.6f;
                Post(M("HouseTrim"), mx - 0.05f, 0.25f, mx + 0.05f, 0.35f, 0f, 1.0f);
                M("PoleMetal").Box(P(mx - 0.11f, 1.0f, 0.05f), P(mx + 0.11f, 1.25f, 0.5f), 1f);
            }

            void InnerWall(float x, float za, float zb, bool garageLeft, float h, int col)
            {
                if (garageLeft) Siding(G(x, za), G(x, zb), 0f, h, col);   // faces +x
                else Siding(G(x, zb), G(x, za), 0f, h, col);              // faces -x
            }

            void Edge(int kind, float a, float b)
            {
                if (kind == 3) { Hedge(a + 0.1f, b - 0.1f, 0.75f, 1.55f, R(0.8f, 1.15f)); return; }
                var fb = M("HouseFence");
                float k = 1f / FenceTile, row = kind - 1;
                float v0 = row * 0.5f + 0.5f / 256f, v1 = row * 0.5f + 0.5f - 0.5f / 256f;
                const float z = 0.7f, y0 = 0.03f, y1 = 1.03f;
                Quad(fb, P(a, y0, z), P(a, y1, z), P(b, y1, z), P(b, y0, z), Vector3.back, UV(a * k, v0), UV(a * k, v1), UV(b * k, v1), UV(b * k, v0));
                Quad(fb, P(b, y0, z + 0.02f), P(b, y1, z + 0.02f), P(a, y1, z + 0.02f), P(a, y0, z + 0.02f), Vector3.forward,
                     UV(-b * k, v0), UV(-b * k, v1), UV(-a * k, v1), UV(-a * k, v0));
            }

            void Hedge(float a, float b, float za, float zb, float h)
            {
                if (b - a < 0.4f) return;
                var hb = M("HouseHedge");
                float k = 1f / HedgeTile, ins = 0.12f, zi0 = za + ins, zi1 = zb - ins;
                Quad(hb, P(a, 0f, za), P(a, h, zi0), P(b, h, zi0), P(b, 0f, za), Vector3.back, UV(a * k, 0f), UV(a * k, h * k), UV(b * k, h * k), UV(b * k, 0f));
                Quad(hb, P(b, 0f, zb), P(b, h, zi1), P(a, h, zi1), P(a, 0f, zb), Vector3.forward, UV(-b * k, 0f), UV(-b * k, h * k), UV(-a * k, h * k), UV(-a * k, 0f));
                Quad(hb, P(a, h, zi0), P(a, h, zi1), P(b, h, zi1), P(b, h, zi0), Vector3.up, UV(a * k, zi0 * k), UV(a * k, zi1 * k), UV(b * k, zi1 * k), UV(b * k, zi0 * k));
                Quad(hb, P(a, 0f, zb), P(a, h, zi1), P(a, h, zi0), P(a, 0f, za), Vector3.left, UV(-zb * k, 0f), UV(-zi1 * k, h * k), UV(-zi0 * k, h * k), UV(-za * k, 0f));
                Quad(hb, P(b, 0f, za), P(b, h, zi0), P(b, h, zi1), P(b, 0f, zb), Vector3.right, UV(za * k, 0f), UV(zi0 * k, h * k), UV(zi1 * k, h * k), UV(zb * k, 0f));
            }

            // brick chimney using the brick column of the siding atlas; skipFace: 0 front,1 right,2 back,3 left (-1 none)
            void Chimney(float x0, float x1, float z0, float z1, float y0, float y1, int skipFace)
            {
                var sb = M("HouseSiding");
                var mul = new Vector2(1f / SidingSpan, 1f / SidingSpan);   // brick column is unstretched: 100 px per metre
                var off = new Vector2((BrickColumn + 0.08f) / SidingColumns, R(0f, 0.5f));
                if (skipFace != 0) sb.Wall(G(x0, z0), G(x1, z0), y0, y1, mul, off);
                if (skipFace != 1) sb.Wall(G(x1, z0), G(x1, z1), y0, y1, mul, off);
                if (skipFace != 2) sb.Wall(G(x1, z1), G(x0, z1), y0, y1, mul, off);
                if (skipFace != 3) sb.Wall(G(x0, z1), G(x0, z0), y0, y1, mul, off);
                M("HouseDriveway").Top(x0, z0, x1, z1, y1, 1f / DriveTile);
            }

            void Posts(float px0, float px1, float z, float y0, float y1, float doorX)
            {
                var tb = M("HouseTrim");
                int n = Mathf.Max(2, Mathf.RoundToInt((px1 - px0) / 2.8f) + 1);
                for (int i = 0; i < n; i++)
                {
                    float x = Mathf.Lerp(px0 + 0.15f, px1 - 0.15f, i / (float)(n - 1));
                    if (i > 0 && i < n - 1 && Mathf.Abs(x - doorX) < 0.95f) continue;   // keep the steps clear
                    Post(tb, x - 0.07f, z - 0.07f, x + 0.07f, z + 0.07f, y0, y1);
                }
            }

            // shed roof over the front porch, attached to the front wall
            void ShedPorch(float px0, float px1, float z0, float pd, float wallH, string mat, float porchY, float doorX)
            {
                var rb = M(mat); var tb = M("HouseTrim");
                float pt = Mathf.Tan(R(14f, 22f) * Mathf.Deg2Rad);
                float yA = Mathf.Min(wallH - 0.15f, 3.5f), zF = z0 - pd - 0.25f, yF = yA - (z0 - zF) * pt;
                const float th = 0.16f;
                float rx0 = px0 - 0.15f, rx1 = px1 + 0.15f, k = 1f / RoofTile, vl = (z0 - zF) * Mathf.Sqrt(1f + pt * pt) * k;
                Quad(rb, P(rx0, yF, zF), P(rx0, yA, z0), P(rx1, yA, z0), P(rx1, yF, zF), P(0f, 1f, -pt), UV(rx0 * k, 0f), UV(rx0 * k, vl), UV(rx1 * k, vl), UV(rx1 * k, 0f));
                Quad(tb, P(rx0, yF - th, zF), P(rx0, yA - th, z0), P(rx1, yA - th, z0), P(rx1, yF - th, zF), Vector3.down);
                Quad(tb, P(rx0, yF - th, zF), P(rx0, yF + 0.02f, zF), P(rx1, yF + 0.02f, zF), P(rx1, yF - th, zF), Vector3.back);
                Quad(tb, P(rx0, yA - th, z0), P(rx0, yA, z0), P(rx0, yF, zF), P(rx0, yF - th, zF), Vector3.left);
                Quad(tb, P(rx1, yF - th, zF), P(rx1, yF, zF), P(rx1, yA, z0), P(rx1, yA - th, z0), Vector3.right);
                float zp = z0 - pd + 0.15f;
                Posts(px0, px1, zp, porchY, yA - th - (z0 - zp) * pt, doorX);
            }

            // ---------------- roofs ----------------
            static float MaxRise(float yw) => Mathf.Max(0.5f, SidingSpan - 0.45f - yw);

            float Roof(int type, float x0, float x1, float z0, float z1, float yw, float t, float o, float oFront, string mat, int col, bool rakes, out float tUsed)
            {
                float span, yr;
                if (type == RoofHip)
                {
                    bool alongX = x1 - x0 >= z1 - z0;
                    span = alongX ? z1 - z0 : x1 - x0;
                    t = Mathf.Min(t, MaxRise(yw) / (span * 0.5f));
                    if (alongX) Hip(x0, x1, z0, z1, yw, t, o, mat);
                    else { var m = Mark(); Hip(-z1, -z0, x0, x1, yw, t, o, mat); Rot90Since(m); }
                }
                else if (type == RoofSideGable)
                {
                    span = z1 - z0;
                    t = Mathf.Min(t, MaxRise(yw) / (span * 0.5f));
                    Gable(x0, x1, z0, z1, yw, t, oFront, o, 0.25f, mat, col, false, rakes);
                }
                else
                {
                    // ridge perpendicular to the street: build with the ridge along x in a rotated frame
                    span = x1 - x0;
                    t = Mathf.Min(t, MaxRise(yw) / (span * 0.5f));
                    var m = Mark();
                    Gable(-z1, -z0, x0, x1, yw, t, o, o, 0.3f, mat, col, true, rakes);
                    Rot90Since(m);
                }
                yr = yw + span * 0.5f * t;
                tUsed = t;
                return yr;
            }

            // gable roof, ridge along x, gable ends at x0/x1. oF/oB eave overhangs (-z/+z), g = gable overhang.
            void Gable(float x0, float x1, float z0, float z1, float yw, float t, float oF, float oB, float g, string mat, int col, bool rakeSoffitX1, bool rakes)
            {
                var rb = M(mat); var tb = M("HouseTrim"); var sb = M("HouseSiding");
                float zm = (z0 + z1) * 0.5f, half = (z1 - z0) * 0.5f, yr = yw + half * t;
                float yeF = yw - oF * t, yeB = yw - oB * t, zF = z0 - oF, zB = z1 + oB;
                float xa = x0 - g, xb = x1 + g, k = 1f / RoofTile, sec = Mathf.Sqrt(1f + t * t), uo = R(0f, 1f);
                float lf = (half + oF) * sec * k, lb = (half + oB) * sec * k;
                const float fh = Fascia;

                // slopes
                Quad(rb, P(xa, yeF, zF), P(xa, yr, zm), P(xb, yr, zm), P(xb, yeF, zF), P(0f, 1f, -t),
                     UV(xa * k + uo, 0f), UV(xa * k + uo, lf), UV(xb * k + uo, lf), UV(xb * k + uo, 0f));
                Quad(rb, P(xb, yeB, zB), P(xb, yr, zm), P(xa, yr, zm), P(xa, yeB, zB), P(0f, 1f, t),
                     UV(-xb * k + uo, 0f), UV(-xb * k + uo, lb), UV(-xa * k + uo, lb), UV(-xa * k + uo, 0f));

                // gable triangles (siding)
                float su = (col + 0.2f) / SidingColumns, sw = 0.5f / SidingColumns, vs = 1f / SidingSpan;
                Tri(sb, P(x0, yw, z1), P(x0, yr, zm), P(x0, yw, z0), Vector3.left, UV(su, yw * vs), UV(su + sw * 0.5f, yr * vs), UV(su + sw, yw * vs));
                Tri(sb, P(x1, yw, z0), P(x1, yr, zm), P(x1, yw, z1), Vector3.right, UV(su, yw * vs), UV(su + sw * 0.5f, yr * vs), UV(su + sw, yw * vs));

                // eave soffits (parallel to the slope) + fascia boards
                Quad(tb, P(xa, yeF - fh, zF), P(xa, yw - fh, z0), P(xb, yw - fh, z0), P(xb, yeF - fh, zF), Vector3.down);
                Quad(tb, P(xa, yeF - fh, zF), P(xa, yeF + 0.03f, zF), P(xb, yeF + 0.03f, zF), P(xb, yeF - fh, zF), Vector3.back);
                Quad(tb, P(xb, yeB - fh, zB), P(xb, yw - fh, z1), P(xa, yw - fh, z1), P(xa, yeB - fh, zB), Vector3.down);
                Quad(tb, P(xb, yeB - fh, zB), P(xb, yeB + 0.03f, zB), P(xa, yeB + 0.03f, zB), P(xa, yeB - fh, zB), Vector3.forward);

                // rake boards along the gable edges
                for (int s = 0; s < 2 && rakes; s++)
                {
                    float x = s == 0 ? xa : xb; Vector3 want = s == 0 ? Vector3.left : Vector3.right;
                    Quad(tb, P(x, yeF - fh, zF), P(x, yeF + 0.03f, zF), P(x, yr + 0.03f, zm), P(x, yr - fh, zm), want);
                    Quad(tb, P(x, yr - fh, zm), P(x, yr + 0.03f, zm), P(x, yeB + 0.03f, zB), P(x, yeB - fh, zB), want);
                }
                // underside of the gable overhang (only where it faces the street)
                if (rakeSoffitX1)
                {
                    Quad(tb, P(x1, yeF - fh, zF), P(x1, yr - fh, zm), P(xb, yr - fh, zm), P(xb, yeF - fh, zF), Vector3.down);
                    Quad(tb, P(x1, yr - fh, zm), P(x1, yeB - fh, zB), P(xb, yeB - fh, zB), P(xb, yr - fh, zm), Vector3.down);
                }
            }

            // hip roof, ridge along x (requires x1-x0 >= z1-z0), uniform overhang o
            void Hip(float x0, float x1, float z0, float z1, float yw, float t, float o, string mat)
            {
                var rb = M(mat); var tb = M("HouseTrim");
                float half = (z1 - z0) * 0.5f, zm = (z0 + z1) * 0.5f, yr = yw + half * t, ye = yw - o * t;
                float rx0 = x0 + half, rx1 = Mathf.Max(rx0, x1 - half);
                float ex0 = x0 - o, ex1 = x1 + o, ez0 = z0 - o, ez1 = z1 + o;
                Vector3 A = P(ex0, ye, ez0), Bp = P(ex1, ye, ez0), C = P(ex1, ye, ez1), Dp = P(ex0, ye, ez1);
                Vector3 RL = P(rx0, yr, zm), RR = P(rx1, yr, zm);
                float k = 1f / RoofTile, sec = Mathf.Sqrt(1f + t * t), uo = R(0f, 1f), vr = (half + o) * sec * k;
                Quad(rb, A, RL, RR, Bp, P(0f, 1f, -t), UV(ex0 * k + uo, 0f), UV(rx0 * k + uo, vr), UV(rx1 * k + uo, vr), UV(ex1 * k + uo, 0f));
                Quad(rb, C, RR, RL, Dp, P(0f, 1f, t), UV(-ex1 * k + uo, 0f), UV(-rx1 * k + uo, vr), UV(-rx0 * k + uo, vr), UV(-ex0 * k + uo, 0f));
                Tri(rb, Dp, RL, A, P(-t, 1f, 0f), UV(-ez1 * k + uo, 0f), UV(-zm * k + uo, vr), UV(-ez0 * k + uo, 0f));
                Tri(rb, Bp, RR, C, P(t, 1f, 0f), UV(ez0 * k + uo, 0f), UV(zm * k + uo, vr), UV(ez1 * k + uo, 0f));

                const float fh = Fascia;
                float ys = ye - fh, yi = yw - fh, yt = ye + 0.03f;
                Quad(tb, P(ex0, ys, ez0), P(x0, yi, z0), P(x1, yi, z0), P(ex1, ys, ez0), Vector3.down);
                Quad(tb, P(ex1, ys, ez0), P(x1, yi, z0), P(x1, yi, z1), P(ex1, ys, ez1), Vector3.down);
                Quad(tb, P(ex1, ys, ez1), P(x1, yi, z1), P(x0, yi, z1), P(ex0, ys, ez1), Vector3.down);
                Quad(tb, P(ex0, ys, ez1), P(x0, yi, z1), P(x0, yi, z0), P(ex0, ys, ez0), Vector3.down);
                Quad(tb, P(ex0, ys, ez0), P(ex0, yt, ez0), P(ex1, yt, ez0), P(ex1, ys, ez0), Vector3.back);
                Quad(tb, P(ex1, ys, ez0), P(ex1, yt, ez0), P(ex1, yt, ez1), P(ex1, ys, ez1), Vector3.right);
                Quad(tb, P(ex1, ys, ez1), P(ex1, yt, ez1), P(ex0, yt, ez1), P(ex0, ys, ez1), Vector3.forward);
                Quad(tb, P(ex0, ys, ez1), P(ex0, yt, ez1), P(ex0, yt, ez0), P(ex0, ys, ez0), Vector3.left);
            }
        }
    }
}
