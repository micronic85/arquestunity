using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

/// <summary>Exercise the same palm-menu callbacks used on the headset without needing BLE hardware.</summary>
public static class ManualDimensionVerification
{
    const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Field(object instance,string name)=>instance.GetType().GetField(name,Hidden).GetValue(instance);
    static void Call(object instance,string method,params object[] args)=>instance.GetType().GetMethod(method,Hidden).Invoke(instance,args);
    static object FindButton(GateColorPicker picker,string name)=>((IList)Field(picker,"_buttons")).Cast<object>()
        .FirstOrDefault(b=>((Transform)b.GetType().GetField("t").GetValue(b)).name==name);
    static void Press(GateColorPicker picker,string name)
    {
        var button=FindButton(picker,name)??throw new InvalidOperationException("Missing button: "+name);
        ((Action)button.GetType().GetField("press").GetValue(button)).Invoke();
    }
    static void Digits(GateColorPicker picker,string value)
    {
        foreach(char digit in value) Press(picker,"ManualDigit_"+digit);
    }
    static void Tab(GateColorPicker picker,GateColorPicker.Tab tab)
    {
        typeof(GateColorPicker).GetField("_tab",Hidden).SetValue(picker,tab);
        Call(picker,"RebuildMenu");
    }

    [MenuItem("Tools/AR Gates/Verify Manual Dimensions In Play Mode")]
    public static void Verify()
    {
        if(!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        var picker=Object.FindFirstObjectByType<GateColorPicker>();
        var measure=picker.measure;
        var builder=picker.builder;
        var mover=builder.GetComponent<GateMover>();
        string originalStatus=measure.Status;
        int? originalWidth=measure.Width,originalHeight=measure.Height;
        var next=measure.Next; var tab=(GateColorPicker.Tab)Field(picker,"_tab");
        var kind=builder.gateKind; float w=builder.openingWidth,h=builder.openingHeight,p=builder.Progress;
        var position=builder.transform.position;var rotation=builder.transform.rotation;var scale=builder.transform.localScale;
        bool locked=mover.IsPlacementLocked;
        int checks=0;
        void Check(bool ok,string message)
        {
            if(!ok) throw new InvalidOperationException("Manual dimension verification failed: "+message);
            checks++;
        }
        try
        {
            measure.ResetValues();Tab(picker,GateColorPicker.Tab.Measure);
            Press(picker,"WidthBtn");
            Check((bool)Field(picker,"_manualEditing"),"Width field did not open the keypad.");
            Check(FindButton(picker,"FitBtn")==null,"Fit is available while an unconfirmed draft is open.");
            Digits(picker,"3500");
            Check(!measure.Width.HasValue,"Typing changed width before confirmation.");
            Press(picker,"ManualConfirm");
            Check(measure.Width==3500 && !measure.Height.HasValue,"Width confirmation changed the wrong field.");
            Check(measure.Next==DistoMeasure.Field.Height,"Next DISTO measurement should target height.");
            Check(!(bool)Field(picker,"_manualEditing") && FindButton(picker,"FitBtn")!=null,"Confirm did not close keypad/restore Fit.");
            Press(picker,"HeightBtn");Digits(picker,"2650");Press(picker,"ManualConfirm");
            Check(measure.Width==3500 && measure.Height==2650,"Height confirmation lost the width.");
            Check(measure.Next==DistoMeasure.Field.Width,"Next DISTO field did not alternate.");

            Press(picker,"WidthBtn");Digits(picker,"999");Press(picker,"ManualCancel");
            Check(measure.Width==3500 && measure.Height==2650,"Cancel changed committed dimensions.");
            Press(picker,"WidthBtn");Press(picker,"ManualBackspace");Digits(picker,"5");
            Check((string)Field(picker,"_manualDraft")=="3505","Backspace did not edit the existing value.");
            Press(picker,"ManualClear");
            Check((string)Field(picker,"_manualDraft")=="","Clear did not empty the draft.");
            Press(picker,"ManualConfirm");
            Check((bool)Field(picker,"_manualEditing") && measure.Width==3500,"Empty input was accepted.");
            Digits(picker,"0");Press(picker,"ManualConfirm");
            Check((bool)Field(picker,"_manualEditing") && measure.Width==3500,"Zero input was accepted.");
            Press(picker,"ManualClear");Digits(picker,"999999");
            Check(((string)Field(picker,"_manualDraft")).Length==5,"Input exceeded five digits.");
            Press(picker,"ManualClear");Digits(picker,"04000");Press(picker,"ManualConfirm");
            Check(measure.Width==4000,"Leading zero handling is incorrect.");
            Check(!measure.TrySetValue(DistoMeasure.Field.Width,-1) && !measure.TrySetValue(DistoMeasure.Field.Width,100000) && measure.Width==4000,
                "Invalid dimensions overwrote a valid value.");

            Press(picker,"WidthBtn");Digits(picker,"3600");
            string hint=((TMPro.TextMeshPro)Field(picker,"_statusText")).text;
            Call(measure,"OnDistoState","scanning");
            Check(((TMPro.TextMeshPro)Field(picker,"_statusText")).text==hint,"BLE connection status replaced the manual-entry hint.");
            Call(measure,"OnDistoValue","2700");
            Check(measure.Width==2700 && (string)Field(picker,"_manualDraft")=="3600","BLE overwrote the draft or failed to update the selected field.");
            Press(picker,"ManualConfirm");
            Check(measure.Width==3600 && measure.Height==2650,"Manual confirmation after BLE changed the wrong field.");
            Press(picker,"WidthBtn");Call(measure,"OnDistoValue","3400");Press(picker,"ManualCancel");
            Check(measure.Width==3400,"Cancel discarded an external BLE update.");
            Press(picker,"WidthBtn");Digits(picker,"7777");Tab(picker,GateColorPicker.Tab.Color);
            Check(!(bool)Field(picker,"_manualEditing") && measure.Width==3400,"Leaving the tab committed or retained the draft.");
            Tab(picker,GateColorPicker.Tab.Measure);

            measure.TrySetValue(DistoMeasure.Field.Width,3500);measure.TrySetValue(DistoMeasure.Field.Height,2650);
            foreach(var gateKind in new[]{GateBuilder.GateKind.Sectional,GateBuilder.GateKind.Roller})
            {
                builder.SetGateType(gateKind);
                Press(picker,"FitBtn");
                Check(Mathf.Abs(builder.openingWidth-3.5f)<.0001f && Mathf.Abs(builder.openingHeight-2.65f)<.0001f,
                    "Fit did not convert manual millimeters to meters for "+gateKind);
                Check(mover.IsPlacementLocked && !mover.CanMove && !mover.CanRotate && !mover.CanScale,
                    "Manual fit did not lock placement for "+gateKind);
            }
            string report=$"Manual dimension input passed: {checks} checks; real menu callbacks, confirm/cancel, delete/clear, invalid values, BLE draft conflict, tab change and fitting both gate types.\n";
            File.AppendAllText("D:/blender/chatgpt/unity-maintenance-report.txt",report);Debug.Log(report);
        }
        finally
        {
            typeof(GateColorPicker).GetField("_manualEditing",Hidden).SetValue(picker,false);
            measure.ResetValues();
            if(originalWidth.HasValue) measure.TrySetValue(DistoMeasure.Field.Width,originalWidth.Value);
            if(originalHeight.HasValue) measure.TrySetValue(DistoMeasure.Field.Height,originalHeight.Value);
            measure.SetNext(next);Call(measure,"SetStatus",originalStatus);
            builder.StopAnimation();builder.SetWicketAngle(0);builder.SetGateType(kind);builder.Build(w,h);builder.SetProgress(p);
            builder.transform.SetPositionAndRotation(position,rotation);builder.transform.localScale=scale;mover.SetPlacementLocked(locked);
            Tab(picker,tab);
        }
    }

    [MenuItem("Tools/AR Gates/Capture Manual Dimension Keypad In Play Mode")]
    public static void Capture()
    {
        if(!Application.isPlaying) throw new InvalidOperationException("Enter Play Mode first.");
        var picker=Object.FindFirstObjectByType<GateColorPicker>();
        var tab=(GateColorPicker.Tab)Field(picker,"_tab");
        var next=picker.measure.Next;
        var disabled=new Dictionary<Renderer,bool>();
        Transform menu=null;bool active=false;
        var cameraObject=new GameObject("_ManualInputPreview",typeof(Camera));
        var camera=cameraObject.GetComponent<Camera>();
        var rt=new RenderTexture(900,1400,24){antiAliasing=4};
        var previous=RenderTexture.active;
        var image=new Texture2D(900,1400,TextureFormat.RGB24,false);
        try
        {
            Tab(picker,GateColorPicker.Tab.Measure);Press(picker,"WidthBtn");Digits(picker,"3500");
            Call(picker,"FitChangedLabels");
            menu=(Transform)Field(picker,"_menu");active=menu.gameObject.activeSelf;menu.gameObject.SetActive(true);
            foreach(var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if(!renderer.transform.IsChildOf(menu)){disabled[renderer]=renderer.enabled;renderer.enabled=false;}
            var half=(Vector2)Field(picker,"_menuHalf");
            camera.orthographic=true;camera.orthographicSize=half.y+.018f;
            camera.transform.position=menu.TransformPoint(new Vector3(0,0,-.6f));camera.transform.rotation=menu.rotation;
            camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.18f,.20f,.23f);
            camera.targetTexture=rt;camera.nearClipPlane=.01f;camera.Render();
            RenderTexture.active=rt;image.ReadPixels(new Rect(0,0,900,1400),0,0);image.Apply();
            File.WriteAllBytes("D:/blender/chatgpt/unity_manual_dimensions.png",image.EncodeToPNG());
            Debug.Log("Saved manual dimension keypad preview.");
        }
        finally
        {
            RenderTexture.active=previous;camera.targetTexture=null;rt.Release();
            Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(rt);Object.DestroyImmediate(image);
            foreach(var item in disabled)if(item.Key)item.Key.enabled=item.Value;
            if(menu)menu.gameObject.SetActive(active);
            typeof(GateColorPicker).GetField("_manualEditing",Hidden).SetValue(picker,false);
            picker.measure.SetNext(next);Tab(picker,tab);
        }
    }
}
