namespace ProxmoxProvisioner.Api;

/// <summary>
/// Polls a Proxmox task until it ends. Gives up after <c>timeout</c> (15 minutes by default), so a task stuck on a
/// locked storage or a hung node fails the run (and triggers the rollback) instead of waiting forever.
/// </summary>
public sealed class ProxmoxTaskWaiter(IProxmoxApi api, TimeSpan pollInterval, TimeProvider timeProvider, TimeSpan? timeout = null)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

    private readonly TimeSpan _timeout = timeout ?? DefaultTimeout;

    public ProxmoxTaskWaiter(IProxmoxApi api)
        : this(api, TimeSpan.FromSeconds(2), TimeProvider.System)
    {
    }

    public async Task WaitForSuccessAsync(ProxmoxTask task, string description, CancellationToken cancellationToken)
    {
        var deadline = timeProvider.GetUtcNow() + _timeout;
        while (true)
        {
            var status = await api.GetTaskStatusAsync(task, cancellationToken).ConfigureAwait(false);
            if (!status.IsRunning)
            {
                if (!status.Succeeded)
                {
                    throw new ProxmoxApiException($"{description} failed: {status.ExitStatus ?? "unknown error"} ({task.Upid})");
                }
                return;
            }

            if (timeProvider.GetUtcNow() >= deadline)
            {
                throw new ProxmoxApiException($"{description} did not finish within {_timeout.TotalMinutes:0.#} minutes ({task.Upid})");
            }

            await Task.Delay(pollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
