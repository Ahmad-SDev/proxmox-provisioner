using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace ProxmoxProvisioner.Provisioning;

public sealed partial record VpsSpec
{
    public required string Name { get; init; }

    public required string Node { get; init; }

    public required int TemplateId { get; init; }

    public required string Username { get; init; }

    public int Cores { get; init; } = 1;

    public int MemoryMb { get; init; } = 1024;

    /// <summary>Final disk size. Leave empty to keep the template's size.</summary>
    public int? DiskSizeGb { get; init; }

    public string Disk { get; init; } = "scsi0";

    public IReadOnlyList<string> SshPublicKeys { get; init; } = [];

    /// <summary>Static address. Leave empty to use DHCP.</summary>
    public StaticNetwork? Network { get; init; }

    public IReadOnlyList<string> Nameservers { get; init; } = [];

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (!HostnamePattern().IsMatch(Name))
        {
            errors.Add($"name '{Name}' must be a valid hostname (lowercase letters, digits and dashes)");
        }
        if (TemplateId < 100)
        {
            errors.Add("template id must be 100 or higher");
        }
        if (Cores is < 1 or > 128)
        {
            errors.Add("cores must be between 1 and 128");
        }
        if (MemoryMb < 256)
        {
            errors.Add("memory must be at least 256 MB");
        }
        if (DiskSizeGb is < 1)
        {
            errors.Add("disk size must be at least 1 GB");
        }
        if (SshPublicKeys.Count == 0)
        {
            errors.Add("add at least one SSH public key, password logins are not configured");
        }
        if (Network is not null)
        {
            errors.AddRange(Network.Validate());
        }

        return errors;
    }

    [GeneratedRegex("^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex HostnamePattern();
}

/// <param name="AddressCidr">The VM's address with prefix length, for example 203.0.113.50/24</param>
public sealed record StaticNetwork(string AddressCidr, string Gateway)
{
    public bool IsIPv6 => AddressCidr.Contains(':', StringComparison.Ordinal);

    public string Address => AddressCidr.Split('/')[0];

    public IReadOnlyList<string> Validate()
    {
        var parts = AddressCidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address))
        {
            return [$"address '{AddressCidr}' must look like 203.0.113.50/24"];
        }

        var maxPrefixLength = address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var prefixLength) ||
            prefixLength > maxPrefixLength)
        {
            return [$"prefix length in '{AddressCidr}' must be between 0 and {maxPrefixLength}"];
        }

        if (!IPAddress.TryParse(Gateway, out var gateway) || gateway.AddressFamily != address.AddressFamily)
        {
            return [$"gateway '{Gateway}' must be an address of the same family as {AddressCidr}"];
        }

        return [];
    }
}
