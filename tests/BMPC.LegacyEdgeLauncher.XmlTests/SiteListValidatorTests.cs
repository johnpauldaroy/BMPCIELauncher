using BMPC.LegacyEdgeLauncher.Core.Enums;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Infrastructure.SiteList;
using Xunit;

namespace BMPC.LegacyEdgeLauncher.XmlTests;

/// <summary>Site-list validation before publish: duplicates, wildcards, version match (spec §6.4, §11).</summary>
public class SiteListValidatorTests
{
    private readonly SiteListGenerator _generator = new();
    private readonly SiteListValidator _validator = new();

    private static LegacyApplication App(string host = "cbs.natcco.coop", string? path = "/barbazampc") => new()
    {
        Name = "app",
        BaseUrl = $"https://{host}{path}",
        NormalizedHost = host,
        PathRule = path,
        OpenIn = OpenInMode.IE11,
        CompatibilityMode = nameof(CompatMode.Default),
        IsEnabled = true
    };

    [Fact]
    public void Validate_GeneratedDocument_IsValid()
    {
        var doc = _generator.Generate(new[] { App() }, System.Array.Empty<NeutralSite>(), 3);
        var result = _validator.Validate(doc);
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Fact]
    public void Validate_VersionMismatch_Fails()
    {
        var doc = _generator.Generate(new[] { App() }, System.Array.Empty<NeutralSite>(), 3);
        var tampered = doc with { Version = 99 };
        var result = _validator.Validate(tampered);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Version mismatch"));
    }

    [Fact]
    public void Validate_MalformedXml_Fails()
    {
        var doc = new SiteListDocument(1, "<site-list><site></broken>", System.Array.Empty<SiteListEntry>());
        var result = _validator.Validate(doc);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("well-formed", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WildcardRule_Fails()
    {
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <site-list version="1"><site url="*.natcco.coop"><open-in>IE11</open-in></site></site-list>
            """;
        var doc = new SiteListDocument(1, xml,
            new[] { new SiteListEntry("*.natcco.coop", "IE11", "Default") });
        var result = _validator.Validate(doc);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("wildcard", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_EmptyList_ProducesWarningNotError()
    {
        var doc = _generator.Generate(System.Array.Empty<LegacyApplication>(), System.Array.Empty<NeutralSite>(), 1);
        var result = _validator.Validate(doc);
        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
    }
}
