using System.Text.Json;

namespace QuantumTransport.Core;

public sealed record CoopFrame(int Type, string Session, int Id, string Owner, double Kilograms, double Capacity,
    float[] Position, float[] Up, float[] Rotation, bool PhysicalPose, byte[] PlayerState,
    string VehicleStorageId="",double VehicleKilograms=0,double VehicleCapacity=0)
{
    public const int MaxBytes = 60 * 1024;
    private static readonly byte[] Magic = [0x51, 0x54, 0x48, 0x35];
    public static bool IsFrame(ReadOnlySpan<byte> bytes) => bytes.Length >= 4 && bytes[..4].SequenceEqual(Magic);
    public byte[] Encode()
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(this);
        if (json.Length + 4 > MaxBytes) throw new InvalidOperationException("Co-op handoff exceeds bounded native packet size");
        return [..Magic, ..json];
    }
    public static bool TryDecode(byte[] bytes, out CoopFrame? frame)
    {
        frame=null;
        if (bytes.Length > MaxBytes || !IsFrame(bytes)) return false;
        try
        {
            frame=JsonSerializer.Deserialize<CoopFrame>(bytes.AsSpan(4), new JsonSerializerOptions { MaxDepth=8 });
            if (frame is null || frame.Type is not (1 or 2) || frame.Id <= 0 || !Guid.TryParseExact(frame.Session,"N",out _)) return false;
            if (frame.Type == 2) return true;
            return SourceRegistry.ValidOwner(frame.Owner) && double.IsFinite(frame.Kilograms) && frame.Kilograms > FlowPlanner.Epsilon &&
                double.IsFinite(frame.Capacity) && frame.Capacity >= frame.Kilograms && frame.Capacity <= 1000 &&
                frame.Position is {Length:3} && frame.Up is {Length:3} && frame.Rotation is {Length:4} &&
                frame.Position.Concat(frame.Up).Concat(frame.Rotation).All(float.IsFinite) &&
                frame.Up.Sum(x=>(double)x*x)>1e-6 && frame.PlayerState is {Length:>0 and <=32768} &&
                frame.VehicleStorageId is {Length:<=512} && double.IsFinite(frame.VehicleKilograms) && double.IsFinite(frame.VehicleCapacity) &&
                (frame.VehicleStorageId.Length==0 ? frame.VehicleKilograms==0 && frame.VehicleCapacity==0 :
                    frame.VehicleKilograms>=0 && frame.VehicleCapacity>=frame.VehicleKilograms && frame.VehicleCapacity is >0 and <=1_000_000);
        }
        catch (JsonException) { frame=null; return false; }
    }
    public static CoopFrame Ack(string session, int id) => new(2,session,id,"",0,0,[],[],[],false,[]);
}
