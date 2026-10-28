using Il2CppMoonCleaners;
using QuantumTransport.Core;

namespace QuantumTransport;

internal static class NativeChecks
{
    internal static void Run(QuantumMod mod)
    {
        int passed = 0;
        void Check(bool ok, string label)
        {
            if (!ok) throw new InvalidOperationException("Native check failed: " + label);
            passed++;
        }
        void Near(double actual, double expected, string label) => Check(Math.Abs(actual - expected) < 1e-7, label);

        // Isolated real IL2CPP game objects: never registered with the world or its save.
        var chamber = MoonWorld.NewProduction(900001, MoonWorld.PressureChamberKind);
        var press = MoonWorld.NewProduction(900002, MoonWorld.StonePressKind);
        int chamberSlot = MoonWorld.InputSlot(MoonWorld.PressureChamberKind, MoonCargo.Dust);
        int pressSlot = MoonWorld.InputSlot(MoonWorld.StonePressKind, MoonCargo.Dust);
        Check(chamberSlot >= 0 && pressSlot >= 0, "dust accepted");
        Check((int)MoonWorld.InputSlot(MoonWorld.PressureChamberKind, MoonCargo.Sand) < 0, "chamber rejects sand");
        Check((int)MoonWorld.InputSlot(MoonWorld.StonePressKind, MoonCargo.Stone) < 0, "press rejects stone");
        Check((int)MoonWorld.InputSlot(MoonWorld.StonePressKind, MoonCargo.Sand) != pressSlot, "sand and dust use different slots");

        var source = new MoonVehicleStorageSnapshot { id = "quantum.nativecheck", kilograms = 6, capacity = 100 };
        Receiver[] receivers = [new(900001, chamberSlot, chamber), new(900002, pressSlot, press)];
        double moved = TransportManager.TransferPackager(source, "quantum-owner-a", receivers, _ => true);
        Near(moved, 6, "supply transferred");
        Near(source.kilograms, 0, "source debited");
        Near(chamber.Stored(chamberSlot), 3, "first receiver fair share");
        Near(press.Stored(pressSlot), 3, "second receiver fair share");
        Near(chamber.stores[chamberSlot].get_Item("quantum-owner-a"), 3, "owner attributed");

        double cap = chamber.Capacity(chamberSlot);
        MoonWorld.Put(chamber.stores[chamberSlot], "existing-owner", cap - (double)chamber.Stored(chamberSlot));
        source.kilograms = 7;
        Near(TransportManager.TransferPackager(source, "quantum-owner-b", [receivers[0]], _ => true), 0, "full receiver blocked");
        Near(source.kilograms, 7, "blocked supply retained");
        Near(TransportManager.TransferPackager(source, "quantum-owner-b", [receivers[1]], _ => false), 0, "removed receiver blocked");
        Near(source.kilograms, 7, "removed receiver cannot debit");
        Near(TransportManager.TransferPackager(source, "quantum-owner-b", [], _ => true), 0, "no receiver blocked");
        Near(source.kilograms, 7, "unroutable supply retained");
        mod.Log.Info($"NATIVE CHECKS PASS: {passed} assertions on real IL2CPP input matching, source debit, capacity, fairness and contributor ownership. Isolated objects only; world state untouched.");
    }
}
