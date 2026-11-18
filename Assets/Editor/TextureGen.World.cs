using UnityEngine;

// Textures for the open world: 2-lane road, ground, park lawn, paths, water,
// manholes, drains, embankment stone, bridge concrete.
public static partial class TextureGen
{
    static void GenerateWorld()
    {
        Road2(); GroundTex(); Lawn(); PathTex(); WaterNormal(); Manhole(); Drain(); Embankment(); BridgeTex();
        HouseAssets.GenerateTextures();
    }

    // 9 m wide residential road, 24 m tile
    static void Road2()
    {
        int w = 384, h = 1024; float W = 9f, L = 24f;
        var alb = new Color[w * h]; var mask = new Color[w * h]; var hg = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float um = (x + 0.5f) / w * W, vm = (y + 0.5f) / h * L;
                Asphalt(um, vm, W, L, 15, out float a, out float ht, out float pud);
                float wear = Fbm(um / W, vm / L, 48, 128, 3, 87);
                float yel = Mathf.Max(Box(um, 4.32f, 4.44f, 0.01f), Box(um, 4.56f, 4.68f, 0.01f)) * SmoothStep(0.3f, 0.5f, wear);
                float edge = Mathf.Max(Box(um, 0.3f, 0.42f, 0.01f), Box(um, W - 0.42f, W - 0.3f, 0.01f)) * SmoothStep(0.4f, 0.6f, wear) * 0.7f;
                float lanePos = Mathf.Repeat(um, 4.5f);
                float track = Box(lanePos, 0.8f, 1.3f, 0.25f) + Box(lanePos, 3.0f, 3.5f, 0.25f);
                a *= 1f - 0.1f * track;
                float gutter = 1f - SmoothStep(0.3f, 1.2f, Mathf.Min(um, W - um));
                pud = Mathf.Clamp01(pud + gutter * 0.45f * SmoothStep(0.45f, 0.6f, wear));
                Color c = new Color(a, a, a * 1.03f);
                c = Color.Lerp(c, new Color(0.7f, 0.7f, 0.68f), edge);
                c = Color.Lerp(c, new Color(0.72f, 0.52f, 0.12f), yel);
                c *= Mathf.Lerp(1f, 0.55f, pud);
                alb[y * w + x] = c;
                float sm = Mathf.Lerp(0.58f, 0.64f, track);
                sm = Mathf.Lerp(sm, 0.42f, Mathf.Max(edge, yel));
                sm = Mathf.Lerp(sm, 0.9f, pud);
                mask[y * w + x] = new Color(0, 0, 0, sm);
                hg[y * w + x] = Mathf.Lerp(ht + Mathf.Max(edge, yel) * 0.5f, 0.15f, pud);
            }
        Save("road2_albedo", alb, w, h, false);
        Save("road2_mask", mask, w, h, true);
        Save("road2_normal", NormalFromHeight(hg, w, h, 0.9f), w, h, true, true);
    }

    // muddy grass / earth, 8 m tile
    static void GroundTex()
    {
        int s = 512; var alb = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = (x + 0.5f) / s, v = (y + 0.5f) / s;
                float g = Fbm(u, v, 64, 64, 3, 201), m = Fbm(u, v, 4, 4, 4, 207);
                Color grass = new Color(0.06f, 0.075f, 0.04f) * (0.8f + g * 0.5f);
                Color mud = new Color(0.07f, 0.06f, 0.05f) * (0.8f + g * 0.4f);
                alb[y * s + x] = Color.Lerp(grass, mud, SmoothStep(0.45f, 0.65f, m));
            }
        Save("ground_albedo", alb, s, s, false);
    }

    // mown lawn with stripes, 6 m tile
    static void Lawn()
    {
        int s = 512; var alb = new Color[s * s]; var hg = new float[s * s]; var mask = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = (x + 0.5f) / s, v = (y + 0.5f) / s;
                float blades = Fbm(u, v, 128, 128, 2, 211);
                float clump = Fbm(u, v, 8, 8, 3, 213);
                float stripe = Mathf.Sin(u * Mathf.PI * 4f) * 0.5f + 0.5f;
                Color c = new Color(0.07f, 0.11f, 0.045f) * (0.75f + blades * 0.45f) * (0.9f + 0.15f * stripe) * (0.85f + clump * 0.3f);
                alb[y * s + x] = c;
                hg[y * s + x] = blades;
                mask[y * s + x] = new Color(0, 0, 0, 0.45f + 0.15f * clump);
            }
        Save("lawn_albedo", alb, s, s, false);
        Save("lawn_mask", mask, s, s, true);
        Save("lawn_normal", NormalFromHeight(hg, s, s, 1.5f), s, s, true, true);
    }

    // light concrete pavers for park paths, 4 m tile
    static void PathTex()
    {
        int s = 512; var alb = new Color[s * s]; var mask = new Color[s * s]; var hg = new float[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float um = (x + 0.5f) / s * 4f, vm = (y + 0.5f) / s * 4f;
                float row = Mathf.Floor(vm / 0.5f);
                float bu = Mathf.Repeat(um + (row % 2) * 0.5f, 1f), bv = Mathf.Repeat(vm, 0.5f) / 0.5f;
                float joint = 1f - Box(bu, 0.02f, 0.98f, 0.008f) * Box(bv, 0.04f, 0.96f, 0.015f);
                float tone = (Hash(Mathf.FloorToInt(um + (row % 2) * 0.5f), (int)row, 9) - 0.5f) * 0.06f;
                float g = Fbm(um / 4f, vm / 4f, 64, 64, 3, 221);
                float pud = SmoothStep(0.62f, 0.68f, Fbm(um / 4f, vm / 4f, 2, 2, 4, 223));
                float a = (0.33f + tone + (g - 0.5f) * 0.05f) * (1f - joint * 0.45f) * Mathf.Lerp(1f, 0.6f, pud);
                alb[y * s + x] = new Color(a, a * 0.98f, a * 0.94f);
                mask[y * s + x] = new Color(0, 0, 0, Mathf.Lerp(Mathf.Lerp(0.5f, 0.3f, joint), 0.92f, pud));
                hg[y * s + x] = (1 - joint) * 0.5f + g * 0.1f;
            }
        Save("path_albedo", alb, s, s, false);
        Save("path_mask", mask, s, s, true);
        Save("path_normal", NormalFromHeight(hg, s, s, 2f), s, s, true, true);
    }

    static void WaterNormal()
    {
        int s = 512; var hg = new float[s * s]; var alb = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = (x + 0.5f) / s, v = (y + 0.5f) / s;
                hg[y * s + x] = Fbm(u, v, 6, 10, 4, 231) * 0.7f + Fbm(u, v, 24, 40, 2, 233) * 0.3f;
                alb[y * s + x] = Color.white;
            }
        Save("water_normal", NormalFromHeight(hg, s, s, 1.6f), s, s, true, true);
    }

    static void Manhole()
    {
        int s = 128; var alb = new Color[s * s]; var mask = new Color[s * s]; var hg = new float[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x + 0.5f) / s * 2 - 1, dy = (y + 0.5f) / s * 2 - 1, r = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Box(r, 0.86f, 0.98f, 0.01f);
                float pattern = (Mathf.Repeat(dx * 7f, 1f) < 0.5f ^ Mathf.Repeat(dy * 7f, 1f) < 0.5f) ? 1f : 0f;
                float a = 0.07f + 0.04f * pattern + 0.05f * ring;
                alb[y * s + x] = new Color(a, a * 0.95f, a * 0.9f);
                mask[y * s + x] = new Color(0.6f, 0, 0, 0.7f + 0.2f * pattern);
                hg[y * s + x] = pattern * 0.5f + ring;
            }
        Save("manhole_albedo", alb, s, s, false);
        Save("manhole_mask", mask, s, s, true);
        Save("manhole_normal", NormalFromHeight(hg, s, s, 3f), s, s, true, true);
    }

    static void Drain()
    {
        int w = 64, h = 128; var alb = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float v = (y + 0.5f) / h;
                float slot = Box(Mathf.Repeat(v * 9f, 1f), 0.25f, 0.75f, 0.02f) * Box((x + 0.5f) / w, 0.12f, 0.88f, 0.02f);
                float a = Mathf.Lerp(0.09f, 0.01f, slot);
                alb[y * w + x] = new Color(a, a, a);
            }
        Save("drain_albedo", alb, w, h, false);
    }

    // stone blocks, 4 m tile
    static void Embankment()
    {
        int s = 512; var alb = new Color[s * s]; var hg = new float[s * s]; var mask = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float um = (x + 0.5f) / s * 4f, vm = (y + 0.5f) / s * 4f;
                float row = Mathf.Floor(vm / 0.6f);
                float bu = Mathf.Repeat(um + (row % 2) * 0.6f, 1.2f) / 1.2f, bv = Mathf.Repeat(vm, 0.6f) / 0.6f;
                float joint = 1f - Box(bu, 0.02f, 0.98f, 0.01f) * Box(bv, 0.04f, 0.96f, 0.02f);
                float tone = (Hash(Mathf.FloorToInt((um + (row % 2) * 0.6f) / 1.2f), (int)row, 19) - 0.5f) * 0.08f;
                float g = Fbm(um / 4f, vm / 4f, 48, 48, 4, 241);
                float wet = SmoothStep(0.3f, 0.7f, Fbm(um / 4f, vm / 4f, 3, 6, 3, 243));
                float a = (0.2f + tone + (g - 0.5f) * 0.1f) * (1f - joint * 0.5f) * Mathf.Lerp(1f, 0.7f, wet);
                alb[y * s + x] = new Color(a, a * 0.97f, a * 0.92f);
                hg[y * s + x] = (1 - joint) * 0.6f + g * 0.4f;
                mask[y * s + x] = new Color(0, 0, 0, Mathf.Lerp(0.35f, 0.7f, wet));
            }
        Save("embank_albedo", alb, s, s, false);
        Save("embank_mask", mask, s, s, true);
        Save("embank_normal", NormalFromHeight(hg, s, s, 3f), s, s, true, true);
    }

    static void BridgeTex()
    {
        int s = 256; var alb = new Color[s * s];
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = (x + 0.5f) / s, v = (y + 0.5f) / s;
                float g = Fbm(u, v, 32, 32, 4, 251), stain = Fbm(u, v, 3, 3, 3, 253);
                float panel = 1f - Box(Mathf.Repeat(u * 2f, 1f), 0.01f, 0.99f, 0.005f);
                float a = (0.26f + (g - 0.5f) * 0.08f - SmoothStep(0.5f, 0.8f, stain) * 0.08f) * (1f - panel * 0.4f);
                alb[y * s + x] = new Color(a, a * 0.98f, a * 0.95f);
            }
        Save("bridge_albedo", alb, s, s, false);
    }
}
