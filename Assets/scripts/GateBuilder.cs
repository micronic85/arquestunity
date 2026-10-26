using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Адаптивные секционные ворота: собирает полотно, направляющие и привод из деталей gate_parts.glb
/// под размер проёма и сам считает открывание (как риг в Blender «перед запеканием8»: шарниры секций
/// едут по траектории вертикаль → дуга → горизонталь, каждая секция — жёсткое звено между двумя шарнирами).
///
/// Оси (локальные, от точки «пол, центр проёма, плоскость полотна»): Y вверх, Z — ширина, +X — вглубь гаража.
/// </summary>
public class GateBuilder : MonoBehaviour
{
    [Header("Детали")]
    [Tooltip("Assets/Models/gate_parts.glb")]
    public GameObject partsPrefab;
    [Tooltip("Смещение новой модели относительно корня (совмещение с прежней lastvorota)")]
    public Vector3 modelOffset = new Vector3(0f, -0.4233f, 0f);
    [Tooltip("Ручка с GrabInteractable из прежней модели — переносится на нижнюю секцию")]
    public Transform handleObject;
    [Tooltip("Скрыть прежнюю запечённую модель (дочерние рендереры этого объекта)")]
    public bool hideLegacyModel = true;

    [Header("Проём, м")]
    public float openingWidth = 4.155f;
    public float openingHeight = 2.743f;
    [Tooltip("Полотно шире проёма на, м (по 50 мм на сторону)")]
    public float widthOverlap = 0.10f;
    [Tooltip("Полотно выше проёма на, м (нахлёст на перемычку)")]
    public float heightOverlap = 0.05f;

    [Header("Комплектация")]
    public bool windowsEnabled = true;
    public bool wicketEnabled = true;
    [Tooltip("Калитка возможна только в проёме не ниже этого, м")]
    public float wicketMinOpeningHeight = 2.0f;
    [Tooltip("Калитка возможна только в проёме не уже этого, м")]
    public float wicketMinOpeningWidth = 2.0f;

    /// <summary>Проём позволяет калитку.</summary>
    public bool WicketAllowed => openingHeight >= wicketMinOpeningHeight - 1e-4f && openingWidth >= wicketMinOpeningWidth - 1e-4f;
    /// <summary>Калитка сейчас есть в воротах.</summary>
    public bool HasWicket => wicketEnabled && WicketAllowed;

    /// <summary>Включить/выключить окна и калитку и пересобрать ворота (размер, цвет и положение сохраняются).</summary>
    public void SetOptions(bool windows, bool wicket)
    {
        windowsEnabled = windows;
        wicketEnabled = wicket;
        Rebuild();
    }

    [Header("Секции, м")]
    public float minSection = 0.45f;
    public float maxSection = 0.60f;
    public float sectionStep = 0.025f;

    [Header("Направляющие, м")]
    public float liftRadius = 0.4f;
    [Tooltip("Горизонтальная направляющая = высота полотна + это значение")]
    public float horizontalExtra = 0.65f;
    [Tooltip("Ось направляющей от края полотна, м (в исходной модели ~0,07)")]
    public float trackSideGap = 0.07f;
    public float trackOffset = 0.06f;
    public Vector2 trackProfile = new Vector2(0.07f, 0.04f);

    [Header("Открывание кнопкой")]
    [Tooltip("Время полного открытия/закрытия, с")]
    public float fullOpenTime = 6f;

    [Header("Debug")]
    public bool debugLogs = false;
    [Range(-1f, 1f)]
    [Tooltip("Проверка в редакторе: 0..1 — положение открывания (-1 — не трогать)")]
    public float debugProgress = -1f;
    [Tooltip("Проверка в редакторе: снять картинки ворот (спереди, сбоку, 3/4) в Logs/gate_preview_*.png")]
    public bool debugSnapshot = false;

    // Размеры исходной модели (gate_parts.glb)
    const float W0 = 4.255f;     // ширина полотна
    const float H0 = 0.5586f;    // шаг секции
    const float HT0 = 5 * H0;    // высота полотна
    const float D0 = 3.9f;       // глубина горизонтальных направляющих

    public event Action Built;

    public float CurtainWidth { get; private set; }
    public float CurtainHeight { get; private set; }
    public IReadOnlyList<float> SectionHeights => _heights;
    public float Progress { get; private set; }

    readonly List<float> _heights = new List<float>();
    readonly List<Transform> _sections = new List<Transform>();
    Transform _templates, _root;
    readonly Transform[] _tmplSections = new Transform[6];
    Transform _tmplHardware;
    float _lv, _lh, _tMax;

    void Awake()
    {
        if (partsPrefab == null) { Debug.LogError("GateBuilder: не задан partsPrefab", this); enabled = false; return; }
        if (handleObject == null) handleObject = transform.Find("ruchka"); // ручка прежней модели с GrabInteractable
        if (hideLegacyModel) HideLegacy();

        var t = Instantiate(partsPrefab, transform).transform;
        t.name = "_gateTemplates";
        t.localPosition = modelOffset; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
        _templates = t;
        for (int i = 1; i <= 5; i++) _tmplSections[i] = t.Find("Section_" + i);
        _tmplHardware = t.Find("Hardware");
        if (debugLogs) Debug.Log($"GateBuilder: шаблоны {string.Join(",", System.Array.ConvertAll(_tmplSections, x => x ? x.name : "-"))}, hardware={(_tmplHardware ? "+" : "-")}");
        // Шаблоны не видны, но их материалы (Sphere — палитра) остаются доступны
        foreach (Transform c in t) if (c.name != "Sphere") c.gameObject.SetActive(false);
        var sphere = t.Find("Sphere");
        if (sphere) foreach (var r in sphere.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

        Rebuild();
    }

    void HideLegacy()
    {
        var anim = GetComponent<Animator>();
        if (anim) anim.enabled = false;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            if (handleObject != null && r.transform.IsChildOf(handleObject)) continue;
            r.enabled = false;
        }
    }

    bool _dirty;
    float _lastDebugProgress = -1f;

    // В Play Mode правка размеров в инспекторе сразу пересобирает ворота
    void OnValidate() { if (Application.isPlaying && _root != null) _dirty = true; }

    void Update()
    {
        if (_dirty) { _dirty = false; Rebuild(); _lastDebugProgress = -2f; }
        if (debugProgress >= 0f && !Mathf.Approximately(debugProgress, _lastDebugProgress))
        {
            _lastDebugProgress = debugProgress;
            SetProgress(debugProgress);
        }
        if (debugSnapshot) { debugSnapshot = false; Snapshot(); }
    }

    void Snapshot()
    {
        if (_root == null) return;
        var rt = new RenderTexture(900, 700, 24);
        var cam = new GameObject("_snapCam").AddComponent<Camera>();
        cam.targetTexture = rt;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.35f, 0.36f, 0.4f);
        cam.fieldOfView = 40f;
        Vector3 c = _root.TransformPoint(new Vector3(1.2f, CurtainHeight * 0.6f, 0));
        float dist = Mathf.Max(CurtainWidth, CurtainHeight + 2f) * 1.9f;
        var views = new (string, Vector3)[] { ("front", new Vector3(-1, 0.15f, 0)), ("side", new Vector3(0, 0.1f, 1)), ("iso", new Vector3(-0.8f, 0.6f, 0.9f)), ("corner", Vector3.zero), ("hanger", Vector3.zero), ("under", Vector3.zero), ("botL", Vector3.zero), ("botR", Vector3.zero), ("user", Vector3.zero), ("seals", Vector3.zero), ("floor", Vector3.zero) };
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        foreach (var (name, dir) in views)
        {
            if (name == "hanger")
            {
                // вдоль левой горизонтальной направляющей на передний подвес: видно сечение профиля и полосу подвеса
                float zl = -(CurtainWidth * 0.5f + trackSideGap), yl = _lv + liftRadius - trackOffset;
                cam.transform.position = _root.TransformPoint(new Vector3(liftRadius + 0.05f, yl + 0.12f, zl + 0.25f));
                cam.transform.LookAt(_root.TransformPoint(new Vector3(liftRadius + 0.15f * _lh + 0.6f, yl + 0.15f, zl)), Vector3.up);
            }
            else if (name == "botL" || name == "botR")
            {
                // ортографически снизу вверх на передний подвес: сечение направляющей и низ полосы
                float sz = name == "botL" ? -1f : 1f;
                float zt = sz * (CurtainWidth * 0.5f + trackSideGap);
                float xh = liftRadius + 0.219f * _lh;
                cam.orthographic = true; cam.orthographicSize = 0.15f;
                cam.transform.position = _root.TransformPoint(new Vector3(xh, 0.5f, zt));
                cam.transform.LookAt(_root.TransformPoint(new Vector3(xh, 5f, zt)), _root.TransformDirection(Vector3.right));
            }
            else if (name == "floor")
            {
                // из гаража на пол у ворот: тень от полотна и направляющих
                cam.orthographic = false; cam.fieldOfView = 60f;
                cam.transform.position = _root.TransformPoint(new Vector3(3.2f, 2.2f, -CurtainWidth * 0.6f));
                cam.transform.LookAt(_root.TransformPoint(new Vector3(0.8f, 0f, 0f)), Vector3.up);
            }
            else if (name == "seals")
            {
                // снаружи у правого края: нижний и верхний уплотнители, торец стойки
                cam.orthographic = false; cam.fieldOfView = 50f;
                cam.transform.position = _root.TransformPoint(new Vector3(-0.9f, 0.35f, CurtainWidth * 0.5f + 0.4f));
                cam.transform.LookAt(_root.TransformPoint(new Vector3(0f, 0.05f, CurtainWidth * 0.25f)), Vector3.up);
            }
            else if (name == "user")
            {
                // примерно как на скриншоте из очков: стоим в гараже левее середины, лицом к воротам, голова сильно запрокинута
                cam.orthographic = false; cam.fieldOfView = 90f;
                cam.transform.position = _root.TransformPoint(new Vector3(1.6f, 1.7f, -0.35f));
                cam.transform.rotation = _root.rotation * Quaternion.LookRotation(new Vector3(-0.35f, 1f, 0.15f), new Vector3(-1f, 0.3f, 0f));
            }
            else if (name == "under")
            {
                // как из очков: стоим в гараже у ворот и смотрим вверх на подвесы и барабаны
                cam.transform.position = _root.TransformPoint(new Vector3(1.6f, 1.6f, 0.1f));
                cam.transform.LookAt(_root.TransformPoint(new Vector3(0.4f, _lv + liftRadius + 0.2f, 0f)), Vector3.up);
                cam.fieldOfView = 80f;
            }
            else if (name == "corner")
            {
                // крупно верхний угол: вертикальная направляющая, дуга, кронштейн барабана, подвесы (вид изнутри снизу)
                Vector3 target = _root.TransformPoint(new Vector3(0.6f, CurtainHeight + 0.3f, CurtainWidth * 0.5f));
                cam.transform.position = _root.TransformPoint(new Vector3(2.2f, CurtainHeight - 0.6f, CurtainWidth * 0.5f - 1.4f));
                cam.transform.LookAt(target, Vector3.up);
            }
            else
            {
                Vector3 d = _root.TransformDirection(dir.normalized);
                cam.transform.position = c + d * dist;
                cam.transform.LookAt(c, Vector3.up);
            }
            cam.Render();
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(Application.dataPath, "../Logs/gate_preview_" + name + ".png"), tex.EncodeToPNG());
        }
        RenderTexture.active = null;
        Destroy(cam.gameObject); Destroy(rt); Destroy(tex);
        Debug.Log("GateBuilder: снимки сохранены в Logs/gate_preview_*.png");
    }

    // ---------- API ----------

    /// <summary>
    /// Поставить ворота в проём: bottomCenter — середина нижней кромки проёма (на полу),
    /// inward — нормаль стены в сторону помещения (туда уходят горизонтальные направляющие).
    /// </summary>
    public void PlaceInOpening(Vector3 bottomCenter, Vector3 inward)
    {
        inward.y = 0;
        if (inward.sqrMagnitude < 1e-4f) inward = transform.right;
        inward.Normalize();
        transform.localScale = Vector3.one; // примерка 1:1
        transform.rotation = Quaternion.LookRotation(Vector3.Cross(inward, Vector3.up), Vector3.up);
        transform.position = bottomCenter - transform.TransformVector(modelOffset);
    }

    /// <summary>Собрать ворота под проём (м).</summary>
    public void Build(float width, float height)
    {
        openingWidth = width;
        openingHeight = height;
        Rebuild();
    }

    public bool IsAnimating => _anim != null;
    /// <summary>Ворота открыты или открываются (для подписи кнопки).</summary>
    public bool OpenOrOpening => _anim != null ? _animTarget > 0.5f : Progress > 0.5f;
    Coroutine _anim;

    /// <summary>Плавно открыть (если закрыты/закрываются) или закрыть.</summary>
    public void ToggleOpen()
    {
        bool opening = _anim != null ? _animTarget < 0.5f : Progress < 0.5f;
        AnimateTo(opening ? 1f : 0f);
    }

    float _animTarget;
    public void AnimateTo(float target)
    {
        StopAnimation();
        _animTarget = Mathf.Clamp01(target);
        _anim = StartCoroutine(AnimateRoutine());
    }

    public void StopAnimation()
    {
        if (_anim != null) StopCoroutine(_anim);
        _anim = null;
    }

    System.Collections.IEnumerator AnimateRoutine()
    {
        while (!Mathf.Approximately(Progress, _animTarget))
        {
            float step = Time.deltaTime / Mathf.Max(0.1f, fullOpenTime);
            SetProgress(Mathf.MoveTowards(Progress, _animTarget, step));
            yield return null;
        }
        _anim = null;
    }

    /// <summary>0 — закрыты, 1 — открыты.</summary>
    public void SetProgress(float p)
    {
        Progress = Mathf.Clamp01(p);
        ApplyPose();
    }

    // ---------- сборка ----------

    public void Rebuild()
    {
        if (_templates == null) return;
        if (handleObject != null) handleObject.SetParent(transform, true);
        if (_root != null) { _root.gameObject.SetActive(false); Destroy(_root.gameObject); }
        _sections.Clear();

        CurtainWidth = Mathf.Max(1.5f, openingWidth + widthOverlap);
        CurtainHeight = Mathf.Max(1.5f, openingHeight + heightOverlap);
        ComputeSectionHeights(CurtainHeight);

        _lv = CurtainHeight;
        _lh = CurtainHeight + horizontalExtra;
        _tMax = _lv + Mathf.PI * liftRadius * 0.5f;

        _root = new GameObject("AdaptiveGate").transform;
        _root.SetParent(transform, false);
        _root.localPosition = modelOffset;

        float kW = CurtainWidth / W0;
        int n = _heights.Count;
        for (int i = 0; i < n; i++)
        {
            int ti = TemplateFor(i, n);
            var tmpl = _templates.Find("Section_" + ti); // ищем каждый раз: ссылки на шаблоны могут обнулиться при перезагрузке скриптов
            if (tmpl == null) { Debug.LogError($"GateBuilder: нет шаблона Section_{ti} (templates={_templates?.name}, children={_templates?.childCount})", this); continue; }
            var s = Instantiate(tmpl.gameObject, _root).transform;
            s.name = "Section_" + (i + 1);
            s.gameObject.SetActive(true);
            ScaleSection(s, kW, _heights[i] / H0);
            _sections.Add(s);
        }

        BuildHardware(kW);
        BuildTrack(+1);
        BuildTrack(-1);
        BuildSeals();
        AttachHandle();
        GateMaterials.ConvertRenderers(_root); // перекрытие реальными предметами + без «пластикового» блеска (MrLighting)
        ApplyPose();

        if (debugLogs)
            Debug.Log($"GateBuilder: проём {openingWidth:F3}×{openingHeight:F3} → полотно {CurtainWidth:F3}×{CurtainHeight:F3}, секции [{string.Join(", ", _heights.ConvertAll(h => (h * 1000).ToString("0")))}] мм");
        Built?.Invoke();
    }

    void ComputeSectionHeights(float hc)
    {
        _heights.Clear();
        int n = Mathf.Max(1, Mathf.CeilToInt(hc / maxSection - 1e-4f));
        while (n > 1 && hc / n < minSection) n--;
        float steps = Mathf.Floor(hc / n / sectionStep + 1e-4f);
        float baseH = Mathf.Clamp(steps * sectionStep, minSection, maxSection);
        for (int i = 0; i < n; i++) _heights.Add(baseH);
        float rest = hc - baseH * n;
        // добираем шагом снизу вверх, остаток (<шага) — в верхнюю секцию
        for (int i = 0; i < n && rest >= sectionStep - 1e-4f; i++)
        {
            if (_heights[i] + sectionStep > maxSection + 1e-4f) continue;
            _heights[i] += sectionStep; rest -= sectionStep;
        }
        _heights[n - 1] += rest;
    }

    // Нижняя (1), с окнами (4) — предпоследняя, верхняя (5); средние — 2, а та, что на высоте ручки калитки, — 3
    int TemplateFor(int i, int n)
    {
        if (i == 0) return 1;
        if (i == n - 1) return 5;
        if (i == n - 2 && n >= 3) return 4;
        int handleIdx = 1; float best = float.MaxValue;
        for (int j = 1; j < n - 2; j++)
        {
            float b = 0; for (int k = 0; k < j; k++) b += _heights[k];
            float d = Mathf.Abs(b - 1.1f); // ручка калитки — в нижней части секции, ~1,2–1,5 м от пола
            if (d < best) { best = d; handleIdx = j; }
        }
        return i == handleIdx ? 3 : 2;
    }

    // Детали секции: широкие тянутся по ширине, крайние сдвигаются к краю, внутренние — пропорционально;
    // высокие (обшивка, заглушки, калитка) тянутся по высоте, остальные (окна, петли) сохраняют размер.
    void ScaleSection(Transform section, float kW, float kH)
    {
        float dW = CurtainWidth - W0;
        var parts = new List<Transform>();
        var bounds = new Dictionary<Transform, Bounds>();
        foreach (Transform c in section)
        {
            parts.Add(c);
            if (LocalBounds(section, c, out var bb)) bounds[c] = bb;
        }

        var windowShift = new Dictionary<Transform, float>();
        var hidden = new HashSet<Transform>();

        // Калитка: детали «калитка*» уже половины полотна (широкая «калитка.004» — нижний профиль полотна, он есть всегда)
        // и ручка калитки. Без калитки они скрываются — под ними сплошная панель.
        bool wicketOn = HasWicket;
        Bounds? wicket = null;
        foreach (var kv in bounds)
        {
            bool wicketPart = (kv.Key.name.StartsWith("калитка") && kv.Value.size.z < 0.5f * W0) || kv.Key.name == "WicketHandle";
            if (!wicketPart) continue;
            if (!wicketOn) { hidden.Add(kv.Key); continue; }
            if (kv.Key.name == "WicketHandle") continue;
            if (wicket == null) wicket = kv.Value; else { var w = wicket.Value; w.Encapsulate(kv.Value); wicket = w; }
        }
        float wkC = wicket?.center.z ?? 0f, wkHalf = wicket?.extents.z ?? 0f;
        float wkShift = wkC * (kW - 1f);

        // Окна: детали шириной 0,5–0,8 м и высотой > 0,2 м (рамка, стекло, резинка), сгруппированные по центру.
        var windowParts = new List<Transform>();
        foreach (var kv in bounds)
        {
            var b0 = kv.Value;
            if (b0.size.z < 0.5f || b0.size.z > 0.8f || b0.size.y < 0.2f || kv.Key.name.StartsWith("калитка")) continue;
            windowParts.Add(kv.Key);
        }
        if (!windowsEnabled)
            foreach (var wp in windowParts) hidden.Add(wp);
        else if (wicket != null)
        {
            // С калиткой: окно в калитке едет с ней; боковые — посередине между калиткой и краем, если мало места — скрываются
            foreach (var wp in windowParts)
            {
                var b0 = bounds[wp];
                float zc = b0.center.z, winW = b0.size.z;
                if (Mathf.Abs(zc - wkC) < wkHalf) { windowShift[wp] = wkShift; continue; }
                float side = Mathf.Sign(zc - wkC);
                float inner = wkC + wkShift + side * wkHalf;
                float outer = side * (CurtainWidth * 0.5f - 0.08f);
                if (Mathf.Abs(outer - inner) < winW + 0.15f) { hidden.Add(wp); continue; }
                windowShift[wp] = (inner + outer) * 0.5f - zc;
            }
        }
        else if (windowParts.Count > 0)
        {
            // Без калитки: окна равномерно по ширине, сколько помещается (ближние к центру — в приоритете)
            var groups = new List<(float z, float w, List<Transform> parts)>();
            foreach (var wp in windowParts)
            {
                var b0 = bounds[wp];
                int gi = groups.FindIndex(g => Mathf.Abs(g.z - b0.center.z) < 0.08f);
                if (gi < 0) groups.Add((b0.center.z, b0.size.z, new List<Transform> { wp }));
                else { var g = groups[gi]; g.parts.Add(wp); groups[gi] = (g.z, Mathf.Max(g.w, b0.size.z), g.parts); }
            }
            float winW = 0f; foreach (var g in groups) winW = Mathf.Max(winW, g.w);
            int n = Mathf.Min(groups.Count, Mathf.FloorToInt((CurtainWidth - 0.16f) / (winW + 0.15f)));
            groups.Sort((a, c) => Mathf.Abs(a.z).CompareTo(Mathf.Abs(c.z)));
            var used = groups.GetRange(0, Mathf.Max(0, n));
            used.Sort((a, c) => a.z.CompareTo(c.z));
            for (int gi = 0; gi < groups.Count; gi++)
                if (gi >= n) foreach (var wp in groups[gi].parts) hidden.Add(wp);
            for (int k = 0; k < used.Count; k++)
            {
                float target = -CurtainWidth * 0.5f + (k + 0.5f) * CurtainWidth / n;
                foreach (var wp in used[k].parts) windowShift[wp] = target - used[k].z;
            }
        }

        foreach (var p in parts)
        {
            if (!bounds.TryGetValue(p, out var b)) continue;
            if (hidden.Contains(p)) { p.gameObject.SetActive(false); continue; }
            bool stretchZ = b.size.z > 0.5f * W0;
            bool stretchY = b.size.y > 0.7f * H0;
            float dz = 0, dy = 0;
            if (windowShift.TryGetValue(p, out var ws)) dz = ws;
            else if (!stretchZ)
                dz = Mathf.Abs(b.center.z) > 0.35f * W0 ? Mathf.Sign(b.center.z) * dW * 0.5f : b.center.z * (kW - 1f);
            if (!stretchY) dy = b.center.y * (kH - 1f);
            Wrap(section, p, new Vector3(1f, stretchY ? kH : 1f, stretchZ ? kW : 1f), new Vector3(0, dy, dz));
        }
    }

    void BuildHardware(float kW)
    {
        if (_tmplHardware == null) _tmplHardware = _templates.Find("Hardware");
        if (_tmplHardware == null) return;
        var hw = Instantiate(_tmplHardware.gameObject, _root).transform;
        hw.name = "Hardware";
        hw.gameObject.SetActive(true);

        float dW = CurtainWidth - W0;
        float dH = CurtainHeight - HT0, kH = CurtainHeight / HT0;
        float depth = liftRadius + _lh;
        float dD = depth - D0, kD = depth / D0;

        var parts = new List<Transform>();
        foreach (Transform c in hw) parts.Add(c);

        // Узел пружины на каждой стороне (пружина + крепление на валу + ограничитель) едет к краю целиком;
        // если до центрального кронштейна мало места — пружина укорачивается от внешнего конца к центру.
        var springZ = new Dictionary<Transform, (float scale, float dz)>();
        foreach (float side in new[] { -1f, 1f })
        {
            Transform spring = null, mount = null; Bounds sb = default, mb = default;
            foreach (var p in parts)
            {
                if (!LocalBounds(hw, p, out var b) || Mathf.Sign(b.center.z) != side) continue;
                if (p.name.StartsWith("Spring_")) { spring = p; sb = b; }
                else if (p.name.StartsWith("ShaftMount_")) { mount = p; mb = b; }
                else if (p.name.StartsWith("SpringStop_")) springZ[p] = (1f, side * dW * 0.5f);
            }
            if (spring == null) continue;
            float e = side * dW * 0.5f;
            float zOuter = side * Mathf.Max(Mathf.Abs(sb.min.z), Mathf.Abs(sb.max.z));
            float zInner = side * Mathf.Min(Mathf.Abs(sb.min.z), Mathf.Abs(sb.max.z));
            float len = Mathf.Abs(zOuter - zInner);
            float newOuter = zOuter + e;
            float maxLen = Mathf.Abs(newOuter) - 0.18f - (mount ? mb.size.z : 0.07f);
            float k = Mathf.Clamp(maxLen / len, 0.35f, 1f);
            springZ[spring] = (k, zOuter * (1f - k) + e);
            if (mount)
            {
                float newInner = newOuter - side * len * k;
                springZ[mount] = (1f, newInner + (mb.center.z - zInner) - mb.center.z);
            }
        }

        foreach (var p in parts)
        {
            // тяга — пока без анимации; горизонтальные профили заменяет процедурная направляющая
            if (p.name == "DriveArm" || p.name.StartsWith("HorizProfile")) { p.gameObject.SetActive(false); continue; }
            if (!LocalBounds(hw, p, out var b)) continue;
            if (debugLogs) Debug.Log($"[GateBuilder] part {p.name} min={b.min.ToString("F3")} max={b.max.ToString("F3")}");
            bool sx = b.size.x > 0.5f * D0, sy = b.size.y > 0.5f * HT0, sz = b.size.z > 0.5f * W0;
            // Каждая координата переносится по правилу оси (у дальнего края — сдвиг, ближе — пропорционально).
            // Длинные детали тянутся так, чтобы оба конца попали туда же, куда и соседние детали:
            // стойка у проёма заканчивается под кронштейном барабана, а не на kH от исходной высоты.
            float MapX(float x) => x > 0.6f * D0 ? x + dD : x * kD;
            float MapY(float y) => y > 0.8f * HT0 ? y + dH : y * kH;
            float MapZ(float z) => Mathf.Abs(z) > 0.35f * W0 ? z + Mathf.Sign(z) * dW * 0.5f : z * kW;
            float scx = 1f, scy = 1f, scz = 1f;
            float dx = MapX(b.center.x) - b.center.x, dy = MapY(b.center.y) - b.center.y, dz = 0;
            if (sx) Stretch(b.min.x, b.max.x, MapX, out scx, out dx);
            if (sy) Stretch(b.min.y, b.max.y, MapY, out scy, out dy);
            if (p.name.StartsWith("TrackHanger"))
            {
                // Подвес к потолку: низ — на горизонтальной направляющей, по ширине — над ней,
                // вдоль — в той же доле длины горизонтали, что и в исходной модели
                const float R0 = 0.4f, LH0 = D0 - R0;
                float f = Mathf.Clamp01((b.center.x - R0) / LH0);
                // Подвес Г-образный: крюк наверху уходит внутрь, а полоса с низом — у внешнего края габарита
                // (в модели низ полосы в 40 мм от него). Полоса прикручена к стенке С-профиля снаружи
                // и доходит до его низа — снизу видно, что она держит направляющую.
                float side = Mathf.Sign(b.center.z);
                float footZ = side * (Mathf.Max(Mathf.Abs(b.min.z), Mathf.Abs(b.max.z)) - 0.04f);
                float trackBottom = _lv + liftRadius - trackOffset - trackProfile.x * 0.5f;
                float stripZ = side * (CurtainWidth * 0.5f + trackSideGap + trackProfile.y * 0.5f + 0.014f); // к стенке С-профиля снаружи (открытая сторона профиля — к полотну, там ходят ролики); полоса в модели наклонена и по габариту низа смещена внутрь на ~14 мм
                Wrap(hw, p, Vector3.one, new Vector3(liftRadius + f * _lh - b.center.x, trackBottom - b.min.y, stripZ - footZ));
                continue;
            }
            if (springZ.TryGetValue(p, out var spz)) { scz = spz.scale; dz = spz.dz; }
            else if (sz) Stretch(b.min.z, b.max.z, MapZ, out scz, out dz);
            else dz = Mathf.Abs(b.center.z) > 0.35f * W0 ? Mathf.Sign(b.center.z) * dW * 0.5f : b.center.z * (kW - 1f);
            Wrap(hw, p, new Vector3(scx, scy, scz), new Vector3(dx, dy, dz));
        }
    }

    // Масштаб и сдвиг (вокруг начала координат обёртки), переводящие отрезок [a, b] в [map(a), map(b)]
    static void Stretch(float a, float b, System.Func<float, float> map, out float scale, out float offset)
    {
        float na = map(a), nb = map(b);
        scale = Mathf.Max(0.05f, (nb - na) / Mathf.Max(1e-4f, b - a));
        offset = na - a * scale;
    }

    static void Wrap(Transform parent, Transform part, Vector3 scale, Vector3 offset)
    {
        var w = new GameObject(part.name + "_w").transform;
        w.SetParent(parent, false);
        part.SetParent(w, false);
        w.localScale = scale;
        w.localPosition = offset;
    }

    // Границы меша детали в системе координат space
    static bool LocalBounds(Transform space, Transform part, out Bounds b)
    {
        b = default;
        bool any = false;
        foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var m = space.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = m.MultiplyPoint3x4(c);
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
        }
        return any;
    }

    void AttachHandle()
    {
        if (handleObject == null || _sections.Count == 0) return;
        var target = FindDeep(_sections[0], "Handle");
        if (target == null) return;
        var tb = RendererBounds(target);
        var hb = RendererBounds(handleObject);
        if (!tb.HasValue || !hb.HasValue) return;
        foreach (var r in target.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        handleObject.position += tb.Value.center - hb.Value.center;
        handleObject.SetParent(_sections[0], true);
        if (debugLogs) Debug.Log($"GateBuilder: ручка {hb.Value.size} → место {tb.Value.size} at {tb.Value.center}");
    }

    static Bounds? RendererBounds(Transform t)
    {
        Bounds? b = null;
        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
        {
            if (b == null) b = r.bounds; else { var x = b.Value; x.Encapsulate(r.bounds); b = x; }
        }
        return b;
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t) { var r = FindDeep(c, name); if (r) return r; }
        return null;
    }

    // ---------- траектория ----------

    // Точка траектории шарниров на расстоянии s от нижнего (закрытого) положения: (вглубь, высота)
    Vector2 PathPoint(float s)
    {
        float r = liftRadius, arc = Mathf.PI * r * 0.5f;
        if (s <= _lv) return new Vector2(0f, s);
        if (s <= _lv + arc)
        {
            float a = (s - _lv) / r;
            return new Vector2(r - r * Mathf.Cos(a), _lv + r * Mathf.Sin(a));
        }
        return new Vector2(r + (s - _lv - arc), _lv + r);
    }

    Vector2 PathTangent(float s)
    {
        float r = liftRadius, arc = Mathf.PI * r * 0.5f;
        if (s <= _lv) return Vector2.up;
        if (s <= _lv + arc) { float a = (s - _lv) / r; return new Vector2(Mathf.Sin(a), Mathf.Cos(a)); }
        return Vector2.right;
    }

    // Следующий шарнир: точка дальше по траектории на расстоянии (по прямой) h
    float SolveChord(float s, float h)
    {
        Vector2 b = PathPoint(s);
        float lo = s + h * 0.9f, hi = s + h * 1.6f;
        for (int i = 0; i < 30; i++)
        {
            float mid = (lo + hi) * 0.5f;
            if ((PathPoint(mid) - b).magnitude < h) lo = mid; else hi = mid;
        }
        return (lo + hi) * 0.5f;
    }

    void ApplyPose()
    {
        if (_sections.Count == 0) return;
        float s = Progress * _tMax;
        for (int i = 0; i < _sections.Count; i++)
        {
            float s2 = SolveChord(s, _heights[i]);
            Vector2 b = PathPoint(s), t = PathPoint(s2);
            var sec = _sections[i];
            sec.localPosition = new Vector3(b.x, b.y, 0f);
            sec.localRotation = Quaternion.FromToRotation(Vector3.up, new Vector3(t.x - b.x, t.y - b.y, 0f));
            s = s2;
        }
    }

    // ---------- направляющие ----------

    void BuildTrack(int side)
    {
        var go = new GameObject(side > 0 ? "Track_L" : "Track_R");
        go.transform.SetParent(_root, false);
        go.transform.localPosition = new Vector3(0, 0, side * (CurtainWidth * 0.5f + trackSideGap));
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var profileMat = FindMaterial("HorizProfile_L");
        if (profileMat) mr.sharedMaterial = profileMat;

        // Сечение — гнутый С-профиль (жесть ~2,5 мм) с полостью для роликов, открыт к полотну.
        // u — вдоль нормали траектории (поперёк профиля), v — по ширине ворот наружу от полотна.
        float H = trackProfile.x * 0.5f, D = trackProfile.y * 0.5f, t = 0.0025f, L = 0.009f;
        var outline = new[] {
            new Vector2(-H + L, -D), new Vector2(-H, -D), new Vector2(-H, D), new Vector2(H, D),
            new Vector2(H, -D), new Vector2(H - L, -D), new Vector2(H - L, -D + t), new Vector2(H - t, -D + t),
            new Vector2(H - t, D - t), new Vector2(-H + t, D - t), new Vector2(-H + t, -D + t), new Vector2(-H + L, -D + t) };
        int m = outline.Length;
        // внешняя нормаль каждой грани контура (2D): берём перпендикуляр, смотрящий из многоугольника
        var segN = new Vector2[m];
        for (int k = 0; k < m; k++)
        {
            Vector2 a = outline[k], b = outline[(k + 1) % m], d = (b - a).normalized;
            var n = new Vector2(d.y, -d.x);
            if (PointInPolygon(outline, (a + b) * 0.5f + n * 0.0005f)) n = -n;
            segN[k] = n;
        }

        // Кольца: концы прямых участков и частые точки на дуге
        float arc = Mathf.PI * liftRadius * 0.5f, end = _lv + arc + _lh;
        var ss = new List<float> { 0f };
        for (int k = 0; k <= 24; k++) ss.Add(_lv + arc * k / 24f);
        ss.Add(end);

        Vector3 vdir = Vector3.forward * side;
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>();
        for (int r = 0; r < ss.Count; r++)
        {
            Vector2 p = PathPoint(ss[r]), tg = PathTangent(ss[r]);
            Vector2 nrm = new Vector2(tg.y, -tg.x); // внутрь гаража / вниз
            Vector2 c = p + nrm * trackOffset;
            Vector3 n3 = new Vector3(nrm.x, nrm.y, 0), c3 = new Vector3(c.x, c.y, 0);
            for (int k = 0; k < m; k++)
            {
                Vector2 a = outline[k], b = outline[(k + 1) % m];
                Vector3 nn = (n3 * segN[k].x + vdir * segN[k].y).normalized;
                verts.Add(c3 + n3 * a.x + vdir * a.y); norms.Add(nn);
                verts.Add(c3 + n3 * b.x + vdir * b.y); norms.Add(nn);
            }
            if (r == 0) continue;
            int pa = (r - 1) * m * 2, pb = r * m * 2;
            for (int k = 0; k < m; k++)
            {
                int a0 = pa + k * 2, a1 = a0 + 1, b0 = pb + k * 2, b1 = b0 + 1;
                // порядок вершин — чтобы лицевая сторона смотрела по внешней нормали
                Vector3 cr = Vector3.Cross(verts[b0] - verts[a0], verts[b1] - verts[a0]);
                if (Vector3.Dot(cr, norms[a0]) > 0) tris.AddRange(new[] { a0, b0, b1, a0, b1, a1 });
                else tris.AddRange(new[] { a0, b1, b0, a0, a1, b1 });
            }
        }
        var mesh = new Mesh { name = go.name };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;
    }

    static bool PointInPolygon(Vector2[] poly, Vector2 q)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            if ((poly[i].y > q.y) != (poly[j].y > q.y) &&
                q.x < (poly[j].x - poly[i].x) * (q.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                inside = !inside;
        return inside;
    }

    // ---------- уплотнители ----------

    // Резина: матовый чёрный на основе материала оконного уплотнителя из модели (или профиля)
    Material RubberMaterial()
    {
        if (_rubber) return _rubber;
        Material baseMat = null;
        foreach (var r in _templates.GetComponentsInChildren<Renderer>(true))
            foreach (var mat in r.sharedMaterials)
                if (baseMat == null && mat && mat.name.StartsWith("резинка.003")) baseMat = mat;
        if (baseMat == null) baseMat = FindMaterial("HorizProfile_L");
        // своя матовая копия: у оконной резинки блик, снизу полоса выглядела синеватой
        _rubber = baseMat ? new Material(baseMat) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        _rubber.name = "GateRubber";
        if (_rubber.HasProperty("baseColorFactor")) _rubber.SetColor("baseColorFactor", new Color(0.04f, 0.04f, 0.045f));
        if (_rubber.HasProperty("_BaseColor")) _rubber.SetColor("_BaseColor", new Color(0.04f, 0.04f, 0.045f));
        if (_rubber.HasProperty("metallicFactor")) _rubber.SetFloat("metallicFactor", 0f);
        if (_rubber.HasProperty("roughnessFactor")) _rubber.SetFloat("roughnessFactor", 0.85f);
        return _rubber;
    }
    Material _rubber;

    // Нижний уплотнитель уже есть в модели (Section_1/«резинка»): делаем его чёрным.
    // Верхний — резиновый профиль на верхней кромке верхней секции (как у DoorHan): едет вместе с ней.
    void BuildSeals()
    {
        var rubber = RubberMaterial();
        foreach (var sec in _sections)
            foreach (var r in sec.GetComponentsInChildren<Renderer>(true))
                for (var t = r.transform; t != null && t != sec; t = t.parent)
                    if (t.name == "резинка")
                    {
                        var mats = r.sharedMaterials; for (int k = 0; k < mats.Length; k++) mats[k] = rubber; r.sharedMaterials = mats;
                        // в модели резинка почти вся спрятана в панели: делаем толще наружу (от плоскости x = 0 секции) и чуть ниже
                        var w = t.parent;
                        if (w != null && w.name == "резинка_w" && w.localScale.x < 1.01f) w.localScale = Vector3.Scale(w.localScale, new Vector3(1.5f, 1.4f, 1f));
                        break;
                    }

        if (_sections.Count == 0) return;
        var topSec = _sections[_sections.Count - 1];
        var top = new GameObject("TopSeal").transform;
        top.SetParent(topSec, false);
        float h = _heights[_sections.Count - 1], cw = CurtainWidth;
        // в координатах секции: низ — шарнир (y = 0), x < 0 — улица, панель от x ≈ -0.064 до -0.02
        AddBox(top, "Cap", new Vector3(-0.044f, h + 0.008f, 0f), new Vector3(0.05f, 0.018f, cw), rubber);
        AddBox(top, "Lip", new Vector3(-0.069f, h - 0.004f, 0f), new Vector3(0.009f, 0.04f, cw), rubber);
    }

    static void AddBox(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    Material FindMaterial(string partName)
    {
        var t = _tmplHardware ? FindDeep(_tmplHardware, partName) : null;
        var r = t ? t.GetComponentInChildren<Renderer>(true) : null;
        return r ? r.sharedMaterial : null;
    }
}
