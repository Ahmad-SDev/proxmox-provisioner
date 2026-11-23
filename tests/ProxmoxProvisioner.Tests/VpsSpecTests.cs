using ProxmoxProvisioner.Provisioning;

namespace ProxmoxProvisioner.Tests;

public sealed class VpsSpecTests
{
    private static readonly VpsSpec ValidSpec = new()
    {
        Name = "db-01",
        Node = "pve1",
        TemplateId = 9000,
        Username = "admin",
        SshPublicKeys = ["ssh-ed25519 AAAA admin"],
    };

    [Fact]
    public void Valid_spec_has_no_errors() => Assert.Empty(ValidSpec.Validate());

    [Theory]
    [InlineData("203.0.113.50", "203.0.113.1")]
    [InlineData("203.0.113.50/33", "203.0.113.1")]
    [InlineData("203.0.113.50/24", "2001:db8::1")]
    [InlineData("not-an-ip/24", "203.0.113.1")]
    public void Invalid_static_network_is_rejected(string address, string gateway)
    {
        var spec = ValidSpec with { Network = new StaticNetwork(address, gateway) };

        Assert.Single(spec.Validate());
    }

    [Fact]
    public void Ipv6_network_uses_ip6_keys()
    {
        var spec = ValidSpec with { Network = new StaticNetwork("2001:db8::50/64", "2001:db8::1") };

        Assert.Empty(spec.Validate());
        Assert.Equal("ip6=2001:db8::50/64,gw6=2001:db8::1", CloudInitSettings.From(spec)["ipconfig0"]);
    }

    [Fact]
    public void Dhcp_is_used_when_no_network_is_given() =>
        Assert.Equal("ip=dhcp", CloudInitSettings.From(ValidSpec)["ipconfig0"]);

    [Fact]
    public void Ssh_keys_are_url_encoded_with_percent_20_for_spaces()
    {
        var encoded = CloudInitSettings.EncodeSshKeys(["ssh-ed25519 AAAA one", " ssh-rsa BBBB two "]);

        Assert.Equal("ssh-ed25519%20AAAA%20one%0Assh-rsa%20BBBB%20two", encoded);
    }
}
