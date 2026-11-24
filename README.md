# proxmox-provisioner

Creates a ready-to-use VPS on Proxmox VE from a cloud-init template, through the API and an API token. The steps are the same ones a hosting panel runs when a customer orders a server:

1. take the next free VM id
2. full-clone the template
3. set cores, RAM, user, SSH keys, IP and DNS through cloud-init
4. grow the boot disk
5. start the VM and wait for every Proxmox task to finish

If any step after the clone fails, the half-built VM is destroyed, so a retry starts clean and you don't end up with orphaned disks. A Proxmox task that hangs (locked storage, busy node) counts as a failure after 15 minutes instead of blocking forever.

Written in C# (.NET 10) with no NuGet dependencies outside the test project. It pairs with [proxmox-cloudinit-templates](https://github.com/Ahmad-SDev/proxmox-cloudinit-templates), which builds the templates.

## Usage

Create an API token (Datacenter → Permissions → API Tokens) with `VM.Clone`, `VM.Config.*`, `VM.PowerMgmt`, `VM.Allocate` and `Datastore.AllocateSpace`, then:

```bash
export PROXMOX_URL=https://pve1.example.com:8006
export PROXMOX_TOKEN_ID='provisioner@pve!api'
export PROXMOX_TOKEN_SECRET=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
export PROXMOX_FINGERPRINT=AB:CD:...   # only if the node uses a self-signed certificate

dotnet run --project src/ProxmoxProvisioner.Cli -- \
  --name web01 --node pve1 --template 9000 \
  --cores 2 --memory 2048 --disk 40 \
  --ip 203.0.113.50/24 --gateway 203.0.113.1 --dns 1.1.1.1 \
  --ssh-key-file ~/.ssh/id_ed25519.pub
```

```
  Cloning...
  Configuring...
  ResizingDisk...
  Starting...
VM 105 'web01' is running on pve1 at 203.0.113.50
```

Leave out `--ip` and `--gateway` for DHCP. IPv6 addresses work the same way.

Self-signed certificates are trusted only when they match the pinned `PROXMOX_FINGERPRINT`. There is no "ignore TLS errors" switch.

## Using it as a library

```csharp
using var http = connection.CreateHttpClient();
var api = new ProxmoxApiClient(http);
var provisioner = new VpsProvisioner(api, new ProxmoxTaskWaiter(api));

var vps = await provisioner.ProvisionAsync(new VpsSpec
{
    Name = "web01",
    Node = "pve1",
    TemplateId = 9000,
    Username = "admin",
    SshPublicKeys = [publicKey],
    DiskSizeGb = 40,
});
```

`VpsProvisioner` only talks to `IProxmoxApi`, so the tests run against an in-memory fake, and you can wrap the real client with retries or logging without touching the provisioning logic.

## Project layout

```
src/ProxmoxProvisioner/Api            HTTP client, API token auth, task polling
src/ProxmoxProvisioner/Provisioning   VpsSpec validation, cloud-init mapping, provisioning flow
src/ProxmoxProvisioner.Cli            command-line front end
tests/ProxmoxProvisioner.Tests        xUnit tests
```

## Development

```bash
dotnet test
```

## License

MIT
