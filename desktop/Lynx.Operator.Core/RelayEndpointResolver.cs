using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Lynx.Protocol;

namespace Lynx.Operator.Core;

public sealed record RelayEndpoint(string Host, int Port)
{
    public IPEndPoint ToIPEndPoint() =>
        new(IPAddress.Parse(Host), Port);
}

public static class RelayEndpointResolver
{
    public static async Task<RelayEndpoint?> ResolveAsync(
        string configuredHost,
        int configuredPort,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(configuredHost, "auto", StringComparison.OrdinalIgnoreCase))
        {
            var addresses = await Dns.GetHostAddressesAsync(configuredHost, cancellationToken);
            var address = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? throw new InvalidOperationException($"No IPv4 address found for {configuredHost}.");
            return new RelayEndpoint(address.ToString(), configuredPort);
        }

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.EnableBroadcast = true;
        var request = Encoding.UTF8.GetBytes(RelayDiscoveryProtocol.Request);

        // Fast path for the common development case where relay and operator are
        // running on the same workstation.
        await TrySendAsync(udp, request, IPAddress.Loopback, configuredPort);

        foreach (var target in BroadcastTargets())
            await TrySendAsync(udp, request, target, configuredPort);

        var response = await ReceiveValidResponseAsync(
            udp,
            TimeSpan.FromMilliseconds(Math.Min(350, timeout.TotalMilliseconds)),
            cancellationToken);
        if (response is not null) return response;

        foreach (var target in BoundedLocalSubnetTargets())
            await TrySendAsync(udp, request, target, configuredPort);

        var remaining = timeout - TimeSpan.FromMilliseconds(350);
        if (remaining < TimeSpan.FromMilliseconds(250))
            remaining = TimeSpan.FromMilliseconds(250);

        return await ReceiveValidResponseAsync(udp, remaining, cancellationToken);
    }

    private static async Task TrySendAsync(
        UdpClient udp,
        byte[] payload,
        IPAddress address,
        int port)
    {
        try
        {
            await udp.SendAsync(payload, new IPEndPoint(address, port));
        }
        catch (SocketException)
        {
            // Discovery is best-effort; another target may still succeed.
        }
    }

    private static async Task<RelayEndpoint?> ReceiveValidResponseAsync(
        UdpClient udp,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        while (!timeoutCts.IsCancellationRequested)
        {
            try
            {
                var received = await udp.ReceiveAsync(timeoutCts.Token);
                var text = Encoding.UTF8.GetString(received.Buffer);
                if (!RelayDiscoveryProtocol.TryParseResponse(text, out var port))
                    continue;

                return new RelayEndpoint(
                    received.RemoteEndPoint.Address.ToString(),
                    port);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
            catch (SocketException)
            {
                // Windows may surface ICMP port unreachable during discovery.
            }
        }

        return null;
    }

    private static IReadOnlyCollection<IPAddress> BroadcastTargets()
    {
        var targets = new HashSet<IPAddress> { IPAddress.Broadcast };

        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up ||
                network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var address in network.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork ||
                    address.IPv4Mask is null)
                    continue;

                var ip = address.Address.GetAddressBytes();
                var mask = address.IPv4Mask.GetAddressBytes();
                var broadcast = new byte[4];
                for (var i = 0; i < 4; i++)
                    broadcast[i] = (byte)(ip[i] | ~mask[i]);
                targets.Add(new IPAddress(broadcast));
            }
        }

        return targets;
    }

    internal static IReadOnlyCollection<IPAddress> BoundedLocalSubnetTargets()
    {
        var targets = new HashSet<IPAddress>();

        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up ||
                network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var address in network.GetIPProperties().UnicastAddresses)
            {
                if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                var bytes = address.Address.GetAddressBytes();
                var own = bytes[3];
                for (var last = 1; last <= 254; last++)
                {
                    if (last == own) continue;
                    targets.Add(new IPAddress(new byte[]
                    {
                        bytes[0], bytes[1], bytes[2], (byte)last
                    }));
                }
            }
        }

        return targets;
    }
}
