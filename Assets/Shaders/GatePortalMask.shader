Shader "VorotaKraft/PortalMask"
{
    Properties
    {
        [HideInInspector] _PortalTravel("Travel enabled",Float)=0
        [HideInInspector] _PortalInside("Inside",Float)=0
        [HideInInspector] _PortalEntryReady("Entry ready",Float)=0
        [HideInInspector] _PortalExitPending("Exit pending",Float)=0
        [HideInInspector] _PortalRadius("Aperture radius",Float)=1
        _Reveal("Reveal",Float)=0
        _Halo("Halo",Float)=0
        _ForegroundOffset("Foreground depth offset",Float)=.23
        [HideInInspector] _PortalCenter("Portal center",Vector)=(0,0,0,0)
        [HideInInspector] _PortalInward("Portal inward",Vector)=(1,0,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-30" "RenderType"="Opaque" }
        Pass
        {
            Name "PortalAperture"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ColorMask 0
            ZWrite Off
            ZTest LEqual
            Cull Off
            Stencil { Ref 64 ReadMask 64 WriteMask 64 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #include "GatePortalCommon.hlsl"
            struct A { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A input)
            {
                V o=(V)0; UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS); return o;
            }
            half4 frag(V input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                PortalFrontOnly(); clip(PortalForegroundVisibility(input.positionWS)-.5); return 0;
            }
            ENDHLSL
        }
    }
}
