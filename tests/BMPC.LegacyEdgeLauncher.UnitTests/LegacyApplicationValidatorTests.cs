using BMPC.LegacyEdgeLauncher.Core.Enums;
using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Core.Validation;
using Xunit;

namespace BMPC.LegacyEdgeLauncher.UnitTests;

/// <summary>FluentValidation rules for registering/editing an application (spec §9.3, §20.1).</summary>
public class LegacyApplicationValidatorTests
{
    private readonly LegacyApplicationValidator _validator = new();

    private static LegacyApplication ValidApp() => new()
    {
        Name = "NATCCO EKoopBanker+ CASA",
        BaseUrl = "https://cbs.natcco.coop/barbazampc/logon.asp",
        NormalizedHost = "cbs.natcco.coop",
        PathRule = "/barbazampc",
        OpenIn = OpenInMode.IE11,
        CompatibilityMode = nameof(CompatMode.Default)
    };

    [Fact]
    public void Valid_Application_Passes()
    {
        Assert.True(_validator.Validate(ValidApp()).IsValid);
    }

    [Fact]
    public void Missing_Name_Fails()
    {
        var app = ValidApp();
        app.Name = "";
        var result = _validator.Validate(app);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LegacyApplication.Name));
    }

    [Fact]
    public void Invalid_Url_Fails()
    {
        var app = ValidApp();
        app.BaseUrl = "javascript:alert(1)";
        Assert.False(_validator.Validate(app).IsValid);
    }

    [Fact]
    public void Missing_NormalizedHost_Fails()
    {
        var app = ValidApp();
        app.NormalizedHost = "";
        Assert.False(_validator.Validate(app).IsValid);
    }

    [Fact]
    public void InferPathRule_UsesTheBaseUrlPath()
    {
        var uri = new Uri("https://cbs.natcco.coop/barbazampc/webaccounting?screen=home");

        Assert.Equal("/barbazampc/webaccounting", UrlNormalizer.InferPathRule(uri));
    }

    [Fact]
    public void InferPathRule_ReturnsNullForHostRoot()
    {
        Assert.Null(UrlNormalizer.InferPathRule(new Uri("https://cbs.natcco.coop/")));
    }
}
