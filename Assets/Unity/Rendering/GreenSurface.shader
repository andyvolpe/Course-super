// Phase 3.1 — honest green surface (URP). Driven ENTIRELY by per-cell tell channels the sim hands
// us via TellAppearance; the shader computes nothing about hidden state. Lesions/thinning/sheen/wilt
// are surfaced exactly as the LegibilitySystem permits. Set the channels with a MaterialPropertyBlock
// from GreenRenderer (one material instance per 3x3 sub-cell).
Shader "Greenkeeper/GreenSurface"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.16, 0.42, 0.16, 1)
        _Lesions   ("Lesions (dollar spot)", Range(0,1)) = 0
        _Thinning  ("Thinning (bare canopy)", Range(0,1)) = 0
        _WetSheen  ("Wet sheen", Range(0,1)) = 0
        _WiltTint  ("Wilt tint", Range(0,1)) = 0
        _SpotScale ("Lesion pattern scale", Float) = 14
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 200

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Lesions;
                float _Thinning;
                float _WetSheen;
                float _WiltTint;
                float _SpotScale;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float3 normalWS : TEXCOORD0; float2 uv : TEXCOORD1; };

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            // cheap value-noise hash for procedural lesion / thinning patches
            float hash21 (float2 p) { p = frac(p * float2(123.34, 345.45)); p += dot(p, p + 34.345); return frac(p.x * p.y); }
            float noise (float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float a = hash21(i), b = hash21(i + float2(1,0)), c = hash21(i + float2(0,1)), d = hash21(i + float2(1,1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float3 albedo = _BaseColor.rgb;

                // Wilt: shift toward dry blue-grey.
                albedo = lerp(albedo, float3(0.55, 0.60, 0.58), saturate(_WiltTint) * 0.6);
                // Wet sheen: darken (and we bump smoothness below).
                albedo = lerp(albedo, albedo * 0.7, saturate(_WetSheen));

                // Lesions: straw/tan dollar-spot blotches where the noise field is high.
                float spots = noise(IN.uv * _SpotScale);
                float lesionMask = saturate((spots - (1.0 - _Lesions)) * 4.0);
                albedo = lerp(albedo, float3(0.72, 0.64, 0.40), lesionMask);

                // Thinning: bare soil shows through where canopy density is low.
                float bare = noise(IN.uv * (_SpotScale * 0.5) + 7.3);
                float bareMask = saturate((bare - (1.0 - _Thinning)) * 3.0);
                albedo = lerp(albedo, float3(0.34, 0.26, 0.18), bareMask);

                // Simple URP main-light Lambert + ambient so it reads in the scene.
                Light mainLight = GetMainLight();
                float ndotl = saturate(dot(normalize(IN.normalWS), mainLight.direction));
                float3 lighting = mainLight.color * ndotl + unity_AmbientSky.rgb;
                float3 color = albedo * max(lighting, 0.25);

                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
