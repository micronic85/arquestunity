#ifndef AR_GATES_PORTAL_COMMON
#define AR_GATES_PORTAL_COMMON
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.meta.xr.sdk.core/Shaders/EnvironmentDepth/URP/EnvironmentOcclusionURP.hlsl"
CBUFFER_START(UnityPerMaterial)
    float4 _PortalCenter;
    float4 _PortalInward;
    float _Reveal;
    float _ForegroundOffset;
    float _Halo;
CBUFFER_END
void PortalFrontOnly()
{
    // A window viewed from the room in front of the door, not a walk-through transition.
    clip(-dot(GetCameraPositionWS()-_PortalCenter.xyz,_PortalInward.xyz)-.015);
    clip(_Reveal-.001);
}
float PortalForegroundVisibility(float3 portalPositionWS)
{
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
#endif
