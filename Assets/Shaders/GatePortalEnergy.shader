Shader "VorotaKraft/PortalEnergy"
{
    Properties
    {
        _SmokeTex("Baked Blender smoke",2D)="black" {}
        [HideInInspector] _PortalTravel("Travel enabled",Float)=0
        [HideInInspector] _PortalInside("Inside",Float)=0
        [HideInInspector] _PortalEntryReady("Entry ready",Float)=0
        [HideInInspector] _PortalExitPending("Exit pending",Float)=0
        [HideInInspector] _PortalRadius("Aperture radius",Float)=1
        _Reveal("Reveal",Float)=0
        _Halo("Volume puffs",Float)=0
        _ForegroundOffset("Foreground depth offset",Float)=.23
        [HideInInspector] _PortalCenter("Portal center",Vector)=(0,0,0,0)
        [HideInInspector] _PortalInward("Portal inward",Vector)=(1,0,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" }
        Pass
        {
            Name "PortalSmoke"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZWrite Off ZTest LEqual
            Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #include "GatePortalCommon.hlsl"
            TEXTURE2D(_SmokeTex);SAMPLER(sampler_SmokeTex);
            struct A
            {
                float4 positionOS:POSITION;float2 uv:TEXCOORD0;float4 puff:TEXCOORD1;half4 color:COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct V
            {
                float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float2 uv:TEXCOORD1;
                float3 relativePosition:TEXCOORD2;half2 phase:TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            V vert(A input)
            {
                V o=(V)0;UNITY_SETUP_INSTANCE_ID(input);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS=TransformObjectToWorld(input.positionOS.xyz);
                if(_Halo>.5)
                {
                    float3 center=TransformObjectToWorld(input.puff.xyz);
                    float3 right=UNITY_MATRIX_I_V._m00_m10_m20;
                    float3 up=UNITY_MATRIX_I_V._m01_m11_m21;
                    float scale=length(TransformObjectToWorld(float3(0,1,0))-TransformObjectToWorld(float3(0,0,0)));
                    float swell=.94+.06*sin(_Time.y*.8+input.color.r*6.2831853);
                    o.positionWS=center+(right*input.uv.x+up*input.uv.y)*input.puff.w*scale*swell;
                }
                o.positionCS=TransformWorldToHClip(o.positionWS);o.uv=input.uv;o.phase=input.color.rg;
                o.relativePosition=TransformWorldToObject(o.positionWS);
                return o;
            }
            half4 frag(V input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);PortalFrontOnly();
                float radius=length(input.relativePosition.yz);
                half feather=smoothstep(.72,.88,radius)*(1-smoothstep(1.16,1.33,radius));
                half detail,alpha;half3 color;
                if(_Halo>.5)
                {
                    float turn=input.phase.x*6.2831853+_Time.y*.09;
                    float sn=sin(turn),cs=cos(turn);
                    float2 p=float2(input.uv.x*cs-input.uv.y*sn,input.uv.x*sn+input.uv.y*cs);
                    float2 uv=float2(.62,.65)+p*.13;
                    detail=saturate(SAMPLE_TEXTURE2D(_SmokeTex,sampler_SmokeTex,uv).r*4.5);
                    half soft=pow(saturate(1-dot(input.uv,input.uv)),1.6);
                    alpha=soft*lerp(.4,1,detail)*.34*feather*_Reveal;
                    half light=saturate(.25+.65*detail+.1*input.phase.y);
                    color=lerp(half3(.02,.16,.36),half3(.30,.75,1.1),light);
                }
                else
                {
                    float angle=atan2(input.uv.y,input.uv.x);
                    float r=saturate((radius-.70)/.58)*.83+.012*sin(angle*5-_Time.y*.65);
                    angle+=.065*sin(radius*9+_Time.y*.7);
                    float2 uv=.5+float2(cos(angle),sin(angle))*r*.5;
                    detail=saturate(SAMPLE_TEXTURE2D(_SmokeTex,sampler_SmokeTex,uv).r*3.3);
                    half pulse=.92+.08*sin(_Time.y*1.3+angle*3);
                    alpha=pow(detail,.8)*feather*pulse*_Reveal*.68;
                    color=lerp(half3(.015,.10,.32),half3(.25,.75,1.2),sqrt(detail));
                }
                alpha*=PortalForegroundVisibility(input.positionWS);
                clip(alpha-.002);
                return half4(color*alpha,alpha);
            }
            ENDHLSL
        }
    }
}
