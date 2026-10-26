#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using UnityEditor.XR.OpenXR;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

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
            if ((t.Contains("Meta") && (t.EndsWith("ARSessionFeature") || t.EndsWith("AROcclusionFeature")))
                || t.EndsWith("OpenXRCompositionLayersFeature"))
            {
                if (!f.enabled) { f.enabled = true; EditorUtility.SetDirty(f); }
                n++;
                Debug.Log($"MrDepthSetup: {t} включена");
            }
        }
        AssetDatabase.SaveAssets();

        // Остальные замечания OpenXR-валидатора: что чинится автоматически — чиним, остальное выводим
        var issues = new List<OpenXRFeature.ValidationRule>();
        OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Android);
        foreach (var r in issues)
        {
            bool fixable = r.fixIt != null && r.fixItAutomatic;
            if (fixable) r.fixIt();
            var msg = $"MrDepthSetup: [{(r.error ? "ошибка" : "предупр.")}] {r.message}" + (fixable ? " → исправлено" : "");
            if (r.error && !fixable) Debug.LogError(msg); else Debug.Log(msg);
        }
        AssetDatabase.SaveAssets();
        if (n < 3) Debug.LogWarning($"MrDepthSetup: найдено фич {n} из 3 — установлен ли пакет com.unity.xr.meta-openxr?");
    }
}
#endif
