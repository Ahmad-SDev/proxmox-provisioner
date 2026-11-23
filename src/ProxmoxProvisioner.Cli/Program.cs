using ProxmoxProvisioner.Api;
using ProxmoxProvisioner.Cli;
using ProxmoxProvisioner.Provisioning;

if (args.Length == 0 || args.Contains("--help"))
{
    Console.WriteLine(CommandLineOptions.Usage);
    return args.Length == 0 ? 2 : 0;
}

VpsSpec spec;
ProxmoxConnection connection;
try
{
    spec = CommandLineOptions.ParseSpec(args);
    connection = new ProxmoxConnection(
        new Uri(RequiredEnvironment("PROXMOX_URL")),
        RequiredEnvironment("PROXMOX_TOKEN_ID"),
        RequiredEnvironment("PROXMOX_TOKEN_SECRET"),
        Environment.GetEnvironmentVariable("PROXMOX_FINGERPRINT"));
}
catch (Exception error) when (error is ArgumentException or FormatException or IOException or UriFormatException)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

using var httpClient = connection.CreateHttpClient();
var api = new ProxmoxApiClient(httpClient);
var progress = new Progress<ProvisioningStep>(step => Console.WriteLine($"  {step}..."));
var provisioner = new VpsProvisioner(api, new ProxmoxTaskWaiter(api), progress);

try
{
    var vps = await provisioner.ProvisionAsync(spec, cancellation.Token);
    Console.WriteLine($"VM {vps.VmId} '{vps.Name}' is running on {vps.Node}" +
                      (vps.IpAddress is null ? " (DHCP)" : $" at {vps.IpAddress}"));
    return 0;
}
catch (Exception error) when (error is ProvisioningException or ProxmoxApiException or HttpRequestException or ArgumentException)
{
    Console.Error.WriteLine($"error: {error.Message}");
    return 1;
}

static string RequiredEnvironment(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new ArgumentException($"set the {name} environment variable");
