using System.Text;
using UnityEditor;
using UnityEngine;

// Временная диагностика: детали секций адаптивных ворот с границами (Play Mode) → Logs/gate_parts_dump.txt
public static class GateDumpParts
{
    [MenuItem("Tools/VorotaKraft/Dump gate parts")]
    static void Dump()
    {
        var gb = Object.FindFirstObjectByType<GateBuilder>();
        if (gb == null || gb.Root == null) { Debug.LogWarning("no gate"); return; }
        var sb = new StringBuilder();
        foreach (Transform s in gb.Root)
        {
            if (!s.name.StartsWith("Section_") && s.name != "Hardware") continue;
            sb.AppendLine($"== {s.name} pos={s.localPosition.ToString("F3")}");
            foreach (var mf in s.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = s.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh ? mf.sharedMesh.bounds : default;
                Bounds b = default; bool any = false;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
                var r = mf.GetComponent<Renderer>();
                string mats = r ? string.Join(",", System.Array.ConvertAll(r.sharedMaterials, x => x ? x.name + "/" + x.shader.name : "null")) : "";
                if (r && r.HasPropertyBlock()) { var mpb = new MaterialPropertyBlock(); r.GetPropertyBlock(mpb); mats += $" CLIP mode={mpb.GetFloat("_ClipMode")} min={mpb.GetVector("_ClipMin")} max={mpb.GetVector("_ClipMax")}"; }
                sb.AppendLine($"{(mf.gameObject.activeInHierarchy ? " " : "-")} {Path(mf.transform, s)} v={(mf.sharedMesh ? mf.sharedMesh.vertexCount : 0)} min={b.min.ToString("F3")} max={b.max.ToString("F3")} [{mats}]");
            }
        }
        System.IO.File.WriteAllText("Logs/gate_parts_dump.txt", sb.ToString());
        Debug.Log("dumped");
    }
    static string Path(Transform t, Transform root) { var p = t.name; while (t.parent != null && t.parent != root) { t = t.parent; p = t.name + "/" + p; } return p; }
}
