using System.Security.Cryptography;
using System.Text.Json;

namespace QuantumTransport.Core;

public enum DiagnosticLevel { Off, Error, Warn, Info, Debug }

public static class BuildCompatibility
{
    public static string ExpectedSteamBuild { get; }
    public static int ExpectedMetadataVersion { get; }
    public static string ExpectedAssemblyHash { get; }
    public static string ExpectedMetadataHash { get; }

    static BuildCompatibility()
    {
        using var stream = typeof(BuildCompatibility).Assembly.GetManifestResourceStream("QuantumTransport.GameCompatibility.json")
            ?? throw new InvalidOperationException("Missing embedded game compatibility profile");
        using var document = JsonDocument.Parse(stream);
        var profile = document.RootElement;
        ExpectedSteamBuild = profile.GetProperty("steamBuildId").GetString()!;
        ExpectedMetadataVersion = profile.GetProperty("metadataVersion").GetInt32();
        var files = profile.GetProperty("gameFiles").EnumerateArray().ToArray();
        ExpectedAssemblyHash = files.Single(f => f.GetProperty("path").GetString() == "GameAssembly.dll").GetProperty("sha256").GetString()!;
        ExpectedMetadataHash = files.Single(f => f.GetProperty("path").GetString() == "MoonSweepers_Data/il2cpp_data/Metadata/global-metadata.dat").GetProperty("sha256").GetString()!;
    }

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
            foreach (var (path, expected) in new[] { (assembly, ExpectedAssemblyHash), (metadata, ExpectedMetadataHash) })
            {
                if (!File.Exists(path)) { reason = "游戏文件缺失：" + Path.GetFileName(path); return false; }
                string actual = HashFile(path);
                if (actual != expected)
                {
                    reason = $"游戏文件指纹不匹配：{Path.GetFileName(path)}；期望 Steam Build {ExpectedSteamBuild}，SHA256={expected}，实际 SHA256={actual}。量子运输未启用。";
                    return false;
                }
            }
            reason = $"Steam Build {ExpectedSteamBuild} / metadata v{ExpectedMetadataVersion} 指纹匹配。";
            return true;
        }
        catch (Exception error) { reason = "游戏文件检查失败：" + error.Message; return false; }
    }
}
