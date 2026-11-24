using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

public static class PortalMusicSetup
{
    const string ClipPath="Assets/Audio/Portal/SpaceJourney.mp3";
    [MenuItem("Tools/AR Gates/Install Portal Music")]
    public static void Install()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(Application.isPlaying || scene.isDirty || scene.path!="Assets/lastvorotanew.unity")
            throw new InvalidOperationException("Open saved main scene in Edit Mode.");
        var importer=(AudioImporter)AssetImporter.GetAtPath(ClipPath);
        if(!importer)throw new InvalidOperationException("Portal music file is missing.");
        importer.forceToMono=false;importer.loadInBackground=true;
        var settings=importer.defaultSampleSettings;
        settings.preloadAudioData=true;settings.loadType=AudioClipLoadType.CompressedInMemory;settings.compressionFormat=AudioCompressionFormat.Vorbis;
        settings.quality=.7f;settings.sampleRateSetting=AudioSampleRateSetting.OptimizeSampleRate;
        importer.defaultSampleSettings=settings;importer.SetOverrideSampleSettings("Android",settings);importer.SaveAndReimport();
        var portal=Object.FindFirstObjectByType<GatePortal>();
        Undo.RecordObject(portal,"Install distance-controlled portal music");
        portal.portalMusic=AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);
        EditorUtility.SetDirty(portal);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Debug.Log($"Portal music installed: {portal.portalMusic.length:F1} s; preloaded compressed stereo; near 1 m, distant 8 m.");
    }

    [MenuItem("Tools/AR Gates/Verify Portal Music In Play Mode")]
    public static void Verify()
    {
        if(!Application.isPlaying)throw new InvalidOperationException("Enter Play Mode first.");
        var portal=Object.FindFirstObjectByType<GatePortal>();var gate=portal.GetComponent<GateBuilder>();
        if(portal.IsInside)throw new InvalidOperationException("Return to room before testing.");
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        void Call(string method,params object[] args)=>typeof(GatePortal).GetMethod(method,flags).Invoke(portal,args);
        AudioSource Source()=>(AudioSource)typeof(GatePortal).GetField("_portalAudio",flags).GetValue(portal);
        int count=0;void Check(bool ok,string reason){if(!ok)throw new Exception("Portal music: "+reason);count++;}
        var oldHead=portal.headOverride;bool oldEnabled=portal.portalEnabled;float oldProgress=gate.Progress,oldDuration=portal.appearanceDuration;
        var head=new GameObject("_PortalMusicTestHead");
        try
        {
            Check(portal.portalMusic && portal.portalMusic.length>30,"installed track");
            var importer=(AudioImporter)AssetImporter.GetAtPath(ClipPath);
            Check(importer.GetOverrideSampleSettings("Android").loadType==AudioClipLoadType.CompressedInMemory
                &&importer.GetOverrideSampleSettings("Android").preloadAudioData,"Android compressed audio preloaded");
            portal.headOverride=head.transform;portal.appearanceDuration=0;gate.SetProgress(1);portal.SetPortalEnabled(true);
            float previous=1;
            for(int i=0;i<=16;i++){float gain=portal.MusicGainAtDistance(i*.5f);Check(gain<=previous && gain>0,"monotonic attenuation");previous=gain;}
            head.transform.position=portal.VisualRoot.position-portal.VisualRoot.right*.5f;Call("UpdatePortalAudio",1f);
            var audio=Source();float near=audio.volume;
            Check(audio && audio.loop && !audio.playOnAwake,"single looping source");
            Check(audio.clip==portal.portalMusic && audio.isPlaying,"track playing near portal");
            Check(Mathf.Abs(near-portal.musicVolume)<.001f,"near volume");
            head.transform.position=portal.VisualRoot.position-portal.VisualRoot.right*8;Call("UpdatePortalAudio",1f);
            Check(audio.isPlaying && audio.volume>0 && audio.volume<near*.05f,"barely audible at 8 m");
            head.transform.position=portal.VisualRoot.position+portal.VisualRoot.right*.5f;Call("UpdatePortalAudio",1f);
            Check(Mathf.Abs(audio.volume-near)<.001f,"same volume on inside side");
            float cursor=audio.time;Call("UpdatePortalAudio",.02f);Check(audio.time>=cursor,"movement preserves playback cursor");
            portal.SetPortalEnabled(false);Call("UpdatePortalAudio",.01f);
            Check(audio.volume>0 && audio.volume<near,"fade on disable");
            Call("UpdatePortalAudio",1f);Check(!audio.isPlaying && audio.volume==0,"stopped when hidden");
            portal.SetPortalEnabled(true);Call("UpdatePortalAudio",1f);gate.SetProgress(0);Call("Tick",Time.unscaledTime);Call("UpdatePortalAudio",1f);
            Check(!audio.isPlaying,"closed gate silent");
            gate.SetProgress(1);Call("Tick",Time.unscaledTime);Call("UpdatePortalAudio",1f);
            portal.enabled=false;Check(!audio.isPlaying && audio.volume==0,"component disable stops music");portal.enabled=true;
            Check(portal.GetComponentsInChildren<AudioSource>(true).Length==1,"no duplicate music sources");
            Debug.Log($"Portal music: {count} checks passed; distance gain, both sides, compressed preload, loop, fade and lifecycle.");
        }
        finally
        {
            portal.enabled=true;Call("StopPortalAudio");portal.headOverride=oldHead;
            typeof(GatePortal).GetField("_travelHead",flags).SetValue(portal,oldHead);
            portal.appearanceDuration=oldDuration;gate.SetProgress(oldProgress);portal.SetPortalEnabled(oldEnabled);Object.Destroy(head);
        }
    }
}
