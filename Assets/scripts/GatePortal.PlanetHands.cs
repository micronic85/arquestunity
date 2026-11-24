using UnityEngine;

public partial class GatePortal
{
    [Header("Глобус ладонями")]
    public bool earthAutoSpin=true;
    [Range(.12f,.35f)] public float insideEarthRadius=.23f;
    [Range(.03f,.15f)] public float palmContactDistance=.075f;
    public float planetMinScale=.5f,planetMaxScale=1.75f;
    public float PlanetScale {get;private set;}=1;
    public float PlanetRadius=>_earth?1.55f*Mathf.Abs(_earth.lossyScale.y):0;
    public Transform EarthTransform=>_earth;
    public bool IsPlanetScaling=>_planetScaling;

    public struct PlanetHandSample
    {
        public bool tracked,open,pointing;
        public Vector3 palm,index;
    }

    sealed class PlanetHandBones
    {
        public OVRHand hand;
        public OVRSkeleton skeleton;
        public Transform wrist,indexBase,middleBase,pinkyBase,index,middle,ring,pinky;
        public bool Ready=>hand && wrist && indexBase && middleBase && pinkyBase && index && middle && ring && pinky;
    }
    readonly PlanetHandBones _planetLeft=new PlanetHandBones(),_planetRight=new PlanetHandBones();
    Vector3 _insideEarthPosition,_leftPalmDirection,_rightPalmDirection;
    bool _leftPalmContact,_rightPalmContact,_planetScaling,_pointReleaseRequired;
    float _earthBlend,_lastEarthTime=-1,_nextPlanetSearch,_scaleHold,_startFingerDistance,_startPlanetScale,_lastFingerDistance;

    public void SetEarthAutoSpin(bool enabled)
    {
        earthAutoSpin=enabled;_lastEarthTime=Time.unscaledTime;ResetPlanetGesture(false);
    }

    public void ResetEarth()
    {
        PlanetScale=1;
        if(_earth)_earth.localRotation=Quaternion.Euler(0,0,23.4f);
        _lastEarthTime=Time.unscaledTime;ResetPlanetGesture(true);UpdateEarthPose(0);
    }

    void UpdateEarthAnimation(float now)
    {
        if(!_earth){_lastEarthTime=now;return;}
        float dt=_lastEarthTime<0?0:Mathf.Max(0,now-_lastEarthTime);
        _lastEarthTime=now;
        if(earthAutoSpin && !_planetScaling && !_leftPalmContact && !_rightPalmContact)
            _earth.localRotation=Quaternion.AngleAxis(dt*6,Vector3.up)*_earth.localRotation;
        UpdateEarthPose(Mathf.Min(dt,.1f));
    }

    void UpdateEarthPose(float dt)
    {
        if(!_earth)return;
        _earthBlend=Mathf.MoveTowards(_earthBlend,IsInside?1:0,dt/.45f);
        float blend=Mathf.SmoothStep(0,1,_earthBlend);
        float worldScale=Mathf.Max(.001f,Mathf.Abs(_world.lossyScale.y));
        float localScale=insideEarthRadius*PlanetScale/(1.55f*worldScale);
        _earth.localPosition=Vector3.Lerp(new Vector3(4.8f,0,0),_insideEarthPosition,blend);
        _earth.localScale=Vector3.one*Mathf.Lerp(1,localScale,blend);
    }

    void ResetPlanetGesture(bool requireRelease)
    {
        _planetScaling=false;_scaleHold=0;_leftPalmContact=_rightPalmContact=false;
        if(requireRelease)_pointReleaseRequired=true;
    }

    void FindPlanetHands()
    {
        if(_planetLeft.Ready && _planetRight.Ready)return;
        if(Time.unscaledTime<_nextPlanetSearch)return;
        _nextPlanetSearch=Time.unscaledTime+.5f;
        foreach(var skeleton in FindObjectsByType<OVRSkeleton>(FindObjectsSortMode.None))
        {
            string kind=skeleton.GetSkeletonType().ToString();
            var hand=kind.Contains("Left")?_planetLeft:kind.Contains("Right")?_planetRight:null;
            if(hand==null || hand.Ready || !skeleton.IsInitialized || skeleton.Bones==null)continue;
            hand.skeleton=skeleton;hand.hand=skeleton.GetComponent<OVRHand>() ?? skeleton.GetComponentInParent<OVRHand>();
            foreach(var bone in skeleton.Bones)
            {
                if(bone?.Transform==null)continue;
                switch(bone.Transform.name)
                {
                    case "Hand_WristRoot":case "XRHand_Wrist":hand.wrist=bone.Transform;break;
                    case "Hand_Index1":case "XRHand_IndexProximal":hand.indexBase=bone.Transform;break;
                    case "Hand_Middle1":case "XRHand_MiddleProximal":hand.middleBase=bone.Transform;break;
                    case "Hand_Pinky1":case "XRHand_LittleProximal":hand.pinkyBase=bone.Transform;break;
                    case "Hand_IndexTip":case "XRHand_IndexTip":hand.index=bone.Transform;break;
                    case "Hand_MiddleTip":case "XRHand_MiddleTip":hand.middle=bone.Transform;break;
                    case "Hand_RingTip":case "XRHand_RingTip":hand.ring=bone.Transform;break;
                    case "Hand_PinkyTip":case "XRHand_LittleTip":hand.pinky=bone.Transform;break;
                }
            }
        }
    }

    PlanetHandSample SamplePlanetHand(PlanetHandBones hand,bool left)
    {
        var sample=new PlanetHandSample();
        if(!hand.Ready || !hand.hand.IsTracked || !hand.hand.IsDataHighConfidence || !hand.skeleton.IsDataValid)return sample;
        sample.tracked=true;
        sample.palm=(hand.wrist.position+hand.middleBase.position)*.5f;sample.index=hand.index.position;
        float length=Vector3.Distance(hand.wrist.position,hand.middleBase.position);
        if(length<.01f)return default;
        float i=Vector3.Distance(hand.wrist.position,hand.index.position)/length;
        float m=Vector3.Distance(hand.wrist.position,hand.middle.position)/length;
        float r=Vector3.Distance(hand.wrist.position,hand.ring.position)/length;
        float p=Vector3.Distance(hand.wrist.position,hand.pinky.position)/length;
        sample.pointing=i>1.7f && m<1.35f && r<1.35f && p<1.35f;
        Vector3 normal=Vector3.Cross(hand.indexBase.position-hand.wrist.position,hand.pinkyBase.position-hand.wrist.position).normalized;
        if(!left)normal=-normal;
        sample.open=i>1.6f && m>1.65f && r>1.5f && p>1.4f
            && Vector3.Dot(normal,(_earth.position-sample.palm).normalized)>.1f;
        return sample;
    }

    void UpdatePlanetHands()
    {
        if(!IsInside || !_earth || _earthBlend<.98f){ResetPlanetGesture(true);return;}
        FindPlanetHands();
        ApplyPlanetHands(SamplePlanetHand(_planetLeft,true),SamplePlanetHand(_planetRight,false),Time.unscaledDeltaTime,
            _travelPicker && _travelPicker.PortalMenuVisible);
    }

    bool PointNearPlanet(PlanetHandSample hand)=>hand.tracked && hand.pointing
        && Vector3.Distance(hand.index,_earth.position)<=PlanetRadius+.22f;

    void ApplyPlanetHands(PlanetHandSample left,PlanetHandSample right,float dt,bool uiBlocked)
    {
        if(!IsInside || !_earth || _earthBlend<.98f || uiBlocked){ResetPlanetGesture(true);return;}
        if(!left.tracked && !right.tracked){ResetPlanetGesture(true);return;}
        bool bothPoint=left.tracked && right.tracked && left.pointing && right.pointing;
        if(left.tracked && right.tracked && !left.pointing && !right.pointing)_pointReleaseRequired=false;
        if(_planetScaling)
        {
            if(!bothPoint){ResetPlanetGesture(true);return;}
            float distance=Vector3.Distance(left.index,right.index);
            if(distance<.04f || distance>1.5f || Mathf.Abs(distance-_lastFingerDistance)>.20f){ResetPlanetGesture(true);return;}
            _lastFingerDistance=distance;
            PlanetScale=Mathf.Clamp(_startPlanetScale*distance/_startFingerDistance,planetMinScale,planetMaxScale);
            UpdateEarthPose(0);return;
        }
        if(bothPoint && !_pointReleaseRequired && PointNearPlanet(left) && PointNearPlanet(right)
            && Vector3.Distance(left.index,right.index)>=.08f)
        {
            _scaleHold+=dt;_leftPalmContact=_rightPalmContact=false;
            if(_scaleHold>=.18f)
            {
                _startFingerDistance=_lastFingerDistance=Vector3.Distance(left.index,right.index);_startPlanetScale=PlanetScale;
                _planetScaling=true;earthAutoSpin=false;
            }
            return;
        }
        _scaleHold=0;
        bool l=PalmDelta(left,ref _leftPalmContact,ref _leftPalmDirection,out var lq);
        bool r=PalmDelta(right,ref _rightPalmContact,ref _rightPalmDirection,out var rq);
        if(_leftPalmContact || _rightPalmContact)earthAutoSpin=false;
        if(l||r)
        {
            Quaternion delta=l&&r?Quaternion.Slerp(lq,rq,.5f):l?lq:rq;
            _earth.rotation=delta*_earth.rotation;
        }
    }

    bool PalmDelta(PlanetHandSample hand,ref bool contact,ref Vector3 previous,out Quaternion delta)
    {
        delta=Quaternion.identity;
        Vector3 direction=hand.palm-_earth.position;
        bool near=hand.tracked && hand.open && Mathf.Abs(direction.magnitude-PlanetRadius)<=palmContactDistance;
        if(!near){contact=false;return false;}
        direction.Normalize();
        if(!contact){contact=true;previous=direction;return false;}
        float angle=Vector3.Angle(previous,direction);
        if(angle>35){contact=false;return false;}
        delta=Quaternion.FromToRotation(previous,direction);previous=direction;return true;
    }
}
