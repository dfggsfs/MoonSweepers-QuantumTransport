using QuantumTransport.Core;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++;
}
void Near(double actual, double expected, string label) => Check(Math.Abs(actual - expected) < 1e-7, $"{label}: {actual} != {expected}");

var fair = FlowPlanner.Allocate(6, [new(1, 20), new(2, 20)]);
Near(fair.Single(x => x.ReceiverId == 1).Kilograms, 3, "first machine share");
Near(fair.Single(x => x.ReceiverId == 2).Kilograms, 3, "second machine share");
var overflow = FlowPlanner.Allocate(10, [new(1, 1), new(2, 20)]);
Near(overflow.Single(x => x.ReceiverId == 1).Kilograms, 1, "small receiver filled");
Near(overflow.Single(x => x.ReceiverId == 2).Kilograms, 9, "remaining supply reassigned");
Near(FlowPlanner.Allocate(12, [new(1, 2), new(2, 3)]).Sum(x => x.Kilograms), 5, "source retains excess");
Check(FlowPlanner.Allocate(8, [new(1, 0)]).Count == 0, "full receiver must not consume");
Check(FlowPlanner.Allocate(8, []).Count == 0, "missing receiver must not consume");

var random = new Random(5335510);
for (int trial = 0; trial < 2000; trial++)
{
    double supply = random.NextDouble() * 200;
    var receivers = Enumerable.Range(1, random.Next(0, 30)).Select(id => new ReceiverSpace(id, random.NextDouble() * 20)).ToArray();
    var allocations = FlowPlanner.Allocate(supply, receivers);
    double moved = allocations.Sum(x => x.Kilograms);
    Check(moved <= supply + 1e-7, "must not create resource");
    Near(moved, Math.Min(supply, receivers.Sum(x => x.FreeKilograms)), "all available space used");
    foreach (var allocation in allocations)
        Check(allocation.Kilograms > 0 && allocation.Kilograms <= receivers.Single(x => x.Id == allocation.ReceiverId).FreeKilograms + 1e-7, "receiver capacity respected");
}
void Reject(Action action, string label)
{
    bool rejected = false;
    try { action(); } catch (Exception) { rejected = true; }
    Check(rejected, label);
}
Reject(() => FlowPlanner.Allocate(double.NaN, []), "NaN supply rejected");
Reject(() => FlowPlanner.Allocate(double.PositiveInfinity, []), "infinite supply rejected");
Reject(() => FlowPlanner.Allocate(-1, []), "negative supply rejected");
Reject(() => FlowPlanner.Allocate(1, [new(1, double.NaN)]), "NaN capacity rejected");
Reject(() => FlowPlanner.Allocate(1, [new(1, 1), new(1, 2)]), "duplicate receiver rejected");
Near(FlowPlanner.Allocate(0.001, [new(1, 0.001)]).Sum(x => x.Kilograms), 0.001, "fractional mass retained");

double source = 10, target = 2;
var commit = TransferCommit.Apply(3, () => source, value => source=value, () => target, value => target+=value, () => target=2);
Near(source, 7, "commit source");
Near(target, 5, "commit destination");
Near(commit.SourceBefore+commit.TargetBefore, commit.SourceAfter+commit.TargetAfter, "commit conserved");

source=10; target=2;
Reject(() => TransferCommit.Apply(3, () => source, value => source=value, () => target,
    value => { target+=value; throw new InvalidOperationException("injected partial credit failure"); }, () => target=2), "partial destination error surfaced");
Near(source, 10, "source restored after destination failure");
Near(target, 2, "destination restored after partial credit");

source=10; target=2;
bool firstWrite = true;
Reject(() => TransferCommit.Apply(3, () => source, value => { source=value; if(firstWrite) { firstWrite=false; throw new InvalidOperationException("injected debit failure"); } },
    () => target, value => target+=value, () => target=2), "partial source error surfaced");
Near(source, 10, "source restored after partial debit");
Near(target, 2, "destination restored after debit failure");

source=10; target=2;
Reject(() => TransferCommit.Apply(3, () => source, value => source=value, () => target, value => target+=value*2, () => target=2), "double credit rejected");
Near(source, 10, "source restored after wrong credit");
Near(target, 2, "target restored after wrong credit");
Reject(() => TransferCommit.Apply(11, () => source, value => source=value, () => target, value => target+=value, () => target=2), "overdraft rejected");
Near(source, 10, "overdraft cannot change source");
Near(target, 2, "overdraft cannot change target");

ReceiverPort[] ports = [new(1,ResourceKind.Dust,10),new(1,ResourceKind.Sand,4),new(2,ResourceKind.Dust,10),new(3,ResourceKind.Sand,8)];
var sandPlan=ResourceRouter.Allocate(ResourceKind.Sand,99,6,ports);
Near(sandPlan.Sum(x=>x.Kilograms),6,"sand supply routed");
Check(sandPlan.All(x=>x.ReceiverId is 1 or 3),"sand cannot enter dust-only receiver");
var dustPlan=ResourceRouter.Allocate(ResourceKind.Dust,99,6,ports);
Check(dustPlan.All(x=>x.ReceiverId is 1 or 2),"dust cannot enter sand-only receiver");
var selfExcluded=ResourceRouter.Allocate(ResourceKind.Sand,1,6,ports);
Check(selfExcluded.Count==1 && selfExcluded[0].ReceiverId==3,"machine cannot route its output to itself");
Near(ResourceRouter.Allocate(ResourceKind.Stone,99,7,ports).Sum(x=>x.Kilograms),0,"unroutable stone retained");

ReceiverPort[] priorityPorts = [new(1, ResourceKind.Dust, 3, 10), new(2, ResourceKind.Dust, 7, 10), new(3, ResourceKind.Dust, 20, -10)];
var firstTier = ResourceRouter.Allocate(ResourceKind.Dust, 99, 6, priorityPorts);
Near(firstTier.Single(a => a.ReceiverId == 1).Kilograms, 3, "same-priority first share");
Near(firstTier.Single(a => a.ReceiverId == 2).Kilograms, 3, "same-priority second share");
Check(firstTier.All(a => a.ReceiverId != 3), "lower priority waits while higher tier needs supply");
var spilled = ResourceRouter.Allocate(ResourceKind.Dust, 99, 15, priorityPorts);
Near(spilled.Single(a => a.ReceiverId == 3).Kilograms, 5, "lower tier receives remainder after high tier full");
Reject(() => ResourceRouter.Allocate(ResourceKind.Dust, 99, 1, [new(1, ResourceKind.Dust, 1, 1), new(1, ResourceKind.Dust, 1, -1)]), "duplicate across priorities rejected");
Reject(() => ResourceRouter.Allocate(ResourceKind.Dust, 99, 0, [new(1, ResourceKind.Dust, double.NaN)]), "invalid capacity rejected even with no supply");

Near(FeedPolicy.PressDustSpace(0, 12, 0, 1, DustDistribution.RecipeBalanced), 1, "sandless press only preloads buffer");
Near(FeedPolicy.PressDustSpace(1, 12, 0, 1, DustDistribution.RecipeBalanced), 0, "sandless press stops taking dust after buffer");
Near(FeedPolicy.PressDustSpace(8, 12, 0, 1, DustDistribution.RecipeBalanced), 0, "old stock is never reclaimed");
Near(FeedPolicy.PressDustSpace(1, 12, 4, 1, DustDistribution.RecipeBalanced), 4, "new sand unlocks corresponding dust demand");
Near(FeedPolicy.PressDustSpace(0, 12, 12, 1, DustDistribution.RecipeBalanced), 12, "demand still respects physical capacity");
Near(FeedPolicy.PressDustSpace(1, 12, 0, 0, DustDistribution.RecipeBalanced), 0, "zero buffer accepted");
Near(FeedPolicy.PressDustSpace(1, 12, 0, 1, DustDistribution.CapacityFair), 11, "legacy capacity policy available");
var startup = ResourceRouter.Allocate(ResourceKind.Dust, 99, 6,
    [new(1, ResourceKind.Dust, 20), new(2, ResourceKind.Dust, FeedPolicy.PressDustSpace(0, 12, 0, 1, DustDistribution.RecipeBalanced))]);
Near(startup.Single(a => a.ReceiverId == 1).Kilograms, 5, "startup dust leaves supply for sand production");
Near(startup.Single(a => a.ReceiverId == 2).Kilograms, 1, "startup dust cannot fill sandless press");
Near(FeedPolicy.TransferableSupply(10, 4), 6, "source reserve retained");
Near(FeedPolicy.TransferableSupply(2, 4), 0, "insufficient source cannot breach reserve");
Reject(() => FeedPolicy.PressDustSpace(0, 12, double.NaN, 1, DustDistribution.RecipeBalanced), "invalid recipe stock rejected");
Reject(() => FeedPolicy.PressDustSpace(0, 12, 0, -1, DustDistribution.RecipeBalanced), "negative buffer rejected");
Reject(() => FeedPolicy.TransferableSupply(double.PositiveInfinity, 0), "invalid supply rejected by reserve guard");

for (int trial = 0; trial < 2000; trial++)
{
    double supply = random.NextDouble() * 100;
    ReceiverPort[] demands = Enumerable.Range(1, random.Next(1, 15))
        .Select(id => new ReceiverPort(id, ResourceKind.Dust, random.NextDouble() * 20, random.Next(-2, 3))).ToArray();
    var plan = ResourceRouter.Allocate(ResourceKind.Dust, 99, supply, demands);
    var amounts = plan.ToDictionary(a => a.ReceiverId, a => a.Kilograms);
    Near(plan.Sum(a => a.Kilograms), Math.Min(supply, demands.Sum(p => p.FreeKilograms)), "priority planner conserves mass and uses eligible space");
    foreach (var port in demands)
    {
        double moved = amounts.GetValueOrDefault(port.MachineId);
        Check(moved >= 0 && moved <= port.FreeKilograms + 1e-7, "priority planner capacity bound");
        if (moved > FlowPlanner.Epsilon)
            Check(demands.Where(p => p.Priority > port.Priority).All(p => amounts.GetValueOrDefault(p.MachineId) >= p.FreeKilograms - 1e-7), "all higher priority demand satisfied before lower tier");
    }
    double cap = random.NextDouble() * 20;
    double dust = random.NextDouble() * cap;
    double sand = random.NextDouble() * 20;
    double buffer = random.NextDouble() * 12;
    double free = FeedPolicy.PressDustSpace(dust, cap, sand, buffer, DustDistribution.RecipeBalanced);
    Check(free >= 0 && free <= cap - dust + 1e-7, "recipe target stays within physical capacity");
    Check(dust + free <= Math.Max(dust, sand + buffer) + 1e-7, "recipe target cannot overstock beyond matched sand plus buffer");
    double stored = random.NextDouble() * 20;
    double reserve = random.NextDouble() * 20;
    double available = FeedPolicy.TransferableSupply(stored, reserve);
    Near(available + Math.Min(stored, reserve), stored, "reserve and transferable supply account for all stock");
}

// Simulate several owner contributions sharing ONE machine reserve, debiting in sequence.
double[] ownerStocks = [3, 7];
double totalMoved = 0;
for (int i = 0; i < ownerStocks.Length; i++)
{
    double available = FeedPolicy.TransferableSupply(ownerStocks.Sum(), 4);
    double moved = Math.Min(ownerStocks[i], available);
    ownerStocks[i] -= moved;
    totalMoved += moved;
}
Near(totalMoved, 6, "multi-owner packager reserve applied once per machine");
Near(ownerStocks.Sum(), 4, "shared reserve survives all owner transfers");

var identities = new SourceRegistry();
identities.Register("explicit-vehicle-storage", new("owner-a", "vehicle"));
identities.Register("explicit-vehicle-storage", new("owner-a", "vehicle"));
Check(identities.TryResolve("explicit-vehicle-storage", out var resolved) && resolved.Owner == "owner-a", "repeated identity registration resolves once");
Check(!identities.TryResolve("looks-like-robot-123", out _), "unregistered ownership is never inferred from key format");
Check(!identities.TryResolve(null, out _), "uninitialized vehicle storage identifier is safely ignored");
identities.Register("ambiguous", new("owner-a", "robot", 12));
identities.Register("ambiguous", new("owner-b", "robot", 12));
identities.Register("ambiguous", new("owner-a", "robot", 12));
Check(!identities.TryResolve("ambiguous", out _), "conflicting owners stay blocked after subsequent registrations");
identities.Register("invalid-owner", new("", "vehicle"));
Check(!identities.TryResolve("invalid-owner", out _), "empty owner cannot become a source");
Check(!SourceRegistry.ValidOwner(new string('x', 129)), "owner length matches native deposit limit");
Check(!SourceRegistry.IsStoredContainerSource("container-a", "container-a"), "loaded container cannot be counted again as stored inventory");
Check(SourceRegistry.IsStoredContainerSource("container-b", "container-a"), "inactive container can be routed separately");
Check(!SourceRegistry.IsStoredContainerSource(null, "container-a"), "invalid container identifier excluded");
Check(SourceRegistry.UseFarRobotPayload(false, 2), "far rover confirmed payload is a valid source");
Check(!SourceRegistry.UseFarRobotPayload(true, 2), "near/far robot representations cannot both supply cargo");
Check(!SourceRegistry.UseFarRobotPayload(false, 0), "unfinished work without confirmed cargo cannot supply resources");
Check(!SourceRegistry.UseFarRobotPayload(false, double.NaN), "invalid far payload is excluded");

double deliverySource = 10, stationStock = 5;
decimal contribution = 2, premium = 3;
var partialDelivery = DeliveryCommit.Apply(() => deliverySource, value => deliverySource = value, () => stationStock,
    amount => { double accepted = Math.Min(amount, 8 - stationStock); stationStock += accepted; contribution += (decimal)accepted; premium += 2 * (decimal)accepted; return accepted; },
    () => { stationStock = 5; contribution = 2; premium = 3; });
Near(partialDelivery.Kilograms, 3, "partial station capacity accepted");
Near(deliverySource, 7, "only accepted amount debited");
Near(stationStock, 8, "station capacity respected");
Check(contribution == 5 && premium == 9, "native contribution and premium preserved after success");
int credits = 0;
for (int i = 0; i < 100; i++)
{
    var blocked = DeliveryCommit.Apply(() => deliverySource, value => deliverySource = value, () => stationStock,
        _ => 0, () => throw new Exception("no rollback should be needed for normal full station"));
    Near(blocked.Kilograms, 0, "full station repeated poll accepts nothing");
}
Near(deliverySource, 7, "repeated full station poll never clears source");
Check(contribution == 5 && premium == 9, "full station cannot duplicate accounting");
stationStock = 0;
var completedDelivery = DeliveryCommit.Apply(() => deliverySource, value => deliverySource = value, () => stationStock,
    amount => { stationStock += amount; credits++; return amount; }, () => stationStock = 0);
Near(completedDelivery.Kilograms + partialDelivery.Kilograms, 10, "resume after station cleared delivers exactly original supply");
DeliveryCommit.Apply(() => deliverySource, value => deliverySource = value, () => stationStock,
    amount => { credits++; stationStock += amount; return amount; }, () => stationStock = 0);
Check(credits == 1, "already emptied stone source never credits again");

deliverySource = 10; stationStock = 2; contribution = 5; premium = 8;
Reject(() => DeliveryCommit.Apply(() => deliverySource, value => deliverySource = value, () => stationStock,
    _ => { stationStock += 1; contribution += 1; premium += 2; throw new InvalidOperationException("injected partial native credit failure"); },
    () => { stationStock = 2; contribution = 5; premium = 8; }), "partial native deposit failure surfaced");
Near(deliverySource, 10, "partial native failure keeps source");
Near(stationStock, 2, "partial native failure restores station");
Check(contribution == 5 && premium == 8, "partial native failure restores contribution and premium");
bool debitFails = true;
Reject(() => DeliveryCommit.Apply(() => deliverySource,
    value => { deliverySource = value; if (debitFails) { debitFails = false; throw new InvalidOperationException("injected source debit failure"); } },
    () => stationStock, amount => { stationStock += amount; contribution += (decimal)amount; premium += 2 * (decimal)amount; return amount; },
    () => { stationStock = 2; contribution = 5; premium = 8; }), "partial source debit failure surfaced");
Near(deliverySource, 10, "failed source debit restored");
Check(contribution == 5 && premium == 8 && stationStock == 2, "failed debit rolls back all deposit accounting");
Reject(() => DeliveryCommit.Apply(() => deliverySource, value => deliverySource = value, () => stationStock,
    amount => { stationStock += amount * 2; return amount; }, () => stationStock = 2), "duplicate station credit rejected");
Near(deliverySource, 10, "duplicate station credit restores source");
Near(stationStock, 2, "duplicate station credit restores station");
foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, -1, 11 })
{
    Reject(() => DeliveryCommit.Apply(() => deliverySource, value => deliverySource = value, () => stationStock,
        _ => { stationStock += 1; return invalid; }, () => stationStock = 2), "invalid native acceptance rejected");
    Near(deliverySource, 10, "invalid acceptance restores source");
    Near(stationStock, 2, "invalid acceptance restores accounting");
}
var tiny = DeliveryCommit.Apply(() => deliverySource, value => deliverySource = value, () => stationStock,
    _ => { stationStock += 1e-10; contribution += 0.0000000001m; return 1e-10; },
    () => { stationStock = 2; contribution = 5; });
Near(tiny.Kilograms, 0, "sub-epsilon station gap is not credited");
Check(contribution == 5 && deliverySource == 10, "tiny station gap cannot accumulate repeated premium or lose source");
bool incompleteRollback = false;
try
{
    DeliveryCommit.Apply(() => deliverySource, _ => throw new InvalidOperationException("source write failed"),
        () => stationStock, amount => { stationStock += amount; return amount; },
        () => throw new InvalidOperationException("accounting rollback failed"));
}
catch (AggregateException error) { incompleteRollback = error.InnerExceptions.Count >= 3; }
Check(incompleteRollback, "incomplete rollback is fatal and preserves all exception evidence");

for (int trial = 0; trial < 2000; trial++)
{
    double supply = random.NextDouble() * 100;
    double stock = random.NextDouble() * 100;
    double capacity = stock + random.NextDouble() * 100;
    double sourceBalance = supply, stationBalance = stock, totalAccepted = 0;
    for (int poll = 0; poll < 3; poll++)
    {
        double beforeStation = stationBalance;
        var delivered = DeliveryCommit.Apply(() => sourceBalance, value => sourceBalance = value,
            () => stationBalance, amount => { double accepted = Math.Min(amount, Math.Max(0, capacity - stationBalance)); stationBalance += accepted; return accepted; },
            () => stationBalance = beforeStation);
        totalAccepted += delivered.Kilograms;
        Near(sourceBalance + stationBalance, supply + stock, "random delivery conserves mass across repeated polls");
        Check(stationBalance <= capacity + 1e-7 && sourceBalance >= 0, "random delivery respects station capacity and source");
    }
    Near(totalAccepted, Math.Min(supply, capacity - stock), "random station repeated polls accept at most original available supply");
}

CompatibilityChecks.Run(Check);
CoopChecks.Run(Check,Near,Reject);
GarageDashboardChecks.Run(Check,Near,Reject);
DashboardVisualChecks.Run(Check,Near);
GarageDashboardChecks.WritePreviews("artifacts");
DashboardVisualChecks.WriteStates("artifacts");
if(args.Length==2 && args[0]=="--benchmark") CoopChecks.Benchmark(args[1]);
Console.WriteLine($"PASS: {passed} assertions; capacity/recipe/reserve/station regressions, co-op authority and packet validation, 10000 receipt scenarios, 5000 handoffs, 3000 batch allocations, retry/reload model, rollback and rotation. No game process started.");
