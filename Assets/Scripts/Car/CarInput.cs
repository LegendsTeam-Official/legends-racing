using UnityEngine;

namespace Legends
{
    // Keyboard + multitouch input for CarController. Touch buttons are drawn with OnGUI from a
    // procedurally generated circle texture (no asset files). Sizes are relative to the screen short side.
    [RequireComponent(typeof(CarController))]
    public class CarInput : MonoBehaviour
    {
        // The game bootstrap subscribes to switch to the free camera.
        public static System.Action OnToggleFlyRequested;

        public bool forceTouchControls = false;   // show touch buttons on desktop too
        public float steerRampUp = 3.2f;          // per second
        public float steerReturn = 5.5f;          // per second
        public float pedalRamp = 7f;              // per second

        CarController car;
        static Texture2D circleTex;

        // button state (true while any touch is inside)
        bool btnLeft, btnRight, btnGas, btnBrake, btnHand;
        float steerValue, throttleValue, brakeValue;

        // centres (GUI coords, origin top-left) and radii
        Vector2 cLeft, cRight, cGas, cBrake, cHand;
        float rSteer, rGas, rBrake, rHand;
        Rect flyRect;
        int lastW, lastH;

        float U => Mathf.Min(Screen.width, Screen.height) / 100f;
        bool ShowTouch => forceTouchControls || Application.isMobilePlatform;

        void Awake()
        {
            car = GetComponent<CarController>();
            if (circleTex == null) circleTex = MakeCircle(128);
        }

        void OnDisable()
        {
            if (car != null) { car.throttle = 0f; car.brake = 0f; car.steer = 0f; car.handbrake = false; }
            steerValue = throttleValue = brakeValue = 0f;
            btnLeft = btnRight = btnGas = btnBrake = btnHand = false;
        }

        void Layout()
        {
            if (lastW == Screen.width && lastH == Screen.height) return;
            lastW = Screen.width; lastH = Screen.height;
            float u = U, w = Screen.width, h = Screen.height;
            rSteer = 10f * u;
            cLeft = new Vector2(15f * u, h - 16f * u);
            cRight = new Vector2(39f * u, h - 16f * u);
            rGas = 11f * u;
            cGas = new Vector2(w - 15f * u, h - 17f * u);
            rBrake = 9.5f * u;
            cBrake = new Vector2(w - 40f * u, h - 14f * u);
            rHand = 7.5f * u;
            cHand = new Vector2(w - 27f * u, h - 40f * u);
            // left of the existing speed-multiplier button (which spans w-16u .. w-4u, y 4u..11u)
            flyRect = new Rect(3f * u, 8f * u, 12f * u, 7f * u);
        }

        void Update()
        {
            if (car == null) return;
            Layout();
            float dt = Time.unscaledDeltaTime;

            btnLeft = btnRight = btnGas = btnBrake = btnHand = false;
            bool flyTap = false;

            // ---- touches (each button checks every touch) ----
            int tc = Input.touchCount;
            for (int i = 0; i < tc; i++)
            {
                Touch t = Input.GetTouch(i);
                Vector2 p = new Vector2(t.position.x, Screen.height - t.position.y);
                bool active = t.phase != TouchPhase.Ended && t.phase != TouchPhase.Canceled;
                if (t.phase == TouchPhase.Began && flyRect.Contains(p)) flyTap = true;
                if (!active || !ShowTouch) continue;
                CheckButtons(p);
            }
            // mouse acts as a single touch on desktop when no real touches exist
            if (tc == 0)
            {
                Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
                if (Input.GetMouseButtonDown(0) && flyRect.Contains(m)) flyTap = true;
                if (ShowTouch && Input.GetMouseButton(0)) CheckButtons(m);
            }
            if (Input.GetKeyDown(KeyCode.F)) flyTap = true;
            if (flyTap && OnToggleFlyRequested != null) OnToggleFlyRequested();

            // ---- keyboard ----
            float kSteer = 0f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) kSteer -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) kSteer += 1f;
            bool kGas = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            bool kBrake = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            bool kHand = Input.GetKey(KeyCode.Space);

            float steerTarget = kSteer + (btnRight ? 1f : 0f) - (btnLeft ? 1f : 0f);
            steerTarget = Mathf.Clamp(steerTarget, -1f, 1f);

            // smooth steering ramp; returning to centre (or reversing direction) is faster
            float rate = (Mathf.Abs(steerTarget) < 0.01f || Mathf.Sign(steerTarget) != Mathf.Sign(steerValue)) ? steerReturn : steerRampUp;
            steerValue = Mathf.MoveTowards(steerValue, steerTarget, rate * dt);

            throttleValue = Mathf.MoveTowards(throttleValue, (kGas || btnGas) ? 1f : 0f, pedalRamp * dt);
            brakeValue = Mathf.MoveTowards(brakeValue, (kBrake || btnBrake) ? 1f : 0f, pedalRamp * 1.5f * dt);

            car.steer = steerValue;
            car.throttle = throttleValue;
            car.brake = brakeValue;
            car.handbrake = kHand || btnHand;
        }

        void CheckButtons(Vector2 p)
        {
            if (Inside(p, cLeft, rSteer)) btnLeft = true;
            if (Inside(p, cRight, rSteer)) btnRight = true;
            if (Inside(p, cGas, rGas)) btnGas = true;
            if (Inside(p, cBrake, rBrake)) btnBrake = true;
            if (Inside(p, cHand, rHand)) btnHand = true;
        }

        static bool Inside(Vector2 p, Vector2 c, float r)
        {
            return (p - c).sqrMagnitude <= (r * 1.15f) * (r * 1.15f);   // generous hit area
        }

        GUIStyle labelStyle;
        int styleSize;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            Layout();
            float u = U;
            int fs = Mathf.Max(10, (int)(3.6f * u));
            if (labelStyle == null || styleSize != fs)
            {
                labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = fs };
                labelStyle.normal.textColor = Color.white;
                styleSize = fs;
            }
            var old = GUI.color;

            if (ShowTouch)
            {
                DrawButton(cLeft, rSteer, btnLeft, "<", new Color(1f, 1f, 1f));
                DrawButton(cRight, rSteer, btnRight, ">", new Color(1f, 1f, 1f));
                DrawButton(cBrake, rBrake, btnBrake, "BRAKE", new Color(1f, 0.35f, 0.3f));
                DrawButton(cGas, rGas, btnGas, "GAS", new Color(0.35f, 1f, 0.45f));
                DrawButton(cHand, rHand, btnHand, "HB", new Color(1f, 0.75f, 0.2f));
            }

            // FLY toggle (pill)
            GUI.color = new Color(0.1f, 0.12f, 0.16f, 0.55f);
            GUI.DrawTexture(flyRect, circleTex, ScaleMode.StretchToFill);
            GUI.color = new Color(1f, 1f, 1f, 0.9f);
            int ofs = labelStyle.fontSize;
            labelStyle.fontSize = Mathf.Max(9, (int)(3.0f * u));
            GUI.Label(flyRect, "FLY", labelStyle);
            labelStyle.fontSize = ofs;

            GUI.color = old;
        }

        void DrawButton(Vector2 c, float r, bool pressed, string label, Color tint)
        {
            var rect = new Rect(c.x - r, c.y - r, 2f * r, 2f * r);
            GUI.color = new Color(tint.r * 0.25f, tint.g * 0.25f, tint.b * 0.25f, pressed ? 0.55f : 0.32f);
            GUI.DrawTexture(rect, circleTex);
            float ir = r * 0.86f;
            GUI.color = new Color(tint.r, tint.g, tint.b, pressed ? 0.45f : 0.16f);
            GUI.DrawTexture(new Rect(c.x - ir, c.y - ir, 2f * ir, 2f * ir), circleTex);
            GUI.color = new Color(1f, 1f, 1f, pressed ? 1f : 0.8f);
            GUI.Label(rect, label, labelStyle);
        }

        // Anti-aliased filled circle.
        public static Texture2D MakeCircle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[size * size];
            float c = (size - 1) * 0.5f, rad = size * 0.5f - 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(rad - d + 0.5f);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            tex.hideFlags = HideFlags.DontSave;
            return tex;
        }
    }
}
