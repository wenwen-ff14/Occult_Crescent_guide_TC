namespace CrescentCompass.Core;

public enum PatrolContextChange { Active, Suspended, Resumed, Reset }

/// <summary>Loading is a pause. Only a confirmed new zone/instance or logout starts a new survey.</summary>
public sealed class PatrolContext
{
    public ushort Territory { get; private set; }
    public uint Instance { get; private set; }
    public bool IsSuspended { get; private set; }
    private (ushort Territory, uint Instance)? landing;
    private long stableSince;
    private long? signedOutSince;

    public void Suspend() { IsSuspended = true; landing = null; }
    public void Logout() { Territory = 0; Instance = 0; IsSuspended = false; landing = null; signedOutSince = null; }

    public PatrolContextChange Update(bool loggedIn, bool loading, bool playerReady, ushort territory, uint instance, long now)
    {
        if (!loggedIn)
        {
            // A transient unavailable login snapshot during a load is not an authoritative logout event.
            if (Territory != 0)
            {
                if (loading) { signedOutSince = null; Suspend(); return PatrolContextChange.Suspended; }
                signedOutSince ??= now;
                if (now - signedOutSince.Value < 5000) { Suspend(); return PatrolContextChange.Suspended; }
            }
            var changed = Territory != 0 || IsSuspended;
            Logout();
            return changed ? PatrolContextChange.Reset : PatrolContextChange.Active;
        }
        signedOutSince = null;
        // Some transition frames report no territory/player or temporarily lose the public-instance number.
        if (loading || !playerReady || territory == 0 || territory == Territory && Instance != 0 && instance == 0)
        { Suspend(); return PatrolContextChange.Suspended; }
        if (IsSuspended)
        {
            if (landing != (territory, instance)) { landing = (territory, instance); stableSince = now; }
            if (now - stableSince < 1000) return PatrolContextChange.Suspended;
        }
        var destination = SpotCatalog.IsSupported(territory) ? territory : (ushort)0;
        var nextInstance = destination == 0 ? 0 : instance;
        var changedSession = destination != Territory || nextInstance != Instance;
        var resumed = IsSuspended;
        Territory = destination; Instance = nextInstance; IsSuspended = false; landing = null;
        return changedSession ? PatrolContextChange.Reset : resumed ? PatrolContextChange.Resumed : PatrolContextChange.Active;
    }
}
