using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class PortalMoonVerification
{
    [MenuItem("Tools/AR Gates/Verify Orbiting Moon In Play Mode")]
    public static void Verify()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Enter Play Mode first.");
        var portal=Object.FindFirstObjectByType<GatePortal>();var gate=portal.GetComponent<GateBuilder>();
        if(portal.IsInside)throw new InvalidOperationException("Return to room before testing.");
        var world=portal.worldKind;var sun=portal.sunLocalPosition;var oldHead=portal.headOverride;
        bool enabled=portal.portalEnabled,allowed=portal.allowEarthEntry,auto=portal.earthAutoSpin;
        float duration=portal.appearanceDuration,progress=gate.Progress;
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        float oldPhase=(float)typeof(GatePortal).GetField("_moonPhase",flags).GetValue(portal);
        var head=new GameObject("_MoonVerificationHead");int checks=0;
        void Check(bool ok,string reason){if(!ok)throw new Exception("Moon verification: "+reason);checks++;}
        void Call(string name,params object[] args)=>typeof(GatePortal).GetMethod(name,flags).Invoke(portal,args);
        void Tick()=>Call("Tick",Time.unscaledTime);
        void Head(float offset)
        {
            head.transform.position=portal.VisualRoot.position+portal.VisualRoot.right*offset;
            Call("SamplePortalHead",head.transform.position,true);Tick();
        }
        void Hands(GatePortal.PlanetHandSample l,GatePortal.PlanetHandSample r,bool ui=false)=>Call("ApplyCelestialHands",l,r,.02f,ui);
        var neutral=new GatePortal.PlanetHandSample{tracked=true};
        GatePortal.PlanetHandSample Palm(Vector3 center,float radius,Vector3 direction)=>new GatePortal.PlanetHandSample
            {tracked=true,open=true,palm=center+direction*radius,palmNormal=-direction};
        try
        {
            portal.headOverride=head.transform;portal.allowEarthEntry=true;portal.appearanceDuration=0;portal.earthAutoSpin=false;
            portal.SetWorldKind(GatePortal.PortalWorldKind.Earth);gate.SetWicketAngle(0);gate.SetProgress(1);portal.SetPortalEnabled(true);Tick();
            Check(portal.moonMesh && portal.moonMesh.triangles.Length/3==2880,"user Moon geometry retained");
            Check(portal.moonTexture.width==2048 && portal.moonNormalTexture.width==2048,"2K color and normal maps");
            Check(!ShaderUtil.ShaderHasError(portal.worldShader),"shader compile");
            Check(portal.MoonTransform && portal.MoonTransform.parent==portal.WorldRoot,"Moon is independent of Earth rotation hierarchy");
            Check(portal.MoonOrbitRadius>portal.PlanetRadius+portal.moonRadius,"overview Moon does not intersect giant Earth");
            Check(portal.WorldRoot.GetComponentsInChildren<MeshFilter>().Sum(f=>f.sharedMesh.triangles.Length/3)<51000,"world geometry budget");
            var start=portal.MoonTransform.position;var rotation=portal.MoonTransform.rotation;
            Call("UpdateMoonAnimation",2f);
            Check(Vector3.Distance(start,portal.MoonTransform.position)>.1f,"Moon moves along orbit");
            Check(Quaternion.Angle(rotation,portal.MoonTransform.rotation)>1,"Moon spins automatically");
            Head(-.2f);Head(.07f);Check(portal.IsInside,"enter space for hands");
            portal.ToggleEarthHandMode();Call("UpdateEarthPose",1f);Call("UpdateMoonAnimation",0f);
            Check(Mathf.Abs(Vector3.Distance(portal.MoonTransform.position,portal.EarthTransform.position)-1)<.001f,"one metre orbit in hand mode");
            Check(Mathf.Abs(portal.MoonTransform.lossyScale.y-portal.moonRadius)<.001f,"physical Moon radius independent of gate scale");
            var moon=portal.MoonTransform;var earth=portal.EarthTransform;
            var direction=-portal.VisualRoot.right;var earthRotation=earth.rotation;var moonRotation=moon.rotation;
            float planetScale=portal.PlanetScale;var gateScale=gate.transform.localScale;var gatePosition=gate.transform.position;
            Hands(Palm(moon.position,portal.moonRadius,direction),neutral);
            Check(portal.MoonHeld,"open palm stops Moon");
            float phase=portal.MoonOrbitPhase;var heldPosition=moon.position;
            Call("UpdateMoonAnimation",1f);
            Check(portal.MoonOrbitPhase==phase && Vector3.Distance(heldPosition,moon.position)<.001f,"contact freezes orbit position");
            Check(Quaternion.Angle(moonRotation,moon.rotation)<.001f,"contact freezes automatic spin");
            var turned=Quaternion.AngleAxis(20,Vector3.up)*direction;
            Hands(Palm(moon.position,portal.moonRadius,turned),neutral);
            Check(Quaternion.Angle(moonRotation,moon.rotation)>15,"left palm rotates Moon directly");
            Check(Quaternion.Angle(earthRotation,earth.rotation)<.001f && portal.PlanetScale==planetScale,"Moon contact cannot manipulate Earth");
            Check(gate.transform.localScale==gateScale && gate.transform.position==gatePosition,"Moon contact cannot move or scale gate");
            Hands(neutral,neutral);Check(!portal.MoonHeld,"release ends contact");
            phase=portal.MoonOrbitPhase;Call("UpdateMoonAnimation",.05f);
            float delta=Mathf.DeltaAngle(phase,portal.MoonOrbitPhase);
            Check(delta>0 && delta<portal.moonOrbitSpeed*.05f,"orbit resumes gradually without jumping");
            moonRotation=moon.rotation;Hands(neutral,Palm(moon.position,portal.moonRadius,direction));
            Hands(neutral,Palm(moon.position,portal.moonRadius,turned));
            Check(Quaternion.Angle(moonRotation,moon.rotation)>15,"right palm rotates Moon");
            Hands(default,default);Check(!portal.MoonHeld,"tracking loss releases contact");
            Hands(Palm(moon.position,3,direction),neutral);Check(!portal.MoonHeld,"distant hands do not stop orbit");
            var backPalm=Palm(moon.position,portal.moonRadius,direction);backPalm.palmNormal=direction;
            Hands(backPalm,neutral);Check(!portal.MoonHeld,"back of hand does not engage Moon");
            moonRotation=moon.rotation;Hands(Palm(moon.position,portal.moonRadius,direction),neutral,true);
            Check(!portal.MoonHeld && Quaternion.Angle(moonRotation,moon.rotation)<.001f,"palm menu blocks Moon manipulation");
            moonRotation=moon.rotation;earthRotation=earth.rotation;
            Hands(Palm(earth.position,portal.PlanetRadius,direction),neutral);
            Hands(Palm(earth.position,portal.PlanetRadius,turned),neutral);
            Check(Quaternion.Angle(earthRotation,earth.rotation)>15,"Earth palm rotation still works");
            Check(Quaternion.Angle(moonRotation,moon.rotation)<.001f,"Earth palm rotation does not spin Moon");
            Hands(neutral,neutral);Call("UpdateMoonAnimation",0f);
            var material=moon.GetComponentInChildren<Renderer>().sharedMaterial;
            Check(Vector3.Distance(material.GetVector("_PortalSunPosition"),portal.SunWorldPosition)<.001f,"shared Sun position");
            Capture(portal,Check);
            portal.SetWorldKind(GatePortal.PortalWorldKind.Loft);
            Check(!portal.MoonTransform && !portal.WorldRoot.Find("OrbitingMoon"),"Loft removes Moon and gesture ownership");
            portal.SetWorldKind(GatePortal.PortalWorldKind.Earth);Tick();
            Check(portal.MoonTransform && portal.WorldRoot.Cast<Transform>().Count(t=>t.name=="OrbitingMoon")==1,"return creates one Moon");
            Debug.Log($"Orbiting Moon: {checks} checks passed; source mesh, orbit, safe radius, Sun lighting, palm stop/rotation/release, gesture isolation and lifecycle.");
        }
        finally
        {
            portal.ReturnToRoom();portal.sunLocalPosition=sun;portal.earthAutoSpin=auto;portal.appearanceDuration=duration;
            typeof(GatePortal).GetField("_moonPhase",flags).SetValue(portal,oldPhase);
            portal.SetWorldKind(world);portal.SetPortalEnabled(true);Head(-.25f);
            portal.allowEarthEntry=allowed;portal.headOverride=oldHead;gate.SetProgress(progress);portal.SetPortalEnabled(enabled);
            Object.DestroyImmediate(head);
        }
    }

    static void Capture(GatePortal portal,Action<bool,string> check)
    {
        var node=new GameObject("_MoonCamera",typeof(Camera));var camera=node.GetComponent<Camera>();
        var rt=new RenderTexture(640,640,24,RenderTextureFormat.ARGB32);var image=new Texture2D(640,640,TextureFormat.RGBA32,false);
        var previous=RenderTexture.active;var hidden=new Dictionary<Renderer,bool>();var sun=portal.sunLocalPosition;
        bool hard=Shader.IsKeywordEnabled("HARD_OCCLUSION"),soft=Shader.IsKeywordEnabled("SOFT_OCCLUSION");
        try
        {
            Shader.DisableKeyword("HARD_OCCLUSION");Shader.DisableKeyword("SOFT_OCCLUSION");
            foreach(var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if(!renderer.transform.IsChildOf(portal.WorldRoot)){hidden[renderer]=renderer.enabled;renderer.enabled=false;}
            foreach(var renderer in portal.WorldRoot.GetComponentsInChildren<Renderer>())
            {var m=renderer.sharedMaterial;m.SetFloat("_PortalInside",1);m.SetFloat("_PortalTravel",1);m.SetFloat("_PortalCompare",8);m.SetFloat("_Reveal",1);}
            camera.targetTexture=rt;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
            camera.nearClipPlane=.005f;camera.fieldOfView=42;
            var moon=portal.MoonTransform;var material=moon.GetComponentInChildren<Renderer>().sharedMaterial;
            var facing=-portal.VisualRoot.right;
            camera.transform.position=moon.position+facing*.3f;camera.transform.LookAt(moon.position,Vector3.up);
            Color[] Render(string label)
            {
                camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,640,640),0,0);image.Apply();
                File.WriteAllBytes("D:/blender/chatgpt/moon_"+label+".png",image.EncodeToPNG());return image.GetPixels();
            }
            material.SetVector("_PortalSunPosition",moon.position+facing*10);var day=Render("day");
            material.SetVector("_PortalSunPosition",moon.position-facing*10);var night=Render("night");
            float lit=0,dark=0;for(int y=280;y<360;y++)for(int x=280;x<360;x++){lit+=day[y*640+x].grayscale;dark+=night[y*640+x].grayscale;}
            check(lit>dark*4,"rendered Moon day and night follow Sun");
            material.SetVector("_PortalSunPosition",portal.SunWorldPosition);
            var center=(portal.EarthTransform.position+moon.position)*.5f;
            camera.transform.position=center-portal.VisualRoot.right*1.8f+Vector3.up*.4f;camera.transform.LookAt(center,Vector3.up);camera.fieldOfView=64;
            Render("earth_and_moon");
        }
        finally
        {
            portal.sunLocalPosition=sun;foreach(var pair in hidden)if(pair.Key)pair.Key.enabled=pair.Value;
            if(hard)Shader.EnableKeyword("HARD_OCCLUSION");if(soft)Shader.EnableKeyword("SOFT_OCCLUSION");
            RenderTexture.active=previous;camera.targetTexture=null;Object.DestroyImmediate(node);rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);
        }
    }
}
