using System.Globalization;
using ProxmoxProvisioner.Api;

namespace ProxmoxProvisioner.Provisioning;

public enum ProvisioningStep
{
    Cloning,
    Configuring,
    ResizingDisk,
    Starting,
    RollingBack,
}

public sealed record ProvisionedVps(int VmId, string Name, string Node, string? IpAddress);

public sealed class ProvisioningException(string message, Exception innerException) : Exception(message, innerException);

/// <summary>
/// Clones a cloud-init template into a ready-to-use VPS. If anything fails after the clone,
/// the half-built VM is destroyed so a retry starts clean.
/// </summary>
public sealed class VpsProvisioner(IProxmoxApi api, ProxmoxTaskWaiter taskWaiter, IProgress<ProvisioningStep>? progress = null)
{
    public async Task<ProvisionedVps> ProvisionAsync(VpsSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var errors = spec.Validate();
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join("; ", errors), nameof(spec));
        }

        var vmId = await api.GetNextVmIdAsync(cancellationToken).ConfigureAwait(false);

        progress?.Report(ProvisioningStep.Cloning);
        var clone = await api.CloneVmAsync(spec.Node, spec.TemplateId, vmId, spec.Name, cancellationToken).ConfigureAwait(false);
        await taskWaiter.WaitForSuccessAsync(clone, $"cloning template {spec.TemplateId}", cancellationToken).ConfigureAwait(false);

        try
        {
            await ConfigureAndStartAsync(spec, vmId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            await RollBackAsync(spec.Node, vmId, error).ConfigureAwait(false);
            throw new ProvisioningException($"provisioning VM {vmId} failed and it was removed: {error.Message}", error);
        }

        return new ProvisionedVps(vmId, spec.Name, spec.Node, spec.Network?.Address);
    }

    private async Task ConfigureAndStartAsync(VpsSpec spec, int vmId, CancellationToken cancellationToken)
    {
        progress?.Report(ProvisioningStep.Configuring);
        await api.UpdateVmConfigAsync(spec.Node, vmId, CloudInitSettings.From(spec), cancellationToken).ConfigureAwait(false);

        if (spec.DiskSizeGb is { } diskSizeGb)
        {
            progress?.Report(ProvisioningStep.ResizingDisk);
            var size = diskSizeGb.ToString(CultureInfo.InvariantCulture) + "G";
            var resize = await api.ResizeDiskAsync(spec.Node, vmId, spec.Disk, size, cancellationToken).ConfigureAwait(false);
            if (resize is not null)
            {
                await taskWaiter.WaitForSuccessAsync(resize, $"resizing {spec.Disk}", cancellationToken).ConfigureAwait(false);
            }
        }

        progress?.Report(ProvisioningStep.Starting);
        var start = await api.StartVmAsync(spec.Node, vmId, cancellationToken).ConfigureAwait(false);
        await taskWaiter.WaitForSuccessAsync(start, $"starting VM {vmId}", cancellationToken).ConfigureAwait(false);
    }

    private async Task RollBackAsync(string node, int vmId, Exception originalError)
    {
        progress?.Report(ProvisioningStep.RollingBack);
        try
        {
            // The caller's token may be what got cancelled, so cleanup runs on its own deadline.
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var delete = await api.DeleteVmAsync(node, vmId, cleanupTimeout.Token).ConfigureAwait(false);
            await taskWaiter.WaitForSuccessAsync(delete, $"removing VM {vmId}", cleanupTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception rollbackError)
        {
            throw new ProvisioningException(
                $"provisioning VM {vmId} failed ({originalError.Message}) and removing it also failed, delete it by hand",
                new AggregateException(originalError, rollbackError));
        }
    }
}
