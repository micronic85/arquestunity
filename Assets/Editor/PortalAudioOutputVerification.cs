using System;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

public static class PortalAudioOutputVerification
{
    static bool running;
    [MenuItem("Tools/AR Gates/Verify Actual Portal Audio Output In Play Mode")]
    public static async void Verify()
    {
        if(running)return;
        if(!Application.isPlaying)throw new InvalidOperationException("Enter Play Mode first.");
        running=true;
        var portal=Object.FindFirstObjectByType<GatePortal>();var gate=portal.GetComponent<GateBuilder>();
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var oldHead=portal.headOverride;float duration=portal.appearanceDuration,progress=gate.Progress;
        bool enabled=portal.portalEnabled,allowed=portal.allowEarthEntry;
        var head=new GameObject("_PortalAudioOutputHead");
        try
        {
            portal.allowEarthEntry=false;portal.headOverride=head.transform;portal.appearanceDuration=0;
            gate.SetProgress(1);portal.SetPortalEnabled(true);
            head.transform.position=portal.VisualRoot.position-portal.VisualRoot.right*.4f;
            double deadline=EditorApplication.timeSinceStartup+5;float peak=0;
            var buffer=new float[1024];AudioSource source=null;
            while(Application.isPlaying && EditorApplication.timeSinceStartup<deadline)
            {
                typeof(GatePortal).GetMethod("UpdatePortalAudio",flags).Invoke(portal,new object[]{.1f});
                source=(AudioSource)typeof(GatePortal).GetField("_portalAudio",flags).GetValue(portal);
                if(source && source.isPlaying)
                {
                    source.GetOutputData(buffer,0);
                    foreach(float sample in buffer)peak=Mathf.Max(peak,Mathf.Abs(sample));
                    if(peak>.0001f)break;
                }
                await Task.Delay(100);
            }
            if(peak<=.0001f)throw new InvalidOperationException($"No decoded music signal. Listener pause={AudioListener.pause}, volume={AudioListener.volume}, clip={portal.portalMusic.loadState}, source playing={source && source.isPlaying}");
            Debug.Log($"Portal decoded audio verified: sample peak={peak:F6}, clip={portal.portalMusic.loadState}, playing={source.isPlaying}, gain={source.volume:F3}. Quest speaker output still requires device verification.");
        }
        catch(Exception ex){Debug.LogException(ex);}
        finally
        {
            if(portal)
            {
                typeof(GatePortal).GetMethod("StopPortalAudio",flags).Invoke(portal,null);
                portal.headOverride=oldHead;typeof(GatePortal).GetField("_travelHead",flags).SetValue(portal,oldHead);
                portal.allowEarthEntry=allowed;portal.appearanceDuration=duration;
                if(Application.isPlaying){gate.SetProgress(progress);portal.SetPortalEnabled(enabled);}
            }
            if(head)Object.Destroy(head);running=false;
        }
    }
}
