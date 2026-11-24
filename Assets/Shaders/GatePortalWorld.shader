Shader "VorotaKraft/PortalWorld"
{
    Properties
    {
        _BaseMap("Baked world lighting",2D)="white" {}
        _BakedWorld("Baked world",Float)=0
        _Reveal("Reveal",Float)=0
        _Halo("Halo",Float)=0
        _ForegroundOffset("Foreground depth offset",Float)=.23
        [HideInInspector] _PortalCenter("Portal center",Vector)=(0,0,0,0)
        [HideInInspector] _PortalInward("Portal inward",Vector)=(1,0,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+5" "RenderType"="Opaque" }
        Pass
        {
            Name "PortalRoom"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            Stencil { Ref 64 ReadMask 64 WriteMask 0 Comp Equal Pass Keep }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "GatePortalCommon.hlsl"
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; half4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; half4 color:COLOR; float2 uv:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A input)
            {
                V o=(V)0; UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS=TransformObjectToWorld(input.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(input.normalOS);o.color=input.color;o.uv=input.uv;return o;
            }
            half4 frag(V input,bool front:SV_IsFrontFace):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);PortalFrontOnly();
                clip(dot(input.positionWS-_PortalCenter.xyz,_PortalInward.xyz)-.005);
                if(_BakedWorld>.5)
                    return half4(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb,_Reveal);
                float3 normal=normalize(input.normalWS)*(front?1:-1);
                half light=.30+.70*saturate(dot(normal,normalize(float3(-.4,.8,.3))));
                half3 color=input.color.rgb*(light+input.color.a*1.7);
                float depth=max(0,dot(input.positionWS-_PortalCenter.xyz,_PortalInward.xyz));
                color=lerp(color,half3(.015,.055,.12),saturate(depth*.025));
                return half4(color,_Reveal);
            }
            ENDHLSL
        }
    }
}
