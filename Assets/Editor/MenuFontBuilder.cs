using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

// Шрифт меню с кириллицей: Lato Semibold (OFL) → статический SDF-атлас 2048, выборка 90, отступ 9.
// Tools → VorotaKraft → Build menu font → Assets/Resources/Fonts/MenuFont SDF.asset (GateColorPicker.MenuFont).
public static class MenuFontBuilder
{
    const string FontPath = "Assets/Fonts/Lato-Semibold.ttf";
    const string AssetPath = "Assets/Resources/Fonts/MenuFont SDF.asset";
    const string Chars =
        "АБВГДЕЁЖЗИЙКЛМНОПРСТУФХЦЧШЩЪЫЬЭЮЯабвгдеёжзийклмнопрстуфхцчшщъыьэюя" +
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" +
        " .,:;!?-–—+=*/\\()[]{}<>«»\"'%№#@&_|~^°×·…→←↑↓±";

    [MenuItem("Tools/VorotaKraft/Build menu font")]
    public static void Build()
    {
        var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (font == null) { Debug.LogError("MenuFontBuilder: нет " + FontPath); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
        AssetDatabase.DeleteAsset(AssetPath);

        var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, false);
        fa.name = "MenuFont SDF";
        AssetDatabase.CreateAsset(fa, AssetPath);
        fa.atlasTextures[0].name = "MenuFont Atlas";
        AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
        fa.material.name = "MenuFont Material";
        AssetDatabase.AddObjectToAsset(fa.material, fa);

        fa.TryAddCharacters(Chars, out string missing);
        fa.atlasPopulationMode = AtlasPopulationMode.Static; // атлас запечён — в сборке ничего не догенерируется
        EditorUtility.SetDirty(fa);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"MenuFontBuilder: {AssetPath} — символов {fa.characterTable.Count}" + (string.IsNullOrEmpty(missing) ? "" : $", нет в шрифте: {missing}"));
    }
}
