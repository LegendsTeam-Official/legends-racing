// Overcast night sky with city light pollution near the horizon.
Shader "Legends/NightSky"
{
    Properties
    {
        _Zenith ("Zenith", Color) = (0.03,0.045,0.08,1)
        _Horizon ("Horizon", Color) = (0.11,0.13,0.19,1)
        _Glow ("City glow", Color) = (0.32,0.22,0.15,1)
        _GlowHeight ("Glow height", Float) = 0.12
        _CloudScale ("Cloud scale", Float) = 3
        _CloudStrength ("Cloud strength", Float) = 0.0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _Horizon, _Glow;
                half _GlowHeight, _CloudScale, _CloudStrength;
            CBUFFER_END

            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            V vert (A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.dir = i.pos.xyz; return o; }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1,0)), f.x), lerp(hash(i + float2(0,1)), hash(i + float2(1,1)), f.x), f.y);
            }
            float fbm(float2 p) { float s = 0, a = 0.5; for (int k = 0; k < 4; k++) { s += a * noise(p); p *= 2.03; a *= 0.5; } return s; }

            half4 frag (V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = saturate(d.y);
                half3 c = lerp(_Horizon.rgb, _Zenith.rgb, pow(h, 0.45));
                c += _Glow.rgb * exp(-h / max(_GlowHeight, 1e-3)) * 0.6;
                float2 uv = frac(d.xz / max(d.y + 0.35, 0.2) * _CloudScale * 0.05 + 0.5) * 16.0;
                float cl = fbm(uv);
                c *= 1.0 + (cl - 0.5) * _CloudStrength * smoothstep(0.05, 0.4, h);
                if (d.y < 0) c = _Horizon.rgb;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
