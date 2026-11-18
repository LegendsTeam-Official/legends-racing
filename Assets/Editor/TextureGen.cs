using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Generates all procedural textures as PNGs (run once from ProjectSetup).
public static class TextureGen
{
    public const string Dir = "Assets/Generated/Textures";

    // ---------- noise ----------
    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xffffff) / 16777215f;
        }
    }
    // Tileable value noise: period in lattice cells
    static float VNoise(float x, float y, int px, int py, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int xa = Mod(x0, px), xb = Mod(x0 + 1, px), ya = Mod(y0, py), yb = Mod(y0 + 1, py);
        float a = Hash(xa, ya, seed), b = Hash(xb, ya, seed), c = Hash(xa, yb, seed), d = Hash(xb, yb, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }
    static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }
    // u,v in [0,1) tile; base frequency fx,fy cells
    public static float Fbm(float u, float v, int fx, int fy, int oct, int seed)
    {
        float s = 0, a = 0.5f, norm = 0;
        for (int o = 0; o < oct; o++)
        {
            s += a * VNoise(u * fx, v * fy, fx, fy, seed + o * 31);
            norm += a; a *= 0.5f; fx *= 2; fy *= 2;
        }
        return s / norm;
    }

    // ---------- io ----------
    static void Save(string name, Color[] px, int w, int h, bool linear, bool normal = false, int aniso = 4)
    {
        Directory.CreateDirectory(Dir);
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false, linear);
        t.SetPixels(px); t.Apply();
        string path = $"{Dir}/{name}.png";
        File.WriteAllBytes(path, t.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        imp.sRGBTexture = !linear;
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.mipmapEnabled = true;
        imp.anisoLevel = aniso;
        imp.filterMode = FilterMode.Trilinear;
        imp.alphaIsTransparency = false;
        var android = imp.GetPlatformTextureSettings("Android");
        android.overridden = true; android.format = TextureImporterFormat.ASTC_6x6; android.maxTextureSize = 2048;
        imp.SetPlatformTextureSettings(android);
        imp.SaveAndReimport();
    }

    static Color[] NormalFromHeight(float[] hgt, int w, int h, float strength)
    {
        var n = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float l = hgt[y * w + Mod(x - 1, w)], r = hgt[y * w + Mod(x + 1, w)];
                float d = hgt[Mod(y - 1, h) * w + x], u = hgt[Mod(y + 1, h) * w + x];
                var v = new Vector3((l - r) * strength, (d - u) * strength, 1f).normalized;
                n[y * w + x] = new Color(v.x * 0.5f + 0.5f, v.y * 0.5f + 0.5f, v.z * 0.5f + 0.5f, 1);
            }
        return n;
    }

    static float SmoothStep(float a, float b, float x) { x = Mathf.Clamp01((x - a) / (b - a)); return x * x * (3 - 2 * x); }
    static float Box(float x, float a, float b, float soft) => SmoothStep(a - soft, a + soft, x) * (1 - SmoothStep(b - soft, b + soft, x));

    public static void GenerateAll()
    {
        Road(); Intersection(); Sidewalk(); Roof();
        Facade("facade_concrete", new Color(0.30f, 0.30f, 0.31f), 0, 11);
        Facade("facade_brick", new Color(0.29f, 0.15f, 0.11f), 1, 23);
        Facade("facade_stone", new Color(0.42f, 0.38f, 0.32f), 2, 37);
        Shop();
        Glow(); Streak(); Splash();
        AssetDatabase.Refresh();
    }

    // Asphalt base shared by road + intersection. u,v in metres
    static void Asphalt(float um, float vm, float tu, float tv, int seed, out float alb, out float hgt, out float puddle)
    {
        float u = um / tu, v = vm / tv;
        int cu = Mathf.Max(1, Mathf.RoundToInt(tu * 6)), cv = Mathf.Max(1, Mathf.RoundToInt(tv * 6));
        float grain = Fbm(u, v, cu * 8, cv * 8, 2, seed);
        float patches = Fbm(u, v, Mathf.Max(1, cu / 3), Mathf.Max(1, cv / 3), 4, seed + 7);
        alb = 0.075f + grain * 0.035f + (patches - 0.5f) * 0.05f;
        hgt = grain * 0.6f + patches * 0.2f;
        // puddles: low-frequency mask, sharpened
        float pm = Fbm(u, v, Mathf.Max(1, Mathf.RoundToInt(tu / 2.5f)), Mathf.Max(1, Mathf.RoundToInt(tv / 2.5f)), 4, seed + 99);
        puddle = SmoothStep(0.56f, 0.62f, pm);
    }

    static void Road()
    {
        int w = 512, h = 1024; float W = 14f, L = 32f;
        var alb = new Color[w * h]; var mask = new Color[w * h]; var hg = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float um = (x + 0.5f) / w * W, vm = (y + 0.5f) / h * L;
                Asphalt(um, vm, W, L, 5, out float a, out float ht, out float pud);
                // wheel tracks: slightly darker & smoother
                float lanePos = Mathf.Repeat(um, 3.5f);
                float track = Box(lanePos, 0.6f, 1.1f, 0.25f) + Box(lanePos, 2.4f, 2.9f, 0.25f);
                a *= 1f - 0.12f * track;
                // markings
                float wear = Fbm(um / W, vm / L, 64, 128, 3, 77);
                float mk = 0;
                mk = Mathf.Max(mk, Box(um, 0.35f, 0.5f, 0.01f));                 // edge lines
                mk = Mathf.Max(mk, Box(um, W - 0.5f, W - 0.35f, 0.01f));
                float dash = Box(Mathf.Repeat(vm, 8f), 0f, 3f, 0.02f);
                mk = Mathf.Max(mk, Box(um, 3.43f, 3.57f, 0.01f) * dash);         // lane dashes
                mk = Mathf.Max(mk, Box(um, 10.43f, 10.57f, 0.01f) * dash);
                float yel = Mathf.Max(Box(um, 6.8f, 6.92f, 0.01f), Box(um, 7.08f, 7.2f, 0.01f)); // centre double yellow
                mk *= SmoothStep(0.25f, 0.45f, wear);
                yel *= SmoothStep(0.2f, 0.4f, wear);
                pud *= 1f - 0.7f * Box(um, 0, 0.6f, 0.4f) * 0; // keep
                // puddles gather near the gutters and in tracks
                float gutter = 1f - SmoothStep(0.4f, 1.6f, Mathf.Min(um, W - um));
                pud = Mathf.Clamp01(pud + gutter * 0.5f * SmoothStep(0.45f, 0.6f, wear) + track * 0.15f * pud);
                Color c = new Color(a, a, a * 1.03f);
                c = Color.Lerp(c, new Color(0.72f, 0.72f, 0.70f), mk);
                c = Color.Lerp(c, new Color(0.75f, 0.55f, 0.12f), yel);
                c *= Mathf.Lerp(1f, 0.55f, pud);              // water darkens
                alb[y * w + x] = c;
                float sm = Mathf.Lerp(0.58f, 0.66f, track);
                sm = Mathf.Lerp(sm, 0.42f, Mathf.Max(mk, yel));
                sm = Mathf.Lerp(sm, 0.97f, pud);
                mask[y * w + x] = new Color(0, 0, 0, sm);
                hg[y * w + x] = Mathf.Lerp(ht + Mathf.Max(mk, yel) * 0.6f, 0.35f, pud);
            }
        Save("road_albedo", alb, w, h, false);
        Save("road_mask", mask, w, h, true);
        Save("road_normal", NormalFromHeight(hg, w, h, 2.2f), w, h, true, true);
    }

    static void Intersection()
    {
        int w = 512, h = 512; float S = 14f;
        var alb = new Color[w * h]; var mask = new Color[w * h]; var hg = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float um = (x + 0.5f) / w * S, vm = (y + 0.5f) / h * S;
                Asphalt(um, vm, S, S, 9, out float a, out float ht, out float pud);
                float wear = Fbm(um / S, vm / S, 64, 64, 3, 51);
                float zebra = 0;
                bool inU = um > 1.0f && um < S - 1.0f, inV = vm > 1.0f && vm < S - 1.0f;
                if (inU && (vm < 3.2f || vm > S - 3.2f) && (vm > 0.4f && vm < S - 0.4f)) zebra = Box(Mathf.Repeat(um - 1f, 1.2f), 0f, 0.6f, 0.01f);
                if (inV && (um < 3.2f || um > S - 3.2f) && (um > 0.4f && um < S - 0.4f)) zebra = Mathf.Max(zebra, Box(Mathf.Repeat(vm - 1f, 1.2f), 0f, 0.6f, 0.01f));
                if ((um < 3.2f || um > S - 3.2f) && (vm < 3.2f || vm > S - 3.2f)) zebra = 0;
                zebra *= SmoothStep(0.25f, 0.45f, wear);
                Color c = new Color(a, a, a * 1.03f);
                c = Color.Lerp(c, new Color(0.7f, 0.7f, 0.68f), zebra);
                c *= Mathf.Lerp(1f, 0.55f, pud);
                alb[y * w + x] = c;
                float sm = Mathf.Lerp(0.6f, 0.42f, zebra);
                sm = Mathf.Lerp(sm, 0.97f, pud);
                mask[y * w + x] = new Color(0, 0, 0, sm);
                hg[y * w + x] = Mathf.Lerp(ht + zebra * 0.6f, 0.35f, pud);
            }
        Save("inter_albedo", alb, w, h, false);
        Save("inter_mask", mask, w, h, true);
        Save("inter_normal", NormalFromHeight(hg, w, h, 2.2f), w, h, true, true);
    }

    static void Sidewalk()
    {
        int w = 512, h = 512; float S = 4f;
        var alb = new Color[w * h]; var mask = new Color[w * h]; var hg = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float um = (x + 0.5f) / w * S, vm = (y + 0.5f) / h * S;
                float gu = Mathf.Repeat(um, 1f), gv = Mathf.Repeat(vm, 1f);
                float joint = 1f - Box(gu, 0.015f, 0.985f, 0.006f) * Box(gv, 0.015f, 0.985f, 0.006f);
                int tx = Mathf.FloorToInt(um), ty = Mathf.FloorToInt(vm);
                float tileTone = (Hash(tx, ty, 3) - 0.5f) * 0.05f;
                float grain = Fbm(um / S, vm / S, 64, 64, 3, 13);
                float stain = Fbm(um / S, vm / S, 4, 4, 4, 17);
                float a = 0.24f + tileTone + (grain - 0.5f) * 0.05f - SmoothStep(0.5f, 0.75f, stain) * 0.08f;
                a *= 1f - joint * 0.5f;
                float pud = SmoothStep(0.64f, 0.7f, Fbm(um / S, vm / S, 2, 2, 4, 21));
                Color c = new Color(a, a * 0.99f, a * 0.97f) * Mathf.Lerp(1f, 0.6f, pud);
                alb[y * w + x] = c;
                mask[y * w + x] = new Color(0, 0, 0, Mathf.Lerp(Mathf.Lerp(0.5f, 0.35f, joint), 0.95f, pud));
                hg[y * w + x] = (1 - joint) * 0.5f + grain * 0.15f;
            }
        Save("sidewalk_albedo", alb, w, h, false);
        Save("sidewalk_mask", mask, w, h, true);
        Save("sidewalk_normal", NormalFromHeight(hg, w, h, 3f), w, h, true, true);
    }

    static void Roof()
    {
        int w = 256, h = 256;
        var alb = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float g = Fbm(x / (float)w, y / (float)h, 32, 32, 3, 61);
                float a = 0.07f + g * 0.05f;
                alb[y * w + x] = new Color(a, a, a * 1.05f);
            }
        Save("roof_albedo", alb, w, h, false);
    }

    // Facade tile: 8 columns x 6 floors, cell 3m x 3.2m -> 24m x 19.2m
    static void Facade(string name, Color wall, int style, int seed)
    {
        int w = 1024, h = 1024; int cols = 8, rows = 6;
        var alb = new Color[w * h]; var emi = new Color[w * h]; var mask = new Color[w * h]; var hg = new float[w * h];
        var rnd = new System.Random(seed);
        // per-window state
        var lit = new int[cols, rows]; var tint = new Color[cols, rows]; var blind = new float[cols, rows];
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                double r = rnd.NextDouble();
                lit[i, j] = r < 0.22 ? 1 : (r < 0.30 ? 2 : (r < 0.33 ? 3 : 0)); // warm, cool, tv, off
                tint[i, j] = lit[i, j] == 1 ? new Color(1f, 0.68f, 0.38f) : lit[i, j] == 2 ? new Color(0.82f, 0.88f, 1f) : new Color(0.35f, 0.5f, 1f);
                tint[i, j] *= 0.6f + (float)rnd.NextDouble() * 0.5f;
                blind[i, j] = (float)rnd.NextDouble() < 0.4 ? (float)rnd.NextDouble() * 0.6f : 0f;
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float um = (x + 0.5f) / w * 24f, vm = (y + 0.5f) / h * 19.2f;
                int ci = Mathf.Min(cols - 1, (int)(um / 3f)), cj = Mathf.Min(rows - 1, (int)(vm / 3.2f));
                float lu = um - ci * 3f, lv = vm - cj * 3.2f;
                float grain = Fbm(um / 24f, vm / 19.2f, 96, 96, 3, seed);
                float dirt = Fbm(um / 24f, vm / 19.2f, 6, 6, 4, seed + 5);
                Color c = wall * (0.85f + grain * 0.3f) * (1f - SmoothStep(0.5f, 0.8f, dirt) * 0.25f);
                float ht = grain * 0.3f;
                if (style == 1) // brick courses
                {
                    float bv = Mathf.Repeat(vm, 0.075f) / 0.075f;
                    float row = Mathf.Floor(vm / 0.075f);
                    float bu = Mathf.Repeat(um + (row % 2) * 0.11f, 0.22f) / 0.22f;
                    float mortar = 1f - Box(bv, 0.12f, 0.95f, 0.03f) * Box(bu, 0.05f, 0.97f, 0.02f);
                    c = Color.Lerp(c * (0.85f + Hash((int)(um / 0.22f), (int)row, seed) * 0.3f), new Color(0.22f, 0.21f, 0.2f), mortar);
                    ht += (1 - mortar) * 0.4f;
                }
                if (style == 2) // stone floor bands
                {
                    float band = Box(lv, 0f, 0.25f, 0.01f);
                    c = Color.Lerp(c, c * 0.8f, band); ht += band * 0.3f;
                }
                // horizontal floor slab line
                float slab = Box(lv, 3.05f, 3.2f, 0.01f);
                c = Color.Lerp(c, c * 0.75f, slab);
                // window opening
                float win = Box(lu, 0.7f, 2.3f, 0.005f) * Box(lv, 0.95f, 2.85f, 0.005f);
                float frame = win * (1f - Box(lu, 0.78f, 2.22f, 0.004f) * Box(lv, 1.03f, 2.77f, 0.004f));
                float mullion = win * Box(lu, 1.47f, 1.53f, 0.003f);
                float sill = Box(lu, 0.6f, 2.4f, 0.005f) * Box(lv, 0.85f, 0.95f, 0.005f);
                float e = 0; Color ec = Color.black; float sm = 0.32f;
                if (win > 0.5f)
                {
                    int st = lit[ci, cj];
                    float gy = (lv - 0.95f) / 1.9f; // 0 bottom .. 1 top
                    Color glass = new Color(0.025f, 0.03f, 0.04f);
                    if (st > 0)
                    {
                        float inner = 0.55f + 0.45f * gy;                       // ceiling light
                        float bl = blind[ci, cj];
                        if (bl > 0 && gy > 1f - bl) inner *= 0.45f + 0.1f * Mathf.Repeat(lv * 30f, 1f);
                        if (st == 3) inner *= 0.4f + 0.2f * Mathf.Sin(lu * 9f);
                        ec = tint[ci, cj] * inner; e = 1;
                        glass = Color.Lerp(glass, ec * 0.2f, 0.5f);
                    }
                    c = glass; sm = 0.93f; ht = 0f;
                }
                if (frame > 0.5f || mullion > 0.5f) { c = new Color(0.05f, 0.05f, 0.055f); sm = 0.5f; ht = 0.5f; ec = Color.black; e = 0; }
                if (sill > 0.5f) { c = wall * 1.1f; ht = 0.8f; }
                alb[y * w + x] = c;
                emi[y * w + x] = ec * e;
                mask[y * w + x] = new Color(0, 0, 0, sm);
                hg[y * w + x] = ht;
            }
        Save(name + "_albedo", alb, w, h, false);
        Save(name + "_emission", emi, w, h, false);
        Save(name + "_mask", mask, w, h, true);
        Save(name + "_normal", NormalFromHeight(hg, w, h, 4f), w, h, true, true);
    }

    // Ground-floor shops: 24m x 4.5m tile, 4 storefronts of 6m
    static void Shop()
    {
        int w = 2048, h = 384;
        var alb = new Color[w * h]; var emi = new Color[w * h]; var mask = new Color[w * h]; var hg = new float[w * h];
        var rnd = new System.Random(5);
        var open = new bool[4]; var col = new Color[4]; var sign = new Color[4];
        Color[] signs = { new Color(1f, 0.35f, 0.1f), new Color(0.2f, 0.6f, 1f), new Color(1f, 0.85f, 0.4f), new Color(0.9f, 0.2f, 0.3f) };
        for (int i = 0; i < 4; i++)
        {
            open[i] = rnd.NextDouble() < 0.6;
            col[i] = Color.Lerp(new Color(1f, 0.72f, 0.42f), new Color(1f, 0.9f, 0.75f), (float)rnd.NextDouble());
            sign[i] = signs[rnd.Next(4)];
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float um = (x + 0.5f) / w * 24f, vm = (y + 0.5f) / h * 4.5f;
                int si = Mathf.Min(3, (int)(um / 6f)); float lu = um - si * 6f;
                float grain = Fbm(um / 24f, vm / 4.5f, 128, 24, 3, 3);
                Color c = new Color(0.10f, 0.10f, 0.105f) * (0.8f + grain * 0.4f);
                Color ec = Color.black; float sm = 0.4f; float ht = grain * 0.2f;
                float glass = Box(lu, 0.4f, 5.6f, 0.005f) * Box(vm, 0.45f, 3.45f, 0.005f);
                float mull = glass * Mathf.Max(Box(lu, 2.97f, 3.03f, 0.003f), Box(vm, 2.6f, 2.66f, 0.003f));
                float door = Box(lu, 4.2f, 5.3f, 0.005f) * Box(vm, 0.0f, 2.6f, 0.005f);
                if (glass > 0.5f || door > 0.5f)
                {
                    c = new Color(0.03f, 0.035f, 0.04f); sm = 0.95f; ht = 0;
                    if (open[si])
                    {
                        float gy = Mathf.Clamp01((vm - 0.45f) / 3f);
                        float shelves = 0.8f + 0.2f * Box(Mathf.Repeat(vm, 0.55f), 0f, 0.06f, 0.01f) * (gy < 0.75f ? 1 : 0);
                        float spots = 1f + 0.5f * Mathf.Exp(-Mathf.Pow((Mathf.Repeat(lu, 1.5f) - 0.75f) / 0.25f, 2)) * Mathf.Exp(-Mathf.Pow((gy - 0.95f) / 0.08f, 2));
                        float silhouettes = 1f - 0.5f * Box(Mathf.Repeat(lu + si * 0.7f, 2.3f), 0.4f, 0.9f, 0.05f) * Box(gy, 0f, 0.55f, 0.05f);
                        ec = col[si] * (0.35f + 0.4f * gy) * shelves * spots * silhouettes;
                        c = Color.Lerp(c, ec * 0.15f, 0.5f);
                    }
                }
                if (mull > 0.5f) { c = new Color(0.04f, 0.04f, 0.045f); ec = Color.black; sm = 0.5f; ht = 0.5f; }
                // sign band
                float band = Box(vm, 3.6f, 4.3f, 0.005f) * Box(lu, 0.5f, 5.5f, 0.005f);
                if (band > 0.5f)
                {
                    c = new Color(0.05f, 0.05f, 0.05f);
                    float letters = Box(vm, 3.78f, 4.12f, 0.01f) * Box(lu, 1.5f, 4.5f, 0.01f) * (Mathf.Repeat(lu * 2.6f, 1f) < 0.62f ? 1 : 0);
                    if (open[si] || si % 2 == 0) ec = sign[si] * letters * 1.6f;
                    sm = 0.6f; ht = 0.4f;
                }
                alb[y * w + x] = c; emi[y * w + x] = ec;
                mask[y * w + x] = new Color(0, 0, 0, sm); hg[y * w + x] = ht;
            }
        Save("shop_albedo", alb, w, h, false);
        Save("shop_emission", emi, w, h, false);
        Save("shop_mask", mask, w, h, true);
        Save("shop_normal", NormalFromHeight(hg, w, h, 3f), w, h, true, true);
    }

    static void Glow()
    {
        int s = 128; var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x + 0.5f) / s * 2 - 1, dy = (y + 0.5f) / s * 2 - 1;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float g = Mathf.Clamp01(1 - r); g = g * g * (0.6f + 0.4f * g);
                g += Mathf.Exp(-r * r * 60f) * 0.6f;
                px[y * s + x] = new Color(1, 1, 1, Mathf.Clamp01(g));
            }
        Save("glow", px, s, s, false, false, 1);
    }

    static void Streak()
    {
        int w = 16, h = 128; var px = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2 - 1, v = (y + 0.5f) / h;
                float a = Mathf.Exp(-u * u * 6f) * Mathf.Sin(v * Mathf.PI);
                px[y * w + x] = new Color(1, 1, 1, a);
            }
        Save("streak", px, w, h, false, false, 1);
    }

    static void Splash()
    {
        int s = 64; var px = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x + 0.5f) / s * 2 - 1, dy = (y + 0.5f) / s * 2 - 1;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Exp(-Mathf.Pow((r - 0.7f) / 0.1f, 2));
                px[y * s + x] = new Color(1, 1, 1, Mathf.Clamp01(ring) * (r < 1 ? 1 : 0));
            }
        Save("splash", px, s, s, false, false, 1);
    }
}
