#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.OpenXR;

/// <summary>Включает OpenXR-фичи, нужные для Depth API (Meta Quest: Session + Occlusion) на Android.</summary>
public static class MrDepthSetup
{
    [MenuItem("Tools/VorotaKraft/Setup Depth API (OpenXR features)")]
    public static void Setup()
    {
        var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
        if (settings == null) { Debug.LogError("MrDepthSetup: нет OpenXR настроек для Android"); return; }
        int n = 0;
        foreach (var f in settings.GetFeatures())
        {
            var t = f.GetType().FullName;
            if (t.Contains("Meta") && (t.EndsWith("ARSessionFeature") || t.EndsWith("AROcclusionFeature")))
            {
                if (!f.enabled) { f.enabled = true; EditorUtility.SetDirty(f); }
                n++;
                Debug.Log($"MrDepthSetup: {t} включена");
            }
        }
        AssetDatabase.SaveAssets();
        if (n < 2) Debug.LogWarning($"MrDepthSetup: найдено фич {n} из 2 — установлен ли пакет com.unity.xr.meta-openxr?");
    }
}
#endif
