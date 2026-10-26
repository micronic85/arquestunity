using System.Collections;
using Meta.XR.EnvironmentDepth;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

/// <summary>
/// Освещение для смешанной реальности:
///  • ровный «комнатный» окружающий свет и отражения вместо стандартного неба Unity (панели не голубеют);
///  • основной свет — тёплый, сверху, как от потолка; тень ворот на реальный пол (ShadowCatcher);
///  • Depth API: реальные предметы и руки перекрывают ворота (материалы → GateLitOccluded);
///  • подстройка яркости и оттенка под комнату по камере очков (Passthrough Camera API, Quest 3/3S).
/// </summary>
[DefaultExecutionOrder(-200)]
public class MrLighting : MonoBehaviour
{
    [Header("Шейдеры (ссылки нужны, чтобы попали в сборку)")]
    public Shader gateShader;
    public Shader shadowCatcherShader;

    [Header("Основной свет")]
    public Light mainLight;
    [Tooltip("Выключить прочие (точечные/прожекторные) источники сцены")]
    public bool disableOtherLights = true;
    public Color lightColor = new Color(1f, 0.97f, 0.93f);
    public float lightIntensity = 1.1f;
    [Tooltip("Направление света в осях ворот (+X — в комнату): свет падает из комнаты сверху, как от потолочной лампы")]
    public Vector3 lightDirectionInGate = new Vector3(-0.6f, -0.7f, 0.25f);
    [Tooltip("Если ворот нет — направление в мировых осях (градусы)")]
    public Vector3 lightEuler = new Vector3(68f, 35f, 0f);
    [Range(0f, 1f)] public float shadowStrength = 0.75f;

    [Header("Окружение (потолок / стены / пол)")]
    public Color ceiling = new Color(0.78f, 0.77f, 0.75f);
    public Color walls = new Color(0.50f, 0.50f, 0.49f);
    public Color floor = new Color(0.22f, 0.21f, 0.20f);
    public float ambientIntensity = 1f;
    [Range(0f, 1f)] public float reflectionIntensity = 0.7f;
    [Tooltip("Минимальная шероховатость материалов ворот: без «пластикового» блеска")]
    [Range(0f, 1f)] public float roughnessMin = 0.3f;

    [Header("Тень на полу")]
    public bool floorShadow = true;
    [Tooltip("Объект, за которым ездит тень (ворота)")]
    public Transform shadowTarget;
    [Range(0f, 1f)] public float floorShadowStrength = 0.55f;
    public float floorShadowSize = 9f;

    [Header("Перекрытие реальными предметами (Depth API)")]
    public bool depthOcclusion = true;
    [Tooltip("Руки тоже перекрывают ворота")]
    public bool handsOcclude = true;
    [Tooltip("Мягкие края перекрытия — красивее, но заметно дороже для GPU очков")]
    public bool softOcclusion = false;

    [Header("Подстройка по камере очков")]
    public bool cameraAdaptation = true;
    [Tooltip("Яркость кадра камеры, при которой свет = номинальный")]
    public float referenceLuminance = 0.38f;
    public Vector2 brightnessRange = new Vector2(0.45f, 1.4f);
    [Range(0f, 1f)] public float tintAmount = 0.5f;
    public float adaptSpeed = 1.5f;

    public bool debugLogs;

    public float Brightness { get; private set; } = 1f;
    public Color Tint { get; private set; } = Color.white;

    Transform _shadowPlane;
    Cubemap _envCube;
    ReflectionProbe _probe;
    float _targetBrightness = 1f;
    Color _targetTint = Color.white;
    float _nextLog;

#if UNITY_EDITOR
    // Ссылки на шейдеры подставляются сами — по ним шейдеры попадают в сборку
    void OnValidate()
    {
        if (gateShader == null) gateShader = Shader.Find("VorotaKraft/GateLitOccluded");
        if (shadowCatcherShader == null) shadowCatcherShader = Shader.Find("VorotaKraft/ShadowCatcher");
    }
#endif

    void Awake()
    {
        GateMaterials.Shader = gateShader;
        GateMaterials.RoughnessMin = roughnessMin;
        if (mainLight == null)
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional && l.enabled) { mainLight = l; break; }
        if (mainLight == null)
        {
            // В сцене «Directional Light» на деле точечный: давал пятно-блик и не давал теней. Свой направленный свет.
            var go = new GameObject("MrMainLight");
            go.transform.SetParent(transform, false);
            mainLight = go.AddComponent<Light>();
            mainLight.type = LightType.Directional;
        }
        if (disableOtherLights)
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l != mainLight && l.type != LightType.Directional) l.enabled = false;
        RenderSettings.sun = mainLight;
        SetupEnvironment();
        ApplyLight();
    }

    void Start()
    {
        if (shadowTarget == null) { var gb = FindFirstObjectByType<GateBuilder>(); if (gb) shadowTarget = gb.transform; }
        if (floorShadow && shadowCatcherShader != null) CreateShadowPlane();
        if (depthOcclusion) SetupDepth();
        if (cameraAdaptation) StartCoroutine(CameraAdaptation());
    }

    // ---------- окружение ----------

    Color Env(Vector3 d)
    {
        float y = d.y;
        Color c = y >= 0 ? Color.Lerp(walls, ceiling, Mathf.SmoothStep(0f, 1f, y)) : Color.Lerp(walls, floor, Mathf.SmoothStep(0f, 1f, -y));
        // мягкое пятно светильника на потолке
        float lamp = Mathf.Pow(Mathf.Max(0f, y), 24f);
        return c + new Color(1f, 0.97f, 0.9f) * lamp * 1.2f;
    }

    void SetupEnvironment()
    {
        // Окружающий свет: три цвета + явная SH-проба (на случай, если движок не пересчитает сам)
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ceiling * ambientIntensity;
        RenderSettings.ambientEquatorColor = walls * ambientIntensity;
        RenderSettings.ambientGroundColor = floor * ambientIntensity;
        var sh = new SphericalHarmonicsL2();
        sh.AddAmbientLight(walls * ambientIntensity);
        sh.AddDirectionalLight(Vector3.up, (ceiling - walls) * ambientIntensity, 1f);
        sh.AddDirectionalLight(Vector3.down, (floor - walls) * ambientIntensity, 1f);
        RenderSettings.ambientProbe = sh;

        // Отражения: маленькая кубическая карта «комнаты»
        const int N = 32;
        _envCube = new Cubemap(N, TextureFormat.RGBA32, true) { name = "MrRoomEnv" };
        var px = new Color[N * N];
        for (int f = 0; f < 6; f++)
        {
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float sc = 2f * (x + 0.5f) / N - 1f, tc = 2f * (y + 0.5f) / N - 1f;
                    Vector3 d = f switch
                    {
                        0 => new Vector3(1f, -tc, -sc),
                        1 => new Vector3(-1f, -tc, sc),
                        2 => new Vector3(sc, 1f, tc),
                        3 => new Vector3(sc, -1f, -tc),
                        4 => new Vector3(sc, -tc, 1f),
                        _ => new Vector3(-sc, -tc, -1f),
                    };
                    px[y * N + x] = Env(d.normalized);
                }
            _envCube.SetPixels(px, (CubemapFace)f);
        }
        _envCube.Apply(true, false);

        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = _envCube;
        RenderSettings.reflectionIntensity = reflectionIntensity;

        var go = new GameObject("MrReflectionProbe");
        go.transform.SetParent(transform, false);
        _probe = go.AddComponent<ReflectionProbe>();
        _probe.mode = ReflectionProbeMode.Custom;
        _probe.customBakedTexture = _envCube;
        _probe.size = new Vector3(200f, 200f, 200f);
        _probe.boxProjection = false;
        _probe.importance = 0;
        _probe.intensity = reflectionIntensity;
    }

    float _appliedBrightness = -1f;
    Color _appliedTint = Color.clear;
    Quaternion _appliedRotation = Quaternion.identity;

    Quaternion CurrentLightRotation()
    {
        if (!shadowTarget) return Quaternion.Euler(lightEuler);
        var d = lightDirectionInGate;
        var cam = Camera.main;
        if (cam != null)
        {
            float side = shadowTarget.InverseTransformPoint(cam.transform.position).x;
            d.x = -Mathf.Sign(side == 0f ? 1f : side) * Mathf.Abs(d.x);
        }
        return Quaternion.LookRotation(shadowTarget.rotation * d.normalized, Vector3.up);
    }

    void ApplyLight()
    {
        if (mainLight == null) return;
        _appliedBrightness = Brightness;
        _appliedTint = Tint;
        _appliedRotation = CurrentLightRotation();
        mainLight.transform.rotation = _appliedRotation; // свет — со стороны зрителя, сверху
        Color tint = Color.Lerp(Color.white, Tint, tintAmount);
        mainLight.color = lightColor * tint;
        mainLight.intensity = lightIntensity * Brightness;
        mainLight.shadows = LightShadows.Soft;
        mainLight.shadowStrength = shadowStrength;
        mainLight.shadowBias = 0.05f;        // детали ворот тонкие: со стандартным смещением тень рвётся
        mainLight.shadowNormalBias = 0.2f;

        float a = ambientIntensity * Brightness;
        var sh = new SphericalHarmonicsL2();
        sh.AddAmbientLight(walls * tint * a);
        sh.AddDirectionalLight(Vector3.up, (ceiling - walls) * tint * a, 1f);
        sh.AddDirectionalLight(Vector3.down, (floor - walls) * tint * a, 1f);
        RenderSettings.ambientProbe = sh;
        RenderSettings.reflectionIntensity = reflectionIntensity * Mathf.Clamp(Brightness, 0.5f, 1.2f);
        if (_probe) _probe.intensity = RenderSettings.reflectionIntensity;
    }

    // ---------- тень на полу ----------

    void CreateShadowPlane()
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = "FloorShadowCatcher";
        Destroy(q.GetComponent<Collider>());
        var r = q.GetComponent<MeshRenderer>();
        var m = new Material(shadowCatcherShader);
        m.SetFloat("_Strength", floorShadowStrength);
        r.sharedMaterial = m;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = true;
        _shadowPlane = q.transform;
        _shadowPlane.rotation = Quaternion.Euler(90f, 0f, 0f);
        _shadowPlane.localScale = new Vector3(floorShadowSize, floorShadowSize, 1f);
    }

    float FloorY()
    {
        var room = Meta.XR.MRUtilityKit.MRUK.Instance != null ? Meta.XR.MRUtilityKit.MRUK.Instance.GetCurrentRoom() : null;
        if (room != null && room.FloorAnchors != null && room.FloorAnchors.Count > 0)
        {
            // пол, ближайший по высоте к низу ворот
            float best = float.MaxValue, y0 = shadowTarget ? shadowTarget.position.y : 0f, fy = 0f;
            foreach (var a in room.FloorAnchors)
            {
                if (a == null) continue;
                float y = a.transform.position.y;
                if (Mathf.Abs(y - y0) < best) { best = Mathf.Abs(y - y0); fy = y; }
            }
            if (best < float.MaxValue) return fy;
        }
        var gb = shadowTarget ? shadowTarget.GetComponent<GateBuilder>() : null;
        if (gb) return shadowTarget.TransformPoint(gb.modelOffset).y;
        return 0f;
    }

    void LateUpdate()
    {
        if (_shadowPlane && shadowTarget)
        {
            var p = shadowTarget.position;
            _shadowPlane.position = new Vector3(p.x, FloorY() + 0.004f, p.z);
        }

        if (cameraAdaptation)
        {
            float k = 1f - Mathf.Exp(-adaptSpeed * Time.deltaTime);
            Brightness = Mathf.Lerp(Brightness, _targetBrightness, k);
            Tint = Color.Lerp(Tint, _targetTint, k);
        }
        // Перенастраиваем свет и окружение только при заметных изменениях (обновление SH/отражений не бесплатно)
        Quaternion camSide = CurrentLightRotation();
        if (Mathf.Abs(Brightness - _appliedBrightness) > 0.01f || Mathf.Abs(Tint.r - _appliedTint.r) + Mathf.Abs(Tint.g - _appliedTint.g) + Mathf.Abs(Tint.b - _appliedTint.b) > 0.02f
            || Quaternion.Angle(camSide, _appliedRotation) > 1f)
            ApplyLight();
    }

    // ---------- Depth API ----------

    void SetupDepth()
    {
        var xr = UnityEngine.XR.Management.XRGeneralSettings.Instance;
        if (xr == null || xr.Manager == null || xr.Manager.activeLoader == null)
        {
            if (debugLogs) Debug.Log("MrLighting: XR не запущен — Depth API пропущен");
            return;
        }
        if (!EnvironmentDepthManager.IsSupported)
        {
            if (debugLogs) Debug.Log("MrLighting: Depth API не поддерживается на этом устройстве");
            return;
        }
        var dm = FindFirstObjectByType<EnvironmentDepthManager>();
        if (dm == null) dm = gameObject.AddComponent<EnvironmentDepthManager>();
        dm.OcclusionShadersMode = softOcclusion ? OcclusionShadersMode.SoftOcclusion : OcclusionShadersMode.HardOcclusion;
        dm.RemoveHands = !handsOcclude;
        if (debugLogs) Debug.Log("MrLighting: Depth API включён (мягкое перекрытие)");
    }

    // ---------- камера очков ----------

    IEnumerator CameraAdaptation()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Meta.XR.PassthroughCameraAccess.IsSupported) { if (debugLogs) Debug.Log("MrLighting: камера очков недоступна"); yield break; }
        const string perm = "horizonos.permission.HEADSET_CAMERA";
        // Не пересекаемся с запросом Bluetooth у DistoMeasure
        yield return new WaitForSeconds(4f);
        if (!Permission.HasUserAuthorizedPermission(perm)) Permission.RequestUserPermission(perm);
        float until = Time.time + 60f;
        while (!Permission.HasUserAuthorizedPermission(perm) && Time.time < until) yield return new WaitForSeconds(0.5f);
        if (!Permission.HasUserAuthorizedPermission(perm)) { if (debugLogs) Debug.Log("MrLighting: нет разрешения на камеру"); yield break; }

        var holder = new GameObject("MrPassthroughCamera");
        holder.SetActive(false);
        holder.transform.SetParent(transform, false);
        var cam = holder.AddComponent<Meta.XR.PassthroughCameraAccess>();
        cam.RequestedResolution = new Vector2Int(320, 240);
        cam.MaxFramerate = 5;
        holder.SetActive(true);

        var small = new RenderTexture(32, 24, 0, RenderTextureFormat.ARGB32) { useMipMap = false };
        var mid = new RenderTexture(128, 96, 0, RenderTextureFormat.ARGB32) { useMipMap = false };
        while (enabled)
        {
            yield return new WaitForSeconds(0.5f);
            if (!cam.IsPlaying) continue;
            var tex = cam.GetTexture();
            if (tex == null) continue;
            Graphics.Blit(tex, mid);
            Graphics.Blit(mid, small);
            var req = AsyncGPUReadback.Request(small, 0, TextureFormat.RGBA32);
            while (!req.done) yield return null;
            if (req.hasError) continue;
            var data = req.GetData<Color32>();
            double r = 0, g = 0, b = 0;
            for (int i = 0; i < data.Length; i++) { r += data[i].r; g += data[i].g; b += data[i].b; }
            float n = data.Length * 255f;
            var avg = new Color((float)(r / n), (float)(g / n), (float)(b / n));
            float lum = 0.2126f * avg.r + 0.7152f * avg.g + 0.0722f * avg.b;
            _targetBrightness = Mathf.Clamp(lum / Mathf.Max(0.01f, referenceLuminance), brightnessRange.x, brightnessRange.y);
            float mx = Mathf.Max(avg.r, Mathf.Max(avg.g, avg.b), 0.01f);
            _targetTint = new Color(avg.r / mx, avg.g / mx, avg.b / mx);
            if (debugLogs && Time.time > _nextLog)
            {
                _nextLog = Time.time + 3f;
                Debug.Log($"MrLighting: камера lum={lum:F2} rgb=({avg.r:F2},{avg.g:F2},{avg.b:F2}) → яркость {_targetBrightness:F2}");
            }
        }
#else
        yield break;
#endif
    }
}
