using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class PortalSunVerification
{
    [MenuItem("Tools/AR Gates/Verify Portal Sun In Play Mode")]
    public static void Verify()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Enter Play Mode first.");
        var portal=Object.FindFirstObjectByType<GatePortal>();var gate=portal.GetComponent<GateBuilder>();
        if(portal.IsInside)throw new InvalidOperationException("Return to room first.");
        var world=portal.worldKind;var sun=portal.sunLocalPosition;bool enabled=portal.portalEnabled,entry=portal.allowEarthEntry;
        float duration=portal.appearanceDuration,progress=gate.Progress;
        var cameraNode=new GameObject("_SunVerificationCamera",typeof(Camera));var camera=cameraNode.GetComponent<Camera>();
        var rt=new RenderTexture(512,512,24,RenderTextureFormat.ARGB32);var image=new Texture2D(512,512,TextureFormat.RGBA32,false);
        var previous=RenderTexture.active;var hidden=new Dictionary<Renderer,bool>();
        bool hard=Shader.IsKeywordEnabled("HARD_OCCLUSION"),soft=Shader.IsKeywordEnabled("SOFT_OCCLUSION");
        int count=0;void Check(bool ok,string text){if(!ok)throw new Exception("Portal sun: "+text);count++;}
        void Tick()=>typeof(GatePortal).GetMethod("Tick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(portal,new object[]{Time.unscaledTime});
        void InsideMaterials()
        {
            foreach(var renderer in portal.WorldRoot.GetComponentsInChildren<Renderer>())
            {var m=renderer.sharedMaterial;m.SetFloat("_PortalInside",1);m.SetFloat("_PortalTravel",1);m.SetFloat("_PortalCompare",8);m.SetFloat("_Reveal",1);}
        }
        Color Render(string name)
        {
            camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,512,512),0,0);image.Apply();
            File.WriteAllBytes("D:/blender/chatgpt/portal_sun_"+name+".png",image.EncodeToPNG());
            Color total=Color.black;for(int y=246;y<266;y++)for(int x=246;x<266;x++)total+=image.GetPixel(x,y);
            return total/400;
        }
        try
        {
            Shader.DisableKeyword("HARD_OCCLUSION");Shader.DisableKeyword("SOFT_OCCLUSION");
            portal.allowEarthEntry=false;portal.appearanceDuration=0;portal.SetWorldKind(GatePortal.PortalWorldKind.Earth);
            portal.SetPortalEnabled(true);gate.SetProgress(1);Tick();
            Check(!ShaderUtil.ShaderHasError(portal.worldShader),"shader compilation");
            Check(portal.WorldRoot.GetComponentsInChildren<Light>().Length==0,"no extra lights");
            Check(portal.WorldRoot.GetComponentsInChildren<Camera>().Length==0,"no extra world cameras");
            var surface=portal.EarthTransform.Find("EarthSurface");var material=surface.GetComponent<Renderer>().sharedMaterial;
            Check(surface.GetComponent<MeshFilter>().sharedMesh.triangles.Length==portal.earthMesh.triangles.Length*4,"original sphere refined");
            var position=portal.SunWorldPosition;
            var rotation=portal.EarthTransform.localRotation;
            portal.EarthTransform.Rotate(37,81,13);Tick();
            Check(Vector3.Distance(position,portal.SunWorldPosition)<.0001f,"sun does not follow globe rotation");
            portal.EarthTransform.localRotation=rotation;
            Check(Vector3.Distance(material.GetVector("_PortalSunPosition"),position)<.0001f,"surface follows sun position");
            Check(Vector3.Distance(portal.WorldRoot.Find("Starfield").GetComponent<Renderer>().sharedMaterial.GetVector("_PortalSunPosition"),position)<.0001f,"visible sun matches lighting");
            foreach(var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if(!renderer.transform.IsChildOf(portal.WorldRoot)){hidden[renderer]=renderer.enabled;renderer.enabled=false;}
            camera.targetTexture=rt;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            camera.nearClipPlane=.01f;camera.fieldOfView=48;
            camera.transform.position=portal.EarthTransform.position-portal.VisualRoot.right*4.5f*portal.ApertureRadius;
            camera.transform.LookAt(portal.EarthTransform.position,Vector3.up);
            portal.sunLocalPosition=portal.WorldRoot.InverseTransformPoint(portal.EarthTransform.position-portal.VisualRoot.right*20);
            Tick();InsideMaterials();var day=Render("day");
            portal.sunLocalPosition=portal.WorldRoot.InverseTransformPoint(portal.EarthTransform.position+portal.VisualRoot.right*20);
            Tick();InsideMaterials();var night=Render("night");
            Check(day.grayscale>night.grayscale*4,"day is visibly brighter than night from fixed viewpoint");
            portal.sunLocalPosition=sun;Tick();InsideMaterials();
            camera.transform.position=portal.VisualRoot.position+portal.VisualRoot.right*.15f;
            camera.transform.LookAt(portal.SunWorldPosition,Vector3.up);Render("distant_sun");var disk=image.GetPixel(256,256);
            Check(disk.r>.85f && disk.g>.7f,"visible bright sun disk at actual light position");
            camera.transform.LookAt(portal.EarthTransform.position,Vector3.up);Render("earth_with_sunlight");
            portal.SetWorldKind(GatePortal.PortalWorldKind.Loft);Tick();
            Check(portal.WorldRoot.GetComponentInChildren<Renderer>().sharedMaterial.GetFloat("_SunAngularRadius")==0,"loft has no sky sun");
            Debug.Log($"Portal sun: {count} checks passed; visible disk, day/night rendering, fixed sun, shared lighting position and original mesh.");
        }
        finally
        {
            foreach(var pair in hidden)if(pair.Key)pair.Key.enabled=pair.Value;
            portal.sunLocalPosition=sun;portal.allowEarthEntry=entry;portal.appearanceDuration=duration;
            portal.SetWorldKind(world);gate.SetProgress(progress);portal.SetPortalEnabled(enabled);
            if(hard)Shader.EnableKeyword("HARD_OCCLUSION");if(soft)Shader.EnableKeyword("SOFT_OCCLUSION");
            RenderTexture.active=previous;camera.targetTexture=null;Object.DestroyImmediate(cameraNode);
            rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);
        }
    }
}
