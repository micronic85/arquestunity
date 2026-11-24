using UnityEngine;

public partial class GatePortal
{
    [Header("Музыка портала")]
    public AudioClip portalMusic;
    [Range(0,1)] public float musicVolume=.7f;
    [Min(.1f)] public float musicNearDistance=1;
    [Min(.2f)] public float musicFarDistance=8;
    [Range(0,1)] public float musicDistantGain=.025f;
    [Min(.05f)] public float musicFadeSeconds=.5f;
    AudioSource _portalAudio;

    public float MusicGainAtDistance(float distance)
    {
        float near=Mathf.Max(.1f,musicNearDistance);
        float far=Mathf.Max(near+.1f,musicFarDistance);
        float t=Mathf.Clamp01((distance-near)/(far-near));
        return Mathf.Lerp(1,Mathf.Clamp01(musicDistantGain),Mathf.SmoothStep(0,1,t));
    }

    void UpdatePortalAudio(float dt)
    {
        bool audible=isActiveAndEnabled && portalEnabled && !_entrySuppressed && _root && Reveal>.001f && portalMusic;
        if(!_portalAudio && !audible)return;
        if(!_portalAudio)
        {
            // Stereo music uses explicit head-to-portal attenuation. This preserves the track's
            // stereo mix and avoids listener-distance attenuation being applied a second time.
            var node=new GameObject("PortalMusic");node.transform.SetParent(transform,false);
            _portalAudio=node.AddComponent<AudioSource>();
            _portalAudio.playOnAwake=false;_portalAudio.loop=true;_portalAudio.spatialBlend=0;
            _portalAudio.dopplerLevel=0;_portalAudio.volume=0;
        }
        if(_root)_portalAudio.transform.position=_root.position;
        if(_portalAudio.clip!=portalMusic)
        {
            _portalAudio.Stop();_portalAudio.clip=portalMusic;_portalAudio.volume=0;
        }
        ResolveTravelReferences();
        float target=audible && _travelHead
            ?musicVolume*Reveal*MusicGainAtDistance(Vector3.Distance(_travelHead.position,_root.position)):0;
        if(target>0 && !_portalAudio.isPlaying)_portalAudio.Play();
        _portalAudio.volume=Mathf.MoveTowards(_portalAudio.volume,target,Mathf.Max(0,dt)/Mathf.Max(.05f,musicFadeSeconds));
        if(target<=0 && _portalAudio.volume<=.0001f)_portalAudio.Stop();
    }

    void StopPortalAudio()
    {
        if(!_portalAudio)return;
        _portalAudio.Stop();_portalAudio.volume=0;
    }
}
