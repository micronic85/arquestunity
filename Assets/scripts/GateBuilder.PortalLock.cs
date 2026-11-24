public partial class GateBuilder
{
    public bool IsPortalInteractionLocked {get;private set;}
    public void SetPortalInteractionLock(bool locked)
    {
        if(IsPortalInteractionLocked==locked)return;
        IsPortalInteractionLocked=locked;
        GetComponent<GateController>()?.SetPortalSuspended(locked);
    }
}
