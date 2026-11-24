using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GatePortalSetup
{
    [MenuItem("Tools/AR Gates/Install Portal In Scene")]
    public static void Install()
    {
        if(Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before installation.");
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.path!="Assets/lastvorotanew.unity" || scene.isDirty)
            throw new InvalidOperationException("Open the saved main scene before installation.");
        var gate=UnityEngine.Object.FindFirstObjectByType<GateBuilder>();
        if(!gate) throw new InvalidOperationException("GateBuilder not found.");
        var mask=Load("Mask");var world=Load("World");var energy=Load("Energy");
        const string smokePath="Assets/Textures/Portal/SmokeVortexDensity.png";
        var importer=(TextureImporter)AssetImporter.GetAtPath(smokePath);
        if(!importer) throw new InvalidOperationException("Baked Blender smoke is missing.");
        importer.textureType=TextureImporterType.Default;
        importer.sRGBTexture=true;importer.alphaSource=TextureImporterAlphaSource.None;
        importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Bilinear;
        importer.mipmapEnabled=true;importer.maxTextureSize=1024;
        importer.textureCompression=TextureImporterCompression.CompressedHQ;
        var android=importer.GetPlatformTextureSettings("Android");
        android.name="Android";android.overridden=true;android.maxTextureSize=1024;
        android.format=TextureImporterFormat.ASTC_6x6;importer.SetPlatformTextureSettings(android);
        importer.SaveAndReimport();
        var smoke=AssetDatabase.LoadAssetAtPath<Texture2D>(smokePath);
        string backup="D:/blender/chatgpt/lastvorotanew.before-portal.unity";
        if(!File.Exists(backup)) File.Copy(scene.path,backup);
        var portal=gate.GetComponent<GatePortal>();
        if(!portal) portal=Undo.AddComponent<GatePortal>(gate.gameObject);
        Undo.RecordObject(portal,"Configure gate portal");
        portal.maskShader=mask;portal.worldShader=world;portal.energyShader=energy;
        portal.smokeTexture=smoke;
        EditorUtility.SetDirty(portal);EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Portal installed: optional Gate menu toggle and replay, shader references saved.");
    }

    static Shader Load(string name)
    {
        var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Shaders/GatePortal"+name+".shader");
        if(!shader || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Portal shader failed: "+name);
        return shader;
    }
}
