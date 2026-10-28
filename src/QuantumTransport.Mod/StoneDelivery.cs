using Il2CppMoonCleaners;
using QuantumTransport.Core;
using GameString = Il2CppSystem.String;

namespace QuantumTransport;

internal static class StoneDelivery
{
    internal static bool HasStation(MoonWorld world)
    {
        if (world.collector != null) return true;
        var stations = world.Stations;
        if (stations != null)
            for (int i = 0; i < (int)stations.Count; i++)
                if (stations[i] != null) return true;
        return false;
    }

    internal static CommittedDelivery Apply(MoonWorld world, ResourceSource source)
    {
        if (source.Resource != ResourceKind.Stone || source.Kind is not ("finished_product" or "network_capsule") || !SourceRegistry.ValidOwner(source.Owner))
            throw new InvalidOperationException("Only attributed finished stone may enter station delivery");
        GameString owner = source.Owner;
        var contributions = world.pendingContributions;
        var premiums = world.premiums;
        if (contributions == null || premiums == null) throw new InvalidOperationException("Station accounting is not initialized");
        bool hadContribution = contributions.ContainsKey(owner), hadPremium = premiums.ContainsKey(owner);
        var contribution = hadContribution ? contributions.get_Item(owner) : default;
        var premium = hadPremium ? premiums.get_Item(owner) : default;
        double deposited = world.depositedKilograms;
        bool dirty = world.progressDirty;

        // CrewCargoDeposit has no event/network callbacks: it clamps to the original station capacity,
        // calls AttributeDeposit + AddPremium, increments depositedKilograms, and sets progressDirty.
        // Restore precisely those accounting fields if credit/debit or verification fails.
        var result = DeliveryCommit.Apply(source.Read, source.Write, () => (double)world.StoredStationKilograms,
            amount => (double)world.CrewCargoDeposit(owner, MoonCargo.Stone, amount),
            () =>
            {
                world.depositedKilograms = deposited;
                if (hadContribution) contributions.set_Item(owner, contribution); else contributions.Remove(owner);
                if (hadPremium) premiums.set_Item(owner, premium); else premiums.Remove(owner);
                world.progressDirty = dirty;
            });
        if (result.Kilograms > 0) source.Cleanup?.Invoke();
        return result;
    }
}
