using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Integration checks for shared fitting, gate switching and opaque roller geometry.</summary>
public static class RollerGateVerification
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Field(object instance,string name)=>instance.GetType().GetField(name,Hidden).GetValue(instance);
    static void MenuType(GateColorPicker picker,GateBuilder.GateKind kind)=>
        typeof(GateColorPicker).GetMethod("SelectGateType",Hidden).Invoke(picker,new object[]{kind});

    [MenuItem("Tools/AR Gates/Verify Roller Gates In Play Mode")]
    public static void Verify()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        var builder=Object.FindFirstObjectByType<GateBuilder>();
        var picker=builder.GetComponent<GateColorPicker>();
        var mover=builder.GetComponent<GateMover>();
        var kind=builder.gateKind;
        float w=builder.openingWidth,h=builder.openingHeight,progress=builder.Progress;
        var position=builder.transform.position; var rotation=builder.transform.rotation; var scale=builder.transform.localScale;
        bool windows=builder.windowsEnabled,wicket=builder.wicketEnabled,locked=mover.IsPlacementLocked;
        int selected=(int)Field(picker,"_selected");
        int checks=0,rayChecks=0;
        void Check(bool ok,string message)
        {
            if (!ok) throw new InvalidOperationException("Roller verification failed: "+message);
            checks++;
        }
        GameObject colliderObject=null;
        try
        {
            builder.SetGateType(GateBuilder.GateKind.Sectional);
            builder.SetWicketAngle(0);
            builder.SetOptions(true,true);
            builder.Fit(3.5f,2.65f);
            builder.PlaceInOpening(new Vector3(2,0,3),Vector3.back);
            picker.Apply(Mathf.Min(6,((IList)Field(picker,"_palette")).Count-1));
            int color=(int)Field(picker,"_selected");
            builder.SetProgress(.37f);
            var before=builder.transform.localToWorldMatrix;
            MenuType(picker,GateBuilder.GateKind.Roller);
            Check(builder.IsRoller,"Menu did not select roller type.");
            Check(Field(picker,"_rollerTypeText")!=null,"Type selector is absent.");
            Check(Field(picker,"_windowsText")==null && Field(picker,"_wicketText")==null,"Sectional-only buttons remain visible.");
            Check(builder.transform.localToWorldMatrix==before,"Type switch moved/rescaled the gate.");
            Check(mover.IsPlacementLocked && !mover.CanMove && !mover.CanRotate && !mover.CanScale,"Type switch released the fitting lock.");
            Check(Mathf.Abs(builder.Progress-.37f)<.0001f,"Type switch changed normalized progress.");
            Check((int)Field(picker,"_selected")==color,"Color selection was lost.");
            Check(!builder.HasWicket && !builder.WicketAllowed,"Roller contains a wicket.");
            Check(builder.windowsEnabled && builder.wicketEnabled,"Sectional options were overwritten.");
            Check(builder.handleObject && builder.handleObject.parent.name=="RollerBottom","ISDK handle did not attach to the bottom rail.");
            Check(Mathf.Abs(builder.RollerClearWidth-3.32f)<.0001f,"Wrong space between the guides.");
            Check(Mathf.Abs(builder.RollerClearHeight-2.33f)<.0001f,"Wrong clear height under the 300 mm box.");

            foreach (var size in new[]{new Vector2(3.5f,2.65f),new Vector2(2,2),new Vector2(5,4.5f)})
            {
                builder.Fit(size.x,size.y);
                Check(builder.IsRoller,"Fit changed the gate type.");
                Check(builder.openingWidth==size.x && builder.openingHeight==size.y,"Wrong measured dimensions.");
                Check(mover.IsPlacementLocked,"Fit did not lock roller placement.");
                Check(builder.Root.GetComponentsInChildren<Renderer>(true).Count(r=>r.enabled)<=8,"Too many roller renderers.");
                var curtain=builder.Root.Find("RollerPaint_Curtain").GetComponent<MeshFilter>().sharedMesh;
                Check(curtain.vertexCount<20000,"Curtain mesh is unexpectedly dense.");
                Check(curtain.tangents.Length==curtain.vertexCount,"Shader tangents are missing.");
                Check(curtain.normals.All(n=>float.IsFinite(n.x)&&float.IsFinite(n.y)&&float.IsFinite(n.z)),"Invalid curtain normals.");
                var painted=builder.Root.GetComponentsInChildren<Renderer>(true).Where(r=>r.name.StartsWith("RollerPaint_")).ToArray();
                Check(painted.Length==5,"Color targets are incomplete.");
                Check(painted.All(r=>r.sharedMaterial==painted[0].sharedMaterial),"Housing/guides/curtain have different colors.");
                Check(painted.All(r=>!r.sharedMaterial.HasProperty("_Ribs") || r.sharedMaterial.GetFloat("_Ribs")==0),"Sectional groove shader leaked into roller materials.");

                colliderObject=new GameObject("_RollerVerificationCollider",typeof(MeshCollider));
                colliderObject.transform.SetParent(builder.Root,false);
                var collider=colliderObject.GetComponent<MeshCollider>();
                foreach (float p in new[]{0f,.5f,1f})
                {
                    builder.SetProgress(p);
                    Check(Mathf.Abs(builder.Progress-p)<.0001f,"Roller opening did not update.");
                    Check(curtain.vertices.All(v=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)),"Invalid mesh positions.");
                    if (p==1)
                    {
                        Check(curtain.bounds.min.y>builder.RollerClearHeight && curtain.bounds.max.y<builder.RollerBoxTop,"Raised curtain is outside the box.");
                        var bottom=builder.Root.Find("RollerBottom");
                        Check(bottom.localPosition.y>builder.RollerClearHeight,"Bottom rail did not enter the box.");
                        continue;
                    }
                    collider.sharedMesh=null; collider.sharedMesh=curtain;
                    Physics.SyncTransforms();
                    for (int i=1;i<builder.RollerSlatCount;i++)
                    {
                        float joint=.054f+i*builder.RollerActualPitch+p*(builder.RollerClearHeight+.13f);
                        if (joint>builder.RollerClearHeight-.005f) continue;
                        foreach (float across in new[]{-.45f,0,.45f})
                            foreach (float slope in new[]{-.35f,0,.35f})
                            {
                                var target=builder.Root.TransformPoint(new Vector3(-.008f,joint,builder.RollerClearWidth*across));
                                var direction=builder.Root.TransformDirection(new Vector3(1,slope,0)).normalized;
                                Check(collider.Raycast(new Ray(target-direction*.3f,direction),out _,.6f),"Light gap between lamellas.");
                                rayChecks++;
                            }
                    }
                }
                Object.DestroyImmediate(colliderObject); colliderObject=null;
                builder.SetProgress(0);
                picker.Apply(0);
                Check(painted.All(r=>r.sharedMaterial==painted[0].sharedMaterial),"Changing roller color did not recolor all painted parts.");
                picker.Apply(color);
            }
            builder.SetProgress(.4f);
            builder.AnimateTo(1);
            float movingProgress=builder.Progress;
            MenuType(picker,GateBuilder.GateKind.Sectional);
            Check(!builder.IsAnimating,"Type switch left an old animation running.");
            Check(!builder.IsRoller && builder.HasWicket && builder.windowsEnabled,"Sectional options were not restored.");
            Check(Field(picker,"_windowsText")!=null && Field(picker,"_wicketText")!=null,"Sectional buttons did not return.");
            Check(Mathf.Abs(builder.Progress-movingProgress)<.0001f,"Switch from a moving roller lost opening state.");
            Check((int)Field(picker,"_selected")==color,"Switch back lost color.");
            Check(builder.handleObject && builder.handleObject.IsChildOf(builder.Root),"Handle was lost when switching back.");
            string report=$"Roller integration passed: {checks} checks, including {rayChecks} seam rays; 3 sizes, menu switching, color, fitting lock, handle, opening and return to sectional.\n";
            File.AppendAllText("D:/blender/chatgpt/unity-maintenance-report.txt",report);
            Debug.Log(report);
        }
        finally
        {
            if(colliderObject) Object.DestroyImmediate(colliderObject);
            builder.StopAnimation(); builder.SetWicketAngle(0);
            builder.windowsEnabled=windows; builder.wicketEnabled=wicket;
            builder.SetGateType(kind); builder.Build(w,h); builder.SetProgress(progress);
            builder.transform.SetPositionAndRotation(position,rotation); builder.transform.localScale=scale;
            mover.SetPlacementLocked(locked);
            picker.Apply(selected);
        }
    }

    [MenuItem("Tools/AR Gates/Capture Roller Preview In Play Mode")]
    public static void Capture()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        var builder=Object.FindFirstObjectByType<GateBuilder>();
        var picker=builder.GetComponent<GateColorPicker>();
        var kind=builder.gateKind;
        float w=builder.openingWidth,h=builder.openingHeight,p=builder.Progress;
        int color=(int)Field(picker,"_selected");
        var cameraObject=new GameObject("_RollerPreviewCamera",typeof(Camera));
        var camera=cameraObject.GetComponent<Camera>();
        var rt=new RenderTexture(1000,900,24) { antiAliasing=4 };
        var previous=RenderTexture.active;
        var image=new Texture2D(1000,900,TextureFormat.RGB24,false);
        var renderers=new System.Collections.Generic.Dictionary<Renderer,bool>();
        var lights=new System.Collections.Generic.Dictionary<Light,bool>();
        GameObject keyObject=null,fillObject=null,ground=null;
        Material groundMaterial=null;
        try
        {
            builder.SetWicketAngle(0); builder.SetGateType(GateBuilder.GateKind.Roller);
            builder.Build(3.5f,2.65f); picker.Apply(Mathf.Min(6,((IList)Field(picker,"_palette")).Count-1));
            camera.targetTexture=rt;
            camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.24f,.27f,.30f);
            camera.fieldOfView=36;
            camera.nearClipPlane=.05f;
            var center=builder.Root.TransformPoint(new Vector3(0,1.32f,0));
            camera.transform.position=builder.Root.TransformPoint(new Vector3(-6.2f,2.25f,1.6f));
            camera.transform.LookAt(center,Vector3.up);
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (!renderer.transform.IsChildOf(builder.Root))
                { renderers[renderer]=renderer.enabled; renderer.enabled=false; }
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            { lights[light]=light.enabled; light.enabled=false; }
            keyObject=new GameObject("_PreviewKey",typeof(Light));
            var key=keyObject.GetComponent<Light>(); key.type=LightType.Directional;
            key.intensity=1.8f; key.color=new Color(1,.95f,.88f);
            keyObject.transform.rotation=Quaternion.LookRotation(builder.Root.TransformDirection(new Vector3(1,-.8f,-.3f)));
            fillObject=new GameObject("_PreviewFill",typeof(Light));
            var fill=fillObject.GetComponent<Light>(); fill.type=LightType.Directional;
            fill.intensity=.6f; fill.color=new Color(.80f,.90f,1);
            fillObject.transform.rotation=Quaternion.LookRotation(builder.Root.TransformDirection(new Vector3(.7f,-.4f,.8f)));
            ground=GameObject.CreatePrimitive(PrimitiveType.Plane);
            Object.DestroyImmediate(ground.GetComponent<Collider>());
            ground.transform.SetParent(builder.Root,false); ground.transform.localPosition=new Vector3(.4f,-.006f,0);
            ground.transform.localScale=new Vector3(.8f,1,.8f);
            groundMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMaterial.SetColor("_BaseColor",new Color(.32f,.34f,.35f));
            groundMaterial.SetFloat("_Smoothness",.15f);
            ground.GetComponent<Renderer>().sharedMaterial=groundMaterial;
            foreach (var sample in new[]{(0f,"closed"),(.5f,"half"),(1f,"open")})
            {
                builder.SetProgress(sample.Item1); camera.Render();
                RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,1000,900),0,0); image.Apply();
                File.WriteAllBytes("D:/blender/chatgpt/unity_roller_"+sample.Item2+".png",image.EncodeToPNG());
            }
            Debug.Log("Saved closed/half/open roller previews to D:/blender/chatgpt.");
        }
        finally
        {
            RenderTexture.active=previous; camera.targetTexture=null;
            Object.DestroyImmediate(cameraObject); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(image);
            if(keyObject) Object.DestroyImmediate(keyObject);
            if(fillObject) Object.DestroyImmediate(fillObject);
            if(ground) Object.DestroyImmediate(ground);
            if(groundMaterial) Object.DestroyImmediate(groundMaterial);
            foreach(var entry in renderers) if(entry.Key) entry.Key.enabled=entry.Value;
            foreach(var entry in lights) if(entry.Key) entry.Key.enabled=entry.Value;
            builder.SetGateType(kind); builder.Build(w,h); builder.SetProgress(p); picker.Apply(color);
        }
    }
}
