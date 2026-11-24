using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Uses the Editor's normal Asset Store service and signed-in account; never reads credentials.
public static class GatePlanetEarthAssetStore
{
    const long ProductId=23399;
    const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.FlattenHierarchy;
    static object Service(string name)
    {
        var assemblies=AppDomain.CurrentDomain.GetAssemblies();
        Type Find(string n)=>assemblies.Select(a=>a.GetType("UnityEditor.PackageManager.UI.Internal."+n,false)).FirstOrDefault(t=>t!=null)
            ??throw new InvalidOperationException("Editor Asset Store service not found: "+n);
        var container=Find("ServicesContainer");
        var instance=container.GetProperty("instance",Flags).GetValue(null);
        var resolve=container.GetMethods(Flags).First(m=>m.Name=="Resolve" && m.IsGenericMethodDefinition && m.GetParameters().Length==0);
        return resolve.MakeGenericMethod(Find(name)).Invoke(instance,null);
    }
    static object Property(object target,string name)=>target?.GetType().GetProperty(name,Flags)?.GetValue(target) ?? target?.GetType().GetField(name,Flags)?.GetValue(target);

    [MenuItem("Tools/AR Gates/Import Downloaded Planet Earth Free")]
    public static void Import()
    {
        string file=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Unity/Asset Store-5.x/headwards/3D ModelsEnvironmentsSci-Fi/Planet Earth Free.unitypackage");
        if(!System.IO.File.Exists(file))throw new InvalidOperationException("Earth package is not downloaded.");
        AssetDatabase.ImportPackage(file,false);
    }

    [MenuItem("Tools/AR Gates/Inspect Imported Planet Earth")]
    public static void InspectImported()
    {
        var lines=new List<string>();
        foreach(string guid in AssetDatabase.FindAssets("",new[]{"Assets/Planet Earth Free"}))
        {
            string path=AssetDatabase.GUIDToAssetPath(guid);
            if(!(path.EndsWith(".FBX",StringComparison.OrdinalIgnoreCase) || path.EndsWith(".png") || path.EndsWith(".mat")))continue;
            foreach(var asset in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if(asset is Mesh mesh)lines.Add($"MESH {path} | {mesh.name} | tris {mesh.triangles.Length/3} | bounds {mesh.bounds}");
                if(asset is Texture2D texture)lines.Add($"TEXTURE {path} | {texture.width}x{texture.height}");
                if(asset is Material mat)lines.Add($"MATERIAL {path} | shader {mat.shader.name} | texture {mat.mainTexture?.name}");
            }
        }
        System.IO.File.WriteAllLines("D:/blender/chatgpt/earth-import-report.txt",lines);
        Debug.Log("Inspected imported Earth assets.");
    }

    [MenuItem("Tools/AR Gates/Download Planet Earth Free")]
    public static void Download()
    {
        UnityEditor.PackageManager.UI.Window.Open(ProductId.ToString());
        var manager=Service("AssetStoreDownloadManager");
        manager.GetType().GetMethod("Download",new[]{typeof(IEnumerable<long>)}).Invoke(manager,new object[]{new[]{ProductId}});
        Debug.Log("Requested Planet Earth Free via the Editor's Asset Store download service.");
    }
    [MenuItem("Tools/AR Gates/Inspect Planet Earth Download")]
    public static void Inspect()
    {
        var cache=Service("AssetStoreCache");
        var local=cache.GetType().GetMethod("GetLocalInfo",Flags).Invoke(cache,new object[]{ProductId});
        var manager=Service("AssetStoreDownloadManager");
        var operation=manager.GetType().GetMethod("GetDownloadOperation",Flags).Invoke(manager,new object[]{(long?)ProductId});
        string result="Earth 23399: packagePath="+Property(local,"packagePath")
            +"; state="+Property(operation,"state")+"; progress="+Property(operation,"progressPercentage");
        System.IO.File.WriteAllText("D:/blender/chatgpt/earth-download-status.txt",result);
        Debug.Log(result);
    }
}
