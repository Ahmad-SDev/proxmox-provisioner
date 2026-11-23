using System.Globalization;
using System.Text.Json;

namespace ProxmoxProvisioner.Api;

public sealed class ProxmoxApiClient(HttpClient httpClient) : IProxmoxApi
{
    public async Task<int> GetNextVmIdAsync(CancellationToken cancellationToken)
    {
        var data = await SendAsync(HttpMethod.Get, "cluster/nextid", form: null, cancellationToken).ConfigureAwait(false);
        return int.Parse(data.GetString()!, CultureInfo.InvariantCulture);
    }

    public async Task<ProxmoxTask> CloneVmAsync(
        string node, int templateId, int newVmId, string name, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["newid"] = newVmId.ToString(CultureInfo.InvariantCulture),
            ["name"] = name,
            ["full"] = "1",
        };
        var data = await SendAsync(HttpMethod.Post, $"{VmPath(node, templateId)}/clone", form, cancellationToken).ConfigureAwait(false);
        return ToTask(node, data) ?? throw new ProxmoxApiException("clone did not return a task id");
    }

    public Task UpdateVmConfigAsync(
        string node, int vmId, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Put, $"{VmPath(node, vmId)}/config", settings, cancellationToken);

    public async Task<ProxmoxTask?> ResizeDiskAsync(
        string node, int vmId, string disk, string size, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string> { ["disk"] = disk, ["size"] = size };
        var data = await SendAsync(HttpMethod.Put, $"{VmPath(node, vmId)}/resize", form, cancellationToken).ConfigureAwait(false);
        return ToTask(node, data);
    }

    public async Task<ProxmoxTask> StartVmAsync(string node, int vmId, CancellationToken cancellationToken)
    {
        var data = await SendAsync(HttpMethod.Post, $"{VmPath(node, vmId)}/status/start", new Dictionary<string, string>(), cancellationToken)
            .ConfigureAwait(false);
        return ToTask(node, data) ?? throw new ProxmoxApiException("start did not return a task id");
    }

    public async Task<ProxmoxTask> DeleteVmAsync(string node, int vmId, CancellationToken cancellationToken)
    {
        var path = $"{VmPath(node, vmId)}?purge=1&destroy-unreferenced-disks=1";
        var data = await SendAsync(HttpMethod.Delete, path, form: null, cancellationToken).ConfigureAwait(false);
        return ToTask(node, data) ?? throw new ProxmoxApiException("delete did not return a task id");
    }

    public async Task<ProxmoxTaskStatus> GetTaskStatusAsync(ProxmoxTask task, CancellationToken cancellationToken)
    {
        var path = $"nodes/{Uri.EscapeDataString(task.Node)}/tasks/{Uri.EscapeDataString(task.Upid)}/status";
        var data = await SendAsync(HttpMethod.Get, path, form: null, cancellationToken).ConfigureAwait(false);

        var isRunning = data.GetProperty("status").GetString() == "running";
        var exitStatus = data.TryGetProperty("exitstatus", out var exit) ? exit.GetString() : null;
        return new ProxmoxTaskStatus(isRunning, exitStatus);
    }

    private static string VmPath(string node, int vmId) =>
        $"nodes/{Uri.EscapeDataString(node)}/qemu/{vmId.ToString(CultureInfo.InvariantCulture)}";

    private static ProxmoxTask? ToTask(string node, JsonElement data) =>
        data.ValueKind == JsonValueKind.String && data.GetString() is { } upid && upid.StartsWith("UPID:", StringComparison.Ordinal)
            ? new ProxmoxTask(node, upid)
            : null;

    private async Task<JsonElement> SendAsync(
        HttpMethod method, string path, IReadOnlyDictionary<string, string>? form, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new ProxmoxApiException(
                $"{method} {path} failed with {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("data", out var data) ? data.Clone() : default;
    }
}
