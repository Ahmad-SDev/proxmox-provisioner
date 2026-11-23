using ProxmoxProvisioner.Api;
using ProxmoxProvisioner.Provisioning;

namespace ProxmoxProvisioner.Tests;

public sealed class VpsProvisionerTests
{
    private readonly FakeProxmoxApi _api = new();

    private static VpsSpec Spec(int? diskSizeGb = 40) => new()
    {
        Name = "web01",
        Node = "pve1",
        TemplateId = 9000,
        Username = "admin",
        Cores = 2,
        MemoryMb = 2048,
        DiskSizeGb = diskSizeGb,
        SshPublicKeys = ["ssh-ed25519 AAAAC3Nza admin@laptop"],
        Network = new StaticNetwork("203.0.113.50/24", "203.0.113.1"),
    };

    private VpsProvisioner CreateProvisioner() =>
        new(_api, new ProxmoxTaskWaiter(_api, TimeSpan.Zero, TimeProvider.System));

    [Fact]
    public async Task Clones_configures_resizes_and_starts_in_order()
    {
        var vps = await CreateProvisioner().ProvisionAsync(Spec());

        Assert.Equal(
            ["clone 9000->105 web01", "config 105", "resize 105 scsi0 40G", "start 105"],
            _api.Calls);
        Assert.Equal(new ProvisionedVps(105, "web01", "pve1", "203.0.113.50"), vps);
    }

    [Fact]
    public async Task Skips_resize_when_no_disk_size_is_given()
    {
        await CreateProvisioner().ProvisionAsync(Spec(diskSizeGb: null));

        Assert.DoesNotContain(_api.Calls, call => call.StartsWith("resize", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Handles_proxmox_versions_where_resize_is_synchronous()
    {
        _api.ResizeIsSynchronous = true;

        await CreateProvisioner().ProvisionAsync(Spec());

        Assert.Equal("start 105", _api.Calls[^1]);
    }

    [Fact]
    public async Task Removes_the_vm_when_a_later_step_fails()
    {
        _api.FailingTaskType = "qmstart";

        var error = await Assert.ThrowsAsync<ProvisioningException>(() => CreateProvisioner().ProvisionAsync(Spec()));

        Assert.Equal("delete 105", _api.Calls[^1]);
        Assert.Contains("storage full", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_delete_anything_when_the_clone_itself_fails()
    {
        _api.FailingTaskType = "qmclone";

        await Assert.ThrowsAsync<ProxmoxApiException>(() => CreateProvisioner().ProvisionAsync(Spec()));

        Assert.Equal(["clone 9000->105 web01"], _api.Calls);
    }

    [Fact]
    public async Task Rejects_an_invalid_spec_before_calling_proxmox()
    {
        var invalid = Spec() with { Name = "Web_01", SshPublicKeys = [] };

        var error = await Assert.ThrowsAsync<ArgumentException>(() => CreateProvisioner().ProvisionAsync(invalid));

        Assert.Contains("hostname", error.Message, StringComparison.Ordinal);
        Assert.Contains("SSH public key", error.Message, StringComparison.Ordinal);
        Assert.Empty(_api.Calls);
    }

    [Fact]
    public async Task Applies_cloud_init_settings()
    {
        await CreateProvisioner().ProvisionAsync(Spec());

        Assert.Equal("ip=203.0.113.50/24,gw=203.0.113.1", _api.AppliedConfig!["ipconfig0"]);
        Assert.Equal("2048", _api.AppliedConfig["memory"]);
    }
}
