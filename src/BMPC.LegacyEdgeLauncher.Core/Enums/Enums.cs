namespace BMPC.LegacyEdgeLauncher.Core.Enums;

/// <summary>Deployment environment of a registered legacy application.</summary>
public enum EnvironmentType
{
    Production,
    Testing,
    Development
}

/// <summary>Enterprise Mode Site List open-in engine assignment.</summary>
public enum OpenInMode
{
    IE11,
    MSEdge,
    None,
    Configurable
}

/// <summary>The browser process used when an application is launched.</summary>
public enum BrowserLaunchMode
{
    EdgeIeMode,
    MicrosoftEdge,
    InternetExplorerLegacy
}

/// <summary>Enterprise Mode Site List compat-mode values (schema v2).</summary>
public enum CompatMode
{
    Default,
    IE7Enterprise,
    IE8Enterprise,
    IE5,
    IE7,
    IE8,
    IE9,
    IE10,
    IE11
}

/// <summary>Outcome of a single diagnostic check.</summary>
public enum DiagnosticStatus
{
    Passed,
    Warning,
    Failed,
    NotApplicable
}

/// <summary>Registry scope for Edge policy values.</summary>
public enum PolicyScope
{
    Machine,
    User
}

/// <summary>Operations that may be explicitly requested through the UAC boundary.</summary>
public enum ElevatedCommand
{
    ApplyPolicies,
    AllowPopups,
    PublishSiteList,
    RollbackSiteList,
    RestoreBackup
}

/// <summary>Status of a required Edge policy value compared to its expected value.</summary>
public enum PolicyValueStatus
{
    Correct,
    Missing,
    Incorrect,
    WrongType,
    ManagedExternally
}
