using Il2CppMoonCleaners;
using QuantumTransport.Core;
using GameString = Il2CppSystem.String;

namespace QuantumTransport;

internal static class NativeHandoff
{
    internal sealed record Ticket(int Id, ResourceSource Source, double SourceBefore, string BagOwner, double BagBefore, bool HadBag, bool Dirty);
    internal static Ticket? Pack(MoonWorld world, MoonCrewSession session, ResourceSource source, double amount)
    {
        if (!(bool)MoonCrew.Joined || source.Resource != ResourceKind.Dust || amount <= FlowPlanner.Epsilon) return null;
        var walker = session.local;
        if (walker == null || world.inventoryMass == null || !SourceRegistry.ValidOwner(source.Owner)) return null;
        GameString owner = source.Owner;
        bool hadBag = world.inventoryMass.ContainsKey(owner);
        double bagBefore = hadBag ? (double)world.inventoryMass.get_Item(owner) : 0;
        double ReadBag() => (bool)world.inventoryMass.ContainsKey(owner) ? (double)world.inventoryMass.get_Item(owner) : 0;
        void WriteBag(double value) => world.inventoryMass.set_Item(owner, value);
        bool sameBag = source.Kind == "player";
        var callbacks = world.LocalCanister_BackingField;
        var store = world.progressStore;
        bool dirty = world.progressDirty;
        int id = 0;
        double packed;
        double sourceBefore=source.Read();
        try
        {
            // Native PackDustPortion saves/publishes internally. Defer those two effects
            // until the donor inventory and temporary bag have reached their final state.
            world.LocalCanister_BackingField = null;
            world.progressStore = null;
            packed = HandoffCommit.Apply(amount, sameBag, source.Read, source.Write, ReadBag, WriteBag,
                quantity =>
                {
                    bool ok = world.PackDustPortion(source.Owner, walker!.transform.position, walker.transform.up, quantity, out id);
                    if (!ok) return 0;
                    if (!(bool)world.canisters.ContainsKey(id)) throw new InvalidOperationException("Packed cargo missing from native world");
                    var cargo = world.canisters.get_Item(id);
                    if ((string)cargo.owner != source.Owner || !cargo.cargo.Equals(MoonCargo.Dust))
                        throw new InvalidOperationException("Native cargo identity mismatch");
                    return (double)cargo.kilograms;
                },
                () => { if ((int)id > 0 && (bool)world.canisters.ContainsKey(id)) world.RemoveCanister(id); });
            if (!hadBag && ReadBag() == 0) world.inventoryMass.Remove(owner);
        }
        catch
        {
            if (!hadBag && ReadBag() == 0) world.inventoryMass.Remove(owner);
            world.progressDirty = dirty;
            throw;
        }
        finally
        {
            world.progressStore = store;
            world.LocalCanister_BackingField = callbacks;
        }
        if (packed <= FlowPlanner.Epsilon) return null;
        world.progressDirty = true;
        // Publishing is performed once, with checked send results, after source reports.
        // Do not replay the native packing action on retries.
        return new((int)id,source,sourceBefore,source.Owner,bagBefore,hadBag,dirty);
    }

    internal static void UndoUnsent(MoonWorld world,Ticket ticket)
    {
        world.RemoveCanister(ticket.Id);
        ticket.Source.Write(ticket.SourceBefore);
        if (ticket.HadBag) world.inventoryMass.set_Item(ticket.BagOwner,ticket.BagBefore);
        else world.inventoryMass.Remove(ticket.BagOwner);
        world.progressDirty=ticket.Dirty;
    }
}
