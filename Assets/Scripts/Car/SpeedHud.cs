using UnityEngine;

namespace Legends
{
    // Top-right speed readout ("112 km/h") with a slanted orange accent bar and the gear letter.
    // Sits below the top-right button row (speed-multiplier button: x = W-16u..W-4u, y = 4u..11u; FLY: W-30u..W-18u).
    public class SpeedHud : MonoBehaviour
    {
        public CarController car;
        public Color accent = new Color(1f, 0.55f, 0.1f, 0.95f);

        static Texture2D slantTex;
        GUIStyle speedStyle, unitStyle, gearStyle;
        int builtFor;
        float shownSpeed;

        void Awake()
        {
            if (car == null) car = GetComponent<CarController>();
            if (slantTex == null) slantTex = MakeSlant(128, 32, 0.35f);
        }

        void Update()
        {
            if (car == null) return;
            shownSpeed = Mathf.Lerp(shownSpeed, car.SpeedKmh, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f));
        }

        void OnGUI()
        {
            if (car == null || Event.current.type != EventType.Repaint) return;
            float u = Mathf.Min(Screen.width, Screen.height) / 100f;
            int key = (int)(u * 100f);
            if (speedStyle == null || builtFor != key)
            {
                builtFor = key;
                speedStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, (int)(9f * u)), fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.LowerRight };
                speedStyle.normal.textColor = Color.white;
                unitStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(9, (int)(3.2f * u)), fontStyle = FontStyle.Italic, alignment = TextAnchor.LowerLeft };
                unitStyle.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
                gearStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(10, (int)(4.6f * u)), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                gearStyle.normal.textColor = new Color(0.08f, 0.06f, 0.04f, 1f);
            }

            float W = Screen.width;
            float top = 13f * u;                     // below the button row (which ends at 11u)
            float right = W - 4f * u;
            var old = GUI.color;

            // speed number + unit
            int spd = Mathf.RoundToInt(shownSpeed);
            Rect unitRect = new Rect(right - 11f * u, top, 11f * u, 10f * u);
            Rect numRect = new Rect(right - 42f * u, top - 1f * u, 30f * u, 11f * u);
            // drop shadow
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.Label(new Rect(numRect.x + 0.4f * u, numRect.y + 0.4f * u, numRect.width, numRect.height), spd.ToString(), speedStyle);
            GUI.color = Color.white;
            GUI.Label(numRect, spd.ToString(), speedStyle);
            GUI.Label(new Rect(unitRect.x + 0.6f * u, unitRect.y - 1.2f * u, unitRect.width, unitRect.height), "km/h", unitStyle);

            // slanted accent bar under the number
            Rect bar = new Rect(right - 34f * u, top + 10.2f * u, 30f * u, 1.6f * u);
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(bar.x + 0.3f * u, bar.y + 0.3f * u, bar.width, bar.height), slantTex, ScaleMode.StretchToFill);
            GUI.color = accent;
            GUI.DrawTexture(bar, slantTex, ScaleMode.StretchToFill);
            // fill showing rpm
            float rpmN = Mathf.InverseLerp(car.idleRpm, car.redlineRpm, car.Rpm);
            GUI.color = new Color(1f, 0.9f, 0.6f, 0.9f);
            GUI.DrawTexture(new Rect(bar.x, bar.y + bar.height * 0.25f, bar.width * rpmN, bar.height * 0.5f), slantTex, ScaleMode.StretchToFill);

            // gear badge (slanted orange box)
            Rect gearRect = new Rect(right - 42f * u - 8.5f * u, top + 2f * u, 7.5f * u, 7f * u);
            GUI.color = accent;
            GUI.DrawTexture(gearRect, slantTex, ScaleMode.StretchToFill);
            GUI.color = Color.white;
            GUI.Label(gearRect, GearLetter(car.Gear), gearStyle);

            GUI.color = old;
        }

        static string GearLetter(int g)
        {
            if (g < 0) return "R";
            if (g == 0) return "N";
            return g.ToString();
        }

        // Parallelogram (slanted) white texture with anti-aliased edges.
        static Texture2D MakeSlant(int w, int h, float slant)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[w * h];
            float off = slant * h;     // horizontal shift top vs bottom, in pixels
            for (int y = 0; y < h; y++)
            {
                float shift = off * ((float)y / (h - 1));     // texture y=0 is bottom -> top shifted right ("/")
                float x0 = shift, x1 = w - 1 - off + shift;
                for (int x = 0; x < w; x++)
                {
                    float a = Mathf.Clamp01(x - x0 + 0.5f) * Mathf.Clamp01(x1 - x + 0.5f);
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.hideFlags = HideFlags.DontSave;
            return tex;
        }
    }
}
