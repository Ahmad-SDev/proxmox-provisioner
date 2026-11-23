namespace ProxmoxProvisioner.Api;

public interface IProxmoxApi
{
    Task<int> GetNextVmIdAsync(CancellationToken cancellationToken);

    Task<ProxmoxTask> CloneVmAsync(string node, int templateId, int newVmId, string name, CancellationToken cancellationToken);

    Task UpdateVmConfigAsync(string node, int vmId, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken);

    Task<ProxmoxTask?> ResizeDiskAsync(string node, int vmId, string disk, string size, CancellationToken cancellationToken);

    Task<ProxmoxTask> StartVmAsync(string node, int vmId, CancellationToken cancellationToken);

    Task<ProxmoxTask> DeleteVmAsync(string node, int vmId, CancellationToken cancellationToken);

    Task<ProxmoxTaskStatus> GetTaskStatusAsync(ProxmoxTask task, CancellationToken cancellationToken);
}
