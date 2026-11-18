using UnityEngine;

namespace Legends
{
    // Rain streaks + ground splashes that follow the camera.
    public class RainFX : MonoBehaviour
    {
        Transform cam; ParticleSystem rain, splash;

        public void Init(Transform camera)
        {
            cam = camera;
            gameObject.layer = 2;
            rain = MakeRain();
            splash = MakeSplash();
        }

        ParticleSystem MakeRain()
        {
            var go = new GameObject("Streaks"); go.layer = 2;
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = true;
            main.duration = 5f;
            main.startLifetime = 1.0f;
            main.startSpeed = 0f;
            main.startSize = 0.013f;
            main.startColor = new Color(0.75f, 0.8f, 0.9f, 0.55f);
            main.maxParticles = 7000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            var em = ps.emission; em.rateOverTime = 5500;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(46f, 1f, 46f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-25f, -21f);
            vel.z = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.035f; r.lengthScale = 1f;
            r.sharedMaterial = Resources.Load<Material>("Materials/Rain");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        ParticleSystem MakeSplash()
        {
            var go = new GameObject("Splashes"); go.layer = 2;
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.startLifetime = 0.28f; main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.startColor = new Color(0.7f, 0.75f, 0.85f, 0.6f);
            main.maxParticles = 2000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 1400;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(36f, 0f, 36f);
            var sol = ps.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.2f, 1, 1f));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            r.sharedMaterial = Resources.Load<Material>("Materials/Splash");
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        void LateUpdate()
        {
            if (cam == null) return;
            Vector3 p = cam.position;
            Vector3 ahead = cam.forward; ahead.y = 0;
            rain.transform.position = new Vector3(p.x, p.y + 14f, p.z) + ahead * 8f;
            splash.transform.position = new Vector3(p.x, 0.04f, p.z) + ahead.normalized * 12f;
        }
    }

    // Procedural rain loop: filtered noise bed + random droplets. No audio assets needed.
    public class RainAudio : MonoBehaviour
    {
        void Start()
        {
            const int rate = 44100; const float seconds = 6f;
            int n = (int)(rate * seconds);
            var data = new float[n];
            var rnd = new System.Random(7);
            float lp1 = 0, lp2 = 0, hp = 0, prev = 0;
            for (int i = 0; i < n; i++)
            {
                float w = (float)(rnd.NextDouble() * 2 - 1);
                lp1 += (w - lp1) * 0.35f;                // soft hiss
                lp2 += (w - lp2) * 0.02f;                // low rumble
                hp = 0.97f * (hp + lp1 - prev); prev = lp1;
                data[i] = hp * 0.32f + lp2 * 0.9f;
            }
            // droplets
            for (int d = 0; d < 2600; d++)
            {
                int s = rnd.Next(n); float amp = (float)rnd.NextDouble() * 0.25f + 0.05f;
                float freq = 1800f + (float)rnd.NextDouble() * 3500f;
                int len = 300 + rnd.Next(500);
                for (int k = 0; k < len; k++)
                {
                    int idx = (s + k) % n;
                    data[idx] += Mathf.Sin(k * freq * 2 * Mathf.PI / rate) * amp * Mathf.Exp(-k / (len * 0.25f));
                }
            }
            // crossfade the loop seam
            int fade = rate / 4;
            for (int i = 0; i < fade; i++)
            {
                float t = i / (float)fade;
                data[i] = data[i] * t + data[n - fade + i] * (1 - t);
            }
            var trimmed = new float[n - fade];
            System.Array.Copy(data, trimmed, trimmed.Length);
            var clip = AudioClip.Create("Rain", trimmed.Length, 1, rate, false);
            clip.SetData(trimmed, 0);
            var src = gameObject.AddComponent<AudioSource>();
            src.clip = clip; src.loop = true; src.volume = 0.55f; src.spatialBlend = 0f;
            src.Play();
        }
    }
}
