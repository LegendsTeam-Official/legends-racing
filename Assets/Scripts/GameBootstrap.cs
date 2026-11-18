using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Legends
{
    // Entry point: builds the world, camera, atmosphere and effects at startup.
    public class GameBootstrap : MonoBehaviour
    {
        public int seed = 1337;

        public static Color FogColor = new Color(0.11f, 0.13f, 0.19f);

        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            Atmosphere();
            var city = new GameObject("City").AddComponent<CityGenerator>();
            city.Generate(seed);

            var cam = CreateCamera(city.SpawnPoint, city.SpawnForward);
            new GameObject("LampLights").AddComponent<LampLightPool>().Init(city.LampHeads, cam.transform);
            new GameObject("Rain").AddComponent<RainFX>().Init(cam.transform);
            new GameObject("RainAudio").AddComponent<RainAudio>();
            new GameObject("Reflections").AddComponent<ReflectionUpdater>().Init(cam.transform);

            var vol = new GameObject("PostFX").AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = Resources.Load<VolumeProfile>("PostFX");

            string shots = ShotDir();
            if (shots != null) StartCoroutine(ShotRoutine(shots, cam.transform));
        }

        void Atmosphere()
        {
            RenderSettings.skybox = Resources.Load<Material>("Materials/Sky");
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.19f, 0.22f, 0.32f);
            RenderSettings.ambientEquatorColor = new Color(0.14f, 0.15f, 0.20f);
            RenderSettings.ambientGroundColor = new Color(0.03f, 0.03f, 0.035f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0105f;
            RenderSettings.fogColor = FogColor;

            var moon = new GameObject("Moon").AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.color = new Color(0.55f, 0.65f, 0.9f);
            moon.intensity = 0.12f;
            moon.shadows = LightShadows.None;
            moon.transform.rotation = Quaternion.Euler(38f, -35f, 0f);
            RenderSettings.sun = moon;
        }

        Camera CreateCamera(Vector3 pos, Vector3 fwd)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd));
            var cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 1600f;
            cam.fieldOfView = 62f;
            cam.clearFlags = CameraClearFlags.Skybox;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.None;
            go.AddComponent<AudioListener>();
            go.AddComponent<FreeCamera>();
            go.AddComponent<HudOverlay>();
            return cam;
        }

        // ---- screenshot mode for checking visuals off-device: -legendsShots <dir>
        static string ShotDir()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-legendsShots") return args[i + 1];
            return null;
        }

        IEnumerator ShotRoutine(string dir, Transform cam)
        {
            Directory.CreateDirectory(dir);
            var fc = cam.GetComponent<FreeCamera>(); fc.enabled = false;
            float c = 0f; float lc = (CityGenerator.Lines / 2 - (CityGenerator.Lines - 1) * 0.5f) * CityGenerator.Pitch;
            var poses = new List<(Vector3 p, Vector3 e)>
            {
                (new Vector3(lc - 3.5f, 2.4f, -150f), new Vector3(4f, 0f, 0f)),   // street level down the avenue
                (new Vector3(lc + 6f, 1.4f, -110f), new Vector3(2f, -25f, 0f)),   // low, across the road toward shops
                (new Vector3(lc - 40f, 60f, -260f), new Vector3(18f, 20f, 0f)),   // elevated overview
                (new Vector3(lc - 3.5f, 2.4f, 60f), new Vector3(3f, 180f, 0f)),   // other direction
            };
            yield return new WaitForSeconds(2f);
            for (int i = 0; i < poses.Count; i++)
            {
                cam.SetPositionAndRotation(poses[i].p, Quaternion.Euler(poses[i].e));
                for (int f = 0; f < 25; f++) yield return null;
                yield return new WaitForSeconds(1.2f);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, $"shot_{i}.png"));
                for (int f = 0; f < 4; f++) yield return null;
            }
            Debug.Log("[Legends] shots done fps~" + (1f / Time.smoothDeltaTime).ToString("0.0"));
            yield return new WaitForSeconds(0.5f);
            Application.Quit();
        }
    }

    // Keeps a small pool of real point lights on the lamps nearest the camera.
    public class LampLightPool : MonoBehaviour
    {
        const int Count = 28;
        List<Vector3> lamps; Transform cam; Light[] pool; float timer;
        readonly List<int> order = new List<int>();

        public void Init(List<Vector3> lampHeads, Transform camera)
        {
            lamps = lampHeads; cam = camera;
            pool = new Light[Count];
            for (int i = 0; i < Count; i++)
            {
                var l = new GameObject("Lamp" + i).AddComponent<Light>();
                l.transform.SetParent(transform, false);
                l.type = LightType.Point;
                l.range = 24f;
                l.intensity = 26f;
                l.color = new Color(1f, 0.66f, 0.36f);
                l.shadows = LightShadows.None;
                l.renderMode = LightRenderMode.ForcePixel;
                pool[i] = l;
            }
            Refresh();
        }

        void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer <= 0f) { timer = 0.2f; Refresh(); }
        }

        void Refresh()
        {
            Vector3 p = cam.position, f = cam.forward; f.y = 0; f.Normalize();
            order.Clear();
            for (int i = 0; i < lamps.Count; i++) order.Add(i);
            float Score(int i)
            {
                Vector3 d = lamps[i] - p; float dist = d.magnitude;
                float ahead = Vector3.Dot(d / Mathf.Max(dist, 0.01f), f);
                return dist * (ahead > 0 ? 1f : 1.8f);
            }
            order.Sort((a, b) => Score(a).CompareTo(Score(b)));
            for (int i = 0; i < pool.Length; i++)
            {
                bool on = i < order.Count;
                pool[i].enabled = on;
                if (on) pool[i].transform.position = lamps[order[i]];
            }
        }
    }

    // Updates one big realtime reflection probe around the camera (wet road reflections).
    public class ReflectionUpdater : MonoBehaviour
    {
        ReflectionProbe probe; Transform cam; float timer;

        public void Init(Transform camera)
        {
            cam = camera;
            probe = gameObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.resolution = 128;
            probe.hdr = true;
            probe.size = new Vector3(4000, 4000, 4000);
            probe.boxProjection = false;
            probe.nearClipPlane = 0.5f;
            probe.farClipPlane = 900f;
            probe.importance = 1;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.cullingMask = ~(1 << 2); // skip Ignore Raycast (rain)
            Refresh();
        }

        void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer <= 0f) { timer = 1.0f; Refresh(); }
        }

        void Refresh()
        {
            // capture from just below street level so the probe "sees" what a puddle would mirror
            transform.position = new Vector3(cam.position.x, 1.5f, cam.position.z);
            probe.RenderProbe();
        }
    }
}
