using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using UnityEngine;

/// <summary>
/// Встроенная калитка: открывается рукой за ручку калитки, всегда наружу, только при закрытых воротах.
/// Створка — куски нижних секций в рамке «калитка*»: в каждой секции свой шарнир WicketLeaf на одной
/// вертикальной оси (у наружной плоскости полотна, со стороны петель — противоположной ручке калитки).
/// Обшивка секций не режется: та же деталь есть и на воротах, и на створке, а шейдер отбрасывает
/// лишнее по рамке калитки (_ClipMode: 1 — всё внутри рамки, 2 — всё снаружи), поэтому гофры и
/// рисунок дерева на створке продолжают рисунок полотна. Кромки проёма в полотне закрыты профилем.
/// </summary>
public partial class GateBuilder
{
    [Header("Калитка")]
    [Tooltip("Высота калитки от пола (верх рамки), м — стандарт 2000 мм; выше рамка не идёт, сколько бы ни было секций")]
    public float wicketHeight = 2.0f;
    [Tooltip("Максимальный угол открывания калитки, °")]
    public float wicketMaxAngle = 100f;
    [Range(-1f, 100f)]
    [Tooltip("Проверка в редакторе: угол калитки (-1 — не трогать)")]
    public float debugWicketAngle = -1f;

    /// <summary>Угол калитки, ° (0 — закрыта).</summary>
    public float WicketAngle { get; private set; }
    public bool WicketOpen => WicketAngle > 0.5f;
    /// <summary>Калитку можно открывать: она есть, а ворота закрыты и не едут.</summary>
    public bool CanSwingWicket => HasWicket && _wicketPivots.Count > 0 && Progress < 0.002f && _anim == null;

    readonly List<Transform> _wicketPivots = new List<Transform>();
    readonly List<Transform> _wicketGrabs = new List<Transform>();
    readonly List<(Transform t, Vector3 pos, Quaternion rot)> _wicketGrabPoses = new List<(Transform, Vector3, Quaternion)>();
    bool _wicketHingeAtMin = true;
    float _lastDebugWicket = -2f;

    static readonly int ClipModeId = Shader.PropertyToID("_ClipMode");
    static readonly int ClipMatrixId = Shader.PropertyToID("_ClipMatrix");
    static readonly int ClipMinId = Shader.PropertyToID("_ClipMin");
    static readonly int ClipMaxId = Shader.PropertyToID("_ClipMax");

    static string PartName(Transform wrapper) => wrapper.childCount > 0 && wrapper.name.EndsWith("_w") ? wrapper.GetChild(0).name : wrapper.name;

    void BuildWicket()
    {
        _wicketPivots.Clear();
        _wicketGrabs.Clear();
        _wicketGrabPoses.Clear();
        _wicketGrab = null;
        WicketAngle = 0f;
        _lastDebugWicket = -2f; // после пересборки снова применить угол из инспектора
        if (!HasWicket || _sections.Count == 0) return;

        // Рамка калитки в каждой секции (в координатах секции)
        int n = _sections.Count, top = -1;
        var frame = new Bounds?[n];
        var frameParts = new List<Transform>[n];
        Material alu = null;
        Bounds? handle = null; int handleSec = -1;
        for (int i = 0; i < n; i++)
        {
            foreach (Transform w in _sections[i])
            {
                if (!w.gameObject.activeSelf || !LocalBounds(_sections[i], w, out var b)) continue;
                string pn = PartName(w);
                if (pn == "WicketHandle") { handle = b; handleSec = i; continue; }
                if (!pn.StartsWith("калитка") || b.size.z > 0.5f * W0) continue;
                if (frame[i] == null) frame[i] = b; else { var f = frame[i].Value; f.Encapsulate(b); frame[i] = f; }
                (frameParts[i] ??= new List<Transform>()).Add(w);
                if (alu == null) { var r = w.GetComponentInChildren<Renderer>(true); if (r) alu = r.sharedMaterial; }
            }
        }
        if (frame[0] == null) return;

        // Высота калитки стандартная: верх рамки на wicketHeight от пола. Секции целиком выше — без калитки,
        // в секции, через которую проходит верх, рамка обрезается шейдером и получает верхнюю перекладину.
        float secBottom = 0f;
        float topCut = float.PositiveInfinity; // верх калитки в системе секции top (∞ — рамка модели ниже)
        for (int i = 0; i < n; i++)
        {
            float local = wicketHeight - secBottom;
            secBottom += _heights[i];
            if (frame[i] == null) continue;
            if (local < 0.05f)
            {
                foreach (var w in frameParts[i]) w.gameObject.SetActive(false); // рамка выше калитки
                frame[i] = null;
                continue;
            }
            top = i;
            if (local < frame[i].Value.max.y - 0.005f)
            {
                topCut = local;
                var f = frame[i].Value; f.max = new Vector3(f.max.x, local, f.max.z); frame[i] = f;
                // остальные секции выше — уже без калитки
                for (int k = i + 1; k < n; k++)
                    if (frameParts[k] != null) foreach (var w in frameParts[k]) w.gameObject.SetActive(false);
                break;
            }
        }
        if (top < 0) return;

        float z0 = float.MaxValue, z1 = float.MinValue, xOut = float.MaxValue, xIn = float.MinValue;
        for (int i = 0; i <= top; i++)
        {
            if (frame[i] == null) continue;
            var f = frame[i].Value;
            z0 = Mathf.Min(z0, f.min.z); z1 = Mathf.Max(z1, f.max.z);
            xOut = Mathf.Min(xOut, f.min.x); xIn = Mathf.Max(xIn, f.max.x);
        }
        float yBot = frame[0].Value.min.y, yTop = frame[top].Value.max.y;
        if (!float.IsPositiveInfinity(topCut))
        {
            // рамку верхнего куска — по линии верха калитки; перекладина — на створку
            var cutMin = new Vector4(-10f, -10f, -10f, 0f); var cutMax = new Vector4(10f, topCut, 10f, 0f);
            foreach (var w in frameParts[top])
                foreach (var r in w.GetComponentsInChildren<Renderer>(true))
                    SetClip(r, 2f, _sections[top].worldToLocalMatrix * r.transform.localToWorldMatrix, cutMin, cutMax);
            const float bar = 0.035f;
            AddBox(_sections[top], "WicketTopBar", new Vector3((xOut + xIn) * 0.5f, topCut - bar * 0.5f, (z0 + z1) * 0.5f),
                new Vector3(xIn - xOut, bar, z1 - z0), alu);
        }
        // Петли — со стороны, противоположной ручке калитки
        _wicketHingeAtMin = handle == null || handle.Value.center.z > (z0 + z1) * 0.5f;
        float hingeZ = _wicketHingeAtMin ? z0 : z1;

        const float strip = 0.012f;
        float stripX = (xOut + 0.002f + xIn - 0.001f) * 0.5f, stripD = (xIn - 0.001f) - (xOut + 0.002f);

        for (int i = 0; i <= top; i++)
        {
            var sec = _sections[i];
            float lo = i == 0 ? yBot : -10f, hi = i == top ? yTop : 10f;

            var pivot = new GameObject("WicketLeaf").transform;
            pivot.SetParent(sec, false);
            pivot.localPosition = new Vector3(xOut, 0f, hingeZ);
            _wicketPivots.Add(pivot);

            var clipMin = new Vector4(-10f, lo, z0, 0f);
            var clipMax = new Vector4(10f, hi, z1, 0f);
            float panelMin = float.MaxValue, panelMax = float.MinValue, sillTop = float.MinValue;

            var children = new List<Transform>();
            foreach (Transform w in sec) if (w != pivot) children.Add(w);
            foreach (var w in children)
            {
                if (!LocalBounds(sec, w, out var b)) continue;
                string pn = PartName(w);
                if (b.size.z > 0.5f * W0)
                {
                    if (pn == "калитка.004") sillTop = Mathf.Max(sillTop, b.max.y); // нижний профиль полотна — остаётся
                    if (!w.gameObject.activeSelf) continue;
                    if (pn.StartsWith("панель")) { panelMin = Mathf.Min(panelMin, b.min.y); panelMax = Mathf.Max(panelMax, b.max.y); }
                    if (b.max.y <= lo + 0.005f || b.min.y >= hi - 0.005f) continue;
                    // Широкая деталь (обшивка): копия на створке, на полотне — вырез по рамке
                    var copy = Instantiate(w.gameObject, pivot).transform;
                    copy.name = w.name;
                    copy.SetPositionAndRotation(w.position, w.rotation);
                    copy.localScale = w.localScale;
                    var src = w.GetComponentsInChildren<Renderer>(true);
                    var dst = copy.GetComponentsInChildren<Renderer>(true);
                    for (int k = 0; k < src.Length && k < dst.Length; k++)
                    {
                        var m = sec.worldToLocalMatrix * src[k].transform.localToWorldMatrix;
                        SetClip(src[k], 1f, m, clipMin, clipMax);
                        SetClip(dst[k], 2f, m, clipMin, clipMax);
                    }
                }
                else if (pn.StartsWith("калитка") && w.gameObject.activeSelf)
                    w.SetParent(pivot, true); // рамка створки (в верхнем куске обрезана по верху калитки)
                else if (b.min.z >= z0 - 0.01f && b.max.z <= z1 + 0.01f && b.min.y >= lo - 0.01f && b.max.y <= hi + 0.01f)
                    w.SetParent(pivot, true); // рамка, ручка калитки, окно в калитке, верхняя перекладина
                else if (i == top && b.min.z < z1 && b.max.z > z0 && b.min.y < hi && b.max.y > hi)
                    w.gameObject.SetActive(false); // окно на линии верха калитки — его разрезало бы, убираем
            }
            if (panelMin > panelMax) { panelMin = 0f; panelMax = _heights[i]; }

            // Кромки проёма в полотне — алюминиевый профиль (на полотне, не на створке)
            float ya = Mathf.Max(lo, panelMin), yb = Mathf.Min(hi, panelMax);
            if (yb > ya)
            {
                AddBox(sec, "WicketJamb_A", new Vector3(stripX, (ya + yb) * 0.5f, z0 - strip * 0.5f), new Vector3(stripD, yb - ya, strip), alu);
                AddBox(sec, "WicketJamb_B", new Vector3(stripX, (ya + yb) * 0.5f, z1 + strip * 0.5f), new Vector3(stripD, yb - ya, strip), alu);
            }
            if (i == top && hi < panelMax - 0.005f)
                AddBox(sec, "WicketHead", new Vector3(stripX, hi + strip * 0.5f, (z0 + z1) * 0.5f), new Vector3(stripD, strip, z1 - z0 + 2f * strip), alu);
            if (i == 0)
            {
                float s0 = sillTop > float.MinValue ? sillTop : panelMin;
                if (lo - s0 > 0.003f)
                    AddBox(sec, "WicketSill", new Vector3(stripX, (s0 + lo) * 0.5f, (z0 + z1) * 0.5f), new Vector3(stripD, lo - s0, z1 - z0), alu);
            }

            // Петли на оси: у низа и у верха куска створки в каждой секции (на стыках секций — парами)
            float pa = Mathf.Max(lo, panelMin) + 0.09f, pb = Mathf.Min(hi, panelMax) - 0.09f;
            AddHinge(sec, new Vector3(xOut, pa, hingeZ), alu);
            if (pb - pa > 0.25f) AddHinge(sec, new Vector3(xOut, pb, hingeZ), alu);
        }

        AttachWicketGrabs(handle, handleSec, xOut, xIn);
        ApplyWicketAngle();
        if (debugLogs) Debug.Log($"GateBuilder: калитка z {z0:F3}..{z1:F3}, y {yBot:F3}..{yTop:F3} (секции 1–{top + 1}), петли {(_wicketHingeAtMin ? "z-" : "z+")}, захватов {_wicketGrabs.Count}");
    }

    static void SetClip(Renderer r, float mode, Matrix4x4 m, Vector4 min, Vector4 max)
    {
        var mpb = new MaterialPropertyBlock();
        r.GetPropertyBlock(mpb);
        mpb.SetFloat(ClipModeId, mode);
        mpb.SetMatrix(ClipMatrixId, m);
        mpb.SetVector(ClipMinId, min);
        mpb.SetVector(ClipMaxId, max);
        r.SetPropertyBlock(mpb);
    }

    static void AddHinge(Transform parent, Vector3 pos, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "WicketHinge";
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(0.018f, 0.035f, 0.018f); // Ø18 × 70 мм
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    // Захват — копии невидимой ручки ворот (ruchka) с ISDK-захватом: снаружи и изнутри ручки калитки.
    // У каждой свой кинематический Rigidbody: иначе ISDK собирает коллайдеры всех ручек с Rigidbody ворот.
    void AttachWicketGrabs(Bounds? handle, int handleSec, float xOut, float xIn)
    {
        if (handle == null || handleSec < 0 || handleObject == null || !_handleCenterLocal.HasValue) return;
        var sec = _sections[handleSec];
        var pivot = _wicketPivots[Mathf.Min(handleSec, _wicketPivots.Count - 1)];
        var hb = handle.Value;
        var points = new List<Vector3>();
        if (hb.min.x < xOut - 0.01f) points.Add(new Vector3((hb.min.x + xOut) * 0.5f, hb.center.y, hb.center.z));
        if (hb.max.x > xIn + 0.01f) points.Add(new Vector3((hb.max.x + xIn) * 0.5f, hb.center.y, hb.center.z));
        if (points.Count == 0) points.Add(hb.center);
        foreach (var p in points)
        {
            var g = Instantiate(handleObject.gameObject, pivot).transform;
            g.name = "WicketGrab";
            g.rotation = handleObject.rotation;
            g.position += sec.TransformPoint(p) - g.TransformPoint(_handleCenterLocal.Value);
            OwnRigidbody(g);
            _wicketGrabs.Add(g);
            _wicketGrabPoses.Add((g, g.localPosition, g.localRotation));
        }
    }

    static void OwnRigidbody(Transform t)
    {
        var rb = t.GetComponent<Rigidbody>();
        if (rb == null) rb = t.gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        foreach (var gi in t.GetComponentsInChildren<GrabInteractable>(true)) gi.InjectRigidbody(rb);
        foreach (var hi in t.GetComponentsInChildren<HandGrabInteractable>(true)) hi.InjectRigidbody(rb);
    }

    /// <summary>Повернуть калитку (°, 0 — закрыта); только при закрытых воротах.</summary>
    public void SetWicketAngle(float angle)
    {
        if (!CanSwingWicket) angle = 0f;
        WicketAngle = Mathf.Clamp(angle, 0f, wicketMaxAngle);
        ApplyWicketAngle();
    }

    void ApplyWicketAngle()
    {
        var rot = Quaternion.Euler(0f, _wicketHingeAtMin ? -WicketAngle : WicketAngle, 0f);
        foreach (var p in _wicketPivots) if (p) p.localRotation = rot;
    }

    // Угол точки вокруг оси петель (в плоскости секции 1): 0 — в плоскости полотна, + — наружу
    float WicketAngleOf(Vector3 world)
    {
        var pivot = _wicketPivots[0];
        Vector3 q = pivot.parent.InverseTransformPoint(world) - pivot.localPosition;
        float along = _wicketHingeAtMin ? q.z : -q.z;
        return Mathf.Atan2(-q.x, along) * Mathf.Rad2Deg;
    }

    // ---------- захват ----------

    Transform _wicketGrab;     // ручка, за которую держат
    float _wicketGrabStartAngle, _wicketGrabStartHand;

    void UpdateWicketGrab()
    {
        if (debugWicketAngle >= 0f && !Mathf.Approximately(debugWicketAngle, _lastDebugWicket))
        {
            _lastDebugWicket = debugWicketAngle;
            SetWicketAngle(debugWicketAngle);
        }
        if (_wicketGrabs.Count == 0) return;

        Transform grabbed = null; Vector3? hand = null;
        foreach (var g in _wicketGrabs)
        {
            if (g == null) continue;
            hand = GrabPoint(g);
            if (hand.HasValue) { grabbed = g; break; }
        }
        if (grabbed == null) { _wicketGrab = null; return; }
        if (_wicketGrab != grabbed)
        {
            if (!CanSwingWicket) return;
            _wicketGrab = grabbed;
            _wicketGrabStartAngle = WicketAngle;
            _wicketGrabStartHand = WicketAngleOf(hand.Value);
            if (debugLogs) Debug.Log($"GateBuilder: калитка — захват, угол {WicketAngle:F1}");
        }
        float delta = Mathf.DeltaAngle(_wicketGrabStartHand, WicketAngleOf(hand.Value));
        SetWicketAngle(_wicketGrabStartAngle + delta);
    }

    // Точка руки/контроллера, держащих ручку (null — не держат)
    static Vector3? GrabPoint(Transform g)
    {
        foreach (var hgi in g.GetComponentsInChildren<HandGrabInteractable>(true))
        {
            if (hgi.State != InteractableState.Select) continue;
            foreach (var v in hgi.SelectingInteractorViews)
                if (v is HandGrabInteractor hgr) return (hgr.PalmPoint ? hgr.PalmPoint : hgr.transform).position;
                else if (v is MonoBehaviour mb) return mb.transform.position;
        }
        foreach (var gi in g.GetComponentsInChildren<GrabInteractable>(true))
        {
            if (gi.State != InteractableState.Select) continue;
            foreach (var v in gi.SelectingInteractorViews)
                if (v is MonoBehaviour mb) return mb.transform.position;
        }
        return null;
    }

    // Grabbable двигает только невидимую пустышку, но на всякий случай держим захваты на месте
    void KeepWicketGrabs()
    {
        foreach (var (t, pos, rot) in _wicketGrabPoses)
            if (t) { t.localPosition = pos; t.localRotation = rot; }
    }
}
