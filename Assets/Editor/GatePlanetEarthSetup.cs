using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GatePlanetEarthSetup
{
    [MenuItem("Tools/AR Gates/Install Planet Earth In Portal")]
    public static void Install()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(Application.isPlaying || scene.isDirty || scene.path!="Assets/lastvorotanew.unity")
            throw new InvalidOperationException("Open saved main scene in Edit Mode.");
        const string root="Assets/Planet Earth Free/";
        var mesh=AssetDatabase.LoadAllAssetsAtPath(root+"Models/Planet3968Tris.FBX").OfType<Mesh>().First();
        var earth=Texture(root+"Materials/Earth2kTexture.png",2048);
        var stars=Texture(root+"Materials/StarsTexture.png",1024);
        var portal=UnityEngine.Object.FindFirstObjectByType<GatePortal>();
        Undo.RecordObject(portal,"Install Planet Earth portal world");
        portal.earthMesh=mesh;portal.earthTexture=earth;portal.spaceTexture=stars;
        portal.worldKind=GatePortal.PortalWorldKind.Earth;
        EditorUtility.SetDirty(portal);EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Debug.Log("Planet Earth installed: medium mesh, 2K surface, stars, atmosphere; Loft preserved.");
    }
    static Texture2D Texture(string path,int size)
    {
        var importer=(TextureImporter)AssetImporter.GetAtPath(path);
        importer.sRGBTexture=true;importer.mipmapEnabled=true;importer.maxTextureSize=size;
        importer.wrapMode=TextureWrapMode.Repeat;importer.textureCompression=TextureImporterCompression.CompressedHQ;
        var android=importer.GetPlatformTextureSettings("Android");android.name="Android";android.overridden=true;
        android.maxTextureSize=size;android.format=TextureImporterFormat.ASTC_6x6;
        importer.SetPlatformTextureSettings(android);importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
