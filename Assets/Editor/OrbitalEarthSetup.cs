using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class OrbitalEarthSetup
{
    public const string Root="Assets/Textures/Portal/Earth/";
    [MenuItem("Tools/AR Gates/Install Orbital Earth Textures")]
    public static void Install()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(Application.isPlaying || scene.isDirty || scene.path!="Assets/lastvorotanew.unity")
            throw new InvalidOperationException("Open saved main scene in Edit Mode.");
        var day=Import("EarthSurface_NASA_8K.jpg",8192,true,TextureImporterFormat.ASTC_6x6);
        var night=Import("EarthNight_NASA_4K.png",4096,false,TextureImporterFormat.ASTC_4x4);
        var cloud=Import("EarthClouds_SSS_8K.png",8192,false,TextureImporterFormat.ASTC_6x6);
        var portal=UnityEngine.Object.FindFirstObjectByType<GatePortal>();
        Undo.RecordObject(portal,"Install orbital Earth surface, clouds and city lights");
        portal.earthTexture=day;portal.earthNightTexture=night;portal.earthCloudTexture=cloud;
        portal.cloudsEnabled=true;portal.cloudDensity=1;portal.cloudDriftDegreesPerSecond=.08f;
        portal.nightLightsIntensity=1.8f;
        EditorUtility.SetDirty(portal);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Debug.Log("Orbital Earth installed: NASA cloud-free surface 8K, Black Marble lights 4K, detached cloud layer 8K. Original mesh retained.");
    }
    static Texture2D Import(string file,int size,bool srgb,TextureImporterFormat format)
    {
        var importer=(TextureImporter)AssetImporter.GetAtPath(Root+file);
        importer.textureType=TextureImporterType.Default;importer.sRGBTexture=srgb;
        importer.mipmapEnabled=true;importer.isReadable=false;importer.npotScale=TextureImporterNPOTScale.None;
        importer.maxTextureSize=size;importer.wrapModeU=TextureWrapMode.Repeat;importer.wrapModeV=TextureWrapMode.Clamp;
        importer.filterMode=FilterMode.Trilinear;importer.anisoLevel=4;
        importer.textureCompression=TextureImporterCompression.CompressedHQ;
        var android=importer.GetPlatformTextureSettings("Android");android.name="Android";android.overridden=true;
        android.maxTextureSize=size;android.format=format;importer.SetPlatformTextureSettings(android);importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(Root+file);
    }
}
