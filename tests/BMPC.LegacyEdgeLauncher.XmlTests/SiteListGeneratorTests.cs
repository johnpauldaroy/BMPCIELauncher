using System.Xml.Linq;
using BMPC.LegacyEdgeLauncher.Core.Enums;
using BMPC.LegacyEdgeLauncher.Core.Interfaces;
using BMPC.LegacyEdgeLauncher.Core.Models;
using BMPC.LegacyEdgeLauncher.Infrastructure.SiteList;
using Xunit;

namespace BMPC.LegacyEdgeLauncher.XmlTests;

/// <summary>Enterprise Mode Site List XML generation (spec §6, §20.1, §36).</summary>
public class SiteListGeneratorTests
{
    private readonly SiteListGenerator _generator = new();

    private static LegacyApplication Natcco(bool enabled = true) => new()
    {
        Name = "NATCCO EKoopBanker+ CASA",
        BaseUrl = "https://cbs.natcco.coop/barbazampc/logon.asp",
        NormalizedHost = "cbs.natcco.coop",
        PathRule = "/barbazampc",
        OpenIn = OpenInMode.IE11,
        CompatibilityMode = nameof(CompatMode.Default),
        IsEnabled = enabled
    };

    [Fact]
    public void Generate_ProducesWellFormedXml_WithVersionAndNatccoRule()
    {
        var doc = _generator.Generate(new[] { Natcco() }, System.Array.Empty<NeutralSite>(), version: 7);

        var xml = XDocument.Parse(doc.Xml);
        Assert.Equal("site-list", xml.Root!.Name.LocalName);
        Assert.Equal("7", xml.Root.Attribute("version")!.Value);

        var site = Assert.Single(xml.Root.Elements("site"));
        Assert.Equal("cbs.natcco.coop/barbazampc", site.Attribute("url")!.Value);
        Assert.Equal("IE11", site.Element("open-in")!.Value);
        Assert.Equal("Default", site.Element("compat-mode")!.Value);
    }

    [Fact]
    public void Generate_ContentIsParseableUnicodeSafe()
    {
        // The in-memory Xml is the declaration-free element body (XDocument.ToString drops the
        // declaration); the publisher writes it to disk as UTF-8 bytes. Verify the body round-trips,
        // including a non-ASCII character, so nothing is mangled before publishing.
        var app = Natcco();
        app.Name = "Café Ledgér";
        var doc = _generator.Generate(new[] { app }, System.Array.Empty<NeutralSite>(), 1);

        var reparsed = XDocument.Parse(doc.Xml);
        Assert.Equal("site-list", reparsed.Root!.Name.LocalName);
    }

    [Fact]
    public void Generate_ExcludesDisabledApplications()
    {
        var doc = _generator.Generate(new[] { Natcco(enabled: false) }, System.Array.Empty<NeutralSite>(), 1);
        var xml = XDocument.Parse(doc.Xml);
        Assert.Empty(xml.Root!.Elements("site"));
        Assert.Empty(doc.Entries);
    }

    [Fact]
    public void Generate_DeduplicatesIdenticalRuleUrls()
    {
        var a = Natcco();
        var b = Natcco();
        b.Name = "Duplicate rule";

        var doc = _generator.Generate(new[] { a, b }, System.Array.Empty<NeutralSite>(), 1);
        Assert.Single(doc.Entries);
    }

    [Fact]
    public void Generate_EscapesXmlSpecialCharactersInUrl()
    {
        var app = Natcco();
        app.NormalizedHost = "host.local";
        app.PathRule = "/app?a=1&b=2";

        var doc = _generator.Generate(new[] { app }, System.Array.Empty<NeutralSite>(), 1);

        // Raw ampersand must be escaped in the serialized text, but parse back to the literal value.
        Assert.Contains("&amp;", doc.Xml);
        var url = XDocument.Parse(doc.Xml).Root!.Element("site")!.Attribute("url")!.Value;
        Assert.Contains("a=1&b=2", url);
    }

    [Fact]
    public void Generate_NeutralSiteEmittedAsOpenInNone()
    {
        var neutral = new NeutralSite { NormalizedHost = "login.example.local", IsEnabled = true };
        var doc = _generator.Generate(new[] { Natcco() }, new[] { neutral }, 1);

        var xml = XDocument.Parse(doc.Xml);
        var neutralSite = xml.Root!.Elements("site")
            .Single(s => s.Attribute("url")!.Value == "login.example.local");
        Assert.Equal("None", neutralSite.Element("open-in")!.Value);
        Assert.Null(neutralSite.Element("compat-mode")); // compat-mode only emitted for IE11
    }

    [Fact]
    public void Generate_OmitsCompatModeForNonIe11Rules()
    {
        var app = Natcco();
        app.OpenIn = OpenInMode.MSEdge;

        var doc = _generator.Generate(new[] { app }, System.Array.Empty<NeutralSite>(), 1);
        var site = XDocument.Parse(doc.Xml).Root!.Element("site")!;
        Assert.Equal("MSEdge", site.Element("open-in")!.Value);
        Assert.Null(site.Element("compat-mode"));
    }
}
