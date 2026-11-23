using ProxmoxProvisioner.Api;

namespace ProxmoxProvisioner.Tests;

/// <summary>In-memory Proxmox that records calls and can be told to fail a specific task.</summary>
internal sealed class FakeProxmoxApi : IProxmoxApi
{
    private readonly Dictionary<string, ProxmoxTaskStatus> _taskResults = [];

    public List<string> Calls { get; } = [];

    public IReadOnlyDictionary<string, string>? AppliedConfig { get; private set; }

    public string? FailingTaskType { get; set; }

    public bool ResizeIsSynchronous { get; set; }

    public Task<int> GetNextVmIdAsync(CancellationToken cancellationToken) => Task.FromResult(105);

    public Task<ProxmoxTask> CloneVmAsync(string node, int templateId, int newVmId, string name, CancellationToken cancellationToken) =>
        Record($"clone {templateId}->{newVmId} {name}", node, "qmclone");

    public Task UpdateVmConfigAsync(string node, int vmId, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        Calls.Add($"config {vmId}");
        AppliedConfig = settings;
        return Task.CompletedTask;
    }

    public async Task<ProxmoxTask?> ResizeDiskAsync(string node, int vmId, string disk, string size, CancellationToken cancellationToken)
    {
        var task = await Record($"resize {vmId} {disk} {size}", node, "resize");
        return ResizeIsSynchronous ? null : task;
    }

    public Task<ProxmoxTask> StartVmAsync(string node, int vmId, CancellationToken cancellationToken) =>
        Record($"start {vmId}", node, "qmstart");

    public Task<ProxmoxTask> DeleteVmAsync(string node, int vmId, CancellationToken cancellationToken) =>
        Record($"delete {vmId}", node, "qmdestroy");

    public Task<ProxmoxTaskStatus> GetTaskStatusAsync(ProxmoxTask task, CancellationToken cancellationToken) =>
        Task.FromResult(_taskResults[task.Upid]);

    private Task<ProxmoxTask> Record(string call, string node, string taskType)
    {
        Calls.Add(call);
        var upid = $"UPID:{node}:{Calls.Count}:{taskType}:";
        _taskResults[upid] = taskType == FailingTaskType
            ? new ProxmoxTaskStatus(IsRunning: false, ExitStatus: "storage full")
            : new ProxmoxTaskStatus(IsRunning: false, ExitStatus: "OK");
        return Task.FromResult(new ProxmoxTask(node, upid));
    }
}
