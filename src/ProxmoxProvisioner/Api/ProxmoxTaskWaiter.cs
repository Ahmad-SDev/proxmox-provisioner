namespace ProxmoxProvisioner.Api;

public sealed class ProxmoxTaskWaiter(IProxmoxApi api, TimeSpan pollInterval, TimeProvider timeProvider)
{
    public ProxmoxTaskWaiter(IProxmoxApi api)
        : this(api, TimeSpan.FromSeconds(2), TimeProvider.System)
    {
    }

    public async Task WaitForSuccessAsync(ProxmoxTask task, string description, CancellationToken cancellationToken)
    {
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

            await Task.Delay(pollInterval, timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }
}
