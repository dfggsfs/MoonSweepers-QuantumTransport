using Il2CppMoonCleaners;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using QuantumTransport.Core;
using Writer=Il2CppSystem.IO.BinaryWriter;
using Stream=Il2CppSystem.IO.MemoryStream;
using Reader=Il2CppSystem.IO.BinaryReader;

namespace QuantumTransport;

internal static class NativeWire
{
    private static byte[] Copy(Il2CppArrayRank1<Il2CppSystem.Byte> source)
    {
        var bytes=new byte[(int)source.Length];
        for(int i=0;i<bytes.Length;i++)bytes[i]=source[i];
        return bytes;
    }
    private static Il2CppArrayRank1<Il2CppSystem.Byte> ToNative(byte[] bytes)
    {
        var array=new Il2CppArrayRank1<Il2CppSystem.Byte>(bytes.Length);
        for(int i=0;i<bytes.Length;i++)array[i]=bytes[i];
        return array;
    }
    private static Stream ToStream(byte[] bytes)
    {
        return new Stream(ToNative(bytes));
    }
    internal static CoopFrame Create(MoonWorld world, MoonDroppedCanister c, string session,ResourceSource source)
    {
        var player=new Stream(); var pw=new Writer(player);
        if (!(bool)world.WriteCrewPlayerState(pw)) throw new InvalidOperationException("Client source state not ready");
        pw.Flush();
        string storage=source.Kind=="vehicle"?source.StorageId??throw new InvalidOperationException("Vehicle storage identity missing"):"";
        double kg=storage.Length>0?(double)world.vehicleStorage.get_Item(storage).kilograms:0;
        double capacity=storage.Length>0?(double)world.vehicleStorage.get_Item(storage).capacity:0;
        return new(1,session,c.id,c.owner,c.kilograms,c.capacity,
            [(float)c.position.x,(float)c.position.y,(float)c.position.z],
            [(float)c.up.x,(float)c.up.y,(float)c.up.z],
            [(float)c.rotation.x,(float)c.rotation.y,(float)c.rotation.z,(float)c.rotation.w],
            c.physicalPose,Copy(player.ToArray()),storage,kg,capacity);
    }
    internal static bool Send(MoonCrewSession session, ulong to, byte[] bytes) =>
        session.SendBytes((Il2CppSystem.UInt64)to, ToNative(bytes), bytes.Length, MoonCrewSession.WorldChannel, true);

    internal static void Apply(MoonWorld world, string player, CoopFrame f)
    {
        // One reliable packet carries both source reports and cargo. These native
        // operations execute together on the main thread before a new save snapshot.
        world.ApplyCrewPlayerState(player,new Reader(ToStream(f.PlayerState)));
        if(f.VehicleStorageId.Length>0)
        {
            // The vanilla report omits starter/non-garage prototypes. Carry this
            // one checked donor snapshot in the same atomic handoff as its cargo.
            if((bool)world.vehicleStorage.ContainsKey(f.VehicleStorageId))
                world.vehicleStorage.get_Item(f.VehicleStorageId).kilograms=f.VehicleKilograms;
            else world.vehicleStorage.set_Item(f.VehicleStorageId,new MoonVehicleStorageSnapshot
                {id=f.VehicleStorageId,kilograms=f.VehicleKilograms,capacity=f.VehicleCapacity});
        }
        var c=new MoonDroppedCanister
        {
            id=f.Id,owner=f.Owner,kilograms=f.Kilograms,capacity=f.Capacity,
            position=new UnityEngine.Vector3(f.Position[0],f.Position[1],f.Position[2]),
            up=new UnityEngine.Vector3(f.Up[0],f.Up[1],f.Up[2]),
            rotation=new UnityEngine.Quaternion(f.Rotation[0],f.Rotation[1],f.Rotation[2],f.Rotation[3]),
            physicalPose=f.PhysicalPose,createdAt=UnityEngine.Time.time,cargo=MoonCargo.Dust
        };
        if (!(bool)world.ApplyCrewCanister(MoonWorld.CanisterOp.Upsert,c,player))
            throw new InvalidOperationException("Native host rejected handed-off cargo");
        world.progressDirty=true;
    }
}
