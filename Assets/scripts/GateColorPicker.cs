using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Меню на левой ладони: цвет полотна ворот + блок замера проёма рулеткой DISTO (если есть DistoMeasure).
///  • Повернуть левую ладонь к лицу — над ней появляется палитра (плитки с материалами панелей).
///  • Ткнуть в плитку указательным пальцем правой руки — лицевые панели ворот получают этот материал.
/// Палитра — список palette: полотно получает текстурный материал из модели (materialName,
/// ищется среди материалов ворот, в т.ч. на «держателе» Sphere), а плитка меню показывает
/// точный цвет RAL (tileColor) — текстуры на маленькой плитке передают цвет неверно.
/// Цели — все рендереры ворот со слотом материала targetMaterialName.
/// </summary>
public class GateColorPicker : MonoBehaviour
{
    [System.Serializable]
    public class Swatch
    {
        public string label;
        [Tooltip("Материал модели, который получает полотно (имя без \" (Instance)\")")]
        public string materialName;
        [Tooltip("Цвет плитки в меню (RAL, sRGB)")]
        public Color tileColor = Color.white;
        [Tooltip("Показать на плитке саму текстуру материала (для дерева)")]
        public bool tileUsesTexture;

        public Swatch(string label, string materialName, string hex, bool tileUsesTexture = false)
        {
            this.label = label;
            this.materialName = materialName;
            ColorUtility.TryParseHtmlString(hex, out tileColor);
            this.tileUsesTexture = tileUsesTexture;
        }
    }

    [Header("Palette")]
    [Tooltip("Цвета плиток — справочные sRGB-значения RAL Classic; полотно красится материалами модели")]
    public List<Swatch> palette = new List<Swatch>
    {
        new Swatch("RAL 9003", "панель_белый RAL 9003", "#F4F4F4"),
        new Swatch("RAL 9006", "панель_серебро RAL 9006", "#A5A5A5"),
        new Swatch("RAL 7004", "панель_серый RAL 7004", "#969992"),
        new Swatch("RAL 7016", "панель_антрацит RAL 7016", "#293133"),
        new Swatch("RAL 7016\nмороз", "RAL 7016 мороз", "#293133"),
        new Swatch("RAL 8017", "панель_коричнево-красный RAL 8017", "#45322E"),
        new Swatch("RAL 8017\nмороз", "панель_коричнево-красный(мороз) RAL", "#45322E"),
        new Swatch("RAL 3005", "панель_бордовый RAL 3005", "#5E2129"),
        new Swatch("RAL 6005", "панель_зеленый RAL 6005", "#2F4538"),
        new Swatch("RAL 5005", "панель_синий RAL 5005", "#154889"),
        new Swatch("RAL 1014", "панель_бежевый RAL 1014", "#DDC49A"),
        new Swatch("Золотой дуб", "панель_золотой дуб", "#FFFFFF", true),
        new Swatch("Венге", "панель_венге", "#FFFFFF", true),
    };
    [Tooltip("Материал лицевой стороны полотна, который заменяем")]
    public string targetMaterialName = "базовый цвет";

    [Header("Menu")]
    [Tooltip("Материал фона меню (URP Unlit). Пусто — создаётся в рантайме, если шейдер есть в сборке")]
    public Material backgroundMaterial;
    [Tooltip("Материал рамки выбранной плитки (URP Unlit)")]
    public Material highlightMaterial;
    public int columns = 4;
    public float tileSize = 0.035f;
    public float tileGap = 0.01f;
    [Tooltip("Насколько меню поднято над ладонью, м")]
    public float palmOffset = 0.1f;
    [Tooltip("Показать меню, когда ладонь смотрит на лицо сильнее порога (cos угла)")]
    public float showDot = 0.65f;
    public float hideDot = 0.4f;

    [Header("Adaptive gate")]
    [Tooltip("Адаптивные ворота; пусто — возьмётся с этого объекта")]
    public GateBuilder builder;

    [Tooltip("Проёмы из разметки пространства; пусто — найдётся в сцене")]
    public OpeningDetector openings;

    [Header("Measure")]
    [Tooltip("Замер рулеткой DISTO; пусто — найдётся в сцене, если есть")]
    public DistoMeasure measure;
    [Tooltip("Ручная разметка проёма (для высоких ворот)")]
    public OpeningMarker marker;

    [Header("Poke")]
    [Tooltip("Глубина зоны нажатия перед/за плиткой, м")]
    public float pokeDepth = 0.015f;
    public float pokeCooldown = 0.4f;

    [Header("Debug")]
    public bool debugLogs = false;
    [Tooltip("Проверка в редакторе: индекс цвета палитры (применяется один раз)")]
    public int debugApply = -1;
    [Tooltip("Проверка в редакторе: раскрыть/свернуть палитру")]
    public bool debugTogglePalette;

    class HandRef
    {
        public OVRSkeleton skeleton;
        public OVRHand hand;
        public Transform wrist, indexKnuckle, pinkyKnuckle, middleKnuckle, indexTip;
        public bool Ready => wrist && indexKnuckle && pinkyKnuckle && middleKnuckle && indexTip;
        public bool Tracked => hand != null && hand.IsTracked && skeleton.IsDataValid;
    }

    readonly HandRef _left = new HandRef();
    readonly HandRef _right = new HandRef();

    class Entry { public string label; public Material gate; public Material tile; public Color color; public bool textured; public Material adaptive; }
    readonly List<Entry> _palette = new List<Entry>();
    readonly List<(Renderer r, int slot)> _targets = new List<(Renderer, int)>();

    Transform _head;
    Transform _menu;
    readonly List<Transform> _tiles = new List<Transform>();
    bool _paletteOpen;   // палитра раскрыта (иначе — одна строка с текущим цветом)
    TextMeshPro _headText;
    Renderer _swatch;
    Transform _highlight;
    int _selected = -1;

    // Всё, что нажимается пальцем: плитки цветов и кнопки замера
    class Button { public Transform t; public Vector2 half; public Action press; }
    readonly List<Button> _buttons = new List<Button>();

    // Блок замера
    TextMeshPro _widthText, _heightText, _statusText, _openText, _fitText, _windowsText, _wicketText, _markText;
    Transform _widthBtn, _heightBtn, _fieldHighlight;

    bool _menuVisible;
    bool _pokeArmed = true;
    float _nextPoke;
    float _nextSearch;

    void Start()
    {
        if (builder == null) builder = GetComponent<GateBuilder>();
        CollectMaterials();
        if (builder != null) builder.Built += RefreshTargets;
        if (openings == null) openings = FindFirstObjectByType<OpeningDetector>();
        if (measure == null) measure = FindFirstObjectByType<DistoMeasure>();
        if (marker == null) marker = FindFirstObjectByType<OpeningMarker>();
        if (marker != null) marker.Changed += OnMarkerChanged;
        BuildMenu();
        RefreshTargets();
        if (measure != null) { measure.Changed += RefreshMeasure; RefreshMeasure(); }
        SetMenuVisible(false);
    }

    // ---------- materials ----------

    void CollectMaterials()
    {
        var byName = new Dictionary<string, Material>();
        Material baseMat = null;
        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string n = CleanName(mats[i].name);
                byName[n] = mats[i];
                if (n == targetMaterialName) { _targets.Add((r, i)); if (baseMat == null) baseMat = mats[i]; }
            }
        }
        if (_targets.Count == 0) { Debug.LogWarning($"GateColorPicker: не найдено рендереров с материалом '{targetMaterialName}'", this); return; }

        foreach (var sw in palette)
        {
            if (!byName.TryGetValue(CleanName(sw.materialName ?? ""), out var gateMat))
            {
                Debug.LogWarning($"GateColorPicker: материал '{sw.materialName}' не найден в модели — плитка '{sw.label}' пропущена", this);
                continue;
            }
            _palette.Add(new Entry
            {
                label = sw.label.Replace("\\n", "\n"),
                gate = gateMat,
                tile = sw.tileUsesTexture ? gateMat : TileMaterial(sw.tileColor),
                color = sw.tileColor,
                textured = sw.tileUsesTexture,
            });
        }
        if (debugLogs) Debug.Log($"GateColorPicker: palette={_palette.Count} targets={_targets.Count} base={baseMat.shader.name}");
    }

    /// <summary>Ворота пересобраны — заново найти панели полотна и вернуть выбранный цвет.</summary>
    public void RefreshTargets()
    {
        _targets.Clear();
        // Адаптивные ворота: наружная обшивка секций — детали «панель_N» (сторона улицы).
        // Прежняя модель (материал «базовый цвет») скрыта — её не трогаем.
        var gate = builder != null ? builder.Root : null;
        if (gate != null)
        {
            foreach (var r in gate.GetComponentsInChildren<Renderer>(true))
                if (IsOuterSkin(r.transform, gate))
                    for (int i = 0; i < r.sharedMaterials.Length; i++) _targets.Add((r, i));
        }
        if (_targets.Count == 0)
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && CleanName(mats[i].name) == targetMaterialName) _targets.Add((r, i));
            }
        // До выбора цвета — белые панели (RAL 9003) с гофрами, а не серый материал модели
        if (_selected < 0 && _palette.Count > 0) _selected = 0;
        if (_selected >= 0) Apply(_selected);
        if (debugLogs) Debug.Log($"GateColorPicker: targets refreshed = {_targets.Count}");
    }

    // Панели адаптивных ворот без UV-развёртки: однотонный RAL — точный цвет плитки,
    // дерево — та же текстура, положенная плоско по ширине/высоте секции
    Material AdaptiveMaterial(Entry e)
    {
        if (e.adaptive != null) return e.adaptive;
        var baseMat = GateMaterials.Get(e.gate);
        if (baseMat == null || !baseMat.HasProperty("_PlanarUV")) return null;
        var m = new Material(baseMat) { name = baseMat.name + " (adaptive)" };
        bool frost = e.label.Contains("мороз");
        if (e.textured)
        {
            // Дерево: текстура плоско вдоль ширины, ~2 м на повтор; волокна — по ширине
            var tex = m.GetTexture("_BaseMap");
            if (tex != null) { tex.wrapModeU = TextureWrapMode.Repeat; tex.wrapModeV = TextureWrapMode.Mirror; }
            m.SetFloat("_PlanarUV", 1f);
            m.SetFloat("_PlanarScale", 0.2f);         // длина текстуры = 5 м по ширине: на воротах без стыка
            m.SetFloat("_PlanarScaleY", 1f / 0.84f);  // повтор по высоте 0,84 м = 6 гофров — стык прячется в канавке
            m.SetFloat("_PlanarURange", 0.84f);
            m.SetFloat("_Stucco", 0.06f);
            m.SetFloat("_RoughnessMin", 0.55f);
        }
        else
        {
            m.SetTexture("_BaseMap", null);
            m.SetColor("_BaseColor", e.color);
            m.SetFloat("_Stucco", frost ? 0.35f : 0.12f);              // «мороз» — крупнее и матовее
            m.SetFloat("_RoughnessMin", frost ? 0.75f : 0.45f);
        }
        m.SetFloat("_Ribs", 1f);
        m.SetFloat("_RibPeriod", 0.14f);
        m.SetFloat("_RibDepth", 0.6f);
        e.adaptive = m;
        return m;
    }

    static bool IsOuterSkin(Transform t, Transform stop)
    {
        for (; t != null && t != stop; t = t.parent)
            if (t.name.StartsWith("панель_")) return true;
        return false;
    }


    // Вписать ворота: место — проём из разметки (на который смотрим), размер — замер DISTO, а если его нет — разметка
    void FitToOpening()
    {
        if (builder == null) return;
        var op = openings != null ? openings.Selected : null;
        bool measured = measure != null && measure.Width.HasValue && measure.Height.HasValue;
        if (!measured && op == null)
        {
            SetStatus("посмотрите на подсвеченный проём или измерьте ширину и высоту");
            return;
        }
        float w = measured ? measure.Width.Value / 1000f : op.WidthM;
        float h = measured ? measure.Height.Value / 1000f : op.HeightM;
        if (op != null) builder.PlaceInOpening(op.BottomCenter, op.Inward);
        builder.Build(w, h);
        SetStatus($"ворота {w * 1000:0} × {h * 1000:0} мм " + (measured ? "(по DISTO)" : "(по разметке)") + (op != null ? " в проёме" : ""));
    }

    void SetStatus(string s) { if (_statusText != null) _statusText.text = s; }

    void OnMarkerChanged()
    {
        if (marker == null) return;
        if (_markText != null) _markText.text = marker.Active ? "Отменить разметку" : "Разметить проём";
        if (!string.IsNullOrEmpty(marker.Hint)) SetStatus(marker.Hint);
    }

    // glTFast-шейдеры: baseColorFactor / roughnessFactor; URP Lit: _BaseColor / _Smoothness
    static void SetColor(Material m, Color c)
    {
        if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", c);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    // Плитка — неосвещённый образец точного цвета
    Material TileMaterial(Color c)
    {
        Material m = backgroundMaterial != null ? new Material(backgroundMaterial)
                   : Shader.Find("Universal Render Pipeline/Unlit") is Shader sh ? new Material(sh) : null;
        if (m != null) SetColor(m, c);
        return m;
    }

    static string Path(Transform t) { var p = t.name; while (t.parent != null) { t = t.parent; p = t.name + "/" + p; } return p; }

    // Unity может добавить " (Instance)" к имени материала
    static string CleanName(string n) => n.Replace(" (Instance)", "").Trim();

    public void Apply(int index)
    {
        if (index < 0 || index >= _palette.Count) return;
        var mat = GateMaterials.Get(_palette[index].gate);
        var adaptiveMat = AdaptiveMaterial(_palette[index]);
        foreach (var (r, slot) in _targets)
        {
            var mats = r.sharedMaterials;
            mats[slot] = adaptiveMat != null && IsOuterSkin(r.transform, transform) ? adaptiveMat : mat;
            r.sharedMaterials = mats;
        }
        _selected = index;
        UpdateHighlight();
        UpdateHeader();
        if (debugLogs) Debug.Log($"GateColorPicker: applied {_palette[index].label} mat={mat.name} shader={mat.shader.name} targets={_targets.Count}");
    }

    // ---------- menu ----------

    void BuildMenu()
    {
        _menu = new GameObject("GateColorMenu").transform;

        int rows = Mathf.CeilToInt(_palette.Count / (float)columns);
        float step = tileSize + tileGap;
        float labelH = tileSize * 0.45f;
        float rowStep = step + labelH;
        float width = columns * step - tileGap;

        // Раскладка сверху вниз от y = 0, в конце всё центрируется
        float y = 0f;

        // Строка выбора цвета: образец текущего цвета + название; нажатие раскрывает/сворачивает палитру
        float headH = 0.026f;
        PanelButton("ColorHeader", new Vector3(0, y - headH * 0.5f, 0), width, headH, out _headText,
            () => { _paletteOpen = !_paletteOpen; RebuildMenu(); });
        var sw = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
        Destroy(sw.GetComponent<Collider>());
        sw.name = "CurrentSwatch";
        sw.SetParent(_menu, false);
        sw.localPosition = new Vector3(-width * 0.5f + headH * 0.5f + 0.003f, y - headH * 0.5f, -0.0015f);
        sw.localScale = new Vector3(headH * 0.75f, headH * 0.75f, 1f);
        _swatch = sw.GetComponent<Renderer>();
        // текст правее образца, чтобы не наезжал на него
        var hr = _headText.rectTransform;
        hr.sizeDelta = new Vector2(width - headH * 2.2f, hr.sizeDelta.y);
        hr.localPosition += new Vector3(headH * 0.5f, 0, 0);
        UpdateHeader();
        y -= headH + tileGap;

        _highlight = Quad("Highlight", _menu, highlightMaterial, Color.white);
        _highlight.localScale = new Vector3(tileSize + 0.006f, tileSize + 0.006f, 1f);
        _highlight.gameObject.SetActive(false);

        if (_paletteOpen)
        {
            for (int i = 0; i < _palette.Count; i++)
            {
                int c = i % columns, row = i / columns;
                float x = -width * 0.5f + tileSize * 0.5f + c * step;
                float ty = y - tileSize * 0.5f - row * rowStep;

                var tile = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
                Destroy(tile.GetComponent<Collider>());
                tile.name = "Tile_" + i;
                tile.SetParent(_menu, false);
                tile.localPosition = new Vector3(x, ty, 0);
                tile.localScale = new Vector3(tileSize, tileSize, 1f);
                if (_palette[i].tile != null) tile.GetComponent<Renderer>().sharedMaterial = _palette[i].tile;
                _tiles.Add(tile);
                int index = i;
                // выбор цвета сворачивает палитру
                AddButton(tile, tileSize, tileSize, () => { Apply(index); _paletteOpen = false; RebuildMenu(); });

                Text("Label_" + i, _menu, _palette[i].label, step * 0.95f, labelH)
                    .localPosition = new Vector3(x, ty - tileSize * 0.5f - labelH * 0.5f, 0);
            }
            y -= rows * rowStep;
        }

        // Открыть / закрыть адаптивные ворота
        if (builder != null)
        {
            y -= 0.006f;
            float half = (width - tileGap) * 0.5f, optH = 0.02f;
            float xl = -width * 0.5f + half * 0.5f, xr = width * 0.5f - half * 0.5f;
            PanelButton("OpenBtn", new Vector3(xl, y - 0.011f, 0), half, 0.022f, out _openText, () => builder.ToggleOpen());
            // замер/разметку сделали снаружи — направляющие ушли на улицу: развернуть в том же проёме
            PanelButton("FlipBtn", new Vector3(xr, y - 0.011f, 0), half, 0.022f, out var flipText, () => builder.FlipSide());
            flipText.text = "Развернуть 180°";
            y -= 0.022f + tileGap;

            // Комплектация: окна и калитка (калитка — только если проём позволяет)
            PanelButton("WindowsBtn", new Vector3(xl, y - optH * 0.5f, 0), half, optH, out _windowsText,
                () => builder.SetOptions(!builder.windowsEnabled, builder.wicketEnabled));
            PanelButton("WicketBtn", new Vector3(xr, y - optH * 0.5f, 0), half, optH, out _wicketText,
                () => { if (builder.WicketAllowed) builder.SetOptions(builder.windowsEnabled, !builder.wicketEnabled); });
            y -= optH;
        }

        if (measure != null) y = BuildMeasure(y, width);

        // Центрируем содержимое и подкладываем фон
        float top = 0f, bottom = y;
        float centerY = (top + bottom) * 0.5f;
        foreach (Transform child in _menu) child.localPosition -= new Vector3(0, centerY, 0);
        var bg = Quad("Background", _menu, backgroundMaterial, new Color(0.08f, 0.08f, 0.1f, 1f));
        bg.localScale = new Vector3(width + 0.02f, top - bottom + 0.02f, 1f);
        bg.localPosition = new Vector3(0, 0, 0.002f);
    }

    void UpdateHeader()
    {
        bool has = _selected >= 0 && _selected < _palette.Count;
        if (_headText != null)
            _headText.text = $"Цвет: {(has ? _palette[_selected].label.Replace("\n", " ") : "—")}   ({(_paletteOpen ? "свернуть" : "выбрать")})";
        if (_swatch != null)
        {
            _swatch.gameObject.SetActive(has && _palette[_selected].tile != null);
            if (has && _palette[_selected].tile != null) _swatch.sharedMaterial = _palette[_selected].tile;
        }
    }

    // Пересобрать меню (раскрыли/свернули палитру): положение и видимость сохраняются
    void RebuildMenu()
    {
        Vector3 pos = _menu != null ? _menu.position : Vector3.zero;
        Quaternion rot = _menu != null ? _menu.rotation : Quaternion.identity;
        if (_menu != null) Destroy(_menu.gameObject);
        _tiles.Clear();
        _buttons.Clear();
        _openText = _fitText = _windowsText = _wicketText = _markText = _headText = null;
        _swatch = null;
        _widthText = _heightText = _statusText = null;
        BuildMenu();
        _menu.SetPositionAndRotation(pos, rot);
        _menu.gameObject.SetActive(_menuVisible);
        _pokeArmed = false; // тот же тычок не должен нажать кнопку на новом месте
        RefreshMeasure();
        OnMarkerChanged();
        UpdateHighlight();
        UpdateButtonTexts();
    }

    float BuildMeasure(float y, float width)
    {
        y -= 0.008f;
        Text("MeasureTitle", _menu, "Проём · DISTO", width, 0.013f).localPosition = new Vector3(0, y - 0.0065f, 0);
        y -= 0.014f;
        _statusText = Text("MeasureStatus", _menu, "", width, 0.009f).GetComponent<TextMeshPro>();
        _statusText.color = new Color(0.75f, 0.75f, 0.8f);
        _statusText.transform.localPosition = new Vector3(0, y - 0.0045f, 0);
        y -= 0.012f;

        float half = (width - tileGap) * 0.5f;
        float bigH = 0.03f, smallH = 0.018f;
        float xl = -width * 0.5f + half * 0.5f, xr = width * 0.5f - half * 0.5f;

        _fieldHighlight = Quad("FieldHighlight", _menu, highlightMaterial, Color.white);
        _fieldHighlight.localScale = new Vector3(half + 0.005f, bigH + 0.005f, 1f);

        _widthBtn = PanelButton("WidthBtn", new Vector3(xl, y - bigH * 0.5f, 0), half, bigH, out _widthText,
            () => measure.SetNext(DistoMeasure.Field.Width));
        _heightBtn = PanelButton("HeightBtn", new Vector3(xr, y - bigH * 0.5f, 0), half, bigH, out _heightText,
            () => measure.SetNext(DistoMeasure.Field.Height));
        y -= bigH + tileGap;

        PanelButton("ConnectBtn", new Vector3(xl, y - smallH * 0.5f, 0), half, smallH, out var connectText, () => measure.Connect());
        connectText.text = "Подключить";
        PanelButton("ResetBtn", new Vector3(xr, y - smallH * 0.5f, 0), half, smallH, out var resetText, () => measure.ResetValues());
        resetText.text = "Сбросить";
        y -= smallH + tileGap;

        // Вписать адаптивные ворота в измеренный проём
        if (builder != null)
        {
            PanelButton("FitBtn", new Vector3(0, y - smallH * 0.5f, 0), width, smallH, out _fitText, FitToOpening);
            _fitText.text = "Вписать ворота в проём";
            y -= smallH;
        }
        // Ручная разметка — для высоких проёмов, которые сканер Quest не видит целиком
        if (marker != null)
        {
            y -= tileGap;
            PanelButton("MarkBtn", new Vector3(xl, y - smallH * 0.5f, 0), half, smallH, out _markText, () => marker.Toggle());
            PanelButton("ClearMarksBtn", new Vector3(xr, y - smallH * 0.5f, 0), half, smallH, out var clearText,
                () => { marker.Cancel(); if (openings != null) openings.ClearManual(); SetStatus("ручная разметка удалена"); });
            clearText.text = "Убрать разметку";
            y -= smallH;
        }
        // Пересканировать комнату (новый объект, недосканированный верх проёма)
        if (openings != null)
        {
            y -= tileGap;
            PanelButton("ScanBtn", new Vector3(0, y - smallH * 0.5f, 0), width, smallH, out var scanText, () =>
            {
                if (openings.Scanning) return;
                if (marker != null) marker.Cancel();
                SetStatus("сканирование пространства…");
                openings.RescanSpace(SetStatus);
            });
            scanText.text = "Сканировать пространство";
            y -= smallH;
        }
        if (marker != null) OnMarkerChanged();
        return y;
    }

    Transform PanelButton(string name, Vector3 pos, float w, float h, out TextMeshPro label, Action press)
    {
        var q = Quad(name, _menu, null, Color.white);
        var m = TileMaterial(new Color(0.22f, 0.24f, 0.3f));
        if (m != null) q.GetComponent<Renderer>().sharedMaterial = m;
        q.localPosition = pos;
        q.localScale = new Vector3(w, h, 1f);
        AddButton(q, w, h, press);
        var t = Text(name + "Label", _menu, "", w * 0.92f, h * 0.85f);
        t.localPosition = pos + new Vector3(0, 0, -0.001f);
        label = t.GetComponent<TextMeshPro>();
        return q;
    }

    void AddButton(Transform t, float w, float h, Action press) =>
        _buttons.Add(new Button { t = t, half = new Vector2(w * 0.5f, h * 0.5f), press = press });

    void RefreshMeasure()
    {
        if (measure == null || _widthText == null) return;
        _widthText.text = "Ширина\n" + DistoMeasure.Format(measure.Width);
        _heightText.text = "Высота\n" + DistoMeasure.Format(measure.Height);
        _statusText.text = measure.Status;
        var target = measure.Next == DistoMeasure.Field.Width ? _widthBtn : _heightBtn;
        _fieldHighlight.localPosition = target.localPosition + new Vector3(0, 0, 0.001f);
    }

    static Transform Quad(string name, Transform parent, Material mat, Color fallback)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
        Destroy(q.GetComponent<Collider>());
        q.name = name;
        q.SetParent(parent, false);
        if (mat == null)
        {
            // Запасной вариант: шейдер может не попасть в сборку — тогда квад останется с материалом по умолчанию
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null)
            {
                mat = new Material(shader);
                mat.SetColor("_BaseColor", fallback);
            }
        }
        if (mat != null) q.GetComponent<Renderer>().sharedMaterial = mat;
        return q;
    }

    // Подписи меню: размер шрифта подбираем сами (одна строка по ширине/высоте кнопки) —
    // встроенный авторазмер TMP после смены текста застревал на мелком кегле
    readonly List<(TextMeshPro tmp, string fitted)> _labels = new List<(TextMeshPro, string)>();
    const float LabelFontMax = 0.15f;

    Transform Text(string name, Transform parent, string text, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.enableAutoSizing = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(w, h);
        tmp.text = text;
        FitLabel(tmp);
        _labels.Add((tmp, text));
        return go.transform;
    }

    static void FitLabel(TextMeshPro tmp)
    {
        if (string.IsNullOrEmpty(tmp.text)) return;
        var box = tmp.rectTransform.sizeDelta;
        tmp.fontSize = 1f;
        Vector2 pref = tmp.GetPreferredValues(tmp.text);
        if (pref.x <= 1e-5f || pref.y <= 1e-5f) return;
        tmp.fontSize = Mathf.Min(LabelFontMax, box.x / pref.x, box.y / pref.y);
    }

    void FitChangedLabels()
    {
        for (int i = _labels.Count - 1; i >= 0; i--)
        {
            var (tmp, fitted) = _labels[i];
            if (tmp == null) { _labels.RemoveAt(i); continue; }
            if (tmp.text == fitted) continue;
            FitLabel(tmp);
            _labels[i] = (tmp, tmp.text);
        }
    }

    void UpdateHighlight()
    {
        if (_highlight == null) return;
        bool on = _selected >= 0 && _selected < _tiles.Count;
        _highlight.gameObject.SetActive(on && _menuVisible);
        if (on) _highlight.localPosition = _tiles[_selected].localPosition + new Vector3(0, 0, 0.001f);
    }

    void SetMenuVisible(bool v)
    {
        _menuVisible = v;
        if (_menu) _menu.gameObject.SetActive(v);
        if (v) UpdateHighlight();
        if (debugLogs) Debug.Log($"GateColorPicker: menu {(v ? "shown" : "hidden")}");
    }

    // ---------- update ----------

    void LateUpdate() => FitChangedLabels(); // тексты кнопок/статуса меняются из Update и событий

    void Update()
    {
        if (debugApply >= 0) { Apply(debugApply); debugApply = -1; }
        if (debugTogglePalette && _menu != null) { debugTogglePalette = false; _paletteOpen = !_paletteOpen; RebuildMenu(); _menu.gameObject.SetActive(true); }
        if (_menu == null || _palette.Count == 0) return;
        if (!EnsureRefs()) { if (_menuVisible) SetMenuVisible(false); return; }

        if (!_left.Tracked) { if (_menuVisible) SetMenuVisible(false); return; }

        Vector3 wrist = _left.wrist.position;
        Vector3 palm = (wrist + _left.middleKnuckle.position) * 0.5f;
        // Нормаль из ладони левой руки (для правой знак был бы обратный)
        Vector3 normal = Vector3.Cross(_left.indexKnuckle.position - wrist, _left.pinkyKnuckle.position - wrist).normalized;
        float facing = Vector3.Dot(normal, (_head.position - palm).normalized);

        if (!_menuVisible && facing > showDot) SetMenuVisible(true);
        else if (_menuVisible && facing < hideDot) SetMenuVisible(false);
        if (!_menuVisible) return;

        if (_fitText != null)
        {
            var op = openings != null ? openings.Selected : null;
            _fitText.text = op != null ? $"Вписать в проём {op.WidthM * 1000:0}×{op.HeightM * 1000:0}" : "Вписать ворота в проём";
        }
        UpdateButtonTexts();

        // Меню над ладонью, развёрнуто к голове, без крена
        Vector3 pos = palm + normal * palmOffset;
        _menu.position = pos;
        Vector3 fwd = pos - _head.position; // +Z меню смотрит от пользователя
        if (fwd.sqrMagnitude > 1e-6f) _menu.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        Poke();
    }

    void UpdateButtonTexts()
    {
        if (_openText != null && builder != null)
            _openText.text = builder.OpenOrOpening ? "Закрыть ворота" : "Открыть ворота";
        if (_windowsText != null && builder != null)
            _windowsText.text = builder.windowsEnabled ? "Окна: есть" : "Окна: нет";
        if (_wicketText != null && builder != null)
        {
            _wicketText.text = !builder.WicketAllowed ? $"Калитка: от {builder.wicketMinOpeningWidth * 1000:0}×{builder.wicketMinOpeningHeight * 1000:0}"
                : builder.wicketEnabled ? "Калитка: есть" : "Калитка: нет";
            _wicketText.color = builder.WicketAllowed ? Color.white : new Color(0.6f, 0.6f, 0.65f);
        }
    }

    void Poke()
    {
        if (!_right.Tracked) { _pokeArmed = true; return; }

        Vector3 p = _menu.InverseTransformPoint(_right.indexTip.position);
        Button hit = null;
        foreach (var b in _buttons)
        {
            Vector3 t = b.t.localPosition;
            if (Mathf.Abs(p.x - t.x) < b.half.x && Mathf.Abs(p.y - t.y) < b.half.y && Mathf.Abs(p.z) < pokeDepth) { hit = b; break; }
        }

        if (hit == null) { _pokeArmed = true; return; }
        if (!_pokeArmed || Time.time < _nextPoke) return;

        _pokeArmed = false;
        _nextPoke = Time.time + pokeCooldown;
        hit.press();
    }

    // ---------- refs ----------

    bool EnsureRefs()
    {
        if (_head == null)
        {
            var rig = FindFirstObjectByType<OVRCameraRig>();
            _head = rig != null ? rig.centerEyeAnchor : (Camera.main != null ? Camera.main.transform : null);
        }
        if (_left.Ready && _right.Ready && _head != null) return true;
        if (Time.time < _nextSearch) return false;
        _nextSearch = Time.time + 0.5f;

        foreach (var sk in FindObjectsByType<OVRSkeleton>(FindObjectsSortMode.None))
        {
            string t = sk.GetSkeletonType().ToString();
            if (!t.Contains("Hand")) continue;
            var h = t.Contains("Left") ? _left : t.Contains("Right") ? _right : null;
            if (h == null || h.Ready) continue;
            h.skeleton = sk;
            h.hand = sk.GetComponent<OVRHand>() ?? sk.GetComponentInParent<OVRHand>();
            if (!sk.IsInitialized || sk.Bones == null) continue;
            foreach (var b in sk.Bones)
            {
                if (b?.Transform == null) continue;
                switch (b.Transform.name)
                {
                    case "Hand_WristRoot": case "XRHand_Wrist": h.wrist = b.Transform; break;
                    case "Hand_Index1": case "XRHand_IndexProximal": h.indexKnuckle = b.Transform; break;
                    case "Hand_Middle1": case "XRHand_MiddleProximal": h.middleKnuckle = b.Transform; break;
                    case "Hand_Pinky1": case "XRHand_LittleProximal": h.pinkyKnuckle = b.Transform; break;
                    case "Hand_IndexTip": case "XRHand_IndexTip": h.indexTip = b.Transform; break;
                }
            }
            if (debugLogs) Debug.Log($"GateColorPicker: {sk.name} ready={h.Ready}");
        }
        return _left.Ready && _right.Ready && _head != null;
    }

    void OnDestroy()
    {
        if (builder != null) builder.Built -= RefreshTargets;
        if (measure != null) measure.Changed -= RefreshMeasure;
        if (marker != null) marker.Changed -= OnMarkerChanged;
        if (_menu != null) Destroy(_menu.gameObject);
    }
}
