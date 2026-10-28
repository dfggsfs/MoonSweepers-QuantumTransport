namespace QuantumTransport.Core;

public enum TransportRole { SinglePlayer, Host, Client, Waiting }

public static class CoopPolicy
{
    public static TransportRole Role(bool joined, bool inLobby, bool isHost, int members) =>
        joined ? TransportRole.Client : !inLobby || members <= 1 ? TransportRole.SinglePlayer :
        isHost ? TransportRole.Host : TransportRole.Waiting;

    public static bool MayRead(TransportRole role, string kind, string owner, string? controller, string localPlayer) => role switch
    {
        TransportRole.SinglePlayer => true,
        TransportRole.Host => kind is "player" or "player_container" ? owner == localPlayer :
            kind == "vehicle" ? string.IsNullOrEmpty(controller) || controller == localPlayer : true,
        TransportRole.Client => kind is "player" or "player_container" ? owner == localPlayer :
            kind == "vehicle" && controller == localPlayer,
        _ => false
    };
}

public sealed record CoopAdvertisement(ulong Host, string Session, long Sequence, bool Enabled, double DustDemand,
    int SourceMask)
{
    public const string Key = "qt_native_handoff_v1";
    public string Encode() => FormattableString.Invariant($"1|{Host}|{Session}|{Sequence}|{(Enabled ? 1 : 0)}|{DustDemand:R}|{SourceMask}");
    public static bool TryParse(string? text, ulong expectedHost, out CoopAdvertisement? value)
    {
        value = null;
        if (text is null || text.Length > 256) return false;
        var p = text.Split('|');
        var c = System.Globalization.CultureInfo.InvariantCulture;
        if (p.Length != 7 || p[0] != "1" || !ulong.TryParse(p[1], out ulong host) || host == 0 || host != expectedHost ||
            !Guid.TryParseExact(p[2], "N", out _) || !long.TryParse(p[3], out long sequence) || sequence < 0 ||
            p[4] is not ("0" or "1") || !double.TryParse(p[5], System.Globalization.NumberStyles.Float, c, out double demand) ||
            !double.IsFinite(demand) || demand < 0 || !int.TryParse(p[6], out int mask) || mask is < 0 or > 3) return false;
        value = new(host, p[2], sequence, p[4] == "1", demand, mask);
        return true;
    }
}
