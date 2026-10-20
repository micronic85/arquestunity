using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class ArGatesProjectSetup
{
    [MenuItem("Tools/AR Gates/Apply Player Settings")]
    public static void Apply()
    {
        PlayerSettings.companyName = "Vorota KRAFT";
        PlayerSettings.productName = "AR Vorota";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.vorotakraft.argates");
        AssetDatabase.SaveAssets();
        Debug.Log($"AR Gates: company={PlayerSettings.companyName}, product={PlayerSettings.productName}, id={PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)}");
    }
}
