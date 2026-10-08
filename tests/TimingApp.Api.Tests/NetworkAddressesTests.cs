using System.Net;
using TimingApp.Api.Hosting;

namespace TimingApp.Api.Tests;

public sealed class NetworkAddressesTests
{
    private static readonly IPAddress[] Local = [IPAddress.Parse("192.168.1.20"), IPAddress.Parse("10.0.0.5")];

    [Theory]
    [InlineData("http://0.0.0.0:5081")]
    [InlineData("http://[::]:5081")]
    [InlineData("http://+:5081")]
    [InlineData("http://*:5081")]
    public void BaseUrls_WildcardHost_IsExpandedToTheLocalAddresses(string listening) =>
        Assert.Equal(["http://192.168.1.20:5081", "http://10.0.0.5:5081"], NetworkAddresses.BaseUrls([listening], Local));

    [Fact]
    public void BaseUrls_SpecificHost_IsKeptAndDuplicatesRemoved() =>
        Assert.Equal(
            ["http://127.0.0.1:5081", "http://192.168.1.20:5081", "http://10.0.0.5:5081"],
            NetworkAddresses.BaseUrls(["http://127.0.0.1:5081", "http://0.0.0.0:5081", "http://192.168.1.20:5081"], Local));
}
