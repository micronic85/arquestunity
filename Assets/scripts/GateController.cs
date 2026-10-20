using System.Collections;
using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;

public class GateController : MonoBehaviour
{
    [Header("Links")]
    public Animator gateAnimator;
    public GrabInteractable handleGrab;
    public HandGrabInteractable handleHandGrab;
    public string animationClipName = "Animation";

    [Header("Settings")]
    public float openHandTravelY = 1f;

    private bool _isGrabbed = false;
    private float _grabStartHandY = 0f;
    private float _grabStartProgress = 0f;
    private float _currentProgress = 0f;
    private OVRCameraRig _rig;
    private OVRHand _grabbingHand = null;

    void Start()
    {
        if (handleHandGrab == null)
            Debug.LogError("handleHandGrab IS NULL — назначь HandGrabInteractable в инспекторе!");
        if (handleGrab == null)
            Debug.LogError("handleGrab IS NULL — назначь GrabInteractable в инспекторе!");

        StartCoroutine(InitAll());
    }

    IEnumerator InitAll()
    {
        yield return null;
        yield return null;

        _rig = FindFirstObjectByType<OVRCameraRig>();
        Debug.Log($"_rig = {(_rig != null ? _rig.name : "NULL")}");

        var hands = FindObjectsByType<OVRHand>(FindObjectsSortMode.None);
        Debug.Log($"OVRHand count = {hands.Length}");
        foreach (var h in hands)
            Debug.Log($"  hand: {h.name} tracked={h.IsTracked}");

        if (_rig == null)
            Debug.LogError("OVRCameraRig not found!");

        if (gateAnimator != null)
        {
            gateAnimator.Play(animationClipName, 0, 0f);
            gateAnimator.speed = 0;
            gateAnimator.SetFloat("GateProgress", 0f);
        }
    }

    void Update()
    {
        bool grabbed = IsGrabbed();

        if (grabbed && !_isGrabbed)
        {
            _grabbingHand = GetGrabbingHand();

            Vector3? handPos = GetHandPosition();
            if (!handPos.HasValue) return;

            _isGrabbed = true;
            _grabStartHandY = handPos.Value.y;
            _grabStartProgress = _currentProgress;
            Debug.Log($"Grab START | handY={_grabStartHandY:F3} | hand={_grabbingHand?.name}");
        }
        else if (!grabbed && _isGrabbed)
        {
            _isGrabbed = false;
            _grabbingHand = null;
            Debug.Log($"Grab END | progress={_currentProgress:F3}");
        }

        if (!_isGrabbed || gateAnimator == null) return;

        Vector3? pos = GetHandPosition();
        if (!pos.HasValue) return;

        float delta = pos.Value.y - _grabStartHandY;
        // Делим на масштаб объекта чтобы скорость не зависела от размера
        float scaleFactor = transform.lossyScale.y;
        _currentProgress = Mathf.Clamp(
      _grabStartProgress + delta / (openHandTravelY * scaleFactor),
      0f, 0.98f
  );
        gateAnimator.SetFloat("GateProgress", _currentProgress);
    }

    bool IsGrabbed()
    {
        bool controllerGrab = handleGrab != null && handleGrab.State == InteractableState.Select;
        bool handGrab = handleHandGrab != null && handleHandGrab.State == InteractableState.Select;

        if (Time.frameCount % 60 == 0)
            Debug.Log($"handGrab state: {handleHandGrab?.State} | controllerGrab state: {handleGrab?.State}");

        return controllerGrab || handGrab;
    }

    OVRHand GetGrabbingHand()
    {
        // Пробуем получить руку через SelectingInteractorViews
        if (handleHandGrab != null && handleHandGrab.State == InteractableState.Select)
        {
            foreach (var interactor in handleHandGrab.SelectingInteractorViews)
            {
                if (interactor is MonoBehaviour mb)
                {
                    var hand = mb.GetComponentInParent<OVRHand>();
                    if (hand != null) return hand;
                }
            }
        }

        // Fallback — ближайшая рука
        return GetClosestHand();
    }

    OVRHand GetClosestHand()
    {
        OVRHand best = null;
        float bestDist = float.MaxValue;

        foreach (var hand in FindObjectsByType<OVRHand>(FindObjectsSortMode.None))
        {
            if (!hand.IsTracked || !hand.IsDataHighConfidence) continue;
            float dist = Vector3.Distance(hand.transform.position, transform.position);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = hand;
            }
        }
        return best;
    }

    Vector3? GetHandPosition()
    {
        // Контроллеры
        if (_rig != null)
        {
            if (OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch))
                return _rig.rightHandAnchor.position;
            if (OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch))
                return _rig.leftHandAnchor.position;
        }

        // Hand Tracking — используем запомненную руку
        if (_grabbingHand != null && _grabbingHand.IsTracked)
            return _grabbingHand.transform.position;

        // Fallback — ближайшая рука
        return GetClosestHand()?.transform.position;
    }
}