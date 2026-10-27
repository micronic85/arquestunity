using UnityEngine;

/// <summary>
/// Тяга привода: изогнутая тяга (DriveArm из gate_parts) шарнирно соединяет кронштейн на верхней кромке
/// верхней секции с кареткой, которая ездит по шине привода (DriveRail). Длина тяги постоянна, поэтому
/// при открывании каретка уходит по шине назад на столько, чтобы расстояние между шарнирами не менялось,
/// а тяга поворачивается вокруг шарнира кронштейна.
/// </summary>
public partial class GateBuilder
{
    // Шарниры в исходной модели (система Hardware при закрытых воротах высотой HT0) — сняты с меша тяги
    static readonly Vector3 ArmDoorPin0 = new Vector3(0.000f, 2.748f, 0.002f);    // кронштейн на верхней секции
    static readonly Vector3 ArmTrolleyPin0 = new Vector3(0.465f, 3.445f, 0.003f); // каретка под шиной
    const float TopSectionY0 = 4 * H0; // низ верхней секции в исходной модели

    Transform _driveArm, _trolley, _hardware;
    Vector3 _armPinDoorLocal, _armPinTrolleyLocal; // шарниры в системе меша тяги
    Vector3 _bracketLocal;                          // шарнир кронштейна в системе верхней секции
    float _armLength, _railPinY;

    // Вызывается из BuildHardware для детали DriveArm (без обёртки: тяга не тянется, только двигается)
    void SetupDriveArm(Transform hw, Transform arm)
    {
        _hardware = hw;
        _driveArm = arm;
        arm.gameObject.SetActive(true);
        // тяга ещё на месте исходной модели: шарниры переводим в её систему
        _armPinDoorLocal = arm.InverseTransformPoint(hw.TransformPoint(ArmDoorPin0));
        _armPinTrolleyLocal = arm.InverseTransformPoint(hw.TransformPoint(ArmTrolleyPin0));
        _armLength = Vector2.Distance(ArmDoorPin0, ArmTrolleyPin0);
        // шина поднимается вместе с верхом полотна (как остальные детали над проёмом)
        _railPinY = ArmTrolleyPin0.y + (CurtainHeight - HT0);
        float hTop = _heights[_heights.Count - 1];
        _bracketLocal = new Vector3(ArmDoorPin0.x, (ArmDoorPin0.y - TopSectionY0) * hTop / H0, 0f);

        // Каретка — колодка под шиной с шарниром тяги
        var mat = arm.GetComponentInChildren<Renderer>(true)?.sharedMaterial;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "DriveTrolley";
        DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(hw, false);
        go.transform.localScale = new Vector3(0.14f, 0.035f, 0.05f);
        if (mat) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        _trolley = go.transform;
    }

    void UpdateDriveArm()
    {
        if (_driveArm == null || _hardware == null || _sections.Count == 0) return;
        var topSec = _sections[_sections.Count - 1];
        Vector3 a = _hardware.InverseTransformPoint(topSec.TransformPoint(_bracketLocal));
        float dy = Mathf.Min(Mathf.Abs(_railPinY - a.y), _armLength * 0.999f);
        Vector3 b = new Vector3(a.x + Mathf.Sqrt(_armLength * _armLength - dy * dy), _railPinY, a.z);

        // поворот вокруг оси Z (плоскость движения ворот): направление «шарнир кронштейна → каретка»
        Vector3 meshDir = _armPinTrolleyLocal - _armPinDoorLocal;
        Vector3 want = b - a;
        float ang = Mathf.Atan2(want.y, want.x) - Mathf.Atan2(meshDir.y, meshDir.x);
        _driveArm.localRotation = Quaternion.Euler(0f, 0f, ang * Mathf.Rad2Deg);
        _driveArm.localPosition = a - _driveArm.localRotation * _armPinDoorLocal;

        if (_trolley) _trolley.localPosition = new Vector3(b.x, _railPinY + 0.012f, b.z);
    }
}
