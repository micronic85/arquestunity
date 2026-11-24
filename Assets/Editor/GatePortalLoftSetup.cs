using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class GatePortalLoftSetup
{
    const string MeshPath="Assets/Models/Portal/IndustrialLoft.asset";
    const string TexturePath="Assets/Textures/Portal/IndustrialLoft_Baked.png";
    [MenuItem("Tools/AR Gates/Install Optimized Loft In Portal")]
    public static void Install()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(Application.isPlaying || scene.isDirty || scene.path!="Assets/lastvorotanew.unity")
            throw new InvalidOperationException("Open saved main scene in Edit Mode.");
        string source="D:/blender/chatgpt/loft-quest/IndustrialLoft.meshbin";
        var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if(File.Exists(source))
        {
            using var reader=new BinaryReader(File.OpenRead(source));
            if(reader.ReadInt32()!=0x4c465431)throw new InvalidDataException("Loft mesh header");
            int nv=reader.ReadInt32(),ni=reader.ReadInt32();
            if(nv<=0 || nv>1000000 || ni<=0 || ni>3000000 || ni%3!=0
                || reader.BaseStream.Length!=12L+nv*20L+ni*4L)throw new InvalidDataException("Loft mesh size");
            var vertices=new Vector3[nv];var uv=new Vector2[nv];var indices=new int[ni];
            for(int i=0;i<nv;i++)
            {
                vertices[i]=new Vector3(reader.ReadSingle(),reader.ReadSingle(),reader.ReadSingle());
                uv[i]=new Vector2(reader.ReadSingle(),reader.ReadSingle());
            }
            for(int i=0;i<ni;i++)
            {
                indices[i]=reader.ReadInt32();
                if(indices[i]<0 || indices[i]>=nv)throw new InvalidDataException("Loft mesh index");
            }
            bool create=!mesh;
            if(create)mesh=new Mesh{name="Industrial Loft - Quest"};else mesh.Clear();
            mesh.indexFormat=IndexFormat.UInt32;mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=indices;
            mesh.RecalculateBounds();mesh.RecalculateNormals();
            if(create)AssetDatabase.CreateAsset(mesh,MeshPath);else EditorUtility.SetDirty(mesh);
        }
        if(!mesh)throw new InvalidOperationException("Optimized loft mesh is missing.");
        var importer=(TextureImporter)AssetImporter.GetAtPath(TexturePath);
        if(!importer)throw new InvalidOperationException("Baked loft atlas is missing.");
        importer.sRGBTexture=true;importer.alphaSource=TextureImporterAlphaSource.None;
        importer.mipmapEnabled=true;importer.wrapMode=TextureWrapMode.Clamp;importer.maxTextureSize=4096;
        importer.textureCompression=TextureImporterCompression.CompressedHQ;
        var android=importer.GetPlatformTextureSettings("Android");android.name="Android";
        android.overridden=true;android.maxTextureSize=4096;android.format=TextureImporterFormat.ASTC_6x6;
        importer.SetPlatformTextureSettings(android);importer.SaveAndReimport();
        var portal=UnityEngine.Object.FindFirstObjectByType<GatePortal>();
        Undo.RecordObject(portal,"Install optimized loft");
        portal.bakedWorldMesh=mesh;portal.bakedWorldTexture=AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        portal.bakedWorldOffset=new Vector3(-2,0,0);
        EditorUtility.SetDirty(portal);AssetDatabase.SaveAssets();EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Installed optimized loft: {mesh.triangles.Length/3} triangles, one baked world material.");
    }
}
