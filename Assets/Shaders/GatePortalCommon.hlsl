#ifndef AR_GATES_PORTAL_COMMON
#define AR_GATES_PORTAL_COMMON
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"
CBUFFER_START(UnityPerMaterial)
    float4 _PortalCenter;
    float4 _PortalInward;
    float4 _PortalSunPosition;
    float _SunAngularRadius;
    float _CloudOffset;
    float _CloudDensity;
    float _EarthRadius;
    float _NightIntensity;
    float _Reveal;
    float _ForegroundOffset;
    float _Halo;
    float _BakedWorld;
    float _PortalTravel;
    float _PortalInside;
    float _PortalEntryReady;
    float _PortalExitPending;
    float _PortalRadius;
CBUFFER_END
bool PortalInsideView()
{
    float side=dot(GetCameraPositionWS()-_PortalCenter.xyz,_PortalInward.xyz);
    if(_PortalInside>.5)return _PortalExitPending<.5 || side>=0;
    return _PortalEntryReady>.5 && side>=0;
}
void PortalFrontOnly()
{
    float side=dot(GetCameraPositionWS()-_PortalCenter.xyz,_PortalInward.xyz);
    if(_PortalTravel<.5 || (!PortalInsideView() && _PortalEntryReady<.5))clip(-side-.001);
    clip(_Reveal-.001);
}
float PortalForegroundVisibility(float3 portalPositionWS)
{
    // In space the real room must not occlude the virtual sky or planet.
    if(_PortalTravel>.5 && PortalInsideView())return 1;
#if defined(HARD_OCCLUSION) || defined(SOFT_OCCLUSION)
    float3 ray=normalize(portalPositionWS-GetCameraPositionWS());
    float facing=max(abs(dot(ray,_PortalInward.xyz)),.08);
    // Compare real depth against a plane just IN FRONT of the mounting wall.
    // The wall behind the opening does not hide the distant virtual room; foreground hands can.
    // Smoke may extend in front of the reference plane: test its actual surface there.
    float distanceBehindProbe=dot(portalPositionWS-_PortalCenter.xyz,_PortalInward.xyz)+_ForegroundOffset;
    float3 probe=portalPositionWS-ray*(max(0,distanceBehindProbe)/facing);
    return CalculateEnvironmentDepthOcclusion(probe,.01);
#else
    return 1;
#endif
}
// World visibility is evaluated per XR eye. The camera rig and the passthrough underlay
// stay in place: a discarded fragment in the return aperture exposes the live real room.
void PortalClipWorld(float3 positionWS)
{
    clip(_Reveal-.001);
    float3 eye=GetCameraPositionWS();
    float cameraSide=dot(eye-_PortalCenter.xyz,_PortalInward.xyz);
    float fragmentSide=dot(positionWS-_PortalCenter.xyz,_PortalInward.xyz);
    if(_PortalTravel<.5)
    {
        PortalFrontOnly();clip(fragmentSide-.005);return;
    }
    bool inside=PortalInsideView();
    float denominator=cameraSide-fragmentSide;
    float t=abs(denominator)>.000001?cameraSide/denominator:-1;
    float3 hit=lerp(eye,positionWS,t);
    float3 lateral=hit-_PortalCenter.xyz;
    lateral-=_PortalInward.xyz*dot(lateral,_PortalInward.xyz);
    float radial=length(lateral);
    bool aperture=radial<_PortalRadius*.88;
    if(inside)
    {
        // Only geometry beyond the return window is cut away. Foreground hands/planet remain visible.
        if(cameraSide>=0 && fragmentSide<0 && t>=0 && t<=1 && aperture)clip(-1);
        return;
    }
    clip(-cameraSide+.001);clip(fragmentSide-.001);
    if(t<0 || t>1 || radial>_PortalRadius*1.08)clip(-1);
    clip(PortalForegroundVisibility(hit)-.5);
}
// Blend only fragments across the aperture, keeping foreground planet geometry intact.
float PortalWorldOpacity(float3 positionWS)
{
    if(_PortalTravel<.5)return 1;
    float3 eye=GetCameraPositionWS();
    float cameraSide=dot(eye-_PortalCenter.xyz,_PortalInward.xyz);
    float fragmentSide=dot(positionWS-_PortalCenter.xyz,_PortalInward.xyz);
    float denominator=cameraSide-fragmentSide;
    float t=abs(denominator)>.000001?cameraSide/denominator:-1;
    if(t<0 || t>1)return 1;
    float3 hit=lerp(eye,positionWS,t)-_PortalCenter.xyz;
    hit-=_PortalInward.xyz*dot(hit,_PortalInward.xyz);
    float radial=length(hit)/max(_PortalRadius,.001);
    if(PortalInsideView())
        return cameraSide>=0 && fragmentSide<0?smoothstep(.88,1.08,radial):1;
    return 1-smoothstep(.88,1.08,radial);
}
#endif
