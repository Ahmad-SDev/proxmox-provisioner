using ProxmoxProvisioner.Api;
using ProxmoxProvisioner.Provisioning;

namespace ProxmoxProvisioner.Tests;

public sealed class ProxmoxTaskWaiterTests
{
    /// <summary>A clock that moves one minute forward every time it is read.</summary>
    private sealed class SteppingClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now = _now.AddMinutes(1);
    }

    private readonly FakeProxmoxApi _api = new() { HangingTaskType = "qmstart" };

    [Fact]
    public async Task Gives_up_on_a_task_that_never_finishes()
    {
        var waiter = new ProxmoxTaskWaiter(_api, TimeSpan.Zero, new SteppingClock(), TimeSpan.FromMinutes(5));
        var task = await _api.StartVmAsync("pve1", 105, CancellationToken.None);

        var error = await Assert.ThrowsAsync<ProxmoxApiException>(
            () => waiter.WaitForSuccessAsync(task, "start VM", CancellationToken.None));

        Assert.Contains("did not finish within 5 minutes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_hung_task_rolls_the_new_vm_back()
    {
        var waiter = new ProxmoxTaskWaiter(_api, TimeSpan.Zero, new SteppingClock(), TimeSpan.FromMinutes(5));
        var provisioner = new VpsProvisioner(_api, waiter);
        var spec = new VpsSpec
        {
            Name = "web01",
            Node = "pve1",
            TemplateId = 9000,
            Username = "admin",
            SshPublicKeys = ["ssh-ed25519 AAAAC3Nza admin@laptop"],
        };

        await Assert.ThrowsAnyAsync<Exception>(() => provisioner.ProvisionAsync(spec));

        Assert.Contains("delete 105", _api.Calls);
    }
}
