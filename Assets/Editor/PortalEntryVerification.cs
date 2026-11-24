using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class PortalEntryVerification
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Hidden).Invoke(target,args);
    static object Field(object target,string name)=>target.GetType().GetField(name,Hidden).GetValue(target);
    static object Button(GateColorPicker picker,string name)=>((IList)Field(picker,"_buttons")).Cast<object>()
        .FirstOrDefault(b=>((Transform)b.GetType().GetField("t").GetValue(b)).name==name);
    static void Press(GateColorPicker picker,string name)
    {
        var button=Button(picker,name)??throw new InvalidOperationException("Button not found: "+name);
        ((Action)button.GetType().GetField("press").GetValue(button)).Invoke();
    }

    [MenuItem("Tools/AR Gates/Verify Portal Entry And Planet Hands In Play Mode")]
    public static void Verify()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Enter Play Mode first.");
        var portal=Object.FindFirstObjectByType<GatePortal>();var gate=portal.GetComponent<GateBuilder>();
        var mover=gate.GetComponent<GateMover>();var picker=gate.GetComponent<GateColorPicker>();
        if(portal.IsInside)throw new InvalidOperationException("Return to the room before testing.");
        var controller=gate.GetComponent<GateController>();
        var oldKind=gate.gateKind;var oldWorld=portal.worldKind;
        float oldW=gate.openingWidth,oldH=gate.openingHeight,oldP=gate.Progress;
        var oldPos=gate.transform.position;var oldRot=gate.transform.rotation;var oldScale=gate.transform.localScale;
        bool oldEnabled=portal.portalEnabled,oldAllowed=portal.allowEarthEntry,oldAuto=portal.earthAutoSpin,oldLocked=mover.IsPlacementLocked;
        float oldDuration=portal.appearanceDuration;var oldHead=portal.headOverride;
        var head=new GameObject("_PortalHeadVerification");
        var room=GameObject.CreatePrimitive(PrimitiveType.Cube);room.name="_RoomVisibilityVerification";
        var roomRenderer=room.GetComponent<Renderer>();
        var disabledRoom=GameObject.CreatePrimitive(PrimitiveType.Cube);disabledRoom.name="_DisabledRoomVerification";
        var disabledRenderer=disabledRoom.GetComponent<Renderer>();disabledRenderer.enabled=false;
        int checks=0;
        void Check(bool ok,string message)
        {
            if(!ok)throw new InvalidOperationException("Portal entry verification failed: "+message);
            checks++;
        }
        void Tick()=>Call(portal,"Tick",Time.unscaledTime);
        Vector3 Position(float d,float z=0)=>portal.VisualRoot.position+portal.VisualRoot.right*d+portal.VisualRoot.forward*z;
        void Head(float d,float z=0)
        {
            head.transform.position=Position(d,z);Call(portal,"SamplePortalHead",head.transform.position,true);Tick();
        }
        void Neutral()=>Call(portal,"ApplyPlanetHands",new GatePortal.PlanetHandSample{tracked=true},
            new GatePortal.PlanetHandSample{tracked=true},.02f,false);
        void Hands(GatePortal.PlanetHandSample l,GatePortal.PlanetHandSample r,float dt=.02f,bool ui=false)
            =>Call(portal,"ApplyPlanetHands",l,r,dt,ui);
        GatePortal.PlanetHandSample Palm(Vector3 direction)=>new GatePortal.PlanetHandSample
            {tracked=true,open=true,palm=portal.EarthTransform.position+direction*portal.PlanetRadius};
        GatePortal.PlanetHandSample Point(float offset)=>new GatePortal.PlanetHandSample
            {tracked=true,pointing=true,index=portal.EarthTransform.position+portal.VisualRoot.forward*offset};
        try
        {
            portal.allowEarthEntry=true;portal.appearanceDuration=0;portal.earthAutoSpin=true;portal.headOverride=head.transform;
            gate.SetWicketAngle(0);gate.StopAnimation();gate.SetGateType(GateBuilder.GateKind.Roller);gate.Build(3.5f,2.65f);
            gate.transform.localScale=Vector3.one*1.15f;mover.SetPlacementLocked(false);
            portal.SetWorldKind(GatePortal.PortalWorldKind.Earth);portal.SetPortalEnabled(true);gate.SetProgress(1);Tick();
            foreach(var sample in new[]{(2f,2f,.75f),(3.5f,2.65f,1f),(5f,4.5f,1.4f)})
            {
                gate.Build(sample.Item1,sample.Item2);gate.transform.localScale=Vector3.one*sample.Item3;Tick();
                var p=portal.EarthTransform.position;float r=portal.PlanetRadius;
                Call(portal,"LoseTravelTracking");Head(-.2f);Head(.07f);Call(portal,"UpdateEarthPose",1f);
                Check(portal.IsInside && Vector3.Distance(p,portal.EarthTransform.position)<.001f
                    &&Mathf.Abs(r-portal.PlanetRadius)<.001f,"entry continuity for gate size/scale "+sample);
                Head(-.1f);Call(portal,"UpdateEarthPose",1f);
                Check(!portal.IsInside && Vector3.Distance(p,portal.EarthTransform.position)<.001f
                    &&Mathf.Abs(r-portal.PlanetRadius)<.001f,"exit continuity for gate size/scale "+sample);
            }
            gate.Build(3.5f,2.65f);gate.transform.localScale=Vector3.one*1.15f;Tick();
            Check(!ShaderUtil.ShaderHasError(portal.worldShader)&&!ShaderUtil.ShaderHasError(portal.maskShader)
                &&!ShaderUtil.ShaderHasError(portal.energyShader),"shader compile");
            Call(portal,"LoseTravelTracking");Head(.1f);Head(.2f);
            Check(!portal.IsInside,"spawning behind portal does not enter");
            Call(portal,"LoseTravelTracking");Head(-.3f);Head(.5f);
            Check(!portal.IsInside,"tracking pose jump cannot enter portal");
            Call(portal,"LoseTravelTracking");float side=portal.ApertureRadius*1.15f+1;
            Head(-.2f,side);Head(-.2f,side);Head(.1f,side);
            Check(!portal.IsInside,"going around portal does not enter");
            Call(portal,"LoseTravelTracking");float edge=portal.ApertureRadius*1.15f-.02f;
            Head(-.2f,edge);Head(.1f,edge);
            Check(!portal.IsInside,"head clearance prevents entry through the edge");
            Call(portal,"LoseTravelTracking");gate.SetProgress(.5f);Head(-.2f);Head(.1f);
            Check(!portal.IsInside,"partially closed gates block entry");
            gate.SetProgress(1);Call(portal,"LoseTravelTracking");Head(-.2f);
            Capture(portal,Check,false);
            for(int i=0;i<3;i++){Head(.02f);Head(-.02f);}
            Check(!portal.IsInside,"entry hysteresis rejects head jitter");
            var fixedPos=gate.transform.position;var fixedRot=gate.transform.rotation;var fixedScale=gate.transform.localScale;
            bool grab=controller.handleGrab && controller.handleGrab.enabled,handGrab=controller.handleHandGrab && controller.handleHandGrab.enabled;
            var overviewPosition=portal.EarthTransform.position;float overviewRadius=portal.PlanetRadius;
            Head(.07f);Call(portal,"UpdateEarthPose",1f);
            Check(!portal.EarthInHandMode && Vector3.Distance(overviewPosition,portal.EarthTransform.position)<.001f
                &&Mathf.Abs(overviewRadius-portal.PlanetRadius)<.001f,"entry preserves Earth world position and radius");
            Check(portal.IsInside,"entry through open circle");
            Check(gate.IsPortalInteractionLocked && mover.PortalSuspended,"separate gate suspension");
            Check(!mover.IsPlacementLocked && !mover.CanMove && !mover.CanRotate && !mover.CanScale,"original unlocked state retained but ineffective");
            Check(!roomRenderer.enabled && !disabledRenderer.enabled,"room renderers hidden");
            Check((!controller.handleGrab || !controller.handleGrab.enabled)
                &&(!controller.handleHandGrab || !controller.handleHandGrab.enabled),"handle interactions disabled");
            Check(Button(picker,"PortalReturnBtn")!=null && Button(picker,"PlanetAutoBtn")!=null
                && Button(picker,"PlanetResetBtn")!=null && Button(picker,"Tab_2")==null,"interior controls replace gate controls");
            Check(Button(picker,"PlanetCloudBtn")!=null && Button(picker,"PlanetDensityBtn")!=null,"weather controls in palm menu");
            bool cloudsBefore=portal.cloudsEnabled;float densityBefore=portal.cloudDensity;
            var weatherScale=portal.PlanetScale;
            Press(picker,"PlanetCloudBtn");Check(portal.cloudsEnabled!=cloudsBefore,"cloud menu toggles off");
            Press(picker,"PlanetCloudBtn");Check(portal.cloudsEnabled==cloudsBefore,"cloud menu toggles back");
            portal.cloudDensity=1;Press(picker,"PlanetDensityBtn");Check(portal.cloudDensity==1.5f,"weather menu sets dense clouds");
            Press(picker,"PlanetDensityBtn");Check(portal.cloudDensity==.5f,"weather menu sets sparse clouds");
            Press(picker,"PlanetDensityBtn");Check(portal.cloudDensity==1,"weather menu restores normal clouds");
            portal.cloudDensity=densityBefore;
            Check(portal.PlanetScale==weatherScale && gate.transform.localScale==fixedScale,"weather controls preserve globe and gate scale");
            var headPose=head.transform.position;
            gate.Build(7,7);gate.Fit(8,8);gate.PlaceInOpening(Vector3.one*20,Vector3.forward);gate.FlipSide();
            gate.SetGateType(GateBuilder.GateKind.Sectional);gate.AnimateTo(0);gate.SetProgress(0);gate.SetWicketAngle(45);
            mover.ResetScale();mover.SetPlacementLocked(true);Call(mover,"Apply");
            Check(gate.openingWidth==3.5f && gate.openingHeight==2.65f && gate.IsRoller && gate.Progress==1 && !gate.WicketOpen,
                "gate APIs cannot change dimensions, type or opening inside");
            Check(gate.transform.position==fixedPos && gate.transform.rotation==fixedRot && gate.transform.localScale==fixedScale,
                "gate pose and scale stay fixed");
            Check(!mover.IsPlacementLocked,"placement preference cannot be overwritten inside");
            Check(head.transform.position==headPose,"camera/head pose is never teleported");
            for(float z=.2f;z<side;z+=.2f)Head(.07f,z);
            Head(.07f,side);Head(-.07f,side);
            Check(portal.IsInside,"crossing outside circle does not exit space");
            Head(.07f,side);for(float z=side;z>0;z-=.2f)Head(.07f,z);Head(.07f);
            Check(Button(picker,"PlanetHandBtn")!=null,"explicit hand interaction button exists");
            Press(picker,"PlanetHandBtn");Call(portal,"UpdateEarthPose",1f);
            Check(portal.EarthInHandMode && Mathf.Abs(portal.PlanetRadius-portal.insideEarthRadius)<.001f,"explicit approach gives reachable globe independent of gate scale");
            Check(Vector3.Distance(head.transform.position,portal.EarthTransform.position)<.85f,"globe within arm reach");
            float handRadius=portal.PlanetRadius;var handPosition=portal.EarthTransform.position;
            Head(-.1f);Call(portal,"UpdateEarthPose",1f);
            Check(!portal.IsInside && portal.EarthInHandMode && Mathf.Abs(portal.PlanetRadius-handRadius)<.001f
                &&Vector3.Distance(handPosition,portal.EarthTransform.position)<.001f,"exit also preserves explicit hand-mode pose");
            Head(-.2f);Head(.07f);Call(portal,"UpdateEarthPose",1f);
            Check(portal.IsInside && portal.EarthInHandMode && Mathf.Abs(portal.PlanetRadius-handRadius)<.001f
                &&Vector3.Distance(handPosition,portal.EarthTransform.position)<.001f,"reentry preserves selected hand-mode pose");
            Neutral();var neutral=new GatePortal.PlanetHandSample{tracked=true};
            var direction=-portal.VisualRoot.right;var q=portal.EarthTransform.rotation;
            Hands(Palm(direction),neutral);Hands(Palm(Quaternion.AngleAxis(20,Vector3.up)*direction),neutral);
            Check(Quaternion.Angle(q,portal.EarthTransform.rotation)>15,"left palm rotates globe");
            Check(!portal.earthAutoSpin,"palm contact pauses automatic rotation");
            q=portal.EarthTransform.rotation;Hands(neutral,Palm(direction));
            Hands(neutral,Palm(Quaternion.AngleAxis(18,portal.VisualRoot.forward)*direction));
            Check(Quaternion.Angle(q,portal.EarthTransform.rotation)>12,"right palm rotates globe");
            q=portal.EarthTransform.rotation;
            Hands(new GatePortal.PlanetHandSample{tracked=true,open=true,palm=portal.EarthTransform.position+direction*3},neutral);
            Check(Quaternion.Angle(q,portal.EarthTransform.rotation)<.01f,"distant hand does not rotate globe");
            Hands(default,default);
            Check(Quaternion.Angle(q,portal.EarthTransform.rotation)<.01f,"tracking loss does not rotate globe");
            Neutral();Hands(Point(-.14f),Point(.14f),.1f);Hands(Point(-.14f),Point(.14f),.1f);
            Check(portal.IsPlanetScaling,"two pointing fingers engage scale after hold");
            q=portal.EarthTransform.rotation;Hands(Point(-.20f),Point(.20f));
            Check(Mathf.Abs(portal.PlanetScale-.4f/.28f)<.001f,"fingers apart increase scale proportionally");
            Check(Quaternion.Angle(q,portal.EarthTransform.rotation)<.01f,"scale excludes palm rotation");
            Hands(Point(-.26f),Point(.26f));Check(portal.PlanetScale==portal.planetMaxScale,"maximum globe scale");
            foreach(float span in new[]{.4f,.28f,.16f,.08f})Hands(Point(-span*.5f),Point(span*.5f));
            Check(portal.PlanetScale==portal.planetMinScale,"minimum globe scale");
            float chosenRadius=portal.PlanetRadius,chosenScale=portal.PlanetScale;
            Head(-.1f);Head(-.2f);Head(.07f);Call(portal,"UpdateEarthPose",1f);
            Check(portal.IsInside && Mathf.Abs(portal.PlanetScale-chosenScale)<.001f && Mathf.Abs(portal.PlanetRadius-chosenRadius)<.001f,
                "finger-selected scale survives exit and reentry");
            Check(gate.transform.localScale==fixedScale && gate.transform.position==fixedPos,"finger gesture never scales or moves gates");
            Hands(default,default);Hands(Point(-.14f),Point(.14f),.3f);
            Check(!portal.IsPlanetScaling,"tracking recovery needs gesture release");
            Neutral();Hands(Point(-.14f),Point(.14f),.2f);Hands(Point(-.4f),Point(.4f));
            Check(!portal.IsPlanetScaling,"abrupt tracking distance jump cancels scale");
            q=portal.EarthTransform.rotation;Hands(Palm(direction),neutral,.02f,true);
            Check(Quaternion.Angle(q,portal.EarthTransform.rotation)<.01f,"palm menu blocks globe manipulation");
            Press(picker,"PlanetResetBtn");Check(Mathf.Abs(portal.PlanetScale-1)<.001f,"reset button restores globe size");
            portal.SetEarthAutoSpin(false);Press(picker,"PlanetAutoBtn");
            Check(portal.earthAutoSpin,"auto rotation button");
            q=portal.EarthTransform.rotation;Call(portal,"UpdateEarthAnimation",Time.unscaledTime+1);
            Check(Quaternion.Angle(q,portal.EarthTransform.rotation)>5,"automatic rotation can resume");
            portal.SetEarthAutoSpin(false);q=portal.EarthTransform.rotation;
            Call(portal,"UpdateEarthAnimation",Time.unscaledTime+2);
            Check(Quaternion.Angle(q,portal.EarthTransform.rotation)<.01f,"paused globe stays still");
            Capture(portal,Check);
            for(int i=0;i<3;i++){Head(-.02f);Head(.02f);}
            Check(portal.IsInside,"exit hysteresis rejects jitter");
            Press(picker,"PlanetHandBtn");Call(portal,"UpdateEarthPose",1f);
            Check(!portal.EarthInHandMode && Vector3.Distance(overviewPosition,portal.EarthTransform.position)<.001f
                &&Mathf.Abs(overviewRadius-portal.PlanetRadius)<.001f,"return-to-distance restores overview pose");
            Head(-.1f);Call(portal,"UpdateEarthPose",1f);
            Check(Vector3.Distance(overviewPosition,portal.EarthTransform.position)<.001f
                &&Mathf.Abs(overviewRadius-portal.PlanetRadius)<.001f,"exit preserves overview Earth scale");
            Check(!portal.IsInside && !mover.PortalSuspended && !gate.IsPortalInteractionLocked,"return through portal releases suspension");
            Check(roomRenderer.enabled && !disabledRenderer.enabled,"room renderer states restored exactly");
            Check(!mover.IsPlacementLocked && mover.CanScale,"unlocked placement state restored");
            Check((bool)Field(mover,"_portalNeedsNeutral"),"gate gestures require a fresh neutral pose after return");
            Check((!controller.handleGrab || controller.handleGrab.enabled==grab)
                &&(!controller.handleHandGrab || controller.handleHandGrab.enabled==handGrab),"handle enabled states restored");
            mover.SetPlacementLocked(true);Head(-.2f);Head(.07f);Call(portal,"UpdateEarthPose",1f);
            Press(picker,"PortalReturnBtn");
            Check(!portal.IsInside && portal.EntrySuppressed && !portal.VisualRoot.gameObject.activeSelf,"return button instantly exposes room");
            Check(mover.IsPlacementLocked && !mover.CanScale,"original placement lock preserved on exit");
            Head(.12f);Check(!portal.IsInside && portal.EntrySuppressed,"return button cannot immediately reenter");
            Head(-.2f);Head(.07f);Check(portal.IsInside,"return to front rearms portal");
            Call(portal,"SamplePortalHead",head.transform.position,false);
            Check(!portal.IsInside && !gate.IsPortalInteractionLocked && !mover.PortalSuspended,"lost head tracking returns to room");
            Head(-.2f);Head(.07f);portal.SetPortalEnabled(false);
            Check(!portal.IsInside && !gate.IsPortalInteractionLocked,"disabling portal inside restores room");
            portal.SetPortalEnabled(true);Head(-.2f);Head(.07f);portal.enabled=false;
            Check(!portal.IsInside && !mover.PortalSuspended && roomRenderer.enabled,"disabling component restores room and gate controls");
            portal.enabled=true;Head(-.2f);Head(.07f);portal.SetWorldKind(GatePortal.PortalWorldKind.Loft);
            Check(!portal.IsInside && !gate.IsPortalInteractionLocked && !portal.EntrySuppressed,"switching worlds inside restores room");
            string report=$"Portal entry and planet hands: {checks} checks passed. Crossing, return window alpha, 360 sky, palm rotation, finger scaling, tracking loss, camera continuity and gate isolation.\n";
            File.AppendAllText("D:/blender/chatgpt/unity-maintenance-report.txt",report);Debug.Log(report);
        }
        finally
        {
            portal.ReturnToRoom();gate.StopAnimation();gate.SetGateType(oldKind);gate.Build(oldW,oldH);gate.SetProgress(oldP);
            gate.transform.SetPositionAndRotation(oldPos,oldRot);gate.transform.localScale=oldScale;
            mover.SetPlacementLocked(oldLocked);portal.SetWorldKind(oldWorld);portal.SetPortalEnabled(true);Head(-.25f);
            portal.allowEarthEntry=oldAllowed;portal.appearanceDuration=oldDuration;portal.earthAutoSpin=oldAuto;
            portal.headOverride=oldHead;portal.SetPortalEnabled(oldEnabled);
            Object.DestroyImmediate(head);Object.DestroyImmediate(room);Object.DestroyImmediate(disabledRoom);
        }
    }

    static void Capture(GatePortal portal,Action<bool,string> check,bool inside=true)
    {
        var go=new GameObject("_PortalEntryCamera",typeof(Camera));var camera=go.GetComponent<Camera>();
        var rt=new RenderTexture(900,900,24,RenderTextureFormat.ARGB32);
        var image=new Texture2D(900,900,TextureFormat.RGBA32,false);
        var previous=RenderTexture.active;var renderers=new Dictionary<Renderer,bool>();
        bool hard=Shader.IsKeywordEnabled("HARD_OCCLUSION"),soft=Shader.IsKeywordEnabled("SOFT_OCCLUSION");
        try
        {
            Shader.DisableKeyword("HARD_OCCLUSION");Shader.DisableKeyword("SOFT_OCCLUSION");
            foreach(var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if(!renderer.transform.IsChildOf(portal.VisualRoot)){renderers[renderer]=renderer.enabled;renderer.enabled=false;}
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.12f,.45f,.18f,0);
            camera.targetTexture=rt;camera.fieldOfView=70;camera.nearClipPlane=.02f;
            var center=portal.VisualRoot.position;var inward=portal.VisualRoot.right;
            float radius=portal.ApertureRadius*Mathf.Abs(portal.transform.lossyScale.y);
            Color32[] Render(string name)
            {
                camera.Render();RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,900,900),0,0);image.Apply();
                File.WriteAllBytes("D:/blender/chatgpt/portal_entry_"+name+".png",image.EncodeToPNG());return image.GetPixels32();
            }
            if(!inside)
            {
                camera.transform.position=center-inward*radius*3;
                camera.transform.LookAt(center,Vector3.up);var outside=Render("outside_window");
                check(outside[450*900+450].a>245,"outside view shows Earth through window");
                check(outside[80*900+80].a<5,"outside view leaves real room visible around portal");
                return;
            }
            camera.transform.position=center+inward*radius*2.2f;
            portal.EarthTransform.gameObject.SetActive(false);
            camera.transform.LookAt(center,Vector3.up);var back=Render("return_window_alpha");
            check(back[450*900+450].a<5,"return window has transparent alpha for live passthrough");
            check(back[80*900+80].a>245,"virtual sky surrounds the return window");
            var smoke=portal.VisualRoot.GetComponentsInChildren<Renderer>().Where(r=>r.sharedMaterial.shader==portal.energyShader).ToArray();
            foreach(var r in smoke)r.enabled=false;
            var softWindow=Render("return_window_soft_edge");
            var edge=camera.WorldToViewportPoint(center+portal.VisualRoot.forward*radius*.98f);
            int edgePixel=Mathf.Clamp((int)(edge.y*900),0,899)*900+Mathf.Clamp((int)(edge.x*900),0,899);
            check(softWindow[edgePixel].a>10 && softWindow[edgePixel].a<240,"return window edge blends room and space without a hard cut");
            foreach(var r in smoke)r.enabled=true;
            portal.EarthTransform.gameObject.SetActive(true);
            camera.transform.LookAt(portal.EarthTransform.position,Vector3.up);
            var foreground=Render("return_with_foreground_globe");
            check(foreground[450*900+450].a>245,"foreground planet occludes return window correctly");
            camera.transform.position=center+inward*.12f;
            camera.transform.LookAt(portal.EarthTransform.position,Vector3.up);var front=Render("earth_inside");
            check(front[450*900+450].a>245,"planet visible inside");
            foreach(var view in new[]{("left",portal.VisualRoot.forward),("right",-portal.VisualRoot.forward),("up",Vector3.up),("down",Vector3.down)})
            {
                camera.transform.rotation=Quaternion.LookRotation(view.Item2,Mathf.Abs(Vector3.Dot(view.Item2,Vector3.up))>.9f?inward:Vector3.up);
                var side=Render("space_"+view.Item1);
                check(side[450*900+450].a>245,"opaque environment when looking "+view.Item1);
            }
        }
        finally
        {
            if(portal.EarthTransform)portal.EarthTransform.gameObject.SetActive(true);
            if(hard)Shader.EnableKeyword("HARD_OCCLUSION");if(soft)Shader.EnableKeyword("SOFT_OCCLUSION");
            foreach(var entry in renderers)if(entry.Key)entry.Key.enabled=entry.Value;
            RenderTexture.active=previous;camera.targetTexture=null;Object.DestroyImmediate(go);
            rt.Release();Object.DestroyImmediate(rt);Object.DestroyImmediate(image);
        }
    }
}
