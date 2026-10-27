using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using TMPro;
using UnityEngine;

/// <summary>
/// Подсветка проёмов из разметки пространства Quest (MR Utility Kit): двери/окна обводятся рамкой
/// с размером, проём, на который смотрит пользователь, выделяется ярче — его берёт кнопка
/// «Вписать ворота в проём» (GateColorPicker → GateBuilder.PlaceInOpening).
/// Проёмы появляются после «Настройки пространства» в шлеме (двери/окна нужно отметить, если шлем не нашёл их сам).
/// </summary>
public class OpeningDetector : MonoBehaviour
{
    [Header("Что подсвечивать")]
    public MRUKAnchor.SceneLabels labels = MRUKAnchor.SceneLabels.DOOR_FRAME | MRUKAnchor.SceneLabels.WINDOW_FRAME;
    public bool showHighlights = true;

    [Header("Фильтр ложных проёмов")]
    [Tooltip("Брать проёмы только из комнаты, где сейчас стоит пользователь (старые/другие разметки дают «висящие» рамки)")]
    public bool onlyCurrentRoom = true;
    [Tooltip("Проём должен быть вертикальным (|наклон нормали| меньше порога)")]
    public float maxNormalTilt = 0.2f;
    [Tooltip("Проём должен лежать на стене своей комнаты: до плоскости стены не дальше, м")]
    public float maxDistanceToWall = 0.25f;
    [Tooltip("Проёмы с центрами ближе этого и одинаковой нормалью считаются дублями, м")]
    public float duplicateDistance = 0.3f;

    [Header("Вид")]
    public Material lineMaterial;
    public Material selectedMaterial;
    public float lineWidth = 0.025f;
    [Tooltip("Насколько рамка отодвинута от стены в комнату, м")]
    public float wallOffset = 0.02f;

    [Header("Выбор взглядом")]
    public float maxDistance = 10f;
    [Tooltip("Минимальный косинус угла между взглядом и направлением на проём")]
    public float minLookDot = 0.6f;

    public bool debugLogs = false;
    [Tooltip("Проверка в редакторе: вписать ворота в проём с этим номером (-1 — нет)")]
    public int debugFitIndex = -1;

    public class Opening
    {
        public MRUKAnchor anchor;
        /// <summary>Система координат проёма: x — по ширине, y — вверх, z (forward) — в комнату. У разметки Quest — якорь,
        /// у ручной разметки — свой объект.</summary>
        public Transform frame;
        public bool manual;
        public Transform Frame => frame != null ? frame : anchor.transform;
        public Rect rect;
        public Transform root;
        public Renderer[] bars;
        public TextMeshPro label;
        public float WidthM => rect.width;
        public float HeightM => rect.height;
        public Vector3 Center => Frame.TransformPoint(new Vector3(rect.center.x, rect.center.y, 0));
        /// <summary>Середина нижней кромки проёма (пол), мир.</summary>
        public Vector3 BottomCenter
        {
            get
            {
                var p = Frame.TransformPoint(new Vector3(rect.center.x, rect.yMin, 0));
                if (floorY.HasValue && Mathf.Abs(p.y - floorY.Value) < 0.3f) p.y = floorY.Value; // низ проёма — на пол
                return p;
            }
        }
        public float? floorY;
        /// <summary>Нормаль стены в сторону комнаты.</summary>
        public Vector3 Inward => Frame.forward;
    }

    public IReadOnlyList<Opening> Openings => _openings;
    public Opening Selected { get; private set; }
    public string Status { get; private set; } = "загрузка разметки пространства…";
    public event System.Action Changed;

    readonly List<Opening> _openings = new List<Opening>();
    Transform _head;

    void Awake()
    {
        // ручная разметка проёмов (для высоких ворот) — живёт рядом, если в сцене её нет (в Awake — до меню)
        if (FindFirstObjectByType<OpeningMarker>() == null) gameObject.AddComponent<OpeningMarker>().openings = this;
    }

    void Start()
    {
        if (MRUK.Instance == null) { Status = "нет MRUK в сцене"; Changed?.Invoke(); return; }
        MRUK.Instance.RegisterSceneLoadedCallback(Rebuild);
    }

    public void Rebuild()
    {
        foreach (var o in _openings) if (o.root && !o.manual) Destroy(o.root.gameObject);
        _openings.Clear();
        _openings.AddRange(_manual); // ручная разметка переживает пересборку
        Selected = null;

        int rejected = 0;
        if (MRUK.Instance != null)
        {
            var current = MRUK.Instance.GetCurrentRoom();
            _room = current;
            var rooms = new List<MRUKRoom>();
            if (onlyCurrentRoom && current != null) rooms.Add(current); else rooms.AddRange(MRUK.Instance.Rooms);
            if (debugLogs) Debug.Log($"OpeningDetector: комнат в разметке {MRUK.Instance.Rooms.Count}, текущая {(current ? current.name : "не определена")}, берём {rooms.Count}");

            foreach (var room in rooms)
                foreach (var a in room.Anchors)
                {
                    if (!a.HasAnyLabel(labels) || !a.PlaneRect.HasValue) continue;
                    var o = new Opening { anchor = a, rect = a.PlaneRect.Value, floorY = FloorY(room) };
                    string why = Reject(o, room);
                    if (why != null)
                    {
                        rejected++;
                        if (debugLogs) Debug.Log($"OpeningDetector: отброшен {a.Label} {o.WidthM * 1000:0}×{o.HeightM * 1000:0} — {why}");
                        continue;
                    }
                    if (showHighlights) BuildHighlight(o);
                    _openings.Add(o);
                }
        }

        Status = _openings.Count > 0
            ? $"проёмов: {_openings.Count}" + (rejected > 0 ? $" (отброшено {rejected})" : "")
            : "проёмы не найдены — разметьте проём вручную";
        if (debugLogs)
            foreach (var o in _openings)
                Debug.Log($"OpeningDetector: {(o.manual ? "ручной" : o.anchor.Label.ToString())} {o.WidthM * 1000:0}×{o.HeightM * 1000:0} мм at {o.BottomCenter}");
        Changed?.Invoke();
    }

    MRUKRoom _room;
    float _nextRoomCheck;

    // Почему проём не подходит (null — подходит)
    string Reject(Opening o, MRUKRoom room)
    {
        Vector3 n = o.anchor.transform.forward;
        if (Mathf.Abs(n.y) > maxNormalTilt) return $"наклонён (нормаль y={n.y:F2})";

        Vector3 c = o.Center;
        bool onWall = false;
        foreach (var w in room.WallAnchors)
        {
            if (w == null || !w.PlaneRect.HasValue) continue;
            Vector3 l = w.transform.InverseTransformPoint(c);
            Rect r = w.PlaneRect.Value;
            if (Mathf.Abs(l.z) <= maxDistanceToWall && l.x > r.xMin - 0.2f && l.x < r.xMax + 0.2f && l.y > r.yMin - 0.2f && l.y < r.yMax + 0.2f)
            { onWall = true; break; }
        }
        if (!onWall && room.WallAnchors.Count > 0) return "не лежит на стене комнаты";

        foreach (var other in _openings)
            if ((other.Center - c).magnitude < duplicateDistance && Vector3.Dot(other.Inward, n) > 0.9f) return "дубль";
        return null;
    }

    static float? FloorY(MRUKRoom room)
    {
        foreach (var f in room.FloorAnchors) if (f) return f.transform.position.y;
        return null;
    }

    void BuildHighlight(Opening o)
    {
        var root = new GameObject("Opening_" + o.Frame.name).transform;
        root.SetParent(o.Frame, false);
        root.localPosition = new Vector3(0, 0, wallOffset);
        o.root = root;

        Rect r = o.rect;
        float t = lineWidth;
        var bars = new List<Renderer>
        {
            Bar(root, new Vector3(r.center.x, r.yMax, 0), new Vector3(r.width + t, t, t)),
            Bar(root, new Vector3(r.center.x, r.yMin, 0), new Vector3(r.width + t, t, t)),
            Bar(root, new Vector3(r.xMin, r.center.y, 0), new Vector3(t, r.height + t, t)),
            Bar(root, new Vector3(r.xMax, r.center.y, 0), new Vector3(t, r.height + t, t)),
        };
        o.bars = bars.ToArray();

        var go = new GameObject("Size", typeof(RectTransform));
        go.transform.SetParent(root, false);
        go.transform.localPosition = new Vector3(r.center.x, r.yMax + 0.08f, 0);
        go.transform.localRotation = Quaternion.Euler(0, 180f, 0); // текст читается из комнаты
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = $"{o.WidthM * 1000:0} × {o.HeightM * 1000:0} мм";
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableAutoSizing = true; tmp.fontSizeMin = 0.05f; tmp.fontSizeMax = 1.2f;
        tmp.rectTransform.sizeDelta = new Vector2(Mathf.Max(0.6f, r.width * 0.6f), 0.12f);
        tmp.color = new Color(0.4f, 0.9f, 1f);
        o.label = tmp;
        SetMaterial(o, lineMaterial);
    }

    static Renderer Bar(Transform parent, Vector3 pos, Vector3 size)
    {
        var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(c.GetComponent<Collider>());
        c.transform.SetParent(parent, false);
        c.transform.localPosition = pos;
        c.transform.localScale = size;
        return c.GetComponent<Renderer>();
    }

    static void SetMaterial(Opening o, Material m)
    {
        if (m == null || o.bars == null) return;
        foreach (var b in o.bars) b.sharedMaterial = m;
    }

    void Update()
    {
        if (debugFitIndex >= 0 && debugLogs) Debug.Log($"OpeningDetector: debugFit {debugFitIndex} of {_openings.Count}");
        if (debugFitIndex >= 0 && debugFitIndex < _openings.Count)
        {
            var o = _openings[debugFitIndex]; debugFitIndex = -1;
            var b = FindFirstObjectByType<GateBuilder>();
            if (b) { b.PlaceInOpening(o.BottomCenter, o.Inward); b.Build(o.WidthM, o.HeightM); }
        }
        // Перешли в другую комнату разметки — пересобрать
        if (onlyCurrentRoom && MRUK.Instance != null && MRUK.Instance.IsInitialized && Time.time >= _nextRoomCheck)
        {
            _nextRoomCheck = Time.time + 1f;
            var cur = MRUK.Instance.GetCurrentRoom();
            if (cur != null && cur != _room) Rebuild();
        }
        if (_openings.Count == 0) return;
        if (_head == null)
        {
            var rig = FindFirstObjectByType<OVRCameraRig>();
            _head = rig ? rig.centerEyeAnchor : (Camera.main ? Camera.main.transform : null);
            if (_head == null) return;
        }

        Opening best = null; float bestDot = minLookDot;
        foreach (var o in _openings)
        {
            if (o.anchor == null && o.frame == null) continue;
            Vector3 d = o.Center - _head.position;
            if (d.magnitude > maxDistance) continue;
            float dot = Vector3.Dot(_head.forward, d.normalized);
            if (dot > bestDot) { bestDot = dot; best = o; }
        }
        if (best != Selected)
        {
            if (Selected != null) { SetMaterial(Selected, lineMaterial); if (Selected.label) Selected.label.color = new Color(0.4f, 0.9f, 1f); }
            Selected = best;
            if (Selected != null) { SetMaterial(Selected, selectedMaterial); if (Selected.label) Selected.label.color = Color.white; }
            Changed?.Invoke();
        }

        // лёгкая пульсация выбранного проёма
        if (Selected?.bars != null)
        {
            float k = 1f + 0.35f * Mathf.Sin(Time.time * 4f);
            foreach (var b in Selected.bars)
            {
                var s = b.transform.localScale;
                if (s.x > s.y) s.y = lineWidth * k; else s.x = lineWidth * k;
                b.transform.localScale = s;
            }
        }
    }

    readonly List<Opening> _manual = new List<Opening>();

    /// <summary>Проём, размеченный вручную: нижние углы на полу (left, right), высота, нормаль стены в комнату.</summary>
    public Opening AddManual(Vector3 left, Vector3 right, float height, Vector3 inward)
    {
        inward.y = 0; inward.Normalize();
        var frame = new GameObject($"ManualOpening_{_manual.Count + 1}").transform;
        frame.SetParent(transform, false);
        frame.position = (left + right) * 0.5f;
        frame.rotation = Quaternion.LookRotation(inward, Vector3.up);
        float w = Vector3.Distance(new Vector3(left.x, 0, left.z), new Vector3(right.x, 0, right.z));
        var o = new Opening { frame = frame, manual = true, rect = new Rect(-w * 0.5f, 0f, w, height), floorY = frame.position.y };
        if (showHighlights) BuildHighlight(o);
        _manual.Add(o);
        _openings.Add(o);
        // сразу выбранный — чтобы «Вписать» сработало, даже если взгляд ещё не на нём
        if (Selected != null) { SetMaterial(Selected, lineMaterial); if (Selected.label) Selected.label.color = new Color(0.4f, 0.9f, 1f); }
        Selected = o;
        SetMaterial(o, selectedMaterial); if (o.label) o.label.color = Color.white;
        Status = $"проёмов: {_openings.Count}";
        if (debugLogs) Debug.Log($"OpeningDetector: ручной проём {w * 1000:0}×{height * 1000:0} мм at {o.BottomCenter}");
        Changed?.Invoke();
        return o;
    }

    /// <summary>Удалить все проёмы, размеченные вручную.</summary>
    public bool Scanning { get; private set; }

    /// <summary>
    /// Принудительное сканирование: системная «Настройка пространства» Quest, затем перезагрузка
    /// разметки MRUK и поиск проёмов заново. done — итоговый текст для строки статуса меню.
    /// </summary>
    public async void RescanSpace(System.Action<string> done = null)
    {
        if (Scanning) return;
        if (MRUK.Instance == null) { done?.Invoke("нет MRUK в сцене"); return; }
        Scanning = true;
        string result;
        try
        {
            bool captured = await OVRScene.RequestSpaceSetup();
            var res = await MRUK.Instance.LoadSceneFromDevice(requestSceneCaptureIfNoDataFound: false);
            if (res == MRUK.LoadDeviceResult.Success)
            {
                Rebuild();
                result = (captured ? "пространство обновлено, " : "сканирование отменено, ") + Status;
            }
            else result = $"разметка не загружена ({res})";
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            result = "ошибка сканирования";
        }
        Scanning = false;
        if (debugLogs) Debug.Log($"OpeningDetector: rescan → {result}");
        done?.Invoke(result);
    }

    public void ClearManual()
    {
        foreach (var o in _manual) { _openings.Remove(o); if (o.frame) Destroy(o.frame.gameObject); if (Selected == o) Selected = null; }
        _manual.Clear();
        Changed?.Invoke();
    }

    public void SetVisible(bool v)
    {
        showHighlights = v;
        foreach (var o in _openings) if (o.root) o.root.gameObject.SetActive(v);
    }
}
