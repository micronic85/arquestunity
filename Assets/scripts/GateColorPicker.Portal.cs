using TMPro;
using UnityEngine;

public partial class GateColorPicker
{
    GatePortal _portal;
    TextMeshPro _portalToggleText;

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
        return y-height;
    }

    void RefreshPortalButtons()
    {
        if(_portalToggleText && _portal)
        {
            _portalToggleText.text=_portal.portalEnabled?"Портал: вкл":"Портал: выкл";
            _portalToggleText.color=_portal.portalEnabled?ColAccent:ColText;
        }
    }
}
