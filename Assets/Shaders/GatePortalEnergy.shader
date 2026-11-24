Shader "VorotaKraft/PortalEnergy"
{
    Properties
    {
        _SmokeTex("Baked Blender smoke",2D)="black" {}
        _Reveal("Reveal",Float)=0
        _Halo("Wisps layer",Float)=0
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
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float2 uv:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A input)
            {
                V o=(V)0;UNITY_SETUP_INSTANCE_ID(input);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS=TransformObjectToWorld(input.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);o.uv=input.uv;return o;
            }
            half4 frag(V input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);PortalFrontOnly();
                float radius=length(input.uv);
                float angle=atan2(input.uv.y,input.uv.x);
                // Expand the small eye of the original vortex into a usable viewing window.
                float r=saturate((radius-.70)/.58)*.83;
                r+=.012*sin(angle*5-_Time.y*.65);
                angle+=.065*sin(radius*9+_Time.y*.7);
                float2 uv=.5+float2(cos(angle),sin(angle))*r*.5;
                half density=SAMPLE_TEXTURE2D(_SmokeTex,sampler_SmokeTex,uv).r;
                half detail=saturate(density*3.3);
                half feather=smoothstep(.72,.88,radius)*(1-smoothstep(1.15,1.28,radius));
                half pulse=.92+.08*sin(_Time.y*1.3+angle*3);
                half alpha=pow(detail,.8)*feather*pulse*_Reveal*lerp(.90,.23,_Halo);
                alpha*=PortalForegroundVisibility(input.positionWS);
                clip(alpha-.002);
                half3 color=lerp(half3(.015,.10,.32),half3(.25,.75,1.2),sqrt(detail));
                return half4(color*alpha,alpha);
            }
            ENDHLSL
        }
    }
}
