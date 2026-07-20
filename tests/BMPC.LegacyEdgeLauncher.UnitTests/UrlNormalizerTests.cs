using BMPC.LegacyEdgeLauncher.Core.Validation;
using Xunit;

namespace BMPC.LegacyEdgeLauncher.UnitTests;

/// <summary>URL normalization, unsafe-scheme rejection, and pre-launch safety (spec §14.1, §20.1).</summary>
public class UrlNormalizerTests
{
    [Theory]
    [InlineData("cbs.natcco.coop/barbazampc/logon.asp", "https://cbs.natcco.coop/barbazampc/logon.asp")]
    [InlineData("https://cbs.natcco.coop/barbazampc/", "https://cbs.natcco.coop/barbazampc/")]
    [InlineData("HTTP://Server-Name:8080/Old-App", "http://server-name:8080/Old-App")]
    public void TryNormalize_ProducesCanonicalUrl(string input, string expected)
    {
        var ok = UrlNormalizer.TryNormalize(input, out var uri, out var error);

        Assert.True(ok, error);
        Assert.NotNull(uri);
        Assert.Equal(expected, uri!.AbsoluteUri);
    }

    [Fact]
    public void TryNormalize_AddsHttpsWhenSchemeMissing()
    {
        Assert.True(UrlNormalizer.TryNormalize("cbs.natcco.coop", out var uri, out _));
        Assert.Equal("https", uri!.Scheme);
    }

    [Fact]
    public void TryNormalize_StripsFragment()
    {
        Assert.True(UrlNormalizer.TryNormalize("https://host.local/app#section", out var uri, out _));
        Assert.Equal(string.Empty, uri!.Fragment);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryNormalize_RejectsEmptyInput(string? input)
    {
        Assert.False(UrlNormalizer.TryNormalize(input, out var uri, out var error));
        Assert.Null(uri);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("file:///c:/windows/system32/cmd.exe")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>1</script>")]
    [InlineData("ftp://host.local/file")]
    public void TryNormalize_RejectsUnsafeSchemes(string input)
    {
        // Rejected either as an invalid URI or by the http/https scheme allowlist — both are safe outcomes.
        Assert.False(UrlNormalizer.TryNormalize(input, out var uri, out var error));
        Assert.Null(uri);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void NormalizedHost_OmitsDefaultPort()
    {
        UrlNormalizer.TryNormalize("https://cbs.natcco.coop/x", out var uri, out _);
        Assert.Equal("cbs.natcco.coop", UrlNormalizer.NormalizedHost(uri!));
    }

    [Fact]
    public void NormalizedHost_KeepsNonDefaultPort()
    {
        UrlNormalizer.TryNormalize("http://server-name:8080/x", out var uri, out _);
        Assert.Equal("server-name:8080", UrlNormalizer.NormalizedHost(uri!));
    }

    [Theory]
    [InlineData("/barbazampc", "/barbazampc")]
    [InlineData("barbazampc/", "/barbazampc")]
    [InlineData("/barbazampc/logon.asp?x=1#f", "/barbazampc/logon.asp")]
    [InlineData("/", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizePathRule_Canonicalizes(string? input, string? expected)
    {
        Assert.Equal(expected, UrlNormalizer.NormalizePathRule(input));
    }

    [Fact]
    public void IsSafeToLaunch_AllowsHttps()
    {
        var uri = new System.Uri("https://cbs.natcco.coop/barbazampc/logon.asp");
        Assert.True(UrlNormalizer.IsSafeToLaunch(uri, out var reason));
        Assert.Empty(reason);
    }

    [Fact]
    public void IsSafeToLaunch_BlocksUnsafeScheme()
    {
        var uri = new System.Uri("file:///c:/x.txt");
        Assert.False(UrlNormalizer.IsSafeToLaunch(uri, out var reason));
        Assert.Contains("Unsafe scheme", reason);
    }
}
