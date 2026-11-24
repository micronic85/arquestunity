using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class OrbitalEarthVerification
{
    [MenuItem("Tools/AR Gates/Verify Orbital Earth In Play Mode")]
    public static void Verify()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Enter Play Mode first.");
        var portal=Object.FindFirstObjectByType<GatePortal>();var gate=portal.GetComponent<GateBuilder>();
        if(portal.IsInside)throw new InvalidOperationException("Return to room before testing.");
        var oldWorld=portal.worldKind;var oldSun=portal.sunLocalPosition;
        bool oldEnabled=portal.portalEnabled,oldEntry=portal.allowEarthEntry,oldCloud=portal.cloudsEnabled,oldSpin=portal.earthAutoSpin;
        float oldDuration=portal.appearanceDuration,oldProgress=gate.Progress,oldDensity=portal.cloudDensity,oldLights=portal.nightLightsIntensity;
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        float oldOffset=(float)typeof(GatePortal).GetField("_cloudOffset",flags).GetValue(portal);
        var go=new GameObject("_OrbitalEarthCamera",typeof(Camera));var camera=go.GetComponent<Camera>();
        var rt=new RenderTexture(640,640,24,RenderTextureFormat.ARGB32);var image=new Texture2D(640,640,TextureFormat.RGBA32,false);
        var previous=RenderTexture.active;var hidden=new Dictionary<Renderer,bool>();
        bool hard=Shader.IsKeywordEnabled("HARD_OCCLUSION"),soft=Shader.IsKeywordEnabled("SOFT_OCCLUSION");
        int checks=0;void Check(bool ok,string reason){if(!ok)throw new Exception("Orbital Earth: "+reason);checks++;}
        void Call(string name,params object[] args)=>typeof(GatePortal).GetMethod(name,flags).Invoke(portal,args);
        void Tick()
        {
            Call("Tick",Time.unscaledTime);
            foreach(var r in portal.WorldRoot.GetComponentsInChildren<Renderer>(true))
            {var m=r.sharedMaterial;m.SetFloat("_PortalTravel",1);m.SetFloat("_PortalInside",1);m.SetFloat("_PortalCompare",8);m.SetFloat("_Reveal",1);}
        }
        Color[] Render(string label)
        {
            camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,640,640),0,0);image.Apply();
            File.WriteAllBytes("D:/blender/chatgpt/earth_orbital_"+label+".png",image.EncodeToPNG());return image.GetPixels();
        }
        int Difference(Color[] a,Color[] b)=>a.Zip(b,(x,y)=>Mathf.Max(Mathf.Abs(x.r-y.r),Mathf.Abs(x.g-y.g),Mathf.Abs(x.b-y.b))>.012f?1:0).Sum();
        try
        {
            Shader.DisableKeyword("HARD_OCCLUSION");Shader.DisableKeyword("SOFT_OCCLUSION");
            portal.allowEarthEntry=false;portal.appearanceDuration=0;portal.earthAutoSpin=false;
            portal.SetWorldKind(GatePortal.PortalWorldKind.Earth);gate.SetProgress(1);portal.SetPortalEnabled(true);Tick();
            Check(!ShaderUtil.ShaderHasError(portal.worldShader),"shader compilation");
            Check(portal.earthTexture.width==8192 && portal.earthTexture.height==4096,"8K cloud-free surface");
            Check(portal.earthNightTexture.width==4096 && portal.earthCloudTexture.width==8192,"4K night and 8K cloud maps");
            foreach(var t in new[]{portal.earthTexture,portal.earthNightTexture,portal.earthCloudTexture})
            {
                var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t));
                Check(importer.mipmapEnabled && !importer.isReadable && importer.GetPlatformTextureSettings("Android").overridden,"Quest texture import");
            }
            Check(!((TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(portal.earthNightTexture))).sRGBTexture,"radiance is linear");
            var globe=portal.EarthTransform;var surface=globe.Find("EarthSurface");var clouds=globe.Find("EarthClouds");
            Check(surface.GetComponent<MeshFilter>().sharedMesh.triangles.Length==portal.earthMesh.triangles.Length*4,"original sphere refined for horizon");
            Check(clouds && clouds.parent==globe && clouds.localScale.x>surface.localScale.x,"clouds physically detached above surface");
            Check(portal.WorldRoot.GetComponentsInChildren<MeshFilter>(true).Sum(f=>f.sharedMesh.triangles.Length/3)<51000,"triangle budget");
            var orientation=globe.rotation;Call("AdvanceClouds",60f);Tick();
            Check(Quaternion.Angle(orientation,globe.rotation)<.001f,"weather drifts independently of globe rotation");
            Check((float)typeof(GatePortal).GetField("_cloudOffset",flags).GetValue(portal)!=oldOffset,"cloud drift advances");
            portal.ToggleClouds();Tick();Check(!clouds.gameObject.activeSelf,"cloud switch hides shell");
            float paused=(float)typeof(GatePortal).GetField("_cloudOffset",flags).GetValue(portal);Call("AdvanceClouds",60f);
            Check((float)typeof(GatePortal).GetField("_cloudOffset",flags).GetValue(portal)==paused,"disabled cloud drift pauses");
            portal.ToggleClouds();portal.cloudDensity=1;portal.CycleCloudDensity();Check(portal.cloudDensity==1.5f,"dense clouds");
            portal.CycleCloudDensity();Check(portal.cloudDensity==.5f,"sparse clouds");portal.CycleCloudDensity();Check(portal.cloudDensity==1,"normal clouds");
            foreach(var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if(!renderer.transform.IsChildOf(portal.WorldRoot)){hidden[renderer]=renderer.enabled;renderer.enabled=false;}
            camera.targetTexture=rt;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            camera.fieldOfView=46;camera.nearClipPlane=.001f;
            // Choose the surface position from the mesh UVs, so the same longitude/latitude
            // is used for the cloud-free map and measured night-light map (Europe).
            var mesh=portal.earthMesh;var uv=mesh.uv;int point=0;float best=float.MaxValue;
            var target=new Vector2((12+180)/360f,(50+90)/180f);
            for(int i=0;i<uv.Length;i++){float score=(uv[i]-target).sqrMagnitude;if(score<best){best=score;point=i;}}
            var normal=surface.TransformDirection(mesh.normals[point]).normalized;float radius=portal.PlanetRadius;
            camera.transform.position=globe.position+normal*radius*2.8f;camera.transform.LookAt(globe.position,Vector3.up);
            portal.cloudsEnabled=false;portal.sunLocalPosition=portal.WorldRoot.InverseTransformPoint(globe.position-normal*radius*20);
            portal.nightLightsIntensity=0;Tick();var dark=Render("night_without_lights");
            portal.nightLightsIntensity=1.8f;Tick();var night=Render("night_cities");
            Check(Difference(dark,night)>150,"measured night cities visibly emit light");
            portal.sunLocalPosition=portal.WorldRoot.InverseTransformPoint(globe.position+normal*radius*20);
            Tick();var day=Render("day_clear");
            portal.nightLightsIntensity=0;Tick();var noDayLights=Render("day_without_emission");
            float centralDifference=0;
            for(int y=220;y<420;y++)for(int x=220;x<420;x++)centralDifference+=Mathf.Abs(day[y*640+x].grayscale-noDayLights[y*640+x].grayscale);
            Check(centralDifference/40000<.002f,"cities off on sunlit side");
            portal.cloudsEnabled=true;Tick();var cloudy=Render("day_clouds");Check(Difference(noDayLights,cloudy)>1000,"cloud layer visible");
            clouds.gameObject.SetActive(false);var shadowed=Render("cloud_shadows_only");
            Check(Difference(noDayLights,shadowed)>100,"clouds cast projected surface shadows");clouds.gameObject.SetActive(true);
            var tangent=Vector3.Cross(Vector3.up,normal).normalized;
            camera.transform.position=globe.position+normal*radius*1.065f;
            camera.transform.rotation=Quaternion.LookRotation((tangent-normal*.27f).normalized,normal);camera.fieldOfView=76;
            portal.sunLocalPosition=portal.WorldRoot.InverseTransformPoint(globe.position+(normal+tangent)*radius*20);
            portal.nightLightsIntensity=1.8f;Tick();Render("orbital_horizon");
            Check(portal.WorldRoot.GetComponentsInChildren<Light>().Length==0,"no added lights");
            Debug.Log($"Orbital Earth: {checks} checks passed; 8K surface/clouds and 4K city map, aligned city emission, daylight suppression, independent clouds, shadows, orbital horizon and controls.");
        }
        finally
        {
            foreach(var pair in hidden)if(pair.Key)pair.Key.enabled=pair.Value;
            portal.cloudsEnabled=oldCloud;portal.cloudDensity=oldDensity;portal.nightLightsIntensity=oldLights;
            portal.sunLocalPosition=oldSun;portal.earthAutoSpin=oldSpin;portal.allowEarthEntry=oldEntry;portal.appearanceDuration=oldDuration;
            typeof(GatePortal).GetField("_cloudOffset",flags).SetValue(portal,oldOffset);
            portal.SetWorldKind(oldWorld);gate.SetProgress(oldProgress);portal.SetPortalEnabled(oldEnabled);
            if(hard)Shader.EnableKeyword("HARD_OCCLUSION");if(soft)Shader.EnableKeyword("SOFT_OCCLUSION");
            RenderTexture.active=previous;camera.targetTexture=null;Object.DestroyImmediate(go);rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);
        }
    }
}
