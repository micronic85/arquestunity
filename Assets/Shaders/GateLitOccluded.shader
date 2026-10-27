// PBR (glTF metallic-roughness) для деталей ворот + перекрытие реальными предметами (Meta Depth API).
// Материалы glTFast конвертируются в этот шейдер в рантайме (GateMaterials.Get).
Shader "VorotaKraft/GateLitOccluded"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _Metallic("Metallic", Range(0, 1)) = 0
        _Roughness("Roughness", Range(0, 1)) = 0.5
        _RoughnessMin("Roughness Min", Range(0, 1)) = 0
        _MRMap("Metallic (B) Roughness (G)", 2D) = "white" {}
        [Normal] _BumpMap("Normal Map", 2D) = "bump" {}
        _BumpScale("Normal Scale", Float) = 1
        _OcclusionMap("Occlusion", 2D) = "white" {}
        _OcclusionStrength("Occlusion Strength", Range(0, 1)) = 1
        _EmissionMap("Emission", 2D) = "white" {}
        [HDR] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)
        _Transparent("Transparent", Float) = 0
        _AlphaClip("Alpha Clip", Float) = 0
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [Enum(Off, 0, On, 1)] _ZWrite("ZWrite", Float) = 1
        _EnvironmentDepthBias("Environment Depth Bias", Float) = 0.2
        _PlanarUV("Planar UV (object ZY)", Float) = 0
        _PlanarScale("Planar V scale along width, 1/m", Float) = 0.33
        _PlanarScaleY("Planar U scale along height, 1/m", Float) = 1.25
        _PlanarURange("Usable U range of the texture", Float) = 0.84
        _Ribs("Ribs (horizontal grooves)", Float) = 0
        _RibPeriod("Rib period, m", Float) = 0.14
        _RibDepth("Rib depth", Float) = 0.6
        _Stucco("Stucco strength", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);
    TEXTURE2D(_MRMap);        SAMPLER(sampler_MRMap);
    TEXTURE2D(_BumpMap);      SAMPLER(sampler_BumpMap);
    TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
    TEXTURE2D(_EmissionMap);  SAMPLER(sampler_EmissionMap);

    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor;
        half _Metallic;
        half _Roughness;
        half _RoughnessMin;
        half _BumpScale;
        half _OcclusionStrength;
        half4 _EmissionColor;
        half _Transparent;
        half _AlphaClip;
        half _Cutoff;
        float _EnvironmentDepthBias;
        float _PlanarUV;
        float _PlanarScale;
        float _PlanarScaleY;
        float _PlanarURange;
        float _Ribs;
        float _RibPeriod;
        float _RibDepth;
        float _Stucco;
    CBUFFER_END

    // Вырез калитки (MaterialPropertyBlock рендерера, не материала): точка объекта → система секции (_ClipMatrix),
    // _ClipMode 1 — отбросить внутри [_ClipMin, _ClipMax] (полотно), 2 — снаружи (створка калитки)
    float4x4 _ClipMatrix;
    float4 _ClipMin;
    float4 _ClipMax;
    float _ClipMode;
    void WicketClip(float3 positionOS)
    {
        if (_ClipMode < 0.5) return;
        float3 p = mul(_ClipMatrix, float4(positionOS, 1.0)).xyz;
        bool inside = all(p >= _ClipMin.xyz) && all(p <= _ClipMax.xyz);
        clip(((_ClipMode < 1.5) == inside) ? -1.0 : 1.0);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            // Как в шейдерах Meta: альфа в кадре = видимость, сквозь «прозрачное» видно passthrough
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            ZWrite [_ZWrite]
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float4 tangentWS : TEXCOORD3;
                half fogFactor : TEXCOORD4;
                float3 positionOS : TEXCOORD6;
                META_DEPTH_VERTEX_OUTPUT(5)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = float4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                // У панелей адаптивных ворот нет развёртки — текстуру (дерево) кладём плоско по ширине/высоте
                // волокна дерева — вдоль ширины ворот (ось Z секции)
                // Текстура дерева: волокна вдоль V; справа в ней чёрное поле (U > _PlanarURange) — берём только левую часть
                float2 uv0 = _PlanarUV > 0.5
                    ? float2(input.positionOS.y * _PlanarScaleY, 0.5 + input.positionOS.z * _PlanarScale)
                    : input.uv;
                o.positionOS = input.positionOS.xyz;
                o.uv = TRANSFORM_TEX(uv0, _BaseMap);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                META_DEPTH_INITIALIZE_VERTEX_OUTPUT(o, input.positionOS);
                return o;
            }

            half4 frag(Varyings input, bool isFront : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                WicketClip(input.positionOS);

                float2 uvS = input.uv;
                if (_PlanarUV > 0.5) uvS.x = frac(uvS.x) * _PlanarURange;
                half4 baseCol = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uvS, ddx(input.uv), ddy(input.uv)) * _BaseColor;
                if (_AlphaClip > 0.5h) clip(baseCol.a - _Cutoff);
                half4 mr = SAMPLE_TEXTURE2D(_MRMap, sampler_MRMap, input.uv);
                half roughness = max(_Roughness * mr.g, _RoughnessMin);
                half metallic = _Metallic * mr.b;

                float3 normalWS = normalize(input.normalWS);
                float3 tangentWS = normalize(input.tangentWS.xyz);
                if (!isFront) { normalWS = -normalWS; }
                float3 bitangentWS = cross(normalWS, tangentWS) * (input.tangentWS.w > 0 ? 1.0 : -1.0);
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(tangentWS, bitangentWS, normalWS)));

                // Сэндвич-панель: горизонтальные гофры + мелкая «штукатурка» (stucco) — процедурно, без UV
                float3 upWS = normalize(TransformObjectToWorldDir(float3(0, 1, 0)));
                float3 sideWS = normalize(TransformObjectToWorldDir(float3(0, 0, 1)));
                if (_Ribs > 0.5)
                {
                    float t = frac(input.positionOS.y / _RibPeriod);
                    const float w = 0.16;                       // ширина гофра — доля шага
                    float slope = t < w ? sin(t / w * 6.2831853) : 0.0;
                    normalWS = normalize(normalWS + upWS * slope * _RibDepth);
                    // затенение в глубине канавки — гофры читаются и на тёмных цветах
                    float groove = t < w ? sin(t / w * 3.1415926) : 0.0;
                    baseCol.rgb *= 1.0 - 0.35 * groove;
                }
                if (_Stucco > 0.001)
                {
                    float2 p = input.positionOS.zy * 260.0;
                    float2 i = floor(p), f = frac(p);
                    float h00 = frac(sin(dot(i, float2(127.1, 311.7))) * 43758.55);
                    float h10 = frac(sin(dot(i + float2(1, 0), float2(127.1, 311.7))) * 43758.55);
                    float h01 = frac(sin(dot(i + float2(0, 1), float2(127.1, 311.7))) * 43758.55);
                    float h11 = frac(sin(dot(i + float2(1, 1), float2(127.1, 311.7))) * 43758.55);
                    float2 u = f * f * (3.0 - 2.0 * f);
                    float dx = lerp(h10 - h00, h11 - h01, u.y);
                    float dy = lerp(h01 - h00, h11 - h10, u.x);
                    normalWS = normalize(normalWS + (sideWS * dx + upWS * dy) * _Stucco);
                }

                SurfaceData s = (SurfaceData)0;
                s.albedo = baseCol.rgb;
                s.alpha = _Transparent > 0.5h ? baseCol.a : 1.0h;
                s.metallic = metallic;
                s.smoothness = 1.0h - roughness;
                s.normalTS = half3(0, 0, 1);
                s.occlusion = lerp(1.0h, SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, input.uv).r, _OcclusionStrength);
                s.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;

                InputData d = (InputData)0;
                d.positionWS = input.positionWS;
                d.positionCS = input.positionCS;
                d.normalWS = normalWS;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                d.fogCoord = input.fogFactor;
                d.bakedGI = SampleSH(normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(d, s);
                color.rgb = MixFog(color.rgb, d.fogCoord);
                color.a = s.alpha;

                META_DEPTH_OCCLUDE_OUTPUT_PREMULTIPLY(input, color, _EnvironmentDepthBias);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionOS : TEXCOORD1; };

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - positionWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDir));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = positionCS;
                o.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                o.positionOS = input.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings input) : SV_Target
            {
                WicketClip(input.positionOS);
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a;
                if (_AlphaClip > 0.5h) clip(a - _Cutoff);
                if (_Transparent > 0.5h) clip(a - 0.5h);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.positionOS = input.positionOS.xyz;
                return o;
            }
            half frag(Varyings input) : SV_Target { WicketClip(input.positionOS); return 0; }
            ENDHLSL
        }
    }
}
