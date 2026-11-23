using System.Globalization;

namespace ProxmoxProvisioner.Provisioning;

/// <summary>Maps a <see cref="VpsSpec"/> to the qemu config keys Proxmox expects.</summary>
public static class CloudInitSettings
{
    public static IReadOnlyDictionary<string, string> From(VpsSpec spec)
    {
        var settings = new Dictionary<string, string>
        {
            ["cores"] = spec.Cores.ToString(CultureInfo.InvariantCulture),
            ["memory"] = spec.MemoryMb.ToString(CultureInfo.InvariantCulture),
            ["ciuser"] = spec.Username,
            ["sshkeys"] = EncodeSshKeys(spec.SshPublicKeys),
            ["ipconfig0"] = IpConfig(spec.Network),
        };

        if (spec.Nameservers.Count > 0)
        {
            settings["nameserver"] = string.Join(' ', spec.Nameservers);
        }

        return settings;
    }

    // Proxmox wants the key list URL-encoded inside the (also URL-encoded) form value,
    // with %20 for spaces. Sending it once-encoded fails with "invalid format - invalid urlencoded string".
    internal static string EncodeSshKeys(IEnumerable<string> keys) =>
        Uri.EscapeDataString(string.Join('\n', keys.Select(key => key.Trim())));

    private static string IpConfig(StaticNetwork? network) => network switch
    {
        null => "ip=dhcp",
        { IsIPv6: true } => $"ip6={network.AddressCidr},gw6={network.Gateway}",
        _ => $"ip={network.AddressCidr},gw={network.Gateway}",
    };
}
