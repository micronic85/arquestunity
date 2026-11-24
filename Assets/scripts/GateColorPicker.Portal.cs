using TMPro;
using UnityEngine;

public partial class GateColorPicker
{
    GatePortal _portal;
    TextMeshPro _portalToggleText,_portalWorldText;

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
        if(_portalWorldText && _portal)
            _portalWorldText.text=_portal.worldKind==GatePortal.PortalWorldKind.Earth?"Внутри: Земля":"Внутри: Лофт";
        if(_portalToggleText && _portal)
        {
            _portalToggleText.text=_portal.portalEnabled?"Портал: вкл":"Портал: выкл";
            _portalToggleText.color=_portal.portalEnabled?ColAccent:ColText;
        }
    }
}
