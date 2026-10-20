using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Смена цвета полотна ворот через меню на левой ладони.
///  • Повернуть левую ладонь к лицу — над ней появляется палитра (плитки с материалами панелей).
///  • Ткнуть в плитку указательным пальцем правой руки — лицевые панели ворот получают этот материал.
/// Палитра — список swatches: RAL задаются цветом (sRGB), дерево — материалом из модели
/// (ищется по имени среди материалов ворот, в т.ч. на «держателе» Sphere).
/// Цели — все рендереры ворот со слотом материала targetMaterialName.
/// </summary>
public class GateColorPicker : MonoBehaviour
{
    [System.Serializable]
    public class Swatch
    {
        public string label;
        [Tooltip("Цвет RAL в sRGB (для текстурных материалов не используется)")]
        public Color color = Color.white;
        [Tooltip("Если задано — берётся материал модели с этим именем (дерево и т.п.), цвет игнорируется")]
        public string materialName;
        [Tooltip("Матовая структура «мороз»")]
        public bool frost;

        public Swatch(string label, string hex, bool frost = false, string materialName = null)
        {
            this.label = label;
            ColorUtility.TryParseHtmlString(hex, out color);
            this.frost = frost;
            this.materialName = materialName;
        }
    }

    [Header("Palette")]
    [Tooltip("Справочные sRGB-значения RAL Classic — при необходимости сверить с каталогом производителя")]
    public List<Swatch> swatches = new List<Swatch>
    {
        new Swatch("RAL 9003", "#F4F4F4"),
        new Swatch("RAL 9006", "#A5A5A5"),
        new Swatch("RAL 7004", "#969992"),
        new Swatch("RAL 7016", "#293133"),
        new Swatch("RAL 7016\nмороз", "#293133", true),
        new Swatch("RAL 8017", "#45322E"),
        new Swatch("RAL 8017\nмороз", "#45322E", true),
        new Swatch("RAL 3005", "#5E2129"),
        new Swatch("RAL 6005", "#2F4538"),
        new Swatch("RAL 5005", "#154889"),
        new Swatch("RAL 1014", "#DDC49A"),
        new Swatch("Золотой дуб", "#FFFFFF", false, "панель_золотой дуб"),
        new Swatch("Венге", "#FFFFFF", false, "панель_венге"),
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

    [Header("Poke")]
    [Tooltip("Глубина зоны нажатия перед/за плиткой, м")]
    public float pokeDepth = 0.015f;
    public float pokeCooldown = 0.4f;

    [Header("Debug")]
    public bool debugLogs = false;

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

    class Entry { public string label; public Material gate; public Material tile; }
    readonly List<Entry> _palette = new List<Entry>();
    readonly List<(Renderer r, int slot)> _targets = new List<(Renderer, int)>();

    Transform _head;
    Transform _menu;
    readonly List<Transform> _tiles = new List<Transform>();
    Transform _highlight;
    int _selected = -1;

    bool _menuVisible;
    bool _pokeArmed = true;
    float _nextPoke;
    float _nextSearch;

    void Start()
    {
        CollectMaterials();
        BuildMenu();
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

        foreach (var sw in swatches)
        {
            var e = new Entry { label = sw.label.Replace("\\n", "\n") };
            if (!string.IsNullOrEmpty(sw.materialName))
            {
                if (!byName.TryGetValue(sw.materialName, out e.gate))
                {
                    Debug.LogWarning($"GateColorPicker: материал '{sw.materialName}' не найден в модели", this);
                    continue;
                }
                e.tile = e.gate;
            }
            else
            {
                e.gate = new Material(baseMat) { name = "Gate_" + sw.label };
                SetColor(e.gate, sw.color);
                SetRoughness(e.gate, sw.frost ? 0.95f : 0.55f);
                e.tile = TileMaterial(sw.color);
            }
            _palette.Add(e);
        }
        if (debugLogs) Debug.Log($"GateColorPicker: palette={_palette.Count} targets={_targets.Count} base={baseMat.shader.name}");
    }

    // glTFast-шейдеры: baseColorFactor / roughnessFactor; URP Lit: _BaseColor / _Smoothness
    static void SetColor(Material m, Color c)
    {
        if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", c);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    static void SetRoughness(Material m, float roughness)
    {
        if (m.HasProperty("roughnessFactor")) m.SetFloat("roughnessFactor", roughness);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 1f - roughness);
        if (m.HasProperty("metallicFactor")) m.SetFloat("metallicFactor", 0f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
    }

    // Плитка — неосвещённый образец точного цвета
    Material TileMaterial(Color c)
    {
        Material m = backgroundMaterial != null ? new Material(backgroundMaterial)
                   : Shader.Find("Universal Render Pipeline/Unlit") is Shader sh ? new Material(sh) : null;
        if (m != null) SetColor(m, c);
        return m;
    }

    // Unity может добавить " (Instance)" к имени материала
    static string CleanName(string n) => n.Replace(" (Instance)", "").Trim();

    public void Apply(int index)
    {
        if (index < 0 || index >= _palette.Count) return;
        var mat = _palette[index].gate;
        foreach (var (r, slot) in _targets)
        {
            var mats = r.sharedMaterials;
            mats[slot] = mat;
            r.sharedMaterials = mats;
        }
        _selected = index;
        UpdateHighlight();
        if (debugLogs) Debug.Log($"GateColorPicker: applied {_palette[index].label}");
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
        float height = rows * rowStep;

        var bg = Quad("Background", _menu, backgroundMaterial, new Color(0.08f, 0.08f, 0.1f, 0.85f));
        bg.localScale = new Vector3(width + 0.02f, height + 0.035f, 1f);
        bg.localPosition = new Vector3(0, 0.0075f, 0.002f);

        var title = Text("Title", _menu, "Цвет полотна", width, 0.015f);
        title.localPosition = new Vector3(0, height * 0.5f + 0.003f, 0);

        _highlight = Quad("Highlight", _menu, highlightMaterial, Color.white);
        _highlight.localScale = new Vector3(tileSize + 0.006f, tileSize + 0.006f, 1f);
        _highlight.gameObject.SetActive(false);

        for (int i = 0; i < _palette.Count; i++)
        {
            int c = i % columns, row = i / columns;
            float x = -width * 0.5f + tileSize * 0.5f + c * step;
            float y = height * 0.5f - tileSize * 0.5f - row * rowStep - 0.012f;

            var tile = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            Destroy(tile.GetComponent<Collider>());
            tile.name = "Tile_" + i;
            tile.SetParent(_menu, false);
            tile.localPosition = new Vector3(x, y, 0);
            tile.localScale = new Vector3(tileSize, tileSize, 1f);
            if (_palette[i].tile != null) tile.GetComponent<Renderer>().sharedMaterial = _palette[i].tile;
            _tiles.Add(tile);

            var label = Text("Label_" + i, _menu, _palette[i].label, step * 0.95f, labelH);
            label.localPosition = new Vector3(x, y - tileSize * 0.5f - labelH * 0.5f, 0);
        }
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

    static Transform Text(string name, Transform parent, string text, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = Color.white;
        tmp.enableAutoSizing = true;
        tmp.fontSizeMin = 0.01f;
        tmp.fontSizeMax = 0.15f;
        tmp.rectTransform.sizeDelta = new Vector2(w, h);
        return go.transform;
    }

    void UpdateHighlight()
    {
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

    void Update()
    {
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

        // Меню над ладонью, развёрнуто к голове, без крена
        Vector3 pos = palm + normal * palmOffset;
        _menu.position = pos;
        Vector3 fwd = pos - _head.position; // +Z меню смотрит от пользователя
        if (fwd.sqrMagnitude > 1e-6f) _menu.rotation = Quaternion.LookRotation(fwd, Vector3.up);

        Poke();
    }

    void Poke()
    {
        if (!_right.Tracked) { _pokeArmed = true; return; }

        Vector3 p = _menu.InverseTransformPoint(_right.indexTip.position);
        int hit = -1;
        float half = tileSize * 0.5f;
        for (int i = 0; i < _tiles.Count; i++)
        {
            Vector3 t = _tiles[i].localPosition;
            if (Mathf.Abs(p.x - t.x) < half && Mathf.Abs(p.y - t.y) < half && Mathf.Abs(p.z) < pokeDepth) { hit = i; break; }
        }

        if (hit < 0) { _pokeArmed = true; return; }
        if (!_pokeArmed || Time.time < _nextPoke) return;

        _pokeArmed = false;
        _nextPoke = Time.time + pokeCooldown;
        Apply(hit);
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
        if (_menu != null) Destroy(_menu.gameObject);
    }
}
