using System.Security.Cryptography;

namespace QuantumTransport.Core;

public enum DiagnosticLevel { Off, Error, Warn, Info, Debug }

public static class BuildCompatibility
{
    public const string ExpectedAssemblyHash = "82b2e7f2a049c332e836fd9b492ce2fa16c1600de1d276b4b79742daf15adea0";
    public const string ExpectedMetadataHash = "f5ac72f32a8d9f5992ec793265b6dec0092a7358c342194fd0afb4e193119c1b";

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    public static bool IsSupported(string gameDirectory, out string reason)
    {
        try
        {
            string assembly = Path.Combine(gameDirectory, "GameAssembly.dll");
            string metadata = Path.Combine(gameDirectory, "MoonSweepers_Data/il2cpp_data/Metadata/global-metadata.dat");
            if (!File.Exists(assembly) || !File.Exists(metadata))
            { reason = "找不到游戏核心文件或元数据。"; return false; }
            if (HashFile(assembly) != ExpectedAssemblyHash || HashFile(metadata) != ExpectedMetadataHash)
            { reason = "游戏文件与本次支持的 Steam Build 26203101 不一致。量子运输未启用，请先更新适配。"; return false; }
            reason = "Steam Build 26203101 / metadata v108 指纹匹配。";
            return true;
        }
        catch (Exception error) { reason = "游戏文件检查失败：" + error.Message; return false; }
    }
}
