namespace Lynx.Protocol;

public static class RelayDiscoveryProtocol
{
    public const string Request = "LYNX_DISCOVER_V1";
    public const string ResponsePrefix = "LYNX_RELAY_V1|";

    public static string CreateResponse(int relayPort) =>
        $"{ResponsePrefix}{relayPort}";

    public static bool TryParseResponse(string value, out int relayPort)
    {
        relayPort = 0;
        if (!value.StartsWith(ResponsePrefix, StringComparison.Ordinal))
            return false;

        return int.TryParse(value[ResponsePrefix.Length..], out relayPort) &&
            relayPort is > 0 and <= 65535;
    }
}