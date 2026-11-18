using UnityEditor;
using UnityEngine;
using Legends;
using TG = TextureGen;

// Procedural textures + materials for the suburban houses emitted by Legends.HouseBuilder.
// Atlas layouts (siding columns, openings rects, window variant semantics) come from HouseBuilder.
public static class HouseAssets
{
    struct Px { public Color a, e; public float sm, h; }

    static readonly Color TrimCol = new Color(0.78f, 0.77f, 0.73f);

    // siding colours (sRGB), muted American palette; column 7 is brick
    static readonly Color[] SidingColours =
    {
        new Color(0.80f, 0.79f, 0.74f), // white
        new Color(0.62f, 0.64f, 0.63f), // light grey
        new Color(0.50f, 0.58f, 0.64f), // dusty blue
        new Color(0.50f, 0.56f, 0.46f), // sage
        new Color(0.70f, 0.62f, 0.50f), // tan
        new Color(0.78f, 0.72f, 0.52f), // butter yellow
        new Color(0.34f, 0.40f, 0.47f), // slate blue
    };

    static readonly Color[] DoorColours =
    {
        new Color(0.34f, 0.06f, 0.05f), new Color(0.07f, 0.10f, 0.20f), new Color(0.05f, 0.075f, 0.06f), new Color(0.74f, 0.73f, 0.69f)
    };

    public static void GenerateTextures()
    {
        SidingTex();
        OpeningsTex();
        RoofTex();
        LawnTex();
        DriveTex();
        FenceTex();
        HedgeTex();
        AssetDatabase.Refresh();
    }

    public static void CreateMaterials()
    {
        ProjectSetup.Lit("HouseSiding", Color.white, 1f, 0f, "house_siding_albedo", "house_siding_mask", "house_siding_normal");
        ProjectSetup.Lit("HouseOpenings", Color.white, 1f, 0f, "house_open_albedo", "house_open_mask", "house_open_normal", "house_open_emission", 2.0f);
        ProjectSetup.Lit("HouseRoof", new Color(0.55f, 0.55f, 0.57f), 0.5f, 0f, "house_roof_albedo", null, "house_roof_normal");
        ProjectSetup.Lit("HouseRoofBrown", new Color(0.66f, 0.50f, 0.38f), 0.5f, 0f, "house_roof_albedo", null, "house_roof_normal");
        ProjectSetup.Lit("HouseTrim", new Color(0.76f, 0.75f, 0.71f), 0.62f);
        ProjectSetup.Lit("HouseLawn", Color.white, 1f, 0f, "house_lawn_albedo", "house_lawn_mask", "house_lawn_normal");
        ProjectSetup.Lit("HouseDriveway", Color.white, 1f, 0f, "house_drive_albedo", "house_drive_mask", "house_drive_normal");
        ProjectSetup.Lit("HouseHedge", Color.white, 0.35f, 0f, "house_hedge_albedo", null, "house_hedge_normal");
        ProjectSetup.Lit("HousePorchLight", new Color(1f, 0.92f, 0.8f), 0.5f, 0f, null, null, null, null, 6f, new Color(1f, 0.7f, 0.4f));

        // alpha-tested fence (albedo alpha = pickets / bars)
        var fence = ProjectSetup.Lit("HouseFence", Color.white, 0.5f, 0f, "house_fence_albedo");
        fence.SetFloat("_AlphaClip", 1f);
        fence.SetFloat("_Cutoff", 0.5f);
        fence.EnableKeyword("_ALPHATEST_ON");
        fence.SetOverrideTag("RenderType", "TransparentCutout");
        fence.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        EditorUtility.SetDirty(fence);
    }

    static float Sq(float x) => x * x;
    static float Gauss(float dx, float dy, float rx, float ry) => Mathf.Exp(-Sq(dx / rx) - Sq(dy / ry));

    // ---------------- siding atlas: 8 columns x 10.24 m ----------------
    static void SidingTex()
    {
        int w = 1024, h = 1024, cw = w / HouseBuilder.SidingColumns;
        float span = HouseBuilder.SidingSpan, found = HouseBuilder.SidingFoundation;
        var alb = new Color[w * h]; var mask = new Color[w * h]; var hg = new float[w * h];
        float courseH = span / 136f; // brick courses tile exactly over the span
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int col = x / cw;
                float lu = (x % cw + 0.5f) / cw, u = (x + 0.5f) / w, v = (y + 0.5f) / h, vm = v * span;
                Color c; float sm, ht;
                if (col == HouseBuilder.BrickColumn)
                {
                    float bu = lu * cw / HouseBuilder.AtlasPxPerMetre;
                    int course = Mathf.FloorToInt(vm / courseH);
                    float off = (course % 2) * 0.11f;
                    float bx = Mathf.Repeat(bu + off, 0.22f) / 0.22f, by = Mathf.Repeat(vm, courseH) / courseH;
                    float mortar = 1f - TG.Box(by, 0.13f, 0.95f, 0.03f) * TG.Box(bx, 0.04f, 0.97f, 0.02f);
                    float tone = TG.Hash(Mathf.FloorToInt((bu + off) / 0.22f), course, 7);
                    float grain = TG.Fbm(u, v, 128, 128, 2, 9);
                    Color brick = new Color(0.40f, 0.19f, 0.14f) * (0.75f + 0.45f * tone) * (0.9f + 0.2f * grain);
                    c = Color.Lerp(brick, new Color(0.30f, 0.29f, 0.27f), mortar);
                    sm = Mathf.Lerp(0.42f, 0.3f, mortar);
                    ht = (1f - mortar) * 0.5f + grain * 0.15f;
                }
                else
                {
                    float grain = TG.Fbm(u, v, 8, 512, 2, 41);      // streaky along the boards (u is stretched in world)
                    float dirt = TG.Fbm(u, v, 16, 6, 4, 43);
                    if (vm < found - 0.06f)
                    {
                        float cg = TG.Fbm(u, v, 64, 256, 3, 47);
                        c = new Color(0.36f, 0.35f, 0.33f) * (0.8f + 0.3f * cg) * Mathf.Lerp(0.7f, 1f, TG.SmoothStep(0f, 0.25f, vm));
                        sm = 0.45f; ht = cg * 0.3f;
                        if (vm > found - 0.1f) c *= 0.7f;                // shadow under the water table
                    }
                    else if (vm < found)
                    {
                        c = TrimCol * 0.95f; sm = 0.58f; ht = 0.95f;     // water table board
                    }
                    else
                    {
                        float t = Mathf.Repeat((vm - found) / 0.2f, 1f);  // 8" lap boards
                        float shade = 1f - 0.45f * TG.SmoothStep(0.75f, 1f, t);
                        if (t < 0.04f) shade *= 1.06f;
                        c = SidingColours[col] * (0.9f + 0.12f * grain) * shade;
                        c *= 1f - 0.18f * TG.SmoothStep(0.55f, 0.85f, dirt);
                        c *= 1f - 0.15f * (1f - TG.SmoothStep(found, found + 0.8f, vm));   // splash dirt near the ground
                        sm = 0.58f + 0.1f * grain + 0.08f * TG.SmoothStep(0.8f, 1f, t);
                        ht = Mathf.Lerp(1f, 0.15f, t) + grain * 0.03f;
                    }
                }
                c.a = 1f;
                alb[y * w + x] = c; mask[y * w + x] = new Color(0, 0, 0, sm); hg[y * w + x] = ht;
            }
        TG.Save("house_siding_albedo", alb, w, h, false);
        TG.Save("house_siding_mask", mask, w, h, true);
        TG.Save("house_siding_normal", TG.NormalFromHeight(hg, w, h, 3f), w, h, true, true);
    }

    // ---------------- openings atlas: windows, picture windows, doors, garage doors ----------------
    static void OpeningsTex()
    {
        int S = HouseBuilder.AtlasSize;
        var alb = new Color[S * S]; var emi = new Color[S * S]; var mask = new Color[S * S]; var hg = new float[S * S];
        for (int i = 0; i < alb.Length; i++) { alb[i] = TrimCol; emi[i] = Color.black; mask[i] = new Color(0, 0, 0, 0.55f); hg[i] = 0.75f; }
        Paint(HouseBuilder.Opening.Window, HouseBuilder.WindowVariants, alb, emi, mask, hg);
        Paint(HouseBuilder.Opening.Picture, HouseBuilder.PictureVariants, alb, emi, mask, hg);
        Paint(HouseBuilder.Opening.Door, HouseBuilder.DoorVariants, alb, emi, mask, hg);
        Paint(HouseBuilder.Opening.GarageSingle, HouseBuilder.GarageVariants, alb, emi, mask, hg);
        Paint(HouseBuilder.Opening.GarageDouble, HouseBuilder.GarageVariants, alb, emi, mask, hg);
        TG.Save("house_open_albedo", alb, S, S, false);
        TG.Save("house_open_emission", emi, S, S, false);
        TG.Save("house_open_mask", mask, S, S, true);
        TG.Save("house_open_normal", TG.NormalFromHeight(hg, S, S, 3f), S, S, true, true);
    }

    static void Paint(HouseBuilder.Opening kind, int count, Color[] alb, Color[] emi, Color[] mask, float[] hg)
    {
        int S = HouseBuilder.AtlasSize; float ppm = HouseBuilder.AtlasPxPerMetre;
        for (int v = 0; v < count; v++)
        {
            RectInt r = HouseBuilder.OpeningRect(kind, v);
            float W = r.width / ppm, H = r.height / ppm;
            for (int py = 0; py < r.height; py++)
                for (int px = 0; px < r.width; px++)
                {
                    float lx = (px + 0.5f) / ppm, ly = (py + 0.5f) / ppm;
                    Px p;
                    switch (kind)
                    {
                        case HouseBuilder.Opening.Window: p = Window(v, lx, ly, W, H); break;
                        case HouseBuilder.Opening.Picture: p = Picture(v, lx, ly, W, H); break;
                        case HouseBuilder.Opening.Door: p = Door(v, lx, ly, W, H); break;
                        default: p = Garage(kind == HouseBuilder.Opening.GarageDouble, v, lx, ly, W, H); break;
                    }
                    int idx = (r.y + py) * S + r.x + px;
                    p.a.a = 1f; p.e.a = 1f;
                    alb[idx] = p.a; emi[idx] = p.e; mask[idx] = new Color(0, 0, 0, p.sm); hg[idx] = p.h;
                }
        }
    }

    static Px TrimPx(float h)
    {
        Px p; p.a = TrimCol; p.e = Color.black; p.sm = 0.55f; p.h = h; return p;
    }

    // frame trim around a rectangular opening of size W x H with border t; returns false when inside
    static bool Frame(float lx, float ly, float W, float H, float t, out Px p)
    {
        p = TrimPx(0.75f);
        if (lx >= t && lx <= W - t && ly >= t && ly <= H - t) return false;
        if (ly < 0.07f) { p.h = 0.95f; p.a = TrimCol * 0.92f; }                 // sill nose
        float d = Mathf.Max(Mathf.Max(t - lx, lx - (W - t)), Mathf.Max(t - ly, ly - (H - t)));
        if (d < 0.015f) { p.a *= 0.7f; p.h = 0.5f; }                             // shadow line at the sash
        return true;
    }

    // interior seen through glass. gx, gy in 0..1 across the glass
    static Px Glass(int state, int cover, float gx, float gy, float seed)
    {
        Px p; p.sm = 0.95f; p.h = 0f;
        float s = Mathf.Repeat(seed, 1f);
        Color a = new Color(0.02f, 0.024f, 0.03f) * (0.8f + 0.6f * gy);          // dark glass, faint sky reflection
        Color e = Color.black;
        if (state > 0)
        {
            Color tint = state == 1 ? new Color(1f, 0.6f, 0.3f) : state == 3 ? new Color(0.78f, 0.86f, 1f) :
                         state == 4 ? new Color(1f, 0.5f, 0.2f) : new Color(0.14f, 0.09f, 0.06f);
            float room = 0.45f + 0.55f * gy;                                     // ceiling light falloff
            float furn = 1f - 0.6f * TG.Box(gx, 0.1f + s * 0.3f, 0.55f + s * 0.3f, 0.04f) * TG.Box(gy, -0.1f, 0.3f, 0.04f);
            float lamp = Gauss(gx - (0.2f + s * 0.6f), gy - 0.45f, 0.12f, 0.16f);
            e = tint * room * furn;
            if (state == 4) e = tint * (0.25f + 0.3f * gy) * furn + new Color(1f, 0.62f, 0.3f) * lamp * 0.9f;
            if (state == 1 && s > 0.5f) e += new Color(1f, 0.7f, 0.4f) * lamp * 0.5f;
            if (state == 2)
            {
                float tv = Gauss(gx - (0.3f + s * 0.4f), gy - 0.3f, 0.35f, 0.3f);
                e = tint * room * furn + new Color(0.22f, 0.4f, 1f) * (0.2f + 0.8f * tv) * (0.85f + 0.15f * Mathf.Sin(gy * 37f + s * 9f));
            }
            a = Color.Lerp(a, e * 0.12f, 0.6f);
        }
        if (cover == 1) // side curtains
        {
            float c = 1f - TG.Box(gx, 0.2f, 0.8f, 0.03f);
            float fold = 0.7f + 0.3f * Mathf.Sin(gx * 70f);
            Color fab = Color.Lerp(new Color(0.55f, 0.3f, 0.18f), new Color(0.5f, 0.45f, 0.35f), s);
            a = Color.Lerp(a, fab * 0.25f * fold, c);
            e = Color.Lerp(e, e * fab * 0.9f * fold, c);
        }
        else if (cover == 4) // curtains drawn, glowing fabric
        {
            float fold = 0.72f + 0.28f * Mathf.Sin(gx * 55f + s * 3f);
            Color fab = Color.Lerp(new Color(0.95f, 0.6f, 0.38f), new Color(0.9f, 0.82f, 0.62f), s);
            a = fab * 0.22f * fold;
            e = e * fab * 0.75f * fold;
        }
        else if (cover == 2 || cover == 3) // venetian blinds, upper half or closed
        {
            float b = cover == 3 ? 1f : TG.SmoothStep(0.46f, 0.5f, gy);
            float slat = 0.55f + 0.45f * TG.SmoothStep(0.2f, 0.6f, Mathf.Repeat(gy * 24f, 1f));
            a = Color.Lerp(a, new Color(0.42f, 0.41f, 0.38f) * 0.35f * slat, b);
            e = Color.Lerp(e, e * 0.5f * slat, b);
        }
        p.a = a; p.e = e;
        return p;
    }

    // double-hung window 1.28 x 1.76 m
    static Px Window(int v, float lx, float ly, float W, float H)
    {
        const float t = 0.1f, f = 0.045f;
        if (Frame(lx, ly, W, H, t, out Px p)) return p;
        float sx = lx - t, sy = ly - t, sw = W - 2 * t, sh = H - 2 * t;
        if (sx < f || sx > sw - f || sy < f || sy > sh - f || Mathf.Abs(sy - sh * 0.5f) < 0.028f)
        {
            p = TrimPx(0.45f); p.a = TrimCol * 0.94f; return p;               // sash frames + meeting rail
        }
        float gw = sw - 2 * f, ghh = sh - 2 * f, gx = (sx - f) / gw, gy = (sy - f) / ghh;
        if (HouseBuilder.WindowGrille[v] &&
            (Mathf.Abs(gx - 0.5f) * gw < 0.012f || Mathf.Abs(gy - 0.25f) * ghh < 0.012f || Mathf.Abs(gy - 0.75f) * ghh < 0.012f))
        {
            p = TrimPx(0.3f); p.a = TrimCol * 0.9f; p.sm = 0.6f; return p;    // muntins
        }
        return Glass(HouseBuilder.WindowState[v], HouseBuilder.WindowCover[v], gx, gy, v * 0.137f + 0.05f);
    }

    // picture window 2.56 x 1.92 m: fixed centre pane flanked by two double-hung sashes
    static Px Picture(int v, float lx, float ly, float W, float H)
    {
        const float t = 0.1f, f = 0.05f;
        if (Frame(lx, ly, W, H, t, out Px p)) return p;
        float sx = lx - t, sy = ly - t, sw = W - 2 * t, sh = H - 2 * t, gx = sx / sw;
        bool mull = Mathf.Abs(sx - sw * 0.25f) < 0.035f || Mathf.Abs(sx - sw * 0.75f) < 0.035f;
        bool rail = (gx < 0.25f || gx > 0.75f) && Mathf.Abs(sy - sh * 0.5f) < 0.025f;
        if (sx < f || sx > sw - f || sy < f || sy > sh - f || mull || rail)
        {
            p = TrimPx(0.45f); p.a = TrimCol * 0.94f; return p;
        }
        int state = v == 0 ? 0 : v == 2 ? 2 : 1;
        p = Glass(state, v == 1 ? 1 : 0, gx, (sy - f) / (sh - 2 * f), 0.31f + v * 0.2f);
        if (v == 3)
        {
            float fold = 0.8f + 0.2f * Mathf.Sin(gx * 90f);
            p.e *= 0.7f * fold;
            p.a = Color.Lerp(p.a, new Color(0.5f, 0.48f, 0.44f) * 0.2f, 0.6f);
        }
        return p;
    }

    // recessed / raised panel: -1 outside, else 0 at the edge .. 1 on the raised field
    static float PanelRamp(float x, float y, float x0, float x1, float y0, float y1, float bev)
    {
        if (x < x0 || x > x1 || y < y0 || y > y1) return -1f;
        float d = Mathf.Min(Mathf.Min(x - x0, x1 - x), Mathf.Min(y - y0, y1 - y));
        return Mathf.Clamp01(d / bev);
    }

    // front door 1.28 x 2.56 m: leaf 0.96 x 2.1 with transom above
    static Px Door(int v, float lx, float ly, float W, float H)
    {
        const float x0 = 0.16f, top = 2.1f;
        float x1 = W - 0.16f;
        if (lx >= x0 && lx <= x1 && ly <= top)
        {
            float mx = lx - x0, lw = x1 - x0;
            float grain = TG.Fbm(lx / W, ly / H, 4, 32, 3, 71 + v);
            Px p; p.a = DoorColours[v] * (0.92f + 0.12f * grain); p.e = Color.black; p.sm = 0.62f; p.h = 0.55f;
            if (v == 3 && mx > 0.12f && mx < lw - 0.12f && ly > 1.3f && ly < 1.98f)
            {
                float gx = (mx - 0.12f) / (lw - 0.24f), gy = (ly - 1.3f) / 0.68f;
                if (Mathf.Abs(gx - 0.5f) < 0.025f || Mathf.Abs(gy - 0.5f) < 0.035f) { p.h = 0.45f; return p; }
                return Glass(1, 0, gx, gy, 0.77f);
            }
            float ramp = -1f;
            for (int c = 0; c < 2; c++)
            {
                float a0 = c == 0 ? 0.12f : lw * 0.5f + 0.04f, a1 = c == 0 ? lw * 0.5f - 0.04f : lw - 0.12f;
                ramp = Mathf.Max(ramp, PanelRamp(mx, ly, a0, a1, 0.12f, 0.62f, 0.035f));
                ramp = Mathf.Max(ramp, PanelRamp(mx, ly, a0, a1, 0.74f, 1.22f, 0.035f));
                if (v != 3) ramp = Mathf.Max(ramp, PanelRamp(mx, ly, a0, a1, 1.36f, 1.98f, 0.035f));
            }
            if (ramp >= 0f) p.h = 0.3f + 0.3f * ramp;
            float kx = lw - 0.09f;
            if (Sq(mx - kx) + Sq(ly - 0.95f) < Sq(0.03f) || Sq(mx - kx) + Sq(ly - 1.15f) < Sq(0.022f))
            {
                p.a = new Color(0.55f, 0.42f, 0.2f); p.sm = 0.85f; p.h = 1f;      // knob + deadbolt
            }
            return p;
        }
        if (ly > 2.17f && ly < 2.40f && lx > 0.2f && lx < W - 0.2f)              // transom
        {
            float gx = (lx - 0.2f) / (W - 0.4f), gy = (ly - 2.17f) / 0.23f;
            if (Mathf.Abs(gx - 0.333f) < 0.012f || Mathf.Abs(gx - 0.667f) < 0.012f) return TrimPx(0.4f);
            return Glass(v == 2 ? 0 : 4, 0, gx, 0.6f + gy * 0.4f, 0.5f);
        }
        var t = TrimPx(0.75f);
        if (lx > x0 - 0.03f && lx < x1 + 0.03f && ly < top + 0.03f) { t.a *= 0.4f; t.h = 0.2f; }   // jamb shadow
        if (ly > 2.14f && ly < 2.43f && lx > 0.17f && lx < W - 0.17f) { t.a *= 0.7f; t.h = 0.45f; } // transom frame
        return t;
    }

    // sectional garage door; single 2.56 x 2.56 m (door 2.32 x 2.3), double 5.12 x 2.24 m (door 4.88 x 2.12)
    static Px Garage(bool dbl, int v, float lx, float ly, float W, float H)
    {
        const float t = 0.12f;
        float top = dbl ? H - 0.12f : H - 0.26f;
        if (lx < t || lx > W - t || ly > top)
        {
            var q = TrimPx(0.75f);
            if (lx > t - 0.03f && lx < W - t + 0.03f && ly < top + 0.03f) { q.a *= 0.4f; q.h = 0.2f; }
            return q;
        }
        float mx = lx - t, dw = W - 2 * t, secH = top / 4f;
        int si = Mathf.Min(3, (int)(ly / secH));
        float sy = ly - si * secH;
        Color baseC = v == 0 ? new Color(0.74f, 0.73f, 0.69f) : (dbl ? new Color(0.25f, 0.16f, 0.10f) : new Color(0.42f, 0.30f, 0.19f));
        float grain = v == 1 ? TG.Fbm(lx / W, ly / H, 4, 96, 3, 91) : TG.Fbm(lx / W, ly / H, 32, 32, 2, 93);
        Px p; p.a = baseC * (0.88f + 0.18f * grain); p.e = Color.black; p.sm = 0.6f; p.h = 0.55f;
        if (ly < 0.03f) { p.a = new Color(0.03f, 0.03f, 0.03f); p.h = 0.3f; p.sm = 0.4f; return p; }   // rubber seal
        if (sy < 0.015f || sy > secH - 0.015f) { p.a *= 0.35f; p.h = 0.1f; return p; }                 // section joints
        int np = v == 0 ? (dbl ? 8 : 4) : (dbl ? 4 : 2);
        float pw = dw / np;
        int pi = Mathf.Min(np - 1, (int)(mx / pw));
        float ramp = PanelRamp(mx - pi * pw, sy, 0.07f, pw - 0.07f, 0.08f, secH - 0.08f, 0.04f);
        if (v == 0 && si == 3 && ramp >= 0f)
        {
            float gx = (mx - pi * pw - 0.07f) / (pw - 0.14f), gy = (sy - 0.08f) / (secH - 0.16f);
            if (gx > 0.06f && gx < 0.94f && gy > 0.1f && gy < 0.9f) return Glass(0, 0, gx, gy, 0.2f);   // top-row lites
        }
        if (ramp >= 0f) p.h = 0.3f + 0.3f * ramp;
        return p;
    }

    // ---------------- roof shingles (4 m tile, tinted by material colour) ----------------
    static void RoofTex()
    {
        int S = 512; float T = HouseBuilder.RoofTile, course = T / 28f, tab = T / 12f;
        var alb = new Color[S * S]; var hg = new float[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S, um = u * T, vm = v * T;
                int ci = Mathf.FloorToInt(vm / course);
                float t = vm / course - ci;
                float off = TG.Hash(ci % 28, 0, 3) * tab;
                float su = Mathf.Repeat(um + off, T);
                int ti = Mathf.FloorToInt(su / tab) % 12;
                float tx = su / tab - Mathf.Floor(su / tab);
                float tone = TG.Hash(ti, ci % 28, 5);
                float gran = TG.Fbm(u, v, 128, 128, 2, 3);
                float streak = TG.Fbm(u, v, 3, 2, 3, 9);
                float slot = (tx < 0.015f || tx > 0.985f) && t < 0.8f ? 1f : 0f;
                float shade = 1f - 0.5f * TG.SmoothStep(0.78f, 1f, t);
                float a = 0.32f * (0.75f + 0.5f * tone) * (0.85f + 0.3f * gran) * shade * (1f - 0.2f * TG.SmoothStep(0.5f, 0.8f, streak));
                a *= 1f - 0.6f * slot;
                alb[y * S + x] = new Color(a, a * 0.99f, a * 0.97f, 1f);
                hg[y * S + x] = (1f - t) * 0.5f + gran * 0.25f - slot * 0.3f;
            }
        TG.Save("house_roof_albedo", alb, S, S, false);
        TG.Save("house_roof_normal", TG.NormalFromHeight(hg, S, S, 2.5f), S, S, true, true);
    }

    // ---------------- wet lawn (4 m tile) ----------------
    static void LawnTex()
    {
        int S = 512;
        var alb = new Color[S * S]; var mask = new Color[S * S]; var hg = new float[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                float g1 = TG.Fbm(u, v, 128, 128, 2, 11), g2 = TG.Fbm(u, v, 32, 32, 3, 12), clumps = TG.Fbm(u, v, 8, 8, 3, 13);
                float pud = TG.SmoothStep(0.6f, 0.66f, TG.Fbm(u, v, 3, 3, 4, 14)) * 0.8f;
                Color c = Color.Lerp(new Color(0.05f, 0.085f, 0.03f), new Color(0.09f, 0.12f, 0.045f), g2) * (0.7f + 0.6f * g1);
                c = Color.Lerp(c, new Color(0.11f, 0.09f, 0.05f), TG.SmoothStep(0.6f, 0.75f, clumps) * 0.5f);
                c *= Mathf.Lerp(1f, 0.5f, pud); c.a = 1f;
                alb[y * S + x] = c;
                mask[y * S + x] = new Color(0, 0, 0, Mathf.Lerp(0.3f + 0.15f * g1, 0.88f, pud));
                hg[y * S + x] = Mathf.Lerp(g1 * 0.7f + g2 * 0.3f, 0.3f, pud);
            }
        TG.Save("house_lawn_albedo", alb, S, S, false);
        TG.Save("house_lawn_mask", mask, S, S, true);
        TG.Save("house_lawn_normal", TG.NormalFromHeight(hg, S, S, 2f), S, S, true, true);
    }

    // ---------------- driveway concrete (4 m tile, joints every 2 m) ----------------
    static void DriveTex()
    {
        int S = 512; float T = HouseBuilder.DriveTile;
        var alb = new Color[S * S]; var mask = new Color[S * S]; var hg = new float[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S, um = u * T, vm = v * T;
                float joint = 1f - TG.Box(Mathf.Repeat(um, 2f), 0.012f, 1.988f, 0.004f) * TG.Box(Mathf.Repeat(vm, 2f), 0.012f, 1.988f, 0.004f);
                float broom = TG.Fbm(u, v, 8, 256, 2, 21);
                float grain = TG.Fbm(u, v, 64, 64, 3, 22);
                float stains = TG.Fbm(u, v, 4, 4, 4, 23);
                float oil = TG.SmoothStep(0.62f, 0.72f, TG.Fbm(u, v, 2, 3, 3, 24));
                float pud = TG.SmoothStep(0.6f, 0.66f, TG.Fbm(u, v, 2, 2, 4, 25));
                float a = 0.30f * (0.85f + 0.2f * grain) * (0.95f + 0.1f * broom) * (1f - 0.25f * TG.SmoothStep(0.5f, 0.8f, stains))
                          * (1f - 0.45f * oil) * (1f - 0.6f * joint) * Mathf.Lerp(1f, 0.6f, pud);
                alb[y * S + x] = new Color(a, a * 0.99f, a * 0.97f, 1f);
                float sm = Mathf.Lerp(0.48f + 0.1f * oil, 0.93f, pud);
                mask[y * S + x] = new Color(0, 0, 0, Mathf.Lerp(sm, 0.3f, joint));
                hg[y * S + x] = Mathf.Lerp(grain * 0.3f + broom * 0.15f, 0.25f, pud) - joint * 0.6f;
            }
        TG.Save("house_drive_albedo", alb, S, S, false);
        TG.Save("house_drive_mask", mask, S, S, true);
        TG.Save("house_drive_normal", TG.NormalFromHeight(hg, S, S, 2.5f), S, S, true, true);
    }

    // ---------------- fence (alpha-tested): row 0 white picket, row 1 black metal; 3.6 m x 1 m each ----------------
    static void FenceTex()
    {
        int w = 512, h = 256; float T = HouseBuilder.FenceTile;
        var alb = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int row = y / 128;
                float um = (x + 0.5f) / w * T, ly = (y % 128 + 0.5f) / 128f;
                float grain = TG.Fbm((x + 0.5f) / w, (y + 0.5f) / h, 32, 64, 2, 61);
                Color c; bool solid;
                if (row == 0)
                {
                    float px = Mathf.Repeat(um, 0.15f);
                    float tip = 0.86f + 0.06f * (1f - Mathf.Abs(px - 0.075f) / 0.045f);
                    bool picket = px > 0.03f && px < 0.12f && ly > 0.02f && ly < tip;
                    bool rail = (ly > 0.18f && ly < 0.25f) || (ly > 0.66f && ly < 0.73f);
                    solid = picket || rail;
                    c = new Color(0.76f, 0.75f, 0.71f) * (0.88f + 0.15f * grain) * (picket ? 1f : 0.6f);
                    c *= Mathf.Lerp(0.7f, 1f, TG.SmoothStep(0f, 0.25f, ly));            // splash dirt
                }
                else
                {
                    float bx = Mathf.Repeat(um, 0.12f), px2 = Mathf.Repeat(um + 0.03f, 1.8f);
                    bool post = px2 < 0.06f && ly < 0.97f;
                    bool bar = bx > 0.049f && bx < 0.071f && ly > 0.04f && ly < 0.93f;
                    bool finial = Mathf.Abs(bx - 0.06f) < 0.02f && ly >= 0.93f && ly < 0.95f;
                    bool rail = (ly > 0.1f && ly < 0.14f) || (ly > 0.84f && ly < 0.89f);
                    solid = post || bar || finial || rail;
                    c = new Color(0.035f, 0.035f, 0.04f) * (0.9f + 0.2f * grain);
                }
                c.a = solid ? 1f : 0f;
                alb[y * w + x] = c;
            }
        TG.Save("house_fence_albedo", alb, w, h, false);
        var imp = AssetImporter.GetAtPath($"{TextureGen.Dir}/house_fence_albedo.png") as TextureImporter;
        if (imp != null)
        {
            imp.alphaIsTransparency = true;
            imp.mipMapsPreserveCoverage = true;
            imp.alphaTestReferenceValue = 0.5f;
            imp.SaveAndReimport();
        }
    }

    // ---------------- hedge leaves (2 m tile) ----------------
    static void HedgeTex()
    {
        int S = 256;
        var alb = new Color[S * S]; var hg = new float[S * S];
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                float leaves = TG.Fbm(u, v, 32, 32, 3, 31), fine = TG.Fbm(u, v, 96, 96, 2, 32);
                Color c = Color.Lerp(new Color(0.02f, 0.04f, 0.015f), new Color(0.07f, 0.12f, 0.04f), TG.SmoothStep(0.3f, 0.7f, leaves)) * (0.75f + 0.5f * fine);
                c.a = 1f;
                alb[y * S + x] = c;
                hg[y * S + x] = leaves * 0.7f + fine * 0.3f;
            }
        TG.Save("house_hedge_albedo", alb, S, S, false);
        TG.Save("house_hedge_normal", TG.NormalFromHeight(hg, S, S, 4f), S, S, true, true);
    }
}
