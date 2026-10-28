using QuantumTransport.Core;

internal static class CompatibilityChecks
{
    internal static void Run(Action<bool, string> check)
    {
        string root = Path.Combine(Path.GetTempPath(), "QuantumCompatibility-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string assembly = Path.Combine(root, "GameAssembly.dll");
        try
        {
            check(!BuildCompatibility.IsSupported(root, out string missing) && missing.Contains("文件缺失") && missing.Contains("GameAssembly.dll"),
                "missing game assembly is distinguished from another fingerprint");
            File.WriteAllText(assembly, "abc");
            check(BuildCompatibility.HashFile(assembly) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                "fingerprint hashes actual file bytes using SHA256");
            check(!BuildCompatibility.IsSupported(root, out string mismatch) && mismatch.Contains("指纹不匹配") && mismatch.Contains(BuildCompatibility.ExpectedSteamBuild)
                && mismatch.Contains(BuildCompatibility.HashFile(assembly)) && mismatch.Contains(BuildCompatibility.ExpectedAssemblyHash),
                "unknown assembly stays disabled and reports expected and actual fingerprints");
            check(File.ReadAllText(assembly) == "abc", "compatibility diagnostics leave checked game file unchanged");
        }
        finally
        {
            if (File.Exists(assembly)) File.Delete(assembly);
            Directory.Delete(root);
        }
    }
}
