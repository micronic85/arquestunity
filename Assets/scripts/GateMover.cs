using UnityEngine;

/// <summary>
/// Перемещение / поворот / масштаб ворот двумя руками (hand tracking).
/// Жест: на обеих руках вытянут указательный, средний согнут.
/// Поворот только вокруг вертикальной оси — ворота не заваливаются.
/// </summary>
public class GateMover : MonoBehaviour
{
    [Header("Hands (необязательно — найдутся автоматически)")]
    public OVRHand leftHand;
    public OVRHand rightHand;

    [Header("Gesture")]
    [Tooltip("Pinch strength указательного ниже порога = палец вытянут")]
    public float indexExtendedThreshold = 0.3f;
    [Tooltip("Pinch strength среднего выше порога = палец согнут")]
    public float middleCurledThreshold = 0.3f;
    [Tooltip("Сколько секунд жест должен удерживаться, чтобы включиться")]
    public float gestureHoldTime = 0.15f;

    [Header("Transform")]
    public bool allowMove = true;
    [Tooltip("Разрешить двигать по высоте (выкл. — ворота остаются на полу)")]
    public bool allowVertical = true;
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

    const float HandSearchInterval = 0.5f;

    bool _active;
    float _gestureTimer;
    float _nextHandSearch;

    Vector3 _prevMid;
    float _prevDist;
    float _prevYaw;

    Vector3 _smoothL, _smoothR;
    bool _smoothInit;

    void Update()
    {
        if (!EnsureHands()) { Deactivate(); return; }

        bool gesture = IsPointing(leftHand) && IsPointing(rightHand);
        _gestureTimer = gesture ? _gestureTimer + Time.deltaTime : 0f;

        if (!_active && _gestureTimer >= gestureHoldTime) Activate();
        else if (_active && !gesture) Deactivate();

        if (_active) Apply();
    }

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

    bool IsPointing(OVRHand hand)
    {
        if (!hand.IsTracked || !hand.IsDataHighConfidence) return false;
        float index = hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
        float middle = hand.GetFingerPinchStrength(OVRHand.HandFinger.Middle);
        return index < indexExtendedThreshold && middle > middleCurledThreshold;
    }

    // ---------- manipulation ----------

    void Activate()
    {
        _active = true;
        _smoothInit = false;
        SampleHands(out var l, out var r);
        _prevMid = (l + r) * 0.5f;
        _prevDist = Vector3.Distance(l, r);
        _prevYaw = Yaw(l, r);
        if (debugLogs) Debug.Log("GateMover: START");
    }

    void Deactivate()
    {
        if (_active && debugLogs) Debug.Log("GateMover: END");
        _active = false;
        _gestureTimer = 0f;
    }

    void Apply()
    {
        SampleHands(out var l, out var r);
        Vector3 mid = (l + r) * 0.5f;
        float dist = Vector3.Distance(l, r);
        float yaw = Yaw(l, r);

        if (allowMove)
        {
            Vector3 delta = mid - _prevMid;
            if (!allowVertical) delta.y = 0f;
            transform.position += delta;
        }

        if (allowRotate)
        {
            float dYaw = Mathf.DeltaAngle(_prevYaw, yaw);
            transform.RotateAround(mid, Vector3.up, dYaw);
        }

        if (allowScale && _prevDist > 0.01f)
        {
            float s = Mathf.Clamp(transform.localScale.x * (dist / _prevDist), minScale, maxScale);
            transform.localScale = Vector3.one * s;
        }

        _prevMid = mid;
        _prevDist = dist;
        _prevYaw = yaw;
    }

    void SampleHands(out Vector3 l, out Vector3 r)
    {
        Vector3 rawL = leftHand.transform.position;
        Vector3 rawR = rightHand.transform.position;
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
}
