using UnityEngine;

namespace Legends
{
    // Fly camera. Phone: left stick = move, drag right side = look, buttons = up/down/speed.
    // PC: WASD + Q/E, hold right mouse to look, Shift = fast, wheel = speed.
    public class FreeCamera : MonoBehaviour
    {
        public float[] speeds = { 6f, 18f, 45f };
        public int speedIndex = 1;
        public float lookSensitivity = 0.12f;

        float yaw, pitch;
        int moveFinger = -1, lookFinger = -1;
        Vector2 moveOrigin, moveVec, lastLook;
        float vertical;
        Texture2D circle;

        // UI rects in screen pixels (GUI space: y down)
        Rect upBtn, downBtn, speedBtn;

        public static bool CarAvailable;
        Rect carBtn;

        void OnEnable()
        {
            var e = transform.eulerAngles; yaw = e.y; pitch = e.x > 180 ? e.x - 360 : e.x;
            moveFinger = lookFinger = -1; moveVec = Vector2.zero;
            if (circle == null) circle = MakeCircle(128);
        }

        static Texture2D MakeCircle(int s)
        {
            var t = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f) / s * 2 - 1, dy = (y + 0.5f) / s * 2 - 1;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float ring = Mathf.Clamp01(1 - Mathf.Abs(r - 0.92f) / 0.06f);
                    float fill = r < 0.92f ? 0.18f : 0f;
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Max(ring, fill)));
                }
            t.Apply();
            return t;
        }

        float U => Mathf.Min(Screen.width, Screen.height) / 100f; // 1% of short side

        void LayoutButtons()
        {
            float s = 12 * U, m = 4 * U;
            upBtn = new Rect(Screen.width - m - s, Screen.height - m - 2 * s - U * 2, s, s);
            downBtn = new Rect(Screen.width - m - s, Screen.height - m - s, s, s);
            speedBtn = new Rect(Screen.width - m - s, m, s, s * 0.6f);
            carBtn = new Rect(3f * U, 8f * U, 12f * U, 7f * U);
        }

        static Vector2 GuiPos(Vector2 touch) => new Vector2(touch.x, Screen.height - touch.y);

        void Update()
        {
            LayoutButtons();
            float dt = Time.unscaledDeltaTime;
            Vector3 move = Vector3.zero; vertical = 0;

            // ---- touch ----
            for (int i = 0; i < Input.touchCount; i++)
            {
                var t = Input.GetTouch(i);
                Vector2 g = GuiPos(t.position);
                if (t.phase == TouchPhase.Began)
                {
                    if (speedBtn.Contains(g)) { speedIndex = (speedIndex + 1) % speeds.Length; continue; }
                    if (CarAvailable && carBtn.Contains(g)) { CarInput.OnToggleFlyRequested?.Invoke(); return; }
                    if (upBtn.Contains(g) || downBtn.Contains(g)) continue;
                    if (t.position.x < Screen.width * 0.45f && moveFinger < 0) { moveFinger = t.fingerId; moveOrigin = t.position; moveVec = Vector2.zero; }
                    else if (lookFinger < 0) { lookFinger = t.fingerId; lastLook = t.position; }
                }
                if (upBtn.Contains(g) && t.phase != TouchPhase.Ended) vertical = 1;
                if (downBtn.Contains(g) && t.phase != TouchPhase.Ended) vertical = -1;
                if (t.fingerId == moveFinger)
                {
                    float maxR = 11 * U;
                    moveVec = Vector2.ClampMagnitude((t.position - moveOrigin) / maxR, 1f);
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) { moveFinger = -1; moveVec = Vector2.zero; }
                }
                if (t.fingerId == lookFinger)
                {
                    Vector2 d = t.position - lastLook; lastLook = t.position;
                    float k = lookSensitivity * 100f / Mathf.Max(1f, Screen.dpi > 0 ? Screen.dpi : 300f);
                    yaw += d.x * k * 2.2f; pitch -= d.y * k * 2.2f;
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) lookFinger = -1;
                }
            }
            move += new Vector3(moveVec.x, 0, moveVec.y);

            // ---- keyboard / mouse ----
            if (CarAvailable && Input.GetKeyDown(KeyCode.F)) { CarInput.OnToggleFlyRequested?.Invoke(); return; }
            if (Input.GetKey(KeyCode.W)) move.z += 1;
            if (Input.GetKey(KeyCode.S)) move.z -= 1;
            if (Input.GetKey(KeyCode.D)) move.x += 1;
            if (Input.GetKey(KeyCode.A)) move.x -= 1;
            if (Input.GetKey(KeyCode.E)) vertical = 1;
            if (Input.GetKey(KeyCode.Q)) vertical = -1;
            if (Input.GetMouseButton(1) && Input.touchCount == 0)
            {
                yaw += Input.GetAxis("Mouse X") * 2.5f;
                pitch -= Input.GetAxis("Mouse Y") * 2.5f;
            }
            float wheel = Input.mouseScrollDelta.y;
            if (wheel > 0) speedIndex = Mathf.Min(speeds.Length - 1, speedIndex + 1);
            if (wheel < 0) speedIndex = Mathf.Max(0, speedIndex - 1);

            pitch = Mathf.Clamp(pitch, -85f, 85f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);

            float speed = speeds[speedIndex] * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f);
            Vector3 fwd = transform.forward, right = transform.right;
            Vector3 v = (fwd * move.z + right * move.x) * speed + Vector3.up * vertical * speed * 0.6f;
            Vector3 p = transform.position + v * dt;
            p.y = Mathf.Max(p.y, 0.5f);
            transform.position = p;
        }

        void OnGUI()
        {
            if (!enabled) return;
            var old = GUI.color;
            // joystick
            if (moveFinger >= 0)
            {
                float r = 11 * U;
                Vector2 o = GuiPos(moveOrigin);
                GUI.color = new Color(1, 1, 1, 0.5f);
                GUI.DrawTexture(new Rect(o.x - r, o.y - r, 2 * r, 2 * r), circle);
                Vector2 k = o + new Vector2(moveVec.x, -moveVec.y) * r;
                GUI.color = new Color(1, 1, 1, 0.85f);
                GUI.DrawTexture(new Rect(k.x - r * 0.4f, k.y - r * 0.4f, r * 0.8f, r * 0.8f), circle);
            }
            else if (Application.isMobilePlatform)
            {
                float r = 9 * U; Vector2 o = new Vector2(16 * U, Screen.height - 18 * U);
                GUI.color = new Color(1, 1, 1, 0.25f);
                GUI.DrawTexture(new Rect(o.x - r, o.y - r, 2 * r, 2 * r), circle);
            }
            if (Application.isMobilePlatform)
            {
                var st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = (int)(4 * U), fontStyle = FontStyle.Bold };
                GUI.color = new Color(1, 1, 1, 0.55f);
                GUI.DrawTexture(upBtn, circle); GUI.DrawTexture(downBtn, circle);
                GUI.color = Color.white;
                GUI.Label(upBtn, "UP", st); GUI.Label(downBtn, "DN", st);
                GUI.color = new Color(1, 1, 1, 0.8f);
                st.fontSize = (int)(3 * U);
                GUI.Box(speedBtn, GUIContent.none);
                GUI.Label(speedBtn, "x" + (speedIndex + 1), st);
            }
            if (CarAvailable)
            {
                var st2 = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = (int)(3 * U), fontStyle = FontStyle.Bold };
                GUI.color = new Color(0.1f, 0.12f, 0.16f, 0.55f);
                GUI.DrawTexture(carBtn, circle, ScaleMode.StretchToFill);
                GUI.color = Color.white;
                GUI.Label(carBtn, "CAR", st2);
            }
            GUI.color = old;
        }
    }

    // Small FPS / help readout.
    public class HudOverlay : MonoBehaviour
    {
        float fps = 60, acc; int frames;
        void Update()
        {
            acc += Time.unscaledDeltaTime; frames++;
            if (acc >= 0.5f) { fps = frames / acc; acc = 0; frames = 0; }
        }
        void OnGUI()
        {
            float u = Mathf.Min(Screen.width, Screen.height) / 100f;
            var st = new GUIStyle(GUI.skin.label) { fontSize = (int)(2.6f * u) };
            st.normal.textColor = new Color(1, 1, 1, 0.75f);
            GUI.Label(new Rect(3 * u, 2 * u, 60 * u, 5 * u), $"{fps:0} FPS", st);
            if (!Application.isMobilePlatform)
                GUI.Label(new Rect(3 * u, 16 * u, 90 * u, 5 * u), "WASD drive · F fly · WASD/QE + RMB fly", st);
        }
    }
}
