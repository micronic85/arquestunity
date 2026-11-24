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
public partial class GateColorPicker : MonoBehaviour
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
        new Swatch("RAL 9005", "панель_антрацит RAL 7016", "#0A0A0D"), // чёрный: своего материала в модели нет — цвет задаёт плитка
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
    [Tooltip("Материалы палитры без держателя Sphere и геометрии прежней модели")]
    public List<Material> paletteMaterials = new List<Material>();

    [Header("Menu")]
    [Tooltip("Материал фона меню (URP Unlit). Пусто — создаётся в рантайме, если шейдер есть в сборке")]
    public Material backgroundMaterial;
    [Tooltip("Материал рамки выбранной плитки (URP Unlit)")]
    public Material highlightMaterial;
    public int columns = 4;
    [Tooltip("Ширина меню, м")]
    public float menuWidth = 0.22f;
    [Tooltip("Высота кнопки, м (Meta: цель для пальца ≥ 2,2 см, удобнее ≥ 3)")]
    public float buttonHeight = 0.032f;
    [Tooltip("Зазор между кнопками, м")]
    public float buttonGap = 0.008f;
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
    [Tooltip("Палец ближе этого перед кнопкой — подсветка, м")]
    public float hoverDistance = 0.05f;
    [Tooltip("Нажатие — кончик пальца ближе этого к плоскости кнопки, м (кость кончика — внутри подушечки)")]
    public float pressDepth = 0.007f;
    [Tooltip("Новое нажатие — после отвода пальца на это расстояние перед меню, м")]
    public float releaseDistance = 0.015f;
    public float pokeCooldown = 0.4f;

    [Header("Debug")]
    public bool debugLogs = false;
    [Tooltip("Проверка в редакторе: индекс цвета палитры (применяется один раз)")]
    public int debugApply = -1;
    [Tooltip("Проверка в редакторе: открыть вкладку (0 — Ворота, 1 — Цвет, 2 — Замер, 3 — Проём) и показать меню")]
    public int debugTab = -1;

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

    class Entry { public string label; public Material gate; public Material tile; public Color color; public bool textured; public Material adaptive; public Material roller; }
    readonly List<Entry> _palette = new List<Entry>();
    readonly List<(Renderer r, int slot)> _targets = new List<(Renderer, int)>();

    Transform _head;
    Transform _menu;
    readonly List<Transform> _tiles = new List<Transform>();
    Tab _tab = Tab.Gate;
    TextMeshPro _headText;
    Renderer _swatch;
    Transform _highlight;
    int _selected = -1;

    // Всё, что нажимается пальцем: вкладки, кнопки, плитки цветов
    readonly List<Button> _buttons = new List<Button>();

    // Блок замера
    TextMeshPro _widthText, _heightText, _statusText, _openText, _fitText, _windowsText, _wicketText, _markText;
    TextMeshPro _placementText;
    GateMover _mover;
    TextMeshPro _sectionalTypeText, _rollerTypeText, _rollerClearText;
    GateBuilder.GateKind _menuGateKind;
    Transform _widthBtn, _heightBtn, _fieldHighlight;

    bool _menuVisible;
    bool _pokeArmed = true;
    float _nextPoke;
    float _nextSearch;

    void Start()
    {
        if (builder == null) builder = GetComponent<GateBuilder>();
        _mover = builder != null ? builder.GetComponent<GateMover>() : GetComponent<GateMover>();
        _portal = builder != null ? builder.GetComponent<GatePortal>() : null;
        // в сцене палитра сохранена списком — чёрный (добавлен позже) дописываем, если его там нет
        if (!palette.Exists(sw => sw.label == "RAL 9005"))
        {
            int at = palette.FindIndex(sw => sw.label.Contains("7016") && sw.label.Contains("мороз"));
            palette.Insert(at >= 0 ? at + 1 : palette.Count, new Swatch("RAL 9005", "панель_антрацит RAL 7016", "#0A0A0D"));
        }
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
        foreach (var mat in paletteMaterials)
            if (mat != null) byName[CleanName(mat.name)] = mat;
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
        if (_targets.Count == 0 && (builder == null || builder.Root == null))
        {
            Debug.LogWarning($"GateColorPicker: не найдено панелей ворот для палитры", this);
            return;
        }

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
        if (debugLogs) Debug.Log($"GateColorPicker: palette={_palette.Count} targets={_targets.Count} base={(baseMat != null ? baseMat.shader.name : "adaptive")}");
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
        if (_menu != null && builder != null && _menuGateKind != builder.gateKind) RebuildMenu();
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

    Material RollerMaterial(Entry e)
    {
        if (e.roller != null) return e.roller;
        var baseMat = AdaptiveMaterial(e);
        if (baseMat == null) return GateMaterials.Get(e.gate);
        e.roller = new Material(baseMat) { name = e.label + " (roller)" };
        // Do not reuse AO/normal/metallic maps baked for the sectional panels' UV atlas.
        e.roller.SetTexture("_MRMap", null);
        e.roller.SetTexture("_BumpMap", null);
        e.roller.SetTexture("_OcclusionMap", null);
        e.roller.SetFloat("_BumpScale", 0f);
        e.roller.SetFloat("_OcclusionStrength", 0f);
        e.roller.SetFloat("_Metallic", .25f);
        e.roller.SetFloat("_Roughness", .45f);
        e.roller.SetFloat("_Ribs", 0f); // the roller profile is actual geometry
        e.roller.SetFloat("_Stucco", e.label.Contains("мороз") ? .25f : .025f);
        e.roller.enableInstancing = true;
        return e.roller;
    }

    static bool IsOuterSkin(Transform t, Transform stop)
    {
        for (; t != null && t != stop; t = t.parent)
            if (t.name.StartsWith("панель_") || t.name.StartsWith("RollerPaint_")) return true;
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
        builder.Fit(w, h);
        SetStatus($"ворота {w * 1000:0} × {h * 1000:0} мм " + (measured ? "(по размерам)" : "(по разметке)") + (op != null ? " в проёме" : ""));
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
        var adaptiveMat = builder != null && builder.IsRoller ? RollerMaterial(_palette[index]) : AdaptiveMaterial(_palette[index]);
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
    // Меню из вкладок «Ворота / Цвет / Замер / Проём»: на каждой — несколько крупных кнопок (≥ 3 см),
    // внизу — строка статуса. Кнопки — скруглённые плашки, текст — свой SDF-шрифт с кириллицей (MenuFont).

    public enum Tab { Gate, Color, Measure, Opening }
    static readonly string[] TabNames = { "Ворота", "Цвет", "Замер", "Проём" };

    static readonly Color ColBg = new Color(0.07f, 0.075f, 0.09f, 1f);
    static readonly Color ColButton = new Color(0.19f, 0.21f, 0.26f, 1f);
    static readonly Color ColHover = new Color(0.29f, 0.32f, 0.40f, 1f);
    static readonly Color ColAccent = new Color(0.16f, 0.46f, 0.88f, 1f);
    static readonly Color ColText = new Color(0.95f, 0.96f, 0.98f, 1f);
    static readonly Color ColDim = new Color(0.62f, 0.65f, 0.72f, 1f);

    static TMP_FontAsset _font;
    static bool _fontLoaded;
    /// <summary>Шрифт меню и подписей (Resources/Fonts/MenuFont SDF); нет — шрифт TMP по умолчанию.</summary>
    public static TMP_FontAsset MenuFont
    {
        get
        {
            if (!_fontLoaded) { _font = Resources.Load<TMP_FontAsset>("Fonts/MenuFont SDF"); _fontLoaded = true; }
            return _font;
        }
    }

    Vector2 _menuHalf; // половина размера меню (для «палец рядом»)

    void BuildMenu()
    {
        _menu = new GameObject("GateColorMenu").transform;
        var audio = _menu.gameObject.AddComponent<AudioSource>();
        audio.playOnAwake = false; audio.spatialBlend = 1f; audio.volume = 0.6f; audio.minDistance = 0.3f;
        audio.clip = ClickClip();
        _click = audio;

        float W = menuWidth, g = buttonGap, H = buttonHeight;
        float half = (W - g) * 0.5f;
        float xl = -W * 0.5f + half * 0.5f, xr = W * 0.5f - half * 0.5f;
        float y = 0f;

        // Вкладки
        float tabW = (W - g * 3f) / 4f, tabH = H * 0.85f;
        for (int i = 0; i < 4; i++)
        {
            var tab = (Tab)i;
            var b = PanelButton("Tab_" + i, new Vector3(-W * 0.5f + tabW * 0.5f + i * (tabW + g), y - tabH * 0.5f, 0), tabW, tabH,
                out var tt, () => { if (_tab != tab) { _tab = tab; RebuildMenu(); } });
            tt.text = TabNames[i];
            if (tab == _tab) { b.baseColor = ColAccent; b.SetColor(ColAccent); }
        }
        y -= tabH + g * 1.5f;

        switch (_tab)
        {
            case Tab.Gate:
                if (builder != null)
                {
                    _menuGateKind = builder.gateKind;
                    PanelButton("SectionalTypeBtn", new Vector3(xl, y - H * .5f, 0), half, H,
                        out _sectionalTypeText, () => SelectGateType(GateBuilder.GateKind.Sectional));
                    PanelButton("RollerTypeBtn", new Vector3(xr, y - H * .5f, 0), half, H,
                        out _rollerTypeText, () => SelectGateType(GateBuilder.GateKind.Roller));
                    _sectionalTypeText.text = "Секционные";
                    _rollerTypeText.text = "Роллетные";
                    y -= H + g;
                    PanelButton("OpenBtn", new Vector3(xl, y - H * 0.5f, 0), half, H, out _openText, () => builder.ToggleOpen());
                    // замер/разметку сделали снаружи — направляющие ушли на улицу: развернуть в том же проёме
                    PanelButton("FlipBtn", new Vector3(xr, y - H * 0.5f, 0), half, H, out var flipText, () => builder.FlipSide());
                    flipText.text = "Развернуть 180°";
                    y -= H + g;
                    // Sectional options are retained when switching to the roller type.
                    if (!builder.IsRoller)
                    {
                    PanelButton("WindowsBtn", new Vector3(xl, y - H * 0.5f, 0), half, H, out _windowsText,
                        () => builder.SetOptions(!builder.windowsEnabled, builder.wicketEnabled));
                    PanelButton("WicketBtn", new Vector3(xr, y - H * 0.5f, 0), half, H, out _wicketText,
                        () => { if (builder.WicketAllowed) builder.SetOptions(builder.windowsEnabled, !builder.wicketEnabled); });
                    y -= H;
                    }
                    else
                    {
                        var sizeLabel = Text("RollerClearance", _menu,
                            $"Под коробом {builder.RollerClearHeight * 1000:0} мм", W, H * .60f);
                        _rollerClearText = sizeLabel.GetComponent<TextMeshPro>();
                        sizeLabel.localPosition = new Vector3(0, y - H * .4f, -.001f);
                        y -= H * .8f;
                    }
                    if (_mover != null)
                    {
                        y -= g;
                        PanelButton("PlacementLockBtn", new Vector3(0, y - H * .5f, 0), W, H,
                            out _placementText, TogglePlacementLock);
                        _placementText.text = _mover.IsPlacementLocked ? "Разблокировать положение" : "Зафиксировать положение";
                        y -= H;
                    }
                    y = BuildPortalButtons(y, W, H, g);
                }
                break;

            case Tab.Color:
            {
                // Текущий цвет: образец + название
                float headH = H * 0.8f;
                var sw = RoundedQuad("CurrentSwatch", _menu, headH * 0.8f, headH * 0.8f, 0.004f, ColText);
                sw.localPosition = new Vector3(-W * 0.5f + headH * 0.5f, y - headH * 0.5f, 0);
                _swatch = sw.GetComponent<Renderer>();
                _headText = Text("ColorHead", _menu, "", W - headH * 1.4f, headH * 0.55f).GetComponent<TextMeshPro>();
                _headText.alignment = TextAlignmentOptions.Left;
                _headText.transform.localPosition = new Vector3(headH * 0.7f, y - headH * 0.5f, -0.001f);
                UpdateHeader();
                y -= headH + g;

                // Плитки цветов
                int cols = Mathf.Max(1, columns);
                float tile = (W - g * (cols - 1)) / cols;
                float tileH = tile * 0.72f, labelH = H * 0.42f, rowStep = tileH + labelH + g;
                _highlight = RoundedQuad("Highlight", _menu, tile + 0.008f, tileH + 0.008f, 0.008f, ColAccent);
                _highlight.gameObject.SetActive(false);
                for (int i = 0; i < _palette.Count; i++)
                {
                    int c = i % cols, row = i / cols;
                    float x = -W * 0.5f + tile * 0.5f + c * (tile + g);
                    float ty = y - tileH * 0.5f - row * rowStep;
                    var t = RoundedQuad("Tile_" + i, _menu, tile, tileH, 0.005f, Color.white);
                    t.localPosition = new Vector3(x, ty, 0);
                    if (_palette[i].tile != null) t.GetComponent<Renderer>().sharedMaterial = _palette[i].tile;
                    // дерево: в атласе текстуры справа (U > 0,84) пусто — на плитке только рабочая часть
                    if (_palette[i].textured) t.GetComponent<MeshFilter>().sharedMesh = RoundedMesh(tile, tileH, 0.005f, 0.8f);
                    _tiles.Add(t);
                    int index = i;
                    AddButton(t, tile, tileH + labelH, () => Apply(index), null, true); // зона нажатия — с подписью
                    var lab = Text("Label_" + i, _menu, _palette[i].label.Replace("\n", " "), tile, labelH * 0.8f);
                    lab.localPosition = new Vector3(x, ty - tileH * 0.5f - labelH * 0.5f, -0.001f);
                    lab.GetComponent<TextMeshPro>().color = ColDim;
                }
                int rows = Mathf.CeilToInt(_palette.Count / (float)cols);
                y -= rows * rowStep - g;
                break;
            }

            case Tab.Measure:
                if (measure != null)
                {
                    float bigH = H * 1.35f;
                    _fieldHighlight = RoundedQuad("FieldHighlight", _menu, half + 0.006f, bigH + 0.006f, 0.008f, ColAccent);
                    _widthBtn = PanelButton("WidthBtn", new Vector3(xl, y - bigH * 0.5f, 0), half, bigH, out _widthText,
                        () => BeginManualInput(DistoMeasure.Field.Width), 0.62f).t;
                    _heightBtn = PanelButton("HeightBtn", new Vector3(xr, y - bigH * 0.5f, 0), half, bigH, out _heightText,
                        () => BeginManualInput(DistoMeasure.Field.Height), 0.62f).t;
                    y -= bigH + g;
                    if (_manualEditing) y = BuildManualKeypad(y, W, H, g);
                    else
                    {
                        PanelButton("ConnectBtn", new Vector3(xl, y - H * 0.5f, 0), half, H, out var connectText, () => measure.Connect());
                        connectText.text = "Подключить DISTO";
                        PanelButton("ResetBtn", new Vector3(xr, y - H * 0.5f, 0), half, H, out var resetText, () => measure.ResetValues());
                        resetText.text = "Сбросить";
                        y -= H + g;
                    }
                }
                if (builder != null && !_manualEditing)
                {
                    PanelButton("FitBtn", new Vector3(0, y - H * 0.5f, 0), W, H, out _fitText, FitToOpening);
                    _fitText.text = "Вписать ворота в проём";
                    y -= H;
                }
                break;

            case Tab.Opening:
                if (builder != null)
                {
                    PanelButton("FitBtn", new Vector3(0, y - H * 0.5f, 0), W, H, out _fitText, FitToOpening);
                    _fitText.text = "Вписать ворота в проём";
                    y -= H + g;
                }
                // Ручная разметка — для высоких проёмов, которые сканер Quest не видит целиком
                if (marker != null)
                {
                    PanelButton("MarkBtn", new Vector3(xl, y - H * 0.5f, 0), half, H, out _markText, () => marker.Toggle());
                    PanelButton("ClearMarksBtn", new Vector3(xr, y - H * 0.5f, 0), half, H, out var clearText,
                        () => { marker.Cancel(); if (openings != null) openings.ClearManual(); SetStatus("ручная разметка удалена"); });
                    clearText.text = "Убрать разметку";
                    y -= H + g;
                }
                // Пересканировать комнату (новый объект, недосканированный верх проёма)
                if (openings != null)
                {
                    PanelButton("ScanBtn", new Vector3(0, y - H * 0.5f, 0), W, H, out var scanText, () =>
                    {
                        if (openings.Scanning) return;
                        if (marker != null) marker.Cancel();
                        SetStatus("сканирование пространства…");
                        openings.RescanSpace(SetStatus);
                    });
                    scanText.text = "Сканировать пространство";
                    y -= H;
                }
                break;
        }

        // Строка статуса — на всех вкладках
        y -= g * 1.5f;
        float statusH = H * 0.42f;
        _statusText = Text("Status", _menu, "", W, statusH).GetComponent<TextMeshPro>();
        _statusText.color = ColDim;
        _statusText.transform.localPosition = new Vector3(0, y - statusH * 0.5f, -0.001f);
        y -= statusH;

        // Центрируем содержимое и подкладываем фон
        float pad = 0.012f;
        float centerY = y * 0.5f;
        foreach (Transform child in _menu) child.localPosition -= new Vector3(0, centerY, 0);
        var bg = RoundedQuad("Background", _menu, W + pad * 2f, -y + pad * 2f, 0.014f, ColBg);
        bg.localPosition = new Vector3(0, 0, 0.002f);
        _menuHalf = new Vector2(W * 0.5f + pad, -y * 0.5f + pad);
    }

    void UpdateHeader()
    {
        bool has = _selected >= 0 && _selected < _palette.Count;
        if (_headText != null)
            _headText.text = "Цвет полотна: " + (has ? _palette[_selected].label.Replace("\n", " ") : "—");
        if (_swatch != null)
        {
            _swatch.gameObject.SetActive(has && _palette[_selected].tile != null);
            if (has && _palette[_selected].tile != null) _swatch.sharedMaterial = _palette[_selected].tile;
        }
    }

    // Пересобрать меню (другая вкладка): положение и видимость сохраняются
    void RebuildMenu()
    {
        if (_tab != Tab.Measure) _manualEditing = false;
        _manualDraftText = null;
        _portalToggleText = _portalWorldText = null;
        Vector3 pos = _menu != null ? _menu.position : Vector3.zero;
        Quaternion rot = _menu != null ? _menu.rotation : Quaternion.identity;
        string status = _statusText != null ? _statusText.text : "";
        if (_menu != null) Destroy(_menu.gameObject);
        _tiles.Clear();
        _buttons.Clear();
        _hover = _pressed = null;
        _openText = _fitText = _windowsText = _wicketText = _markText = _headText = _placementText = null;
        _sectionalTypeText = _rollerTypeText = _rollerClearText = null;
        _swatch = null; _highlight = null; _fieldHighlight = null; _widthBtn = _heightBtn = null;
        _widthText = _heightText = _statusText = null;
        BuildMenu();
        _menu.SetPositionAndRotation(pos, rot);
        _menu.gameObject.SetActive(_menuVisible);
        _pokeArmed = false; // тот же тычок не должен нажать кнопку на новом месте
        SetStatus(status);
        RefreshMeasure();
        OnMarkerChanged();
        UpdateHighlight();
        UpdateButtonTexts();
    }

    class Button
    {
        public Transform t;          // плашка/плитка (зона нажатия — вокруг её центра)
        public Vector2 half;
        public Action press;
        public Material mat;         // свой материал плашки (подсветка); у плиток цвета — null
        public Color baseColor;
        public Transform label;
        public bool tile;
        public Vector3 restPos, labelRest;
        public void SetColor(Color c) { if (mat != null) SetUnlitColor(mat, c); }
    }

    Button PanelButton(string name, Vector3 pos, float w, float h, out TextMeshPro label, Action press, float textFrac = 0.4f)
    {
        var q = RoundedQuad(name, _menu, w, h, Mathf.Min(0.008f, h * 0.3f), ColButton);
        q.localPosition = pos;
        var t = Text(name + "Label", _menu, "", w * 0.88f, h * textFrac);
        t.localPosition = pos + new Vector3(0, 0, -0.001f);
        label = t.GetComponent<TextMeshPro>();
        return AddButton(q, w, h, press, t, false);
    }

    Button AddButton(Transform t, float w, float h, Action press, Transform label, bool tile)
    {
        var r = t.GetComponent<Renderer>();
        var b = new Button
        {
            t = t, half = new Vector2(w * 0.5f, h * 0.5f), press = press, label = label, tile = tile,
            mat = tile ? null : r.sharedMaterial, baseColor = ColButton,
        };
        _buttons.Add(b);
        return b;
    }

    void RefreshMeasure()
    {
        if (measure == null) return;
        if (_widthText != null) _widthText.text = "Ширина\n" + DistoMeasure.Format(measure.Width);
        if (_heightText != null) _heightText.text = "Высота\n" + DistoMeasure.Format(measure.Height);
        if (_statusText != null && !_manualEditing && !string.IsNullOrEmpty(measure.Status)) _statusText.text = measure.Status;
        if (_fieldHighlight != null && _widthBtn != null && _heightBtn != null)
        {
            var field = _manualEditing ? _manualField : measure.Next;
            var target = field == DistoMeasure.Field.Width ? _widthBtn : _heightBtn;
            _fieldHighlight.localPosition = target.localPosition + new Vector3(0, 0, 0.001f);
        }
    }

    // Скруглённый прямоугольник (меш кэшируется по размеру) с неосвещённым материалом
    static readonly Dictionary<Vector3, Mesh> _roundMeshes = new Dictionary<Vector3, Mesh>();

    Transform RoundedQuad(string name, Transform parent, float w, float h, float r, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = RoundedMesh(w, h, r);
        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        var m = TileMaterial(color);
        if (m != null) mr.sharedMaterial = m;
        return go.transform;
    }

    static Mesh RoundedMesh(float w, float h, float r, float uScale = 1f)
    {
        var key = new Vector3(Mathf.Round(w * 10000f), Mathf.Round(h * 10000f), Mathf.Round(r * 10000f) + (uScale < 1f ? 0.5f : 0f));
        if (_roundMeshes.TryGetValue(key, out var cached) && cached != null) return cached;
        r = Mathf.Min(r, w * 0.5f, h * 0.5f);
        const int seg = 6;
        var verts = new List<Vector3> { Vector3.zero };
        var corners = new[] { new Vector2(w * 0.5f - r, h * 0.5f - r), new Vector2(-w * 0.5f + r, h * 0.5f - r),
                              new Vector2(-w * 0.5f + r, -h * 0.5f + r), new Vector2(w * 0.5f - r, -h * 0.5f + r) };
        for (int c = 0; c < 4; c++)
            for (int i = 0; i <= seg; i++)
            {
                float a = (c * 90f + i * 90f / seg) * Mathf.Deg2Rad;
                verts.Add(new Vector3(corners[c].x + Mathf.Cos(a) * r, corners[c].y + Mathf.Sin(a) * r, 0f));
            }
        var tris = new List<int>();
        int n = verts.Count - 1;
        for (int i = 1; i <= n; i++) { tris.Add(0); tris.Add(i % n + 1); tris.Add(i); } // лицом к -Z (к пользователю)
        var uv = new List<Vector2>();
        foreach (var v in verts) uv.Add(new Vector2((v.x / w + 0.5f) * uScale, v.y / h + 0.5f));
        var mesh = new Mesh { name = "RoundRect" };
        mesh.SetVertices(verts); mesh.SetTriangles(tris, 0); mesh.SetUVs(0, uv);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        _roundMeshes[key] = mesh;
        return mesh;
    }

    static void SetUnlitColor(Material m, Color c) => SetColor(m, c);

    // Короткий щелчок для подтверждения нажатия
    static AudioClip _clickClip;
    static AudioClip ClickClip()
    {
        if (_clickClip != null) return _clickClip;
        const int rate = 44100; int n = rate * 35 / 1000;
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            data[i] = Mathf.Sin(2f * Mathf.PI * 2200f * t) * Mathf.Exp(-t * 160f) * 0.5f;
        }
        _clickClip = AudioClip.Create("MenuClick", n, 1, rate, false);
        _clickClip.SetData(data, 0);
        return _clickClip;
    }

    static Transform Quad(string name, Transform parent, Material mat, Color fallback)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
        Destroy(q.GetComponent<Collider>());
        q.name = name;
        q.SetParent(parent, false);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader != null) { mat = new Material(shader); mat.SetColor("_BaseColor", fallback); }
        }
        if (mat != null) q.GetComponent<Renderer>().sharedMaterial = mat;
        return q;
    }

    // Подписи меню: размер шрифта подбираем сами (одна строка по ширине/высоте) —
    // встроенный авторазмер TMP после смены текста застревал на мелком кегле
    readonly List<(TextMeshPro tmp, string fitted)> _labels = new List<(TextMeshPro, string)>();
    const float LabelFontMax = 0.3f;

    Transform Text(string name, Transform parent, string text, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshPro>();
        if (MenuFont != null) tmp.font = MenuFont;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = ColText;
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
        _highlight.gameObject.SetActive(on);
        if (on) _highlight.localPosition = _tiles[_selected].localPosition + new Vector3(0, 0, 0.0008f);
    }

    void SetMenuVisible(bool v)
    {
        _menuVisible = v;
        if (_menu) _menu.gameObject.SetActive(v);
        if (!v) { _hover = _pressed = null; _pinned = false; }
        if (v) UpdateHighlight();
        if (debugLogs) Debug.Log($"GateColorPicker: menu {(v ? "shown" : "hidden")}");
    }

    // ---------- update ----------

    void LateUpdate() => FitChangedLabels(); // тексты кнопок/статуса меняются из Update и событий

    float _palmUpTime, _hideTime;
    bool _pinned;

    void Update()
    {
        if (debugApply >= 0) { Apply(debugApply); debugApply = -1; }
        if (debugTab >= 0 && _menu != null)
        {
            _tab = (Tab)Mathf.Clamp(debugTab, 0, 3); debugTab = -1; RebuildMenu(); _menu.gameObject.SetActive(true);
            var cam = Camera.main; // в редакторе — перед камерой, в стороне от моделей контроллеров
            if (cam != null) _menu.SetPositionAndRotation(cam.transform.position + cam.transform.forward * 0.6f + Vector3.up * 0.5f,
                Quaternion.LookRotation(cam.transform.forward, Vector3.up));
        }
        if (_menu == null || _palette.Count == 0) return;
        if (!EnsureRefs()) { if (_menuVisible) SetMenuVisible(false); return; }

        bool leftOk = _left.Tracked;
        Vector3 wrist = leftOk ? _left.wrist.position : Vector3.zero;
        Vector3 palm = leftOk ? (wrist + _left.middleKnuckle.position) * 0.5f : Vector3.zero;
        // Нормаль из ладони левой руки (для правой знак был бы обратный)
        Vector3 normal = leftOk ? Vector3.Cross(_left.indexKnuckle.position - wrist, _left.pinkyKnuckle.position - wrist).normalized : Vector3.zero;
        float facing = leftOk ? Vector3.Dot(normal, (_head.position - palm).normalized) : -1f;

        if (!_menuVisible)
        {
            // ладонь к лицу чуть дольше мгновения — меню появляется над ней (не мигает при случайном повороте руки)
            _palmUpTime = facing > showDot ? _palmUpTime + Time.deltaTime : 0f;
            if (_palmUpTime < 0.15f) return;
            SetMenuVisible(true);
            PlaceAtPalm(palm, normal, 1f);
            _hideTime = 0f;
        }

        // Палец правой руки рядом с меню — меню замирает в воздухе, чтобы не дрожало вместе с ладонью
        bool fingerNear = false;
        if (_right.Tracked)
        {
            Vector3 p = _menu.InverseTransformPoint(_right.indexTip.position);
            fingerNear = Mathf.Abs(p.x) < _menuHalf.x + 0.07f && Mathf.Abs(p.y) < _menuHalf.y + 0.07f && p.z > -0.18f && p.z < 0.08f;
        }
        bool palmUp = facing > hideDot;
        if (fingerNear) _pinned = true;
        else if (palmUp) _pinned = false;
        if (!_pinned && palmUp) PlaceAtPalm(palm, normal, 1f - Mathf.Exp(-22f * Time.deltaTime));

        // Убрать: ладонь опущена и палец далеко, или меню ушло из поля зрения
        Vector3 toMenu = _menu.position - _head.position;
        bool outOfView = Vector3.Angle(_head.forward, toMenu) > 65f || toMenu.magnitude > 1.2f;
        bool idle = (!palmUp && !fingerNear) || outOfView;
        _hideTime = idle ? _hideTime + Time.deltaTime : 0f;
        if (_hideTime > 0.6f) { SetMenuVisible(false); return; }

        if (_fitText != null)
        {
            var op = openings != null ? openings.Selected : null;
            _fitText.text = op != null ? $"Вписать в проём {op.WidthM * 1000:0}×{op.HeightM * 1000:0}" : "Вписать ворота в проём";
        }
        UpdateButtonTexts();
        Poke();
    }

    // Меню над ладонью, развёрнуто к голове, без крена (k — доля сглаживания)
    void PlaceAtPalm(Vector3 palm, Vector3 normal, float k)
    {
        Vector3 pos = palm + normal * palmOffset + Vector3.up * (_menuHalf.y * 0.6f);
        Vector3 fwd = pos - _head.position; // +Z меню смотрит от пользователя
        fwd.y *= 0.5f;                      // меньше наклон — текст читается ровнее
        Quaternion rot = fwd.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(fwd, Vector3.up) : _menu.rotation;
        _menu.SetPositionAndRotation(Vector3.Lerp(_menu.position, pos, k), Quaternion.Slerp(_menu.rotation, rot, k));
    }

    void SelectGateType(GateBuilder.GateKind kind)
    {
        if (builder == null) return;
        builder.SetGateType(kind);
        SetStatus(kind == GateBuilder.GateKind.Roller ? "Роллетные ворота" : "Секционные ворота");
        UpdateButtonTexts();
    }

    void TogglePlacementLock()
    {
        if (_mover == null) return;
        _mover.SetPlacementLocked(!_mover.IsPlacementLocked);
        SetStatus(_mover.IsPlacementLocked ? "Положение ворот зафиксировано" : "Жесты перемещения, поворота и масштаба включены");
        UpdateButtonTexts();
    }

    void UpdateButtonTexts()
    {
        RefreshPortalButtons();
        if (_rollerClearText != null && builder != null)
            _rollerClearText.text = $"Под коробом {builder.RollerClearHeight * 1000:0} мм";
        if (_sectionalTypeText != null && builder != null)
            _sectionalTypeText.color = !builder.IsRoller ? ColAccent : ColDim;
        if (_rollerTypeText != null && builder != null)
            _rollerTypeText.color = builder.IsRoller ? ColAccent : ColDim;
        if (_placementText != null && _mover != null)
            _placementText.text = _mover.IsPlacementLocked ? "Разблокировать положение" : "Зафиксировать положение";
        if (_openText != null && builder != null)
            _openText.text = builder.WicketOpen ? "Закройте калитку" : builder.OpenOrOpening ? "Закрыть ворота" : "Открыть ворота";
        if (_windowsText != null && builder != null)
            _windowsText.text = builder.windowsEnabled ? "Окна: есть" : "Окна: нет";
        if (_wicketText != null && builder != null)
        {
            _wicketText.text = !builder.WicketAllowed ? $"Калитка: от {builder.wicketMinOpeningWidth * 1000:0}×{builder.wicketMinOpeningHeight * 1000:0}"
                : builder.wicketEnabled ? "Калитка: есть" : "Калитка: нет";
            _wicketText.color = builder.WicketAllowed ? ColText : ColDim;
        }
    }

    // ---------- нажатие пальцем ----------
    // Наведение: палец перед кнопкой (до hoverDistance) — она подсвечивается; из нескольких — ближайшая к центру.
    // Нажатие: палец дошёл почти до плоскости кнопки (pressDepth) — действие сразу, кнопка «проваливается».
    // Повторное нажатие — только после отвода пальца на releaseDistance перед меню.

    Button _hover, _pressed;
    AudioSource _click;

    void Poke()
    {
        if (!_right.Tracked) { SetHover(null); CancelPress(); _pokeArmed = true; return; }
        Vector3 p = _menu.InverseTransformPoint(_right.indexTip.position);

        if (_pressed != null)
        {
            // кнопка уже сработала — держим её «нажатой», пока палец не отведён или не ушёл в сторону
            bool still = Mathf.Abs(p.x - _pressed.restPos.x) < _pressed.half.x * 1.25f && Mathf.Abs(p.y - _pressed.restPos.y) < _pressed.half.y * 1.25f;
            if (!still || p.z < -releaseDistance || p.z > 0.12f) CancelPress();
            return;
        }

        Button over = null; float best = float.MaxValue;
        if (p.z > -hoverDistance && p.z < 0.04f)
            foreach (var b in _buttons)
            {
                if (b.t == null) continue;
                Vector3 t = b.t.localPosition;
                float dx = Mathf.Abs(p.x - t.x) / b.half.x, dy = Mathf.Abs(p.y - t.y) / b.half.y;
                if (dx >= 1f || dy >= 1f) continue;
                float d = Mathf.Max(dx, dy);
                if (d < best) { best = d; over = b; }
            }
        SetHover(over);

        if (p.z < -releaseDistance) _pokeArmed = true; // палец перед меню — можно нажимать снова
        if (over != null && _pokeArmed && p.z > -pressDepth && Time.time >= _nextPoke)
        {
            // срабатывает сразу при касании (без ожидания отвода пальца) — отклик мгновенный
            _pokeArmed = false;
            _nextPoke = Time.time + pokeCooldown;
            StartPress(over);
            if (_click) _click.Play();
            over.press?.Invoke(); // может пересобрать меню (вкладка) — тогда _pressed сбросится в RebuildMenu
        }
    }

    void SetHover(Button b)
    {
        if (_hover == b) return;
        if (_hover != null && _hover != _pressed) Visual(_hover, 0);
        _hover = b;
        if (_hover != null) Visual(_hover, 1);
    }

    void StartPress(Button b)
    {
        _pressed = b;
        b.restPos = b.t.localPosition;
        if (b.label) b.labelRest = b.label.localPosition;
        Visual(b, 2);
    }

    void CancelPress()
    {
        if (_pressed == null) return;
        var b = _pressed;
        _pressed = null;
        if (b.t) b.t.localPosition = b.restPos;
        if (b.label) b.label.localPosition = b.labelRest;
        Visual(b, b == _hover ? 1 : 0);
    }

    // 0 — обычная, 1 — под пальцем, 2 — нажата
    void Visual(Button b, int state)
    {
        if (b.t == null) return;
        if (b.tile)
        {
            float s = state == 0 ? 1f : 1.06f;
            b.t.localScale = new Vector3(s, s, 1f);
        }
        else b.SetColor(state == 0 ? b.baseColor : state == 1 ? Color.Lerp(b.baseColor, ColHover, 0.8f) : ColAccent);
        if (state == 2)
        {
            b.t.localPosition = b.restPos + new Vector3(0, 0, 0.004f);
            if (b.label) b.label.localPosition = b.labelRest + new Vector3(0, 0, 0.004f);
        }
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
        foreach (var entry in _palette)
        {
            if (entry.adaptive != null) Destroy(entry.adaptive);
            if (entry.roller != null) Destroy(entry.roller);
        }
    }
}
