using System.Globalization;
using ProxmoxProvisioner.Provisioning;

namespace ProxmoxProvisioner.Cli;

internal static class CommandLineOptions
{
    public const string Usage = """
        usage: proxmox-provision --name <hostname> --node <node> --template <id> --ssh-key-file <path> [options]

        options:
          --user <name>          cloud-init user (default: admin)
          --cores <n>            vCPU cores (default: 1)
          --memory <mb>          RAM in MB (default: 1024)
          --disk <gb>            grow the boot disk to this size
          --ip <address/prefix>  static address, e.g. 203.0.113.50/24 (default: DHCP)
          --gateway <address>    gateway for --ip
          --dns <address>        nameserver, can be repeated

        environment:
          PROXMOX_URL            https://pve1.example.com:8006
          PROXMOX_TOKEN_ID       user@pam!provisioner
          PROXMOX_TOKEN_SECRET   token UUID
          PROXMOX_FINGERPRINT    optional SHA-256 fingerprint to trust a self-signed certificate
        """;

    public static VpsSpec ParseSpec(IReadOnlyList<string> args)
    {
        var values = ParseFlags(args);

        string Required(string flag) =>
            values.TryGetValue(flag, out var found) ? found[^1] : throw new ArgumentException($"missing --{flag}");

        string? Optional(string flag) => values.TryGetValue(flag, out var found) ? found[^1] : null;

        int? OptionalNumber(string flag) =>
            Optional(flag) is { } text ? int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture) : null;

        var ip = Optional("ip");
        var gateway = Optional("gateway");
        if ((ip is null) != (gateway is null))
        {
            throw new ArgumentException("--ip and --gateway must be used together");
        }

        return new VpsSpec
        {
            Name = Required("name"),
            Node = Required("node"),
            TemplateId = int.Parse(Required("template"), NumberStyles.None, CultureInfo.InvariantCulture),
            Username = Optional("user") ?? "admin",
            Cores = OptionalNumber("cores") ?? 1,
            MemoryMb = OptionalNumber("memory") ?? 1024,
            DiskSizeGb = OptionalNumber("disk"),
            SshPublicKeys = ReadKeys(Required("ssh-key-file")),
            Network = ip is null ? null : new StaticNetwork(ip, gateway!),
            Nameservers = values.TryGetValue("dns", out var dns) ? dns : [],
        };
    }

    private static Dictionary<string, List<string>> ParseFlags(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index += 2)
        {
            var flag = args[index];
            if (!flag.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count)
            {
                throw new ArgumentException($"expected '--option value' but got '{flag}'");
            }

            var name = flag[2..];
            if (!values.TryGetValue(name, out var list))
            {
                values[name] = list = [];
            }
            list.Add(args[index + 1]);
        }
        return values;
    }

    private static List<string> ReadKeys(string path) =>
        File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();
}
