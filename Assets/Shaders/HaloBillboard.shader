// Camera-facing glow halos around lamp heads, all lamps in one mesh.
// POSITION = halo centre (world), TEXCOORD1.xy = corner offset in metres.
Shader "Legends/HaloBillboard"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (1,0.7,0.4,1)
        _Intensity ("Intensity", Float) = 1
        _NearFade ("Near fade distance", Float) = 3
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Cull Off
        Pass
        {
            Name "Halo"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                half _Intensity;
                half _NearFade;
            CBUFFER_END

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float2 corner : TEXCOORD1; half4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; half fog : TEXCOORD1; half fade : TEXCOORD2; };

            V vert (A i)
            {
                V o;
                float3 centreWS = TransformObjectToWorld(i.pos.xyz);
                float3 vp = TransformWorldToView(centreWS);
                float dist = length(vp);
                // grow a little with distance so far lamps still read through the fog
                float grow = 1.0 + saturate(dist / 250.0) * 1.5;
                vp.xy += i.corner * grow;
                o.pos = TransformWViewToHClip(vp);
                o.uv = i.uv;
                o.col = i.col;
                o.fog = ComputeFogFactor(o.pos.z);
                o.fade = saturate((dist - 0.5) / _NearFade);
                return o;
            }
            half4 frag (V i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half3 c = t.rgb * t.a * i.col.rgb * _Color.rgb * _Intensity * i.fade;
                // halos survive fog better than geometry: the fog is lit by them
                c *= lerp(0.35, 1.0, i.fog);
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
