using System.Net;
using System.Text;
using ProxmoxProvisioner.Api;

namespace ProxmoxProvisioner.Tests;

public sealed class ProxmoxApiClientTests : IDisposable
{
    private readonly RecordingHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly ProxmoxApiClient _client;

    public ProxmoxApiClientTests()
    {
        _httpClient = new HttpClient(_handler) { BaseAddress = new Uri("https://pve.test:8006/api2/json/") };
        _client = new ProxmoxApiClient(_httpClient);
    }

    public void Dispose() => _httpClient.Dispose();

    [Fact]
    public async Task Next_vm_id_is_parsed_from_string_data()
    {
        _handler.Respond("""{"data":"105"}""");

        Assert.Equal(105, await _client.GetNextVmIdAsync(CancellationToken.None));
        Assert.Equal("https://pve.test:8006/api2/json/cluster/nextid", _handler.LastRequestUri);
    }

    [Fact]
    public async Task Clone_posts_a_full_clone_and_returns_the_task()
    {
        const string upid = "UPID:pve1:0000ABCD:00001234:65000000:qmclone:9000:root@pam!prov:";
        _handler.Respond($$"""{"data":"{{upid}}"}""");

        var task = await _client.CloneVmAsync("pve1", 9000, 105, "web01", CancellationToken.None);

        Assert.Equal(new ProxmoxTask("pve1", upid), task);
        Assert.Equal(HttpMethod.Post, _handler.LastMethod);
        Assert.EndsWith("/nodes/pve1/qemu/9000/clone", _handler.LastRequestUri, StringComparison.Ordinal);
        Assert.Equal("newid=105&name=web01&full=1", _handler.LastBody);
    }

    [Fact]
    public async Task Resize_without_a_task_id_returns_null()
    {
        _handler.Respond("""{"data":null}""");

        Assert.Null(await _client.ResizeDiskAsync("pve1", 105, "scsi0", "40G", CancellationToken.None));
    }

    [Fact]
    public async Task Task_status_reports_exit_status()
    {
        _handler.Respond("""{"data":{"status":"stopped","exitstatus":"OK"}}""");

        var status = await _client.GetTaskStatusAsync(new ProxmoxTask("pve1", "UPID:x"), CancellationToken.None);

        Assert.True(status.Succeeded);
    }

    [Fact]
    public async Task Http_errors_include_the_proxmox_reason()
    {
        _handler.Respond("""{"data":null}""", HttpStatusCode.Forbidden, "Permission check failed (/vms/9000, VM.Clone)");

        var error = await Assert.ThrowsAsync<ProxmoxApiException>(
            () => _client.CloneVmAsync("pve1", 9000, 105, "web01", CancellationToken.None));

        Assert.Contains("VM.Clone", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Connection_sends_the_api_token_header()
    {
        var connection = new ProxmoxConnection(new Uri("https://pve.test:8006"), "root@pam!prov", "secret-uuid");

        using var client = connection.CreateHttpClient();

        Assert.Equal("PVEAPIToken root@pam!prov=secret-uuid", client.DefaultRequestHeaders.Authorization?.ToString());
        Assert.Equal(new Uri("https://pve.test:8006/api2/json/"), client.BaseAddress);
    }

    [Fact]
    public void Fingerprints_are_accepted_with_or_without_colons()
    {
        Assert.Equal("AB12CD", ProxmoxConnection.NormalizeFingerprint(" AB:12:CD "));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private string _responseBody = "{}";
        private HttpStatusCode _statusCode = HttpStatusCode.OK;
        private string? _reasonPhrase;

        public HttpMethod? LastMethod { get; private set; }

        public string? LastRequestUri { get; private set; }

        public string? LastBody { get; private set; }

        public void Respond(string body, HttpStatusCode statusCode = HttpStatusCode.OK, string? reasonPhrase = null)
        {
            _responseBody = body;
            _statusCode = statusCode;
            _reasonPhrase = reasonPhrase;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_statusCode)
            {
                ReasonPhrase = _reasonPhrase,
                Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
