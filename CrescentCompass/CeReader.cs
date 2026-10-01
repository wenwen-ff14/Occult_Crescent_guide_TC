using CrescentCompass.Core;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace CrescentCompass;

internal static class CeReader
{
    // Read-only, framework thread, using the bundled TC API 13 structs rather than copied offsets.
    internal static unsafe bool TryRead(out List<CeObservation> observations)
    {
        observations = [];
        if (Plugin.Client.TerritoryType != CeCooldownTracker.Territory) return false;
        var content = PublicContentOccultCrescent.GetInstance();
        if (content == null || !content->StateLoaded) return false;
        foreach (ref var entry in content->DynamicEventContainer.Events)
        {
            if (CeCooldownTracker.Find(entry.DynamicEventId) is null) continue;
            var phase = entry.State switch
            {
                DynamicEventState.Inactive => CePhase.Inactive,
                DynamicEventState.Register => CePhase.Register,
                DynamicEventState.Warmup => CePhase.Warmup,
                DynamicEventState.Battle => CePhase.Battle,
                _ => (CePhase?)null,
            };
            if (phase is null || entry.Progress > 100) return false;
            observations.Add(new(entry.DynamicEventId, phase.Value, entry.Progress));
        }
        return observations.Count > 0 && observations.Select(o => o.Id).Distinct().Count() == observations.Count;
    }
}
