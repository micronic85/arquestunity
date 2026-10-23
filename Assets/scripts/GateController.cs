using System.Collections;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;

/// <summary>
/// Открывание ворот за ручку: вертикальное движение руки/контроллера
/// переводится в прогресс анимации (параметр GateProgress, Motion Time).
/// </summary>
public class GateController : MonoBehaviour
{
    [Header("Links")]
    public Animator gateAnimator;
    [Tooltip("Адаптивные ворота: если задан, открывание считает он, а не Animator")]
    public GateBuilder builder;
    public GrabInteractable handleGrab;
    public HandGrabInteractable handleHandGrab;
    public string animationClipName = "Animation";

    [Header("Settings")]
    [Tooltip("Сколько метров (при масштабе 1) нужно провести рукой вверх для полного открытия")]
    public float openHandTravelY = 1f;
    [Range(0f, 1f)]
    public float maxProgress = 0.98f;

    [Header("Debug")]
    public bool debugLogs = false;

    static readonly int GateProgressId = Animator.StringToHash("GateProgress");

    enum GrabSource { None, LeftController, RightController, Hand }

    OVRCameraRig _rig;
    OVRHand[] _hands = new OVRHand[0];

    GrabSource _source = GrabSource.None;
    OVRHand _grabbingHand;
    float _grabStartY;
    float _grabStartProgress;
    float _progress;

    /// <summary>Ручку сейчас держат (контроллером или рукой).</summary>
    public bool IsHandleGrabbed => _source != GrabSource.None;

    Transform HandleTransform =>
        handleHandGrab != null ? handleHandGrab.transform :
        handleGrab != null ? handleGrab.transform : transform;

    void Start()
    {
        if (handleHandGrab == null) Debug.LogError("GateController: назначь HandGrabInteractable (handleHandGrab)", this);
        if (handleGrab == null) Debug.LogError("GateController: назначь GrabInteractable (handleGrab)", this);
        StartCoroutine(Init());
    }

    IEnumerator Init()
    {
        // ждём, пока Building Blocks создадут риг и руки
        yield return null;
        yield return null;

        _rig = FindFirstObjectByType<OVRCameraRig>();
        if (_rig == null) Debug.LogError("GateController: OVRCameraRig не найден", this);
        RefreshHands();

        if (builder == null) builder = GetComponent<GateBuilder>();
        if (builder != null)
        {
            builder.SetProgress(_progress);
        }
        else if (gateAnimator != null)
        {
            gateAnimator.Play(animationClipName, 0, 0f);
            gateAnimator.speed = 0f;
            gateAnimator.SetFloat(GateProgressId, _progress);
        }
    }

    void RefreshHands()
    {
        _hands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
        Log($"OVRHand count = {_hands.Length}");
    }

    void Update()
    {
        bool grabbed = IsGrabbed();

        if (grabbed && _source == GrabSource.None)
            BeginGrab();
        else if (!grabbed && _source != GrabSource.None)
            EndGrab();

        if (_source == GrabSource.None || (gateAnimator == null && builder == null)) return;

        Vector3? pos = GetSourcePosition();
        if (!pos.HasValue) return;

        // Для адаптивных ворот ход руки ≈ высоте полотна (чем выше ворота, тем дольше тянуть)
        float baseTravel = builder != null ? builder.CurtainHeight * 0.8f : openHandTravelY;
        float travel = baseTravel * Mathf.Max(0.0001f, transform.lossyScale.y);
        _progress = Mathf.Clamp(_grabStartProgress + (pos.Value.y - _grabStartY) / travel, 0f, maxProgress);
        if (builder != null) builder.SetProgress(_progress);
        else gateAnimator.SetFloat(GateProgressId, _progress);
    }

    bool IsGrabbed()
    {
        bool controller = handleGrab != null && handleGrab.State == InteractableState.Select;
        bool hand = handleHandGrab != null && handleHandGrab.State == InteractableState.Select;
        return controller || hand;
    }

    void BeginGrab()
    {
        _source = DetectSource();
        Vector3? pos = GetSourcePosition();
        if (!pos.HasValue) { _source = GrabSource.None; return; }

        if (builder != null) { builder.StopAnimation(); _progress = builder.Progress; }
        _grabStartY = pos.Value.y;
        _grabStartProgress = _progress;
        Log($"Grab START | source={_source} hand={_grabbingHand?.name} y={_grabStartY:F3}");
    }

    void EndGrab()
    {
        Log($"Grab END | progress={_progress:F3}");
        _source = GrabSource.None;
        _grabbingHand = null;
    }

    GrabSource DetectSource()
    {
        _grabbingHand = null;
        Vector3 handle = HandleTransform.position;

        // Контроллер: из тех, у кого зажат grip, берём ближайший к ручке
        if (_rig != null && handleGrab != null && handleGrab.State == InteractableState.Select)
        {
            bool l = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);
            bool r = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);
            if (l && r)
            {
                float dl = Vector3.Distance(_rig.leftHandAnchor.position, handle);
                float dr = Vector3.Distance(_rig.rightHandAnchor.position, handle);
                return dl < dr ? GrabSource.LeftController : GrabSource.RightController;
            }
            if (l) return GrabSource.LeftController;
            if (r) return GrabSource.RightController;
        }

        // Рука: сначала через интерактор, иначе — ближайшая к ручке
        if (handleHandGrab != null)
        {
            foreach (var interactor in handleHandGrab.SelectingInteractorViews)
            {
                if (interactor is MonoBehaviour mb)
                {
                    var hand = mb.GetComponentInParent<OVRHand>();
                    if (hand != null) { _grabbingHand = hand; return GrabSource.Hand; }
                }
            }
        }

        _grabbingHand = ClosestHand(handle);
        return _grabbingHand != null ? GrabSource.Hand : GrabSource.None;
    }

    Vector3? GetSourcePosition()
    {
        switch (_source)
        {
            case GrabSource.LeftController: return _rig != null ? _rig.leftHandAnchor.position : (Vector3?)null;
            case GrabSource.RightController: return _rig != null ? _rig.rightHandAnchor.position : (Vector3?)null;
            case GrabSource.Hand:
                if (_grabbingHand != null && _grabbingHand.IsTracked) return _grabbingHand.transform.position;
                return null; // рука потеряла трекинг — замираем, а не прыгаем на другую руку
            default: return null;
        }
    }

    OVRHand ClosestHand(Vector3 point)
    {
        if (_hands.Length == 0) RefreshHands();

        OVRHand best = null;
        float bestDist = float.MaxValue;
        foreach (var hand in _hands)
        {
            if (hand == null || !hand.IsTracked || !hand.IsDataHighConfidence) continue;
            float d = Vector3.Distance(hand.transform.position, point);
            if (d < bestDist) { bestDist = d; best = hand; }
        }
        return best;
    }

    void Log(string msg)
    {
        if (debugLogs) Debug.Log("GateController: " + msg, this);
    }
}
