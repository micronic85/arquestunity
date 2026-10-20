using UnityEngine;

/// <summary>
/// Манипуляция воротами двумя руками (hand tracking).
///  • Два кулака    — перенос + поворот вокруг вертикали (масштаб не меняется).
///  • Два указательных (остальные пальцы согнуты) — масштаб вокруг середины между руками (если allowScale).
/// Жесты определяются по костям OVRSkeleton (работает и для OVR-, и для OpenXR-скелета).
/// Пока ручку ворот держат (GateController), манипуляция отключена.
/// </summary>
public class GateMover : MonoBehaviour
{
    [Header("Hands (необязательно — найдутся автоматически)")]
    public OVRHand leftHand;
    public OVRHand rightHand;
    [Tooltip("Если задан — пока держат ручку, ворота не двигаются")]
    public GateController gateController;

    [Header("Gesture")]
    [Tooltip("Палец согнут, если (кончик→запястье) / (костяшка→запястье) меньше порога")]
    public float curledRatio = 1.35f;
    [Tooltip("Палец вытянут, если отношение больше порога")]
    public float extendedRatio = 1.7f;
    [Tooltip("Сколько секунд жест должен удерживаться, чтобы включиться")]
    public float gestureHoldTime = 0.15f;

    [Header("Transform")]
    public bool allowMove = true;
    [Tooltip("Разрешить двигать по высоте (выкл. — ворота остаются на полу)")]
    public bool allowVertical = false;
    public bool allowRotate = true;
    [Tooltip("Для примерки 1:1 масштаб лучше выключить")]
    public bool allowScale = true;
    public float minScale = 0.1f;
    public float maxScale = 5f;

    [Header("Smoothing")]
    [Range(0f, 0.95f)]
    [Tooltip("0 — без сглаживания, ближе к 1 — плавнее, но с задержкой")]
    public float smoothing = 0.5f;

    [Header("Debug")]
    public bool debugLogs = false;
    [Tooltip("Раз в N секунд писать в лог состояние рук и пальцев (0 — выкл.)")]
    public float diagInterval = 1f;

    enum Mode { None, Move, Scale }

    const float HandSearchInterval = 0.5f;

    class HandBones
    {
        public OVRSkeleton skeleton;
        public Transform wrist, knuckle, indexTip, middleTip, ringTip, pinkyTip;
        public bool Ready => wrist && knuckle && indexTip && middleTip && ringTip && pinkyTip;
    }

    readonly HandBones _l = new HandBones();
    readonly HandBones _r = new HandBones();

    Mode _mode = Mode.None;
    Mode _candidate = Mode.None;
    float _gestureTimer;
    float _nextHandSearch;
    float _nextDiag;

    Vector3 _prevMid;
    float _prevDist;
    float _prevYaw;

    Vector3 _smoothL, _smoothR;
    bool _smoothInit;

    void Awake()
    {
        if (gateController == null) gateController = GetComponent<GateController>();
    }

    void Update()
    {
        bool hands = EnsureHands();
        bool bonesL = hands && EnsureBones(leftHand, _l);
        bool bonesR = hands && EnsureBones(rightHand, _r);
        Diag(hands, bonesL, bonesR);
        if (!hands || !bonesL || !bonesR) { SetMode(Mode.None); return; }

        Mode gesture = DetectGesture();
        if (HandleGrabbed()) gesture = Mode.None;

        if (gesture != _candidate) { _candidate = gesture; _gestureTimer = 0f; }
        else _gestureTimer += Time.deltaTime;

        if (gesture == Mode.None) SetMode(Mode.None);
        else if (gesture != _mode && _gestureTimer >= gestureHoldTime) SetMode(gesture);

        if (_mode != Mode.None) Apply();
    }

    bool HandleGrabbed() => gateController != null && gateController.IsHandleGrabbed;

    // ---------- hands ----------

    bool EnsureHands()
    {
        if (leftHand != null && rightHand != null) return true;
        if (Time.time < _nextHandSearch) return false;
        _nextHandSearch = Time.time + HandSearchInterval;

        foreach (var hand in FindObjectsByType<OVRHand>(FindObjectsSortMode.None))
        {
            bool? isLeft = IsLeft(hand);
            if (isLeft == true && leftHand == null) leftHand = hand;
            else if (isLeft == false && rightHand == null) rightHand = hand;
        }
        if (debugLogs) Debug.Log($"GateMover: hands L={leftHand?.name} R={rightHand?.name}");
        return leftHand != null && rightHand != null;
    }

    static bool? IsLeft(OVRHand hand)
    {
        var skeleton = hand.GetComponent<OVRSkeleton>();
        if (skeleton != null)
        {
            string t = skeleton.GetSkeletonType().ToString();
            if (t.Contains("Left")) return true;
            if (t.Contains("Right")) return false;
        }
        string n = hand.name.ToLowerInvariant();
        if (n.Contains("left")) return true;
        if (n.Contains("right")) return false;
        return null;
    }

    bool EnsureBones(OVRHand hand, HandBones b)
    {
        if (b.Ready) return true;
        if (b.skeleton == null) b.skeleton = hand.GetComponent<OVRSkeleton>();
        if (b.skeleton == null || !b.skeleton.IsInitialized || b.skeleton.Bones == null) return false;

        // Имена костей: OVR-скелет (Hand_*) или OpenXR-скелет (XRHand_*)
        foreach (var bone in b.skeleton.Bones)
        {
            if (bone?.Transform == null) continue;
            switch (bone.Transform.name)
            {
                case "Hand_WristRoot": case "XRHand_Wrist": b.wrist = bone.Transform; break;
                case "Hand_Middle1": case "XRHand_MiddleProximal": b.knuckle = bone.Transform; break;
                case "Hand_IndexTip": case "XRHand_IndexTip": b.indexTip = bone.Transform; break;
                case "Hand_MiddleTip": case "XRHand_MiddleTip": b.middleTip = bone.Transform; break;
                case "Hand_RingTip": case "XRHand_RingTip": b.ringTip = bone.Transform; break;
                case "Hand_PinkyTip": case "XRHand_LittleTip": b.pinkyTip = bone.Transform; break;
            }
        }
        if (debugLogs && b.Ready) Debug.Log($"GateMover: bones {hand.name} ready");
        return b.Ready;
    }

    bool Tracked(OVRHand hand, HandBones b) =>
        hand.IsTracked && hand.IsDataHighConfidence && b.skeleton.IsDataValid;

    float Ratio(HandBones b, Transform tip)
    {
        Vector3 w = b.wrist.position;
        float baseLen = Vector3.Distance(b.knuckle.position, w);
        return baseLen > 0.001f ? Vector3.Distance(tip.position, w) / baseLen : 99f;
    }

    bool Curled(HandBones b, Transform tip) => Ratio(b, tip) < curledRatio;
    bool Extended(HandBones b, Transform tip) => Ratio(b, tip) > extendedRatio;

    bool IsFist(HandBones b) =>
        Curled(b, b.indexTip) && Curled(b, b.middleTip) && Curled(b, b.ringTip) && Curled(b, b.pinkyTip);

    bool IsPointing(HandBones b) =>
        Extended(b, b.indexTip) && Curled(b, b.middleTip) && Curled(b, b.ringTip) && Curled(b, b.pinkyTip);

    Mode DetectGesture()
    {
        if (!Tracked(leftHand, _l) || !Tracked(rightHand, _r)) return Mode.None;
        if (allowMove || allowRotate)
            if (IsFist(_l) && IsFist(_r)) return Mode.Move;
        if (allowScale)
            if (IsPointing(_l) && IsPointing(_r)) return Mode.Scale;
        return Mode.None;
    }

    // ---------- diagnostics ----------

    void Diag(bool hands, bool bonesL, bool bonesR)
    {
        if (!debugLogs || diagInterval <= 0f || Time.time < _nextDiag) return;
        _nextDiag = Time.time + diagInterval;

        if (!hands) { Debug.Log("GateMover DIAG: OVRHand не найдены (L/R)"); return; }
        string Hand(string tag, OVRHand h, HandBones b, bool ready)
        {
            var sk = h.GetComponent<OVRSkeleton>();
            string head = $"{tag}: tracked={h.IsTracked} highConf={h.IsDataHighConfidence} " +
                          $"skel={(sk != null ? sk.GetSkeletonType().ToString() : "none")} init={(sk != null && sk.IsInitialized)} " +
                          $"bones={(sk != null && sk.Bones != null ? sk.Bones.Count : 0)} ready={ready}";
            if (!ready) return head;
            return head + $" valid={b.skeleton.IsDataValid} ratio I={Ratio(b, b.indexTip):F2} M={Ratio(b, b.middleTip):F2} " +
                          $"R={Ratio(b, b.ringTip):F2} P={Ratio(b, b.pinkyTip):F2} fist={IsFist(b)} point={IsPointing(b)}";
        }
        Debug.Log($"GateMover DIAG: mode={_mode} grabbed={HandleGrabbed()} | {Hand("L", leftHand, _l, bonesL)} | {Hand("R", rightHand, _r, bonesR)}");
    }

    // ---------- manipulation ----------

    void SetMode(Mode m)
    {
        if (m == _mode) return;
        if (debugLogs) Debug.Log($"GateMover: {_mode} -> {m}");
        _mode = m;
        if (m == Mode.None) return;

        _smoothInit = false;
        SampleHands(out var l, out var r);
        _prevMid = (l + r) * 0.5f;
        _prevDist = Vector3.Distance(l, r);
        _prevYaw = Yaw(l, r);
    }

    void Apply()
    {
        SampleHands(out var l, out var r);
        Vector3 mid = (l + r) * 0.5f;
        float dist = Vector3.Distance(l, r);
        float yaw = Yaw(l, r);

        if (_mode == Mode.Move)
        {
            if (allowMove)
            {
                Vector3 delta = mid - _prevMid;
                if (!allowVertical) delta.y = 0f;
                transform.position += delta;
            }
            if (allowRotate)
            {
                float dYaw = Mathf.DeltaAngle(_prevYaw, yaw);
                Vector3 pivot = new Vector3(mid.x, transform.position.y, mid.z);
                transform.RotateAround(pivot, Vector3.up, dYaw);
            }
        }
        else if (_mode == Mode.Scale && _prevDist > 0.01f)
        {
            float cur = transform.localScale.x;
            float next = Mathf.Clamp(cur * (dist / _prevDist), minScale, maxScale);
            float k = next / cur;
            // масштаб вокруг середины между руками (по горизонтали), низ ворот остаётся на месте
            Vector3 pivot = new Vector3(mid.x, transform.position.y, mid.z);
            transform.position = pivot + (transform.position - pivot) * k;
            transform.localScale = Vector3.one * next;
        }

        _prevMid = mid;
        _prevDist = dist;
        _prevYaw = yaw;
    }

    void SampleHands(out Vector3 l, out Vector3 r)
    {
        // центр ладони/кулака — костяшка среднего пальца
        Vector3 rawL = _l.knuckle.position;
        Vector3 rawR = _r.knuckle.position;
        if (!_smoothInit || smoothing <= 0f)
        {
            _smoothL = rawL; _smoothR = rawR; _smoothInit = true;
        }
        else
        {
            float k = 1f - smoothing;
            _smoothL = Vector3.Lerp(_smoothL, rawL, k);
            _smoothR = Vector3.Lerp(_smoothR, rawR, k);
        }
        l = _smoothL; r = _smoothR;
    }

    static float Yaw(Vector3 l, Vector3 r)
    {
        Vector3 d = r - l;
        return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    }

    /// <summary>Сбросить масштаб в 1:1 (можно повесить на кнопку/событие).</summary>
    public void ResetScale()
    {
        transform.localScale = Vector3.one;
    }
}
