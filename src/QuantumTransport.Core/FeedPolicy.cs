namespace QuantumTransport.Core;

public enum DustDistribution { RecipeBalanced, CapacityFair }

/// <summary>Limits future transfers only; no existing inventory is reclaimed or discarded.</summary>
public static class FeedPolicy
{
    // The supported game's stone recipe consumes equal masses of dust and sand.
    // Sand in an active batch is already committed, so only unprocessed sand counts here.
    public static double PressDustSpace(double dustStored, double dustCapacity, double sandStored,
        double bufferKilograms, DustDistribution distribution)
    {
        Nonnegative(dustStored, nameof(dustStored));
        Nonnegative(dustCapacity, nameof(dustCapacity));
        Nonnegative(sandStored, nameof(sandStored));
        Nonnegative(bufferKilograms, nameof(bufferKilograms));
        if (!Enum.IsDefined(distribution)) throw new ArgumentOutOfRangeException(nameof(distribution));
        double target = distribution == DustDistribution.CapacityFair ? dustCapacity :
            Math.Min(dustCapacity, sandStored + bufferKilograms);
        return Math.Max(0, target - dustStored);
    }

    public static double TransferableSupply(double totalStored, double reservedKilograms)
    {
        Nonnegative(totalStored, nameof(totalStored));
        Nonnegative(reservedKilograms, nameof(reservedKilograms));
        return Math.Max(0, totalStored - reservedKilograms);
    }

    private static void Nonnegative(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(name);
    }
}
