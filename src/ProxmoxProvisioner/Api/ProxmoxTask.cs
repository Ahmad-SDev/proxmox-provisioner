namespace ProxmoxProvisioner.Api;

/// <summary>A background task on a Proxmox node, identified by its UPID.</summary>
public sealed record ProxmoxTask(string Node, string Upid);

public sealed record ProxmoxTaskStatus(bool IsRunning, string? ExitStatus)
{
    public bool Succeeded => !IsRunning && ExitStatus == "OK";
}

public sealed class ProxmoxApiException : Exception
{
    public ProxmoxApiException()
    {
    }

    public ProxmoxApiException(string message)
        : base(message)
    {
    }

    public ProxmoxApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
