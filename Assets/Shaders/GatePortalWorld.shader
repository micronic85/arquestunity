Shader "VorotaKraft/PortalWorld"
{
    Properties
    {
        _BaseMap("Baked world lighting",2D)="white" {}
        _BakedWorld("World shading mode",Float)=0
        _NightMap("Night city radiance",2D)="black" {}
        _CloudMap("Cloud optical density",2D)="black" {}
        [HideInInspector] _CloudOffset("Cloud drift",Float)=0
        [HideInInspector] _CloudDensity("Cloud density",Float)=0
        [HideInInspector] _EarthRadius("World globe radius",Float)=1
        [HideInInspector] _NightIntensity("Night lights",Float)=0
        [HideInInspector] _PortalSunPosition("Sun world position",Vector)=(2,6,-12,0)
        [HideInInspector] _SunAngularRadius("Sun angular radius",Float)=.009
        [HideInInspector] _ZWrite("Depth write",Float)=1
        _PortalCompare("Portal stencil comparison",Float)=3
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
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+5" "RenderType"="Opaque" }
        Pass
        {
            Name "PortalRoom"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite [_ZWrite]
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            Stencil { Ref 64 ReadMask 64 WriteMask 0 Comp [_PortalCompare] Pass Keep }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ HARD_OCCLUSION SOFT_OCCLUSION
            #include "GatePortalCommon.hlsl"
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NightMap);SAMPLER(sampler_NightMap);
            TEXTURE2D(_CloudMap);SAMPLER(sampler_CloudMap);
            float2 CloudUV(float2 uv)
            {
                return float2(uv.x+_CloudOffset*(.85+.15*cos(uv.y*6.2831853)),uv.y);
            }
            half CloudAmount(float2 uv)
            {
                return saturate(SAMPLE_TEXTURE2D(_CloudMap,sampler_CloudMap,CloudUV(uv)).r*_CloudDensity*1.35);
            }
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
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);PortalClipWorld(input.positionWS);
                if(_BakedWorld>.5 && _BakedWorld<1.5)
                {
                    half3 background=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb;
                    // Only Earth's starfield has a sun; the baked loft uses this mode too.
                    if(_SunAngularRadius>.001)
                    {
                        float3 eye=GetCameraPositionWS();
                        float3 ray=normalize(input.positionWS-eye);
                        float3 toSun=normalize(_PortalSunPosition.xyz-eye);
                        float angle=length(ray-toSun);
                        float r=_SunAngularRadius;
                        float aa=max(fwidth(angle),.0003);
                        half disk=1-smoothstep(r-aa,r+aa,angle);
                        half halo=exp(-angle*angle/(r*r*8))*.38;
                        half corona=exp(-max(0,angle-r)/(r*.6))*.25;
                        background=lerp(background,half3(1,.95,.78),disk);
                        background+=half3(1,.55,.16)*(halo+corona)*(1-disk);
                    }
                    return half4(background,_Reveal);
                }
                if(_BakedWorld>1.5)
                {
                    float3 n=normalize(input.normalWS);
                    float3 view=normalize(GetCameraPositionWS()-input.positionWS);
                    float3 sun=normalize(_PortalSunPosition.xyz-input.positionWS);
                    half sunFacing=dot(n,sun);
                    if(_BakedWorld>3.5)
                    {
                        clip(front?1:-1);
                        half cloud=CloudAmount(input.uv);clip(cloud-.012);
                        // Local optical-density gradient gives structure to the lit cloud tops.
                        half neighbor=CloudAmount(input.uv+float2(.0007,.0004));
                        half relief=clamp((cloud-neighbor)*.35,-.05,.05);
                        half daylight=saturate(sunFacing);
                        half3 lighting=half3(.008,.013,.025)+half3(1.08,1.04,1.0)*daylight;
                        lighting*=1+relief;
                        half dusk=smoothstep(-.12,.02,sunFacing)*(1-smoothstep(.02,.24,sunFacing));
                        lighting+=half3(.12,.045,.012)*dusk;
                        return half4(lighting,pow(cloud,.7)*.95*_Reveal);
                    }
                    if(_BakedWorld>2.5)
                    {
                        clip(front?1:-1);
                        half daylight=smoothstep(-.15,.35,sunFacing);
                        half glow=pow(1-saturate(dot(n,view)),3.5)*lerp(.10,.65,daylight)*_Reveal;
                        clip(glow-.003);
                        return half4(.05,.40,1.0,glow);
                    }
                    half3 surface=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb;
                    half daylight=smoothstep(-.08,.12,sunFacing);
                    half diffuse=.025+1.05*saturate(sunFacing)*daylight;
                    half water=saturate((surface.b-surface.r)*4);
                    half spec=pow(saturate(dot(reflect(-sun,n),view)),40)*water*.18*daylight;
                    // Project the sun ray to the detached cloud shell using the UV/world Jacobian.
                    float2 du=ddx(input.uv),dv=ddy(input.uv);
                    float3 dx=ddx(input.positionWS),dy=ddy(input.positionWS);
                    float det=du.x*dv.y-du.y*dv.x;
                    float inverseDet=abs(det)>.00000001?1/det:0;
                    float3 tangentU=(dx*dv.y-dy*du.y)*inverseDet;
                    float3 tangentV=(dy*du.x-dx*dv.x)*inverseDet;
                    float height=_EarthRadius*.0035/max(sunFacing,.12);
                    float2 shift=float2(dot(sun,tangentU)/max(dot(tangentU,tangentU),.00001),
                        dot(sun,tangentV)/max(dot(tangentV,tangentV),.00001))*height;
                    half shadow=CloudAmount(input.uv+shift)*.42*daylight;
                    half lights=SAMPLE_TEXTURE2D(_NightMap,sampler_NightMap,input.uv).r;
                    half night=1-smoothstep(-.18,.08,sunFacing);
                    half3 cities=half3(1,.62,.24)*pow(saturate(lights-.008),.75)*_NightIntensity*night;
                    return half4((surface*diffuse+spec)*(1-shadow)+cities,_Reveal);
                }
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
