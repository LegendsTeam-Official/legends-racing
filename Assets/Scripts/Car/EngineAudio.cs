using UnityEngine;

namespace Legends
{
    // Procedural engine + tyre sound: no audio assets. A few short seamless loops are synthesised once
    // in Start (harmonic sawtooth/pulse blend + a little noise) and crossfaded/pitched by RPM and throttle.
    [RequireComponent(typeof(CarController))]
    public class EngineAudio : MonoBehaviour
    {
        public float masterVolume = 0.55f;
        public float tyreVolume = 0.35f;
        public int cylinders = 6;

        // rpm each layer was synthesised for
        static readonly float[] LayerRpm = { 1000f, 3200f, 6000f };

        CarController car;
        AudioSource[] layers;
        AudioSource intake;
        AudioSource squeal;
        float smoothThrottle;

        void Start()
        {
            car = GetComponent<CarController>();
            int sr = AudioSettings.outputSampleRate;
            if (sr <= 0) sr = 44100;
            int len = sr / 2;   // 0.5 s loops

            layers = new AudioSource[LayerRpm.Length];
            for (int i = 0; i < LayerRpm.Length; i++)
            {
                float f0 = LayerRpm[i] / 60f * (cylinders * 0.5f);   // firing frequency
                // brightness: low layer is darker, high layer more raspy
                float bright = 0.55f + 0.2f * i;
                var clip = MakeEngineClip("eng_" + i, sr, len, f0, bright, 0.06f + 0.03f * i, 1234 + i * 77);
                layers[i] = MakeSource("EngineLayer" + i, clip, 0f);
            }
            intake = MakeSource("Intake", MakeNoiseClip("intake", sr, len, 999), 0f);
            squeal = MakeSource("TyreSqueal", MakeSquealClip("squeal", sr, len), 0f);
        }

        AudioSource MakeSource(string name, AudioClip clip, float vol)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            var s = go.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.spatialBlend = 0f;   // player car: 2D
            s.volume = vol;
            s.dopplerLevel = 0f;
            s.priority = 32;
            s.Play();
            return s;
        }

        void Update()
        {
            if (car == null || layers == null) return;
            float dt = Time.deltaTime;
            float rpm = car.Rpm;
            float thr = car.ThrottleApplied;
            if (car.IsShifting) thr *= 0.3f;
            smoothThrottle = Mathf.Lerp(smoothThrottle, thr, 1f - Mathf.Exp(-dt * 10f));
            float load = Mathf.Lerp(0.5f, 1f, smoothThrottle);

            // triangular crossfade between neighbouring layers (equal-power-ish)
            for (int i = 0; i < layers.Length; i++)
            {
                float w;
                if (i == 0) w = Mathf.InverseLerp(LayerRpm[1], LayerRpm[0], rpm);
                else if (i == layers.Length - 1) w = Mathf.InverseLerp(LayerRpm[i - 1], LayerRpm[i], rpm);
                else w = rpm < LayerRpm[i]
                        ? Mathf.InverseLerp(LayerRpm[i - 1], LayerRpm[i], rpm)
                        : Mathf.InverseLerp(LayerRpm[i + 1], LayerRpm[i], rpm);
                w = Mathf.Sin(w * Mathf.PI * 0.5f);
                var s = layers[i];
                s.volume = masterVolume * w * load;
                s.pitch = Mathf.Clamp(rpm / LayerRpm[i], 0.4f, 2.5f) * (1f + 0.03f * smoothThrottle);
            }

            float rpmN = Mathf.InverseLerp(car.idleRpm, car.redlineRpm, rpm);
            intake.volume = masterVolume * 0.35f * smoothThrottle * (0.3f + 0.7f * rpmN);
            intake.pitch = 0.7f + 0.8f * rpmN;

            float slip = car.SlipAmount;
            float speedF = Mathf.InverseLerp(1.5f, 8f, Mathf.Abs(car.ForwardSpeed) + car.SpeedKmh / 3.6f * 0.5f);
            float sq = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 0.8f, slip)) * speedF;
            squeal.volume = Mathf.Lerp(squeal.volume, tyreVolume * sq, 1f - Mathf.Exp(-dt * 12f));
            squeal.pitch = 0.9f + 0.25f * slip;
        }

        // ------------------------------------------------------------------ synthesis

        // Harmonic blend of sawtooth (1/k) and pulse (odd harmonics) with a sub-harmonic rumble and
        // firing-modulated noise. Every partial completes an integer number of cycles -> seamless loop.
        static AudioClip MakeEngineClip(string name, int sr, int len, float f0, float bright, float noiseAmt, int seed)
        {
            float dur = (float)len / sr;
            int cycles = Mathf.Max(2, Mathf.RoundToInt(f0 * dur));
            if ((cycles & 1) == 1) cycles++;                   // even -> half-frequency rumble also loops
            float f = cycles / dur;
            int maxK = Mathf.Max(1, Mathf.FloorToInt((sr * 0.22f) / f));   // headroom for pitch-up
            maxK = Mathf.Min(maxK, 24);

            var data = new float[len];
            var rnd = new System.Random(seed);
            float twoPi = Mathf.PI * 2f;
            float[] amp = new float[maxK + 1];
            float[] phase = new float[maxK + 1];
            for (int k = 1; k <= maxK; k++)
            {
                float saw = 1f / k;
                float pulse = (k % 2 == 1) ? 1.2f / k : 0.2f / k;
                amp[k] = Mathf.Lerp(pulse, saw, 0.5f) * Mathf.Pow(bright, (k - 1) * 0.35f);
                phase[k] = (float)rnd.NextDouble() * twoPi;
            }
            float noiseLp = 0f;
            for (int n = 0; n < len; n++)
            {
                float t = (float)n / len;              // 0..1 over the loop
                float ph = twoPi * cycles * t;
                float v = 0f;
                for (int k = 1; k <= maxK; k++) v += amp[k] * Mathf.Sin(ph * k + phase[k]);
                v += 0.45f * Mathf.Sin(ph * 0.5f);     // sub-harmonic rumble
                // noise bursts at firing rate
                float white = (float)rnd.NextDouble() * 2f - 1f;
                noiseLp += (white - noiseLp) * 0.35f;
                float env = 0.5f + 0.5f * Mathf.Sin(ph);
                v += noiseLp * noiseAmt * 4f * env;
                data[n] = v;
            }
            Normalize(data, 0.8f);
            var clip = AudioClip.Create(name, len, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        static AudioClip MakeNoiseClip(string name, int sr, int len, int seed)
        {
            var data = new float[len];
            var rnd = new System.Random(seed);
            float lp = 0f, lp2 = 0f;
            for (int n = 0; n < len; n++)
            {
                float w = (float)rnd.NextDouble() * 2f - 1f;
                lp += (w - lp) * 0.25f;
                lp2 += (lp - lp2) * 0.25f;
                data[n] = lp - lp2 * 0.6f;   // band-ish noise
            }
            Normalize(data, 0.7f);
            var clip = AudioClip.Create(name, len, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        // Tyre squeal: a few detuned partials around 1 kHz with periodic vibrato + a little hiss.
        static AudioClip MakeSquealClip(string name, int sr, int len)
        {
            float dur = (float)len / sr;
            float[] baseF = { 980f, 1210f, 1630f };
            float[] gain = { 1f, 0.6f, 0.3f };
            var data = new float[len];
            var rnd = new System.Random(4242);
            float twoPi = Mathf.PI * 2f;
            int vibCycles = Mathf.Max(1, Mathf.RoundToInt(7f * dur));
            float lp = 0f;
            for (int p = 0; p < baseF.Length; p++)
            {
                int cycles = Mathf.RoundToInt(baseF[p] * dur);
                float depth = 6f + 3f * p;    // cycles of phase deviation (integer vibrato keeps the loop seamless)
                for (int n = 0; n < len; n++)
                {
                    float t = (float)n / len;
                    float ph = twoPi * cycles * t + depth * Mathf.Sin(twoPi * vibCycles * t + p);
                    data[n] += gain[p] * Mathf.Sin(ph);
                }
            }
            for (int n = 0; n < len; n++)
            {
                float w = (float)rnd.NextDouble() * 2f - 1f;
                lp += (w - lp) * 0.6f;
                data[n] += lp * 0.25f;
            }
            Normalize(data, 0.6f);
            var clip = AudioClip.Create(name, len, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        static void Normalize(float[] d, float peak)
        {
            float m = 0f;
            for (int i = 0; i < d.Length; i++) { float a = Mathf.Abs(d[i]); if (a > m) m = a; }
            if (m < 1e-6f) return;
            float s = peak / m;
            for (int i = 0; i < d.Length; i++) d[i] *= s;
        }
    }
}
