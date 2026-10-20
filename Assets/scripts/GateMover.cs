using UnityEngine;

public class GateMover : MonoBehaviour
{
    [Header("Settings")]
    public float minScale = 0.1f;
    public float maxScale = 5f;

    [Tooltip("Порог pinch strength ниже которого палец считается вытянутым")]
    public float indexExtendedThreshold = 0.3f;

    private OVRHand _leftHand;
    private OVRHand _rightHand;

    private bool _isTwoHandActive = false;
    private Vector3 _prevMidpoint;
    private float _prevHandDistance;
    private Quaternion _prevHandRotation;

    void Update()
    {
        FindHands();

        bool leftIndexExtended = IsIndexExtended(_leftHand);
        bool rightIndexExtended = IsIndexExtended(_rightHand);
        bool twoHandActive = leftIndexExtended && rightIndexExtended;

        if (Time.frameCount % 60 == 0)
            Debug.Log($"GateMover: leftExtended={leftIndexExtended} rightExtended={rightIndexExtended}");

        if (twoHandActive && !_isTwoHandActive)
        {
            StartTwoHand();
        }
        else if (!twoHandActive && _isTwoHandActive)
        {
            _isTwoHandActive = false;
            Debug.Log("GateMover: END");
        }

        if (_isTwoHandActive && twoHandActive)
        {
            UpdateTwoHand();
        }
    }

    void FindHands()
    {
        _leftHand = null;
        _rightHand = null;

        foreach (var hand in FindObjectsByType<OVRHand>(FindObjectsSortMode.None))
        {
            string n = hand.gameObject.name.ToLower();
            if (n.Contains("left")) _leftHand = hand;
            else if (n.Contains("right")) _rightHand = hand;
        }
    }

    bool IsIndexExtended(OVRHand hand)
    {
        if (hand == null) return false;

        // Указательный вытянут = низкий pinch strength у указательного
        // и хотя бы один другой палец согнут (средний pinching)
        float indexStrength = hand.GetFingerPinchStrength(OVRHand.HandFinger.Index);
        float middleStrength = hand.GetFingerPinchStrength(OVRHand.HandFinger.Middle);

        if (Time.frameCount % 60 == 0)
            Debug.Log($"  {hand.name}: index={indexStrength:F2} middle={middleStrength:F2}");

        // Указательный НЕ щипает + средний согнут (рука не просто открыта)
        return indexStrength < indexExtendedThreshold && middleStrength > 0.3f;
    }

    void StartTwoHand()
    {
        _isTwoHandActive = true;
        _prevMidpoint = GetMidpoint();
        _prevHandDistance = GetHandDistance();
        _prevHandRotation = GetHandRotation();
        Debug.Log("GateMover: START");
    }

    void UpdateTwoHand()
    {
        Vector3 currentMidpoint = GetMidpoint();
        float currentDistance = GetHandDistance();
        Quaternion currentRotation = GetHandRotation();

        // Позиция
        transform.position += currentMidpoint - _prevMidpoint;

        // Поворот
        Quaternion rotationDelta = currentRotation * Quaternion.Inverse(_prevHandRotation);
        transform.rotation = rotationDelta * transform.rotation;

        // Масштаб
        if (_prevHandDistance > 0.001f)
        {
            float multiplier = currentDistance / _prevHandDistance;
            float newScale = Mathf.Clamp(
                transform.localScale.x * multiplier,
                minScale,
                maxScale
            );
            transform.localScale = Vector3.one * newScale;
        }

        _prevMidpoint = currentMidpoint;
        _prevHandDistance = currentDistance;
        _prevHandRotation = currentRotation;
    }

    Vector3 GetMidpoint()
    {
        return (_leftHand.transform.position + _rightHand.transform.position) * 0.5f;
    }

    float GetHandDistance()
    {
        return Vector3.Distance(
            _leftHand.transform.position,
            _rightHand.transform.position
        );
    }

    Quaternion GetHandRotation()
    {
        Vector3 dir = (_rightHand.transform.position - _leftHand.transform.position).normalized;
        if (dir == Vector3.zero) return Quaternion.identity;
        return Quaternion.LookRotation(dir, Vector3.up);
    }
}