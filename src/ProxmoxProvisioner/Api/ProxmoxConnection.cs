using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ProxmoxProvisioner.Api;

/// <param name="BaseUri">For example https://pve1.example.com:8006/</param>
/// <param name="TokenId">API token id in the form user@realm!token-name</param>
/// <param name="TokenSecret">The token's UUID secret</param>
/// <param name="CertificateSha256Fingerprint">
/// Optional pin for self-signed node certificates (the "Fingerprint" shown under Datacenter > Certificates).
/// </param>
public sealed record ProxmoxConnection(
    Uri BaseUri,
    string TokenId,
    string TokenSecret,
    string? CertificateSha256Fingerprint = null)
{
    public HttpClient CreateHttpClient()
    {
        var client = new HttpClient(CreateHandler(), disposeHandler: true)
        {
            BaseAddress = new Uri(BaseUri, "/api2/json/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("PVEAPIToken", $"{TokenId}={TokenSecret}");
        return client;
    }

    private SocketsHttpHandler CreateHandler()
    {
        var handler = new SocketsHttpHandler();
        if (CertificateSha256Fingerprint is { } pinnedFingerprint)
        {
            var expected = NormalizeFingerprint(pinnedFingerprint);
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                errors == SslPolicyErrors.None || MatchesFingerprint(certificate, expected);
        }
        return handler;
    }

    private static bool MatchesFingerprint(X509Certificate? certificate, string expectedFingerprint) =>
        certificate is not null &&
        string.Equals(certificate.GetCertHashString(HashAlgorithmName.SHA256), expectedFingerprint, StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeFingerprint(string fingerprint) =>
        fingerprint.Replace(":", string.Empty, StringComparison.Ordinal).Trim();
}
