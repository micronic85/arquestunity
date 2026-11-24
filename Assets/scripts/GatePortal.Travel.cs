using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

public partial class GatePortal
{
    [Header("Вход в портал")]
    public bool allowEarthEntry=true;
    [Min(.01f)] public float crossingMargin=.035f;
    [Min(.01f)] public float headClearance=.08f;
    public Transform headOverride;
    public bool IsInside {get;private set;}
    public bool EntrySuppressed=>_entrySuppressed;

    Transform _travelHead;
    GateMover _travelMover;
    GateColorPicker _travelPicker;
    bool _hasHeadSample,_entryArmed,_pendingEntry,_pendingExit,_entrySuppressed;
    Vector3 _previousHead;
    float _nextTravelSearch,_nextVisibilityScan;
    readonly Dictionary<Renderer,bool> _roomRenderers=new Dictionary<Renderer,bool>();

    void ResolveTravelReferences()
    {
        if(headOverride)_travelHead=headOverride;
        if(_travelHead && _travelMover && _travelPicker)return;
        if(Time.unscaledTime<_nextTravelSearch)return;
        _nextTravelSearch=Time.unscaledTime+.5f;
        if(!_travelHead)
        {
            var rig=FindFirstObjectByType<OVRCameraRig>();
            _travelHead=rig?rig.centerEyeAnchor:(Camera.main?Camera.main.transform:null);
        }
        if(!_travelMover)_travelMover=GetComponent<GateMover>();
        if(!_travelPicker)_travelPicker=GetComponent<GateColorPicker>();
    }

    void UpdatePortalTravel()
    {
        ResolveTravelReferences();
        bool valid=_travelHead;
        if(valid && !Application.isEditor)
        {
            var device=InputDevices.GetDeviceAtXRNode(XRNode.Head);
            valid=device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked) && tracked;
        }
        if(!valid){LoseTravelTracking();return;}
        SamplePortalHead(_travelHead.position,true);
        if(IsInside)
        {
            if(Time.unscaledTime>=_nextVisibilityScan)
            {
                _nextVisibilityScan=Time.unscaledTime+.25f;HideRoomRenderers();
            }
            if(OVRInput.GetDown(OVRInput.Button.Two) || OVRInput.GetDown(OVRInput.Button.Four))ReturnToRoom();
        }
    }

    // Segment/plane intersection is used, rather than merely testing which side the camera is on.
    // A lateral walk around the portal or a recovered tracking pose cannot start a visit.
    void SamplePortalHead(Vector3 position,bool tracked)
    {
        if(!tracked){LoseTravelTracking();return;}
        if(!_root || !portalEnabled || worldKind!=PortalWorldKind.Earth || !allowEarthEntry)
        {
            if(IsInside)ExitPortal(false);
            _hasHeadSample=false;_entryArmed=_pendingEntry=_pendingExit=false;return;
        }
        float d=Vector3.Dot(position-_root.position,_root.right);
        float radius=ApertureRadius*Mathf.Abs(transform.lossyScale.y);
        float clearRadius=Mathf.Max(.02f,radius-headClearance);
        float radial=PortalRadialDistance(position);
        if(_entrySuppressed)
        {
            if(d<-crossingMargin*2)_entrySuppressed=false;
            else {_previousHead=position;_hasHeadSample=false;return;}
        }
        if(!_hasHeadSample)
        {
            _previousHead=position;_hasHeadSample=true;
            _entryArmed=d<-crossingMargin;return;
        }
        if(Vector3.Distance(_previousHead,position)>.60f)
        {
            if(IsInside)ReturnToRoom();
            _previousHead=position;_entryArmed=false;_pendingEntry=_pendingExit=false;return;
        }
        float previousD=Vector3.Dot(_previousHead-_root.position,_root.right);
        bool crossed=(previousD<=0 && d>0)||(previousD>=0 && d<0);
        bool through=false;
        if(crossed)
        {
            float t=previousD/(previousD-d);
            through=PortalRadialDistance(Vector3.Lerp(_previousHead,position,t))<=clearRadius;
        }
        if(!IsInside)
        {
            if(d<-crossingMargin){_entryArmed=true;_pendingEntry=false;}
            if(previousD<=0 && d>0)
            {
                _pendingEntry=_entryArmed && through && _gate.Progress>=.98f;
                if(!_pendingEntry)_entryArmed=false;
            }
            if(_pendingEntry && radial>clearRadius){_pendingEntry=false;_entryArmed=false;}
            if(_pendingEntry && d>=crossingMargin)EnterPortal(position);
        }
        else
        {
            if(d>crossingMargin)_pendingExit=false;
            if(previousD>=0 && d<0)_pendingExit=through;
            if(_pendingExit && d<=-crossingMargin)ExitPortal(false);
        }
        _previousHead=position;
    }

    float PortalRadialDistance(Vector3 position)
    {
        Vector3 offset=position-_root.position;
        return (offset-_root.right*Vector3.Dot(offset,_root.right)).magnitude;
    }

    void EnterPortal(Vector3 headPosition)
    {
        if(IsInside || !_earth)return;
        ResolveTravelReferences();
        _gate.StopAnimation();_gate.SetWicketAngle(0);_gate.SetProgress(1);
        _gate.SetPortalInteractionLock(true);
        if(_travelMover)_travelMover.SetPortalSuspended(true);
        IsInside=true;_pendingEntry=false;_pendingExit=false;
        // Hand/overview mode is changed only by its explicit menu command, never by crossing.
        ResetPlanetGesture(true);
        NotifyTravelMenu();HideRoomRenderers();
    }

    void ExitPortal(bool suppress)
    {
        bool wasInside=IsInside;
        IsInside=false;_pendingEntry=_pendingExit=false;
        _entrySuppressed=suppress;_entryArmed=false;
        if(_gate)_gate.SetPortalInteractionLock(false);
        if(_travelMover)_travelMover.SetPortalSuspended(false);
        ResetPlanetGesture(true);
        foreach(var entry in _roomRenderers)if(entry.Key)entry.Key.enabled=entry.Value;
        _roomRenderers.Clear();
        if(wasInside && isActiveAndEnabled)NotifyTravelMenu();
    }

    public void ReturnToRoom()
    {
        ExitPortal(true);
        if(_root)_root.gameObject.SetActive(false);
    }

    void LoseTravelTracking()
    {
        if(IsInside)ReturnToRoom();
        _hasHeadSample=false;_entryArmed=_pendingEntry=_pendingExit=false;
        ResetPlanetGesture(true);
    }

    void NotifyTravelMenu()
    {
        if(_travelPicker)_travelPicker.OnPortalTravelChanged(IsInside);
    }

    bool IsHandRenderer(Renderer renderer)
    {
        return renderer.GetComponentInParent<OVRHand>() || renderer.GetComponentInParent<OVRSkeleton>()
            || (renderer is SkinnedMeshRenderer && renderer.GetComponentInParent<OVRCameraRig>());
    }

    void HideRoomRenderers()
    {
        foreach(var renderer in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if(renderer.transform.IsChildOf(_root) || IsHandRenderer(renderer)
                || (_travelPicker && _travelPicker.OwnsMenuRenderer(renderer)))continue;
            if(!_roomRenderers.ContainsKey(renderer))_roomRenderers.Add(renderer,renderer.enabled);
            renderer.enabled=false;
        }
    }

    void BeforePortalCameraRender(ScriptableRenderContext context,Camera camera)
    {
        if(!IsInside)return;
        // Other AR components can enable their markers during Update; keep them out of space.
        foreach(var entry in _roomRenderers)if(entry.Key)entry.Key.enabled=false;
    }

    void SetTravelShaderState(Material material)
    {
        bool enabled=allowEarthEntry && worldKind==PortalWorldKind.Earth;
        material.SetFloat("_PortalTravel",enabled?1:0);
        material.SetFloat("_PortalInside",IsInside?1:0);
        float radius=ApertureRadius*Mathf.Abs(transform.lossyScale.y);
        bool ready=(_entryArmed || _pendingEntry) && _hasHeadSample && !_entrySuppressed
            && PortalRadialDistance(_previousHead)<=Mathf.Max(.02f,radius-headClearance) && _gate.Progress>=.98f;
        material.SetFloat("_PortalEntryReady",ready?1:0);
        material.SetFloat("_PortalExitPending",_pendingExit?1:0);
        material.SetFloat("_PortalRadius",ApertureRadius*Mathf.Abs(transform.lossyScale.y));
        if(material.shader==worldShader)
            material.SetFloat("_PortalCompare",enabled?(float)CompareFunction.Always:(float)CompareFunction.Equal);
    }

    void OnApplicationPause(bool paused){if(paused)LoseTravelTracking();}
    void OnApplicationFocus(bool focused){if(!focused && !Application.isEditor)LoseTravelTracking();}
}
