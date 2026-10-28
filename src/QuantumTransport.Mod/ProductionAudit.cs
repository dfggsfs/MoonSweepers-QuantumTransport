using Il2CppMoonCleaners;

namespace QuantumTransport;

internal sealed record ProcessorAudit(int Id, int Kind, bool Running, float Progress, float Power, float DemandKw, float UsedKw, int Cycles,
    float LastInputTime, double[] StoredKilograms, double[] CapacityKilograms);

// Read existing states only: diagnostics must never create processors or start cycles.
internal sealed record ProductionAudit(DateTimeOffset Utc, float UnityDeltaTime, float ProductionNow,
    float? ProductionTimeAdvanced, bool SavePending, bool SaveFaulted, int TotalProcessors, ProcessorAudit[] Processors,
    double StationStoredKilograms)
{
    internal static ProductionAudit Read(MoonWorld world, ProductionAudit? previous)
    {
        var processors = new List<ProcessorAudit>();
        int total = 0;
        if (world.production != null)
        {
            var states = world.production.GetEnumerator();
            while ((bool)states.MoveNext())
            {
                var pair = states.Current;
                var state = pair.Value;
                if (state == null || state.stores == null) continue;
                if ((int)state.Kind != (int)MoonWorld.PressureChamberKind && (int)state.Kind != (int)MoonWorld.StonePressKind) continue;
                total++;
                if (processors.Count >= 32) continue;
                int slots = (int)state.stores.Length;
                processors.Add(new((int)pair.Key, (int)state.Kind, (bool)state.Running,
                    (float)state.Progress, (float)state.Power, (float)state.DemandKw, (float)state.UsedKw, (int)state.Cycles, (float)state.lastInput,
                    Enumerable.Range(0, slots).Select(i => (double)state.Stored(i)).ToArray(),
                    Enumerable.Range(0, slots).Select(i => (double)state.Capacity(i)).ToArray()));
            }
        }
        var save = world.PendingSave;
        float now = world.productionNow;
        return new(DateTimeOffset.UtcNow, (float)UnityEngine.Time.deltaTime, now,
            previous == null ? null : now - previous.ProductionNow,
            save != null && !(bool)save.IsCompleted, save != null && (bool)save.IsFaulted, total, processors.ToArray(),
            (double)world.StoredStationKilograms);
    }
}
