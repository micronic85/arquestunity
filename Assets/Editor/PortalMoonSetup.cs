using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class PortalMoonSetup
{
    const string Root="Assets/Models/PortalMoon/";
    [Serializable] class Geometry { public Vector3[] vertices,normals;public Vector2[] uv;public int[] triangles; }
    [MenuItem("Tools/AR Gates/Install Orbiting Moon")]
    public static void Install()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(Application.isPlaying || scene.isDirty || scene.path!="Assets/lastvorotanew.unity")
            throw new InvalidOperationException("Open saved main scene in Edit Mode.");
        var json=AssetDatabase.LoadAssetAtPath<TextAsset>(Root+"MoonGeometry.json");
        var data=JsonUtility.FromJson<Geometry>(json.text);
        for(int i=0;i<data.vertices.Length;i++)
        {
            data.vertices[i].x=-data.vertices[i].x;data.normals[i].x=-data.normals[i].x;
            data.uv[i].y=1-data.uv[i].y;
        }
        for(int i=0;i<data.triangles.Length;i+=3)
        {int swap=data.triangles[i];data.triangles[i]=data.triangles[i+2];data.triangles[i+2]=swap;}
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(Root+"MoonMesh.asset");
        bool create=!mesh;if(create)mesh=new Mesh();else mesh.Clear();
        mesh.name="Sketchfab Moon - original 2880 triangles";mesh.vertices=data.vertices;mesh.normals=data.normals;
        mesh.uv=data.uv;mesh.triangles=data.triangles;mesh.RecalculateBounds();mesh.RecalculateTangents();
        if(create)AssetDatabase.CreateAsset(mesh,Root+"MoonMesh.asset");else EditorUtility.SetDirty(mesh);
        var color=Import("MoonColor.png",false);var normal=Import("MoonNormal.png",true);
        var portal=UnityEngine.Object.FindFirstObjectByType<GatePortal>();
        Undo.RecordObject(portal,"Install palm-controlled orbiting Moon");
        portal.moonMesh=mesh;portal.moonTexture=color;portal.moonNormalTexture=normal;
        portal.moonRadius=.065f;portal.moonOrbitDistance=1;portal.moonOrbitSpeed=8;portal.moonSpinSpeed=3;portal.moonOrbitEnabled=true;
        EditorUtility.SetDirty(portal);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
        Debug.Log("Moon installed: user's Sketchfab mesh, 2880 triangles, 2K color/normal, matte lighting from portal Sun, metre orbit with Earth clearance.");
    }
    static Texture2D Import(string file,bool normal)
    {
        var importer=(TextureImporter)AssetImporter.GetAtPath(Root+file);
        importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;importer.sRGBTexture=!normal;
        importer.mipmapEnabled=true;importer.isReadable=false;importer.maxTextureSize=2048;importer.anisoLevel=4;
        importer.wrapMode=TextureWrapMode.Clamp;importer.filterMode=FilterMode.Trilinear;
        var android=importer.GetPlatformTextureSettings("Android");android.name="Android";android.overridden=true;
        android.maxTextureSize=2048;android.format=TextureImporterFormat.ASTC_6x6;
        importer.SetPlatformTextureSettings(android);importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(Root+file);
    }
}
