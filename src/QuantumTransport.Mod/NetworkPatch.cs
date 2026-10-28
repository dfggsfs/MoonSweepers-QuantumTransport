using System.Reflection;
using HarmonyLib;
using Il2CppMoonCleaners;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using QuantumTransport.Core;

namespace QuantumTransport;

[HarmonyPatch]
internal static class NetworkPatch
{
    // Only the native network entry point is intercepted. Its uint64 and pointer
    // arguments use integer registers on Windows x64; no float/production detours.
    internal static MethodBase TargetMethod() => typeof(MoonCrewSession).GetMethod(nameof(MoonCrewSession.HandleWorld),
        [typeof(Il2CppSystem.UInt64), typeof(Il2CppArrayRank1<Il2CppSystem.Byte>)])!;

    private static bool Prefix(MoonCrewSession __instance, Il2CppSystem.UInt64 __0, Il2CppArrayRank1<Il2CppSystem.Byte> __1)
    {
        if ((int)__1.Length < 4 || (byte)__1[0]!=0x51 || (byte)__1[1]!=0x54 || (byte)__1[2]!=0x48 || (byte)__1[3]!=0x35) return true;
        if ((int)__1.Length > CoopFrame.MaxBytes) return false;
        var bytes=new byte[(int)__1.Length];
        for(int i=0;i<bytes.Length;i++)bytes[i]=__1[i];
        QuantumMod.Instance.ReceiveNetwork(__instance, __0, bytes);
        return false;
    }
}
