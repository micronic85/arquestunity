using UnityEngine;

public partial class GatePortal
{
    [Header("Луна")]
    public Mesh moonMesh;
    public Texture2D moonTexture,moonNormalTexture;
    [Min(.02f)] public float moonRadius=.065f;
    [Min(.3f)] public float moonOrbitDistance=1;
    [Range(0,30)] public float moonOrbitSpeed=8;
    [Range(0,20)] public float moonSpinSpeed=3;
    public bool moonOrbitEnabled=true;
    public Transform MoonTransform=>_moon;
    public bool MoonHeld=>_moonLeftContact || _moonRightContact;
    public float MoonOrbitRadius=>Mathf.Max(moonOrbitDistance,PlanetRadius+moonRadius+.15f);
    public float MoonOrbitPhase=>_moonPhase;
    Transform _moon;
    Material _moonMaterial;
    float _moonPhase=65,_moonResume=1;
    bool _moonLeftContact,_moonRightContact;
    Vector3 _moonLeftDirection,_moonRightDirection;

    void BuildMoonWorld()
    {
        if(!moonMesh || !moonTexture)return;
        _moonMaterial=new Material(worldShader){name="Portal Moon matte sunlit surface"};
        _moonMaterial.SetFloat("_BakedWorld",5);_moonMaterial.SetTexture("_BaseMap",moonTexture);
        _moonMaterial.SetTexture("_MoonNormal",moonNormalTexture);_materials.Add(_moonMaterial);
        _moon=new GameObject("OrbitingMoon").transform;_moon.SetParent(_world,false);
        _moon.localRotation=Quaternion.Euler(0,20,6.7f);
        float radius=Mathf.Max(moonMesh.bounds.extents.x,Mathf.Max(moonMesh.bounds.extents.y,moonMesh.bounds.extents.z));
        var surface=Create("MoonSurface",moonMesh,_moon,_moonMaterial,false);
        surface.localScale=Vector3.one/Mathf.Max(.001f,radius);
        surface.localPosition=-moonMesh.bounds.center/Mathf.Max(.001f,radius);
        ResetMoonGesture();PlaceMoon();
    }

    void ClearMoonWorld()
    {
        ResetMoonGesture();
        if(_moonMaterial){_materials.Remove(_moonMaterial);Destroy(_moonMaterial);}
        _moonMaterial=null;_moon=null;
    }

    void UpdateMoonAnimation(float dt)
    {
        if(!_moon || !_earth)return;
        dt=Mathf.Max(0,dt);
        if(MoonHeld)_moonResume=0;
        else
        {
            _moonResume=Mathf.MoveTowards(_moonResume,1,dt/.45f);
            if(moonOrbitEnabled)_moonPhase=Mathf.Repeat(_moonPhase+moonOrbitSpeed*dt*_moonResume,360);
            _moon.Rotate(Vector3.up,moonSpinSpeed*dt*_moonResume,Space.Self);
        }
        PlaceMoon();
        _moonMaterial.SetVector("_PortalSunPosition",(Vector4)SunWorldPosition);
    }

    void PlaceMoon()
    {
        if(!_moon || !_earth)return;
        float angle=_moonPhase*Mathf.Deg2Rad;
        Vector3 lateral=_root.forward*Mathf.Cos(12*Mathf.Deg2Rad)+_root.up*Mathf.Sin(12*Mathf.Deg2Rad);
        _moon.position=_earth.position+(-_root.right*Mathf.Cos(angle)+lateral*Mathf.Sin(angle))*MoonOrbitRadius;
        _moon.localScale=Vector3.one*moonRadius/Mathf.Max(.001f,Mathf.Abs(_world.lossyScale.y));
    }

    void ResetMoonGesture()
    {
        _moonLeftContact=_moonRightContact=false;
    }

    bool ApplyMoonHands(PlanetHandSample left,PlanetHandSample right,bool uiBlocked)
    {
        if(!IsInside || !_moon || uiBlocked){ResetMoonGesture();return false;}
        bool l=MoonPalmDelta(left,ref _moonLeftContact,ref _moonLeftDirection,out var lq);
        bool r=MoonPalmDelta(right,ref _moonRightContact,ref _moonRightDirection,out var rq);
        if(MoonHeld)_moonResume=0;
        if(l || r)_moon.rotation=(l && r?Quaternion.Slerp(lq,rq,.5f):l?lq:rq)*_moon.rotation;
        return MoonHeld;
    }

    bool MoonPalmDelta(PlanetHandSample hand,ref bool contact,ref Vector3 previous,out Quaternion delta)
    {
        delta=Quaternion.identity;
        Vector3 direction=hand.palm-_moon.position;
        bool near=hand.tracked && hand.open && direction.magnitude>.005f
            && Mathf.Abs(direction.magnitude-moonRadius)<=palmContactDistance
            && PalmFaces(hand,_moon.position);
        if(!near){contact=false;return false;}
        direction.Normalize();
        if(!contact){contact=true;previous=direction;return false;}
        if(Vector3.Angle(previous,direction)>35){contact=false;return false;}
        delta=Quaternion.FromToRotation(previous,direction);previous=direction;return true;
    }

    static bool PalmFaces(PlanetHandSample sample,Vector3 target)
        =>sample.palmNormal.sqrMagnitude<.01f || Vector3.Dot(sample.palmNormal,(target-sample.palm).normalized)>.1f;
}
