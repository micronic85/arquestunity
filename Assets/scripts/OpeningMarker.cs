using System;
using TMPro;
using UnityEngine;

/// <summary>
/// Ручная разметка проёма — для высоких ворот, до верха которых не достаёт сканер глубины Quest.
/// Сенсор не нужен: луч (контроллер — курок, рука — щипок указательным) пересекается
///  1–2) с полом — левый и правый нижние углы проёма (уровень пола известен всегда);
///  3)   с вертикальной плоскостью стены через эти углы — верх проёма (любой высоты).
/// Готовый проём попадает в OpeningDetector как обычный: подсветка, размер, «Вписать ворота».
/// Кнопка B на правом контроллере — шаг назад.
/// </summary>
public class OpeningMarker : MonoBehaviour
{
    public enum Step { Idle, Left, Right, Top }

    public OpeningDetector openings;
    [Tooltip("Материал линий (по умолчанию — как у подсветки проёмов)")]
    public Material lineMaterial;
    public float lineWidth = 0.006f;
    [Tooltip("Минимальная ширина и высота проёма, м")]
    public float minSize = 0.4f;
    public bool debugLogs;
    [Tooltip("Проверка в редакторе: разметить проём 3,5 × 4,5 м в 4 м перед началом координат")]
    public bool debugTestOpening;

    public Step Current { get; private set; } = Step.Idle;
    public bool Active => Current != Step.Idle;
    public string Hint { get; private set; } = "";
    public event Action Changed;

    OVRCameraRig _rig;
    OVRHand _hand;
    OVRSkeleton _handSkeleton;
    bool _wasPinching;
    float _armTime;
    Vector3 _left, _right;
    LineRenderer _ray, _outline;
    Transform _dot;
    TextMeshPro _label;

    void Start()
    {
        if (openings == null) openings = FindFirstObjectByType<OpeningDetector>();
        if (lineMaterial == null && openings != null) lineMaterial = openings.selectedMaterial != null ? openings.selectedMaterial : openings.lineMaterial;
        _ray = MakeLine("MarkerRay", 2);
        _outline = MakeLine("MarkerOutline", 5);
        var d = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        DestroyImmediate(d.GetComponent<Collider>());
        d.name = "MarkerDot";
        d.transform.SetParent(transform, false);
        d.transform.localScale = Vector3.one * 0.03f;
        if (lineMaterial) d.GetComponent<Renderer>().sharedMaterial = lineMaterial;
        _dot = d.transform;
        var go = new GameObject("MarkerLabel", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        _label = go.AddComponent<TextMeshPro>();
        _label.alignment = TextAlignmentOptions.Center;
        _label.fontSize = 2f;
        _label.rectTransform.sizeDelta = new Vector2(1.2f, 0.2f);
        _label.color = Color.white;
        ShowVisuals(false);
    }

    LineRenderer MakeLine(string name, int points)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = points;
        lr.useWorldSpace = true;
        lr.startWidth = lr.endWidth = lineWidth;
        lr.numCapVertices = 2;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        if (lineMaterial) lr.sharedMaterial = lineMaterial;
        return lr;
    }

    // ---------- управление ----------

    /// <summary>Начать разметку (или отменить, если уже идёт).</summary>
    public void Toggle()
    {
        if (Active) Cancel();
        else { SetStep(Step.Left); _armTime = Time.time + 0.6f; } // пауза: палец только что нажал кнопку меню
    }

    public void Cancel() => SetStep(Step.Idle);

    void SetStep(Step s)
    {
        Current = s;
        Hint = s switch
        {
            Step.Left => "наведите луч на левый нижний угол проёма (у пола) и нажмите курок / сожмите щипок",
            Step.Right => "теперь правый нижний угол",
            Step.Top => "наведите луч на верх проёма (перемычку)",
            _ => "",
        };
        if (s == Step.Idle) ShowVisuals(false);
        if (debugLogs) Debug.Log($"OpeningMarker: шаг {s}");
        Changed?.Invoke();
    }

    void ShowVisuals(bool v)
    {
        if (_ray) _ray.enabled = v;
        if (_outline) _outline.enabled = v && Current != Step.Left;
        if (_dot) _dot.gameObject.SetActive(v);
        if (_label) _label.gameObject.SetActive(v);
    }

    // ---------- указатель ----------

    bool EnsureRefs()
    {
        if (_rig == null) _rig = FindFirstObjectByType<OVRCameraRig>();
        if (_hand == null || _handSkeleton == null)
            foreach (var sk in FindObjectsByType<OVRSkeleton>(FindObjectsSortMode.None))
                if (sk.GetSkeletonType().ToString().Contains("Right")) { _handSkeleton = sk; _hand = sk.GetComponent<OVRHand>(); if (_hand) break; }
        return _rig != null;
    }

    // Луч и нажатие: контроллер (если в руке) точнее, иначе — щипок правой рукой
    bool GetPointer(out Ray ray, out bool press, out bool back)
    {
        ray = default; press = false; back = false;
        if (OVRInput.IsControllerConnected(OVRInput.Controller.RTouch) &&
            (OVRInput.GetActiveController() & OVRInput.Controller.RTouch) != 0)
        {
            var a = _rig.rightControllerAnchor;
            ray = new Ray(a.position, a.forward);
            press = OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
            back = OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch);
            _wasPinching = false;
            return true;
        }
        if (_hand != null && _hand.IsTracked && _hand.IsPointerPoseValid && _hand.PointerPose != null)
        {
            ray = new Ray(_hand.PointerPose.position, _hand.PointerPose.forward);
            bool pinch = _hand.GetFingerIsPinching(OVRHand.HandFinger.Index);
            press = pinch && !_wasPinching;
            _wasPinching = pinch;
            return true;
        }
        _wasPinching = false;
        return false;
    }

    float FloorY()
    {
        var room = Meta.XR.MRUtilityKit.MRUK.Instance != null ? Meta.XR.MRUtilityKit.MRUK.Instance.GetCurrentRoom() : null;
        if (room != null)
            foreach (var f in room.FloorAnchors) if (f) return f.transform.position.y;
        return _rig != null ? _rig.trackingSpace.position.y : 0f; // начало трекинга — на уровне пола
    }

    static bool HitPlane(Ray ray, Plane plane, float maxDist, out Vector3 hit)
    {
        hit = default;
        if (!plane.Raycast(ray, out float t) || t <= 0f || t > maxDist) return false;
        hit = ray.GetPoint(t);
        return true;
    }

    // ---------- цикл ----------

    void Update()
    {
        if (debugTestOpening && openings != null)
        {
            debugTestOpening = false;
            EnsureRefs();
            float f = FloorY();
            openings.AddManual(new Vector3(-1.75f, f, 4f), new Vector3(1.75f, f, 4f), 4.5f, Vector3.back);
        }
        if (!Active || !EnsureRefs()) return;
        if (!GetPointer(out var ray, out bool press, out bool back)) { ShowVisuals(false); return; }
        if (Time.time < _armTime) press = false;
        if (back) { SetStep(Current == Step.Top ? Step.Right : Step.Left); return; }

        float floor = FloorY();
        Vector3 hit; bool ok;
        float height = 0f;
        if (Current == Step.Top)
        {
            Vector3 along = _right - _left; along.y = 0;
            Vector3 n = Vector3.Cross(along.normalized, Vector3.up);
            ok = HitPlane(ray, new Plane(n, _left), 40f, out hit);
            if (ok) height = Mathf.Max(0f, hit.y - floor);
        }
        else ok = HitPlane(ray, new Plane(Vector3.up, new Vector3(0, floor, 0)), 25f, out hit);

        ShowVisuals(true);
        _ray.SetPosition(0, ray.origin);
        _ray.SetPosition(1, ok ? hit : ray.GetPoint(3f));
        _dot.gameObject.SetActive(ok);
        if (ok) _dot.position = hit;

        // предпросмотр: линия низа, затем прямоугольник проёма
        if (Current == Step.Right && ok)
        {
            _outline.enabled = true;
            _outline.positionCount = 2;
            _outline.SetPosition(0, _left); _outline.SetPosition(1, hit);
            Label(hit, $"{Flat(hit - _left) * 1000:0} мм");
        }
        else if (Current == Step.Top && ok)
        {
            _outline.enabled = true;
            _outline.positionCount = 5;
            Vector3 up = Vector3.up * height;
            _outline.SetPosition(0, _left); _outline.SetPosition(1, _left + up);
            _outline.SetPosition(2, _right + up); _outline.SetPosition(3, _right); _outline.SetPosition(4, _left);
            Label(hit, $"{Flat(_right - _left) * 1000:0} × {height * 1000:0} мм");
        }
        else if (ok) Label(hit, "");

        if (!press || !ok) return;
        switch (Current)
        {
            case Step.Left:
                _left = hit; SetStep(Step.Right); break;
            case Step.Right:
                if (Flat(hit - _left) < minSize) { Hint = "слишком узко — отметьте правый угол дальше"; Changed?.Invoke(); break; }
                _right = hit; SetStep(Step.Top); break;
            case Step.Top:
                if (height < minSize) { Hint = "слишком низко — наведите на верх проёма"; Changed?.Invoke(); break; }
                Finish(height);
                break;
        }
    }

    void Finish(float height)
    {
        // Нормаль «в комнату» — в сторону того, кто размечает (туда уйдут горизонтальные направляющие)
        Vector3 along = _right - _left; along.y = 0;
        Vector3 n = Vector3.Cross(along.normalized, Vector3.up);
        var head = _rig != null ? _rig.centerEyeAnchor.position : Camera.main.transform.position;
        if (Vector3.Dot(head - _left, n) < 0) n = -n;
        float floor = FloorY();
        var l = new Vector3(_left.x, floor, _left.z);
        var r = new Vector3(_right.x, floor, _right.z);
        // углы — слева направо, если смотреть из комнаты
        if (Vector3.Dot(Vector3.Cross(Vector3.up, n), r - l) < 0) (l, r) = (r, l);
        var o = openings != null ? openings.AddManual(l, r, height, n) : null;
        SetStep(Step.Idle);
        Hint = o != null ? $"проём {o.WidthM * 1000:0} × {o.HeightM * 1000:0} мм размечен — «Вписать ворота»" : "";
        Changed?.Invoke();
    }

    static float Flat(Vector3 v) { v.y = 0; return v.magnitude; }

    void Label(Vector3 at, string text)
    {
        _label.text = text;
        _label.gameObject.SetActive(!string.IsNullOrEmpty(text));
        _label.transform.position = at + Vector3.up * 0.12f;
        var cam = _rig != null ? _rig.centerEyeAnchor : Camera.main.transform;
        _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - cam.position, Vector3.up);
    }
}
