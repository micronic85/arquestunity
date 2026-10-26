using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Перевод материалов glTFast (glTF metallic-roughness) в шейдер VorotaKraft/GateLitOccluded:
/// тот же вид + перекрытие реальными предметами и руками (Depth API) + общая нижняя граница шероховатости.
/// Шейдер задаёт MrLighting; без него материалы остаются как есть.
/// </summary>
public static class GateMaterials
{
    public static Shader Shader;
    public static float RoughnessMin = 0.3f;

    static readonly Dictionary<Material, Material> Cache = new Dictionary<Material, Material>();

    public static bool Enabled => Shader != null;

    public static bool IsGltf(Material m) => m != null && m.shader != null && m.shader.name.Contains("glTF-pbrMetallicRoughness");

    /// <summary>Конвертированная копия материала (кэшируется); не glTF или уже конвертированный — как есть.</summary>
    public static Material Get(Material src)
    {
        if (!Enabled || !IsGltf(src)) return src;
        if (Cache.TryGetValue(src, out var m) && m != null) return m;

        m = new Material(Shader) { name = src.name };
        bool tt = src.IsKeywordEnabled("_TEXTURE_TRANSFORM");
        CopyTex(src, "baseColorTexture", m, "_BaseMap");
        if (tt && src.HasProperty("baseColorTexture_ST"))
        {
            var st = src.GetVector("baseColorTexture_ST");
            m.SetTextureScale("_BaseMap", new Vector2(st.x, st.y));
            m.SetTextureOffset("_BaseMap", new Vector2(st.z, st.w));
        }
        if (src.HasProperty("baseColorFactor")) m.SetColor("_BaseColor", src.GetColor("baseColorFactor"));
        if (src.HasProperty("metallicFactor")) m.SetFloat("_Metallic", src.GetFloat("metallicFactor"));
        if (src.HasProperty("roughnessFactor")) m.SetFloat("_Roughness", src.GetFloat("roughnessFactor"));
        // Стекло и зеркальное (шероховатость в модели < 0,05) не трогаем — иначе блик расплывается белым пятном.
        float srcRough = src.HasProperty("roughnessFactor") ? src.GetFloat("roughnessFactor") : 0.5f;
        m.SetFloat("_RoughnessMin", srcRough < 0.05f ? 0f : RoughnessMin);
        // Резина в модели «металлическая» — из-за этого отражала окружение и синела
        if (src.name.StartsWith("резинка"))
        {
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_RoughnessMin", 0.75f);
        }
        CopyTex(src, "metallicRoughnessTexture", m, "_MRMap");
        CopyTex(src, "normalTexture", m, "_BumpMap");
        if (src.HasProperty("normalTexture_scale")) m.SetFloat("_BumpScale", src.GetFloat("normalTexture_scale"));
        CopyTex(src, "occlusionTexture", m, "_OcclusionMap");
        if (src.HasProperty("occlusionTexture_strength")) m.SetFloat("_OcclusionStrength", src.GetFloat("occlusionTexture_strength"));
        if (src.HasProperty("occlusionTexture") && src.GetTexture("occlusionTexture") == null) m.SetFloat("_OcclusionStrength", 0f);
        CopyTex(src, "emissiveTexture", m, "_EmissionMap");
        if (src.HasProperty("emissiveFactor")) m.SetColor("_EmissionColor", src.GetColor("emissiveFactor"));

        bool transparent = src.renderQueue >= 2600 || src.GetTag("RenderType", false) == "Transparent";
        bool clip = src.IsKeywordEnabled("_ALPHATEST_ON") || src.renderQueue == 2450;
        m.SetFloat("_Transparent", transparent ? 1f : 0f);
        m.SetFloat("_ZWrite", transparent ? 0f : 1f);
        m.SetFloat("_AlphaClip", clip ? 1f : 0f);
        if (clip && src.HasProperty("alphaCutoff")) m.SetFloat("_Cutoff", src.GetFloat("alphaCutoff"));
        m.renderQueue = transparent ? 3000 : (clip ? 2450 : 2000);

        // Ворота не принимают тени на себя: двойные поверхности панелей давали полосы (shadow acne);
        // тень от ворот на пол остаётся
        m.EnableKeyword("_RECEIVE_SHADOWS_OFF");
        Cache[src] = m;
        return m;
    }

    static void CopyTex(Material src, string from, Material dst, string to)
    {
        if (!src.HasProperty(from)) return;
        var t = src.GetTexture(from);
        if (t != null) dst.SetTexture(to, t);
    }

    /// <summary>Заменить материалы на всех рендерерах под root.</summary>
    public static void ConvertRenderers(Transform root)
    {
        if (!Enabled || root == null) return;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var c = Get(mats[i]);
                if (c != mats[i]) { mats[i] = c; changed = true; }
            }
            if (changed) r.sharedMaterials = mats;
        }
    }
}
