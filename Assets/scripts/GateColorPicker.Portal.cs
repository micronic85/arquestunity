using TMPro;
using UnityEngine;

public partial class GateColorPicker
{
    GatePortal _portal;
    TextMeshPro _portalToggleText,_portalWorldText,_portalAutoText;
    public bool PortalMenuVisible=>_menuVisible;
    public bool OwnsMenuRenderer(Renderer renderer)=>_menu && renderer.transform.IsChildOf(_menu);
    public void OnPortalTravelChanged(bool inside)
    {
        _tab=Tab.Gate;
        if(_menu)RebuildMenu();
        SetStatus(inside?"Космос · Земля управляется руками":"Вы в реальной комнате");
    }

    float BuildPortalInteriorButtons(float y,float width,float height,float gap)
    {
        PanelButton("PortalReturnBtn",new Vector3(0,y-height*.5f,0),width,height,
            out var back,()=>_portal.ReturnToRoom());back.text="Вернуться в комнату";
        y-=height+gap;
        PanelButton("PlanetAutoBtn",new Vector3(0,y-height*.5f,0),width,height,
            out _portalAutoText,()=>{_portal.SetEarthAutoSpin(!_portal.earthAutoSpin);RefreshPortalButtons();});
        y-=height+gap;
        PanelButton("PlanetResetBtn",new Vector3(0,y-height*.5f,0),width,height,
            out var reset,()=>{_portal.ResetEarth();RefreshPortalButtons();});reset.text="Сбросить Землю";
        RefreshPortalButtons();return y-height;
    }

    float BuildPortalButtons(float y,float width,float height,float gap)
    {
        if(!_portal) return y;
        y-=gap;
        float half=(width-gap)*.5f;
        PanelButton("PortalToggleBtn",new Vector3(-(half+gap)*.5f,y-height*.5f,0),half,height,
            out _portalToggleText,()=>
            {
                _portal.SetPortalEnabled(!_portal.portalEnabled);
                RefreshPortalButtons();
                SetStatus(_portal.portalEnabled?"Портал появится за открытыми воротами":"Портал выключен");
            });
        PanelButton("PortalReplayBtn",new Vector3((half+gap)*.5f,y-height*.5f,0),half,height,
            out var replayText,()=>
            {
                _portal.ReplayAppearance();RefreshPortalButtons();
                SetStatus("Открываем ворота с порталом");
            });
        replayText.text="Повторить";
        RefreshPortalButtons();
        y-=height+gap;
        PanelButton("PortalWorldBtn",new Vector3(0,y-height*.5f,0),width,height,
            out _portalWorldText,()=>
            {
                _portal.SetWorldKind(_portal.worldKind==GatePortal.PortalWorldKind.Earth
                    ?GatePortal.PortalWorldKind.Loft:GatePortal.PortalWorldKind.Earth);
                RefreshPortalButtons();
            });
        RefreshPortalButtons();
        return y-height;
    }

    void RefreshPortalButtons()
    {
        if(_portalAutoText && _portal)_portalAutoText.text=_portal.earthAutoSpin?"Автовращение: вкл":"Автовращение: выкл";
        if(_portalWorldText && _portal)
            _portalWorldText.text=_portal.worldKind==GatePortal.PortalWorldKind.Earth?"Внутри: Земля":"Внутри: Лофт";
        if(_portalToggleText && _portal)
        {
            _portalToggleText.text=_portal.portalEnabled?"Портал: вкл":"Портал: выкл";
            _portalToggleText.color=_portal.portalEnabled?ColAccent:ColText;
        }
    }
}
