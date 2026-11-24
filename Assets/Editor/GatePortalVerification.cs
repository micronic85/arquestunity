using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class GatePortalVerification
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Tick(GatePortal portal,float time)=>typeof(GatePortal).GetMethod("Tick",Hidden).Invoke(portal,new object[]{time});
    static void Tab(GateColorPicker picker)
    {
        typeof(GateColorPicker).GetField("_tab",Hidden).SetValue(picker,GateColorPicker.Tab.Gate);
        typeof(GateColorPicker).GetMethod("RebuildMenu",Hidden).Invoke(picker,null);
    }
    static void Press(GateColorPicker picker,string name)
    {
        var buttons=(IList)typeof(GateColorPicker).GetField("_buttons",Hidden).GetValue(picker);
        var button=buttons.Cast<object>().First(b=>((Transform)b.GetType().GetField("t").GetValue(b)).name==name);
        ((Action)button.GetType().GetField("press").GetValue(button)).Invoke();
    }

    [MenuItem("Tools/AR Gates/Verify And Capture Portal In Play Mode")]
    public static void Verify()
    {
        if(!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        var gate=Object.FindFirstObjectByType<GateBuilder>();var portal=gate.GetComponent<GatePortal>();
        var picker=gate.GetComponent<GateColorPicker>();
        var kind=gate.gateKind;float w=gate.openingWidth,h=gate.openingHeight,p=gate.Progress;
        var pos=gate.transform.position;var rot=gate.transform.rotation;var scale=gate.transform.localScale;
        bool enabled=portal.portalEnabled;float duration=portal.appearanceDuration;var worldKind=portal.worldKind;
        int checks=0;
        void Check(bool condition,string name)
        {
            if(!condition) throw new InvalidOperationException("Portal verification failed: "+name);
            checks++;
        }
        try
        {
            Check(!ShaderUtil.ShaderHasError(portal.maskShader)&&!ShaderUtil.ShaderHasError(portal.worldShader)
                &&!ShaderUtil.ShaderHasError(portal.energyShader),"shader compilation");
            Check(portal.smokeTexture && portal.smokeTexture.width==1024,"serialized Blender smoke texture");
            portal.appearanceDuration=0;
            gate.StopAnimation();gate.SetWicketAngle(0);gate.Build(3.5f,2.65f);
            int cameras=Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length;
            Tab(picker);portal.SetPortalEnabled(false);Press(picker,"PortalToggleBtn");
            Check(portal.portalEnabled,"menu toggle");
            var visual=portal.VisualRoot;
            Check(portal.RimRoot.GetComponent<Renderer>().sharedMaterial.GetTexture("_SmokeTex")==portal.smokeTexture,
                "main cloud uses baked smoke");
            Check(portal.RimRoot.GetComponent<MeshFilter>().sharedMesh.bounds.size.x>.05f,
                "smoke shell has depth");
            if(portal.worldKind==GatePortal.PortalWorldKind.Loft && portal.bakedWorldMesh)
            {
                Check(portal.bakedWorldMesh.triangles.Length/3<=350000,"loft triangle budget");
                Check(portal.bakedWorldTexture && portal.bakedWorldTexture.width==4096,"loft baked atlas");
                Check(portal.WorldRoot.childCount==1,"one static loft renderer replaces prototype world");
                var loft=portal.WorldRoot.GetComponentInChildren<MeshFilter>();
                Check(loft.sharedMesh==portal.bakedWorldMesh,"shared optimized loft mesh");
                Check(loft.sharedMesh.uv.Length==loft.sharedMesh.vertexCount,"loft UVs survived import");
                var material=loft.GetComponent<Renderer>().sharedMaterial;
                Check(material.GetFloat("_BakedWorld")>.5f && material.GetTexture("_BaseMap")==portal.bakedWorldTexture,
                    "loft uses baked stencil world shader");
                Check(portal.WorldRoot.GetComponentsInChildren<Light>().Length==0
                    &&portal.WorldRoot.GetComponentsInChildren<Camera>().Length==0,"no additional loft lights or cameras");
            }
            if(portal.earthMesh)
            {
                var originalWorld=portal.worldKind;
                Check(portal.earthMesh.triangles.Length/3==3968,"original Earth medium mesh");
                Press(picker,"PortalWorldBtn");
                Check(portal.worldKind!=originalWorld,"menu world selector");
                Press(picker,"PortalWorldBtn");
                Check(portal.worldKind==originalWorld,"menu returns to original world");
                portal.SetWorldKind(GatePortal.PortalWorldKind.Loft);
                Check(portal.WorldRoot.childCount==1 && portal.bakedWorldMesh,"loft preserved as alternative");
                portal.SetWorldKind(GatePortal.PortalWorldKind.Earth);
                var earth=portal.WorldRoot.Find("RotatingEarth");
                Check(earth && portal.WorldRoot.GetComponentsInChildren<Renderer>().Length==3,"Earth, atmosphere and starfield");
                Check(earth.GetComponentInChildren<MeshFilter>().sharedMesh==portal.earthMesh,"original Asset Store mesh reused");
                Tick(portal,1);var earthRotation=earth.localRotation;Tick(portal,3);
                Check(Quaternion.Angle(earthRotation,earth.localRotation)>5,"Earth rotation");
                Check(portal.WorldRoot.GetComponentsInChildren<Light>().Length==0
                    &&portal.WorldRoot.GetComponentsInChildren<Camera>().Length==0,"Earth uses no extra cameras or lights");
                Check(portal.WorldRoot.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3)<10000,
                    "Earth scene stays below 10000 triangles");
            }
            var puffRenderer=visual.Find("VolumetricSmokePuffs").GetComponent<MeshFilter>();
            var puffCenters=new List<Vector4>();puffRenderer.sharedMesh.GetUVs(1,puffCenters);
            Check(puffCenters.Count==96*4,"96 smoke puffs use one mesh");
            Check(puffCenters.Max(c=>c.x)-puffCenters.Min(c=>c.x)>.30f,"puff centers occupy real depth");
            foreach(var type in new[]{GateBuilder.GateKind.Sectional,GateBuilder.GateKind.Roller})
            {
                gate.SetGateType(type);gate.SetProgress(0);Tick(portal,10);
                Check(portal.Reveal==0&&!visual.gameObject.activeSelf,"closed gate hides portal: "+type);
                gate.SetProgress(.1f);Tick(portal,11);
                Check(portal.Reveal>0&&portal.Reveal<1,"partial reveal: "+type);
                gate.SetProgress(1);Tick(portal,12);
                Check(portal.Reveal==1&&visual.gameObject.activeSelf,"open reveal: "+type);
                Check(portal.VisualRoot==visual&&visual.parent==gate.transform,"survives rebuild: "+type);
                Check(portal.ApertureRadius<Mathf.Min(gate.openingWidth,gate.openingHeight)*.5f,"aperture fits opening");
                var maskRotation=portal.MaskRoot.rotation;var rimRotation=portal.RimRoot.rotation;
                Tick(portal,13);
                Check(Quaternion.Angle(maskRotation,portal.MaskRoot.rotation)<.001f
                    &&Quaternion.Angle(rimRotation,portal.RimRoot.rotation)>5,"only rim rotates");
                gate.Build(2f,2f);Tick(portal,14);
                Check(portal.VisualRoot==visual&&portal.ApertureRadius<1,"resize");
                gate.Build(3.5f,2.65f);
            }
            gate.SetProgress(1);Tick(portal,15);
            var before=visual.position;gate.transform.position+=new Vector3(.3f,.2f,-.1f);Tick(portal,16);
            Check(Vector3.Distance(visual.position-before,new Vector3(.3f,.2f,-.1f))<.001f,"follows placement");
            gate.transform.SetPositionAndRotation(pos,rot);gate.FlipSide();Tick(portal,17);
            Check(Vector3.Dot(visual.right,gate.transform.right)>.999f,"follows mounting side");
            gate.transform.SetPositionAndRotation(pos,rot);
            Press(picker,"PortalReplayBtn");
            Check(portal.portalEnabled&&gate.Progress<.1f&&gate.OpenOrOpening,"menu replay starts opening from closed");
            gate.StopAnimation();
            Check(Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Length==cameras,"no secondary camera");
            portal.SetPortalEnabled(false);
            Check(!visual.gameObject.activeSelf&&portal.Reveal==0,"disable removes world and ring");
            portal.SetPortalEnabled(true);gate.SetProgress(1);Tick(portal,20);
            Check(gate.transform.Cast<Transform>().Count(t=>t.name=="GatePortalVisual")==1,"no duplicate visual roots");
            Capture(gate,portal,Check);
            string report="Portal: "+checks+" checks passed; stencil clipping, camera parallax, replay, resize, both gate types; 3D smoke puffs, clear center, grazing angle and selectable Earth/Loft worlds.\n";
            File.AppendAllText("D:/blender/chatgpt/unity-maintenance-report.txt",report);Debug.Log(report);
        }
        finally
        {
            gate.StopAnimation();gate.transform.SetPositionAndRotation(pos,rot);gate.transform.localScale=scale;
            gate.SetGateType(kind);gate.Build(w,h);gate.SetProgress(p);
            portal.SetWorldKind(worldKind);portal.appearanceDuration=duration;portal.SetPortalEnabled(enabled);Tab(picker);
        }
    }

    static void Capture(GateBuilder gate,GatePortal portal,Action<bool,string> check)
    {
        var states=new Dictionary<Renderer,bool>();
        var previous=RenderTexture.active;
        var go=new GameObject("_PortalVerificationCamera",typeof(Camera));var camera=go.GetComponent<Camera>();
        var rt=new RenderTexture(1000,900,24);var image=new Texture2D(1000,900,TextureFormat.RGB24,false);
        bool hard=Shader.IsKeywordEnabled("HARD_OCCLUSION"),soft=Shader.IsKeywordEnabled("SOFT_OCCLUSION");
        try
        {
            Shader.DisableKeyword("HARD_OCCLUSION");Shader.DisableKeyword("SOFT_OCCLUSION");
            foreach(var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if(!renderer.transform.IsChildOf(gate.transform)){states[renderer]=renderer.enabled;renderer.enabled=false;}
            camera.targetTexture=rt;camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.12f,.14f,.17f);camera.fieldOfView=40;camera.nearClipPlane=.05f;
            var center=portal.VisualRoot.position;
            void CameraAt(float z)
            {
                camera.transform.position=center+gate.transform.TransformVector(new Vector3(-5,0,z));
                camera.transform.rotation=Quaternion.LookRotation(gate.transform.right,gate.transform.up);
            }
            Color32[] Render(string name)
            {
                camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,1000,900),0,0);image.Apply();
                if(name!=null)File.WriteAllBytes("D:/blender/chatgpt/unity_portal_"+name+".png",image.EncodeToPNG());
                return image.GetPixels32();
            }
            // Compare with and without the world. Changed pixels must lie inside the projected circular mask.
            CameraAt(0);gate.SetProgress(1);Tick(portal,30);
            portal.WorldRoot.gameObject.SetActive(false);var empty=Render(null);
            portal.WorldRoot.gameObject.SetActive(true);var full=Render("front");
            int changed=0,outside=0;
            var screenCenter=camera.WorldToScreenPoint(center);
            float radius=Vector3.Distance(camera.WorldToScreenPoint(center+gate.transform.up*portal.ApertureRadius),screenCenter);
            for(int y=0;y<900;y++)for(int x=0;x<1000;x++)
            {
                int i=y*1000+x;
                int diff=Math.Abs(full[i].r-empty[i].r)+Math.Abs(full[i].g-empty[i].g)+Math.Abs(full[i].b-empty[i].b);
                if(diff<=10)continue;
                changed++;
                if(Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(screenCenter.x,screenCenter.y))>radius+3)outside++;
            }
            check(changed>10000,"world is visible through aperture");check(outside==0,"world cannot leak outside aperture: "+outside);
            var smoke=portal.VisualRoot.GetComponentsInChildren<Renderer>()
                .Where(r=>r.sharedMaterial.shader==portal.energyShader).ToArray();
            check(smoke.Length==2,"two smoke layers replace the old ring");
            foreach(var renderer in smoke)renderer.enabled=false;
            var noSmoke=Render(null);
            foreach(var renderer in smoke)renderer.enabled=true;
            int smokePixels=0,centerPixels=0;
            for(int y=0;y<900;y++)for(int x=0;x<1000;x++)
            {
                int i=y*1000+x;
                int diff=Math.Abs(full[i].r-noSmoke[i].r)+Math.Abs(full[i].g-noSmoke[i].g)+Math.Abs(full[i].b-noSmoke[i].b);
                if(diff<=10)continue;
                smokePixels++;
                if(Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(screenCenter.x,screenCenter.y))<radius*.65f)centerPixels++;
            }
            check(smokePixels>5000,"baked cloud visibly replaces ring: "+smokePixels);
            check(centerPixels==0,"cloud leaves the center clear: "+centerPixels);
            CameraAt(-.032f);var left=Render("left");CameraAt(.032f);var right=Render("right");
            int parallax=0;
            for(int y=270;y<630;y++)for(int x=320;x<680;x++)
            {
                int i=y*1000+x;
                if(Math.Abs(left[i].r-right[i].r)+Math.Abs(left[i].g-right[i].g)+Math.Abs(left[i].b-right[i].b)>10)parallax++;
            }
            check(parallax>100,"camera translation changes interior perspective: "+parallax);
            CameraAt(1.3f);camera.transform.LookAt(center,gate.transform.up);Render("angle");
            camera.transform.position=center+gate.transform.TransformVector(new Vector3(-2.8f,0,.55f));
            camera.transform.LookAt(center,gate.transform.up);Render("close");
            // Nearly edge-on, matching the headset feedback: puffs must still make a broad body.
            camera.transform.position=center+gate.transform.TransformVector(new Vector3(-.85f,0,4.2f));
            camera.transform.LookAt(center,gate.transform.up);
            var side=Render("grazing");
            var puffs=smoke.Single(r=>r.transform.name=="VolumetricSmokePuffs");
            puffs.enabled=false;var thinSide=Render(null);puffs.enabled=true;
            int sidePixels=0;int minX=1000,maxX=0;
            for(int y=80;y<820;y++)for(int x=0;x<1000;x++)
            {
                int i=y*1000+x;
                int diff=Math.Abs(side[i].r-thinSide[i].r)+Math.Abs(side[i].g-thinSide[i].g)+Math.Abs(side[i].b-thinSide[i].b);
                if(diff<=10)continue;
                sidePixels++;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);
            }
            check(sidePixels>3000,"cloud stays visible nearly edge-on: "+sidePixels);
            check(maxX-minX>60,"cloud has visible thickness nearly edge-on: "+(maxX-minX));
            CameraAt(0);gate.SetProgress(.5f);Tick(portal,31);Render("half");
            gate.SetProgress(0);Tick(portal,32);Render("closed");
            gate.SetProgress(1);Tick(portal,33);
            camera.transform.position=center+gate.transform.right*5;camera.transform.LookAt(center,gate.transform.up);
            portal.WorldRoot.gameObject.SetActive(true);var back=Render("back");
            portal.WorldRoot.gameObject.SetActive(false);var noWorld=Render(null);
            check(back.SequenceEqual(noWorld),"portal world is invisible from behind");
            portal.WorldRoot.gameObject.SetActive(true);
        }
        finally
        {
            if(portal.WorldRoot)portal.WorldRoot.gameObject.SetActive(true);
            if(hard)Shader.EnableKeyword("HARD_OCCLUSION");if(soft)Shader.EnableKeyword("SOFT_OCCLUSION");
            foreach(var state in states)if(state.Key)state.Key.enabled=state.Value;
            RenderTexture.active=previous;camera.targetTexture=null;Object.DestroyImmediate(go);
            rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);
        }
    }
}
