# MASTER PROMPT — BMPC Legacy Edge IE Mode Launcher

## Role

Act as a senior Windows desktop application architect, .NET engineer, Windows security engineer, Microsoft Edge enterprise deployment specialist, and QA lead.

Build a production-ready Windows desktop application named:

```text
BMPC Legacy Edge IE Mode Launcher
```

The application must allow approved legacy web systems to open in Microsoft Edge using Internet Explorer mode without relying on Edge's temporary manual "Reload in Internet Explorer mode" list.

The application must use Microsoft Edge's supported policy-based IE mode configuration through an Enterprise Mode Site List.

Do not create a custom browser engine.  
Do not embed Internet Explorer.  
Do not use undocumented Edge command-line flags to force IE mode.  
Do not automate clicks inside the Edge settings interface.

---

# 1. Primary Objective

Create a Windows tool that:

1. Registers approved legacy system URLs.
2. Generates and maintains a Microsoft Edge Enterprise Mode Site List.
3. Enables Microsoft Edge Internet Explorer integration through Windows policies.
4. Assigns approved URLs to the IE11 rendering engine.
5. Checks and repairs missing IE mode configuration.
6. Launches approved systems in Microsoft Edge.
7. Detects when Edge must restart before changes take effect.
8. Provides diagnostics, backup, rollback, audit logs, and deployment support.
9. Prevents standard users from adding arbitrary URLs.
10. Works on supported Windows 10 and Windows 11 computers with Microsoft Edge installed.

---

# 2. Core Technical Principle

The application must launch Microsoft Edge normally.

Example:

```csharp
Process.Start(new ProcessStartInfo
{
    FileName = edgeExecutablePath,
    Arguments = $"\"{targetUrl}\"",
    UseShellExecute = true
});
```

The Enterprise Mode Site List must be responsible for opening the registered URL in IE mode.

Do not attempt to force IE mode using a fake or undocumented command such as:

```text
--ie-mode-force
--ie-mode-test
```

The correct workflow is:

```text
Register URL
    ↓
Generate Enterprise Mode Site List XML
    ↓
Apply Microsoft Edge IE mode policies
    ↓
Restart Edge when required
    ↓
Launch URL normally in Edge
    ↓
Edge matches URL against the site list
    ↓
Approved URL opens in IE mode
```

---

# 3. Required Technology Stack

Use:

```text
.NET 8
WPF
C#
MVVM architecture
CommunityToolkit.Mvvm
Microsoft.Extensions.DependencyInjection
Microsoft.Extensions.Hosting
Microsoft.Extensions.Logging
Entity Framework Core
SQLite
System.Xml.Linq
FluentValidation
Serilog
xUnit
WiX Toolset for MSI installer
```

Use Windows Forms only if required for a specific Windows-native dialog.

Do not use Electron.

Do not require internet access for normal operation.

---

# 4. Solution Structure

Create this solution structure:

```text
BMPC.LegacyEdgeLauncher/
│
├── src/
│   ├── BMPC.LegacyEdgeLauncher.Desktop/
│   │   ├── App.xaml
│   │   ├── App.xaml.cs
│   │   ├── Views/
│   │   ├── ViewModels/
│   │   ├── Controls/
│   │   ├── Converters/
│   │   ├── Resources/
│   │   └── Themes/
│   │
│   ├── BMPC.LegacyEdgeLauncher.Core/
│   │   ├── Models/
│   │   ├── Enums/
│   │   ├── Interfaces/
│   │   ├── Validation/
│   │   ├── Constants/
│   │   └── Results/
│   │
│   ├── BMPC.LegacyEdgeLauncher.Infrastructure/
│   │   ├── Database/
│   │   ├── Registry/
│   │   ├── Edge/
│   │   ├── SiteList/
│   │   ├── Diagnostics/
│   │   ├── Backup/
│   │   ├── Logging/
│   │   ├── Security/
│   │   └── Services/
│   │
│   ├── BMPC.LegacyEdgeLauncher.SiteListService/
│   │   ├── Hosting/
│   │   ├── Health/
│   │   ├── Configuration/
│   │   └── WindowsService/
│   │
│   └── BMPC.LegacyEdgeLauncher.Cli/
│       ├── Commands/
│       ├── Output/
│       └── Program.cs
│
├── tests/
│   ├── BMPC.LegacyEdgeLauncher.UnitTests/
│   ├── BMPC.LegacyEdgeLauncher.IntegrationTests/
│   └── BMPC.LegacyEdgeLauncher.XmlTests/
│
├── installer/
│   └── BMPC.LegacyEdgeLauncher.Setup/
│
├── deployment/
│   ├── powershell/
│   ├── group-policy/
│   ├── intune/
│   └── samples/
│
├── docs/
│   ├── ADMINISTRATOR_GUIDE.md
│   ├── USER_GUIDE.md
│   ├── TROUBLESHOOTING.md
│   ├── DEPLOYMENT.md
│   └── SECURITY.md
│
├── BMPC.LegacyEdgeLauncher.sln
├── README.md
└── .gitignore
```

---

# 5. Required Microsoft Edge Policies

The application must read, apply, verify, back up, and restore the following documented Microsoft Edge policies.

Registry path:

```text
HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Edge
```

## 5.1 Enable Internet Explorer Integration

```text
Value name: InternetExplorerIntegrationLevel
Type: REG_DWORD
Value: 1
```

Interpretation:

```text
1 = Internet Explorer mode
```

## 5.2 Configure Enterprise Mode Site List

```text
Value name: InternetExplorerIntegrationSiteList
Type: REG_SZ
Value: configured site-list URL
```

Default standalone configuration:

```text
http://127.0.0.1:8765/sitelist.xml
```

## 5.3 Optional Manual Reload Policy

Support but do not enable by default:

```text
Value name: InternetExplorerIntegrationReloadInIEModeAllowed
Type: REG_DWORD
Value: 1
```

The permanent Enterprise Mode Site List must remain the primary enforcement method.

## 5.4 Policy Rules

The application must:

- Prefer machine scope under `HKLM`.
- Require administrative elevation before writing machine policies.
- Read policies without elevation where possible.
- Back up existing values before modification.
- Detect whether values appear to be managed by domain Group Policy or organizational management.
- Never overwrite organization-managed policies silently.
- Display a warning when local values may be replaced by Group Policy.
- Never delete the entire Microsoft Edge registry policy key.
- Restore only values previously changed by this application.

---

# 6. Enterprise Mode Site List

Use Enterprise Mode Site List schema version 2.

Generate XML similar to:

```xml
<?xml version="1.0" encoding="utf-8"?>
<site-list version="1">
  <created-by>
    <tool>BMPC Legacy Edge IE Mode Launcher</tool>
    <version>1.0.0</version>
    <date-created>2026-07-16</date-created>
  </created-by>

  <site url="192.168.1.50">
    <compat-mode>Default</compat-mode>
    <open-in>IE11</open-in>
  </site>

  <site url="login.example.local">
    <open-in>None</open-in>
  </site>
</site-list>
```

## 6.1 Supported Open-In Values

Support:

```text
IE11
MSEdge
None
Configurable
```

For the MVP UI, expose:

```text
IE11
MSEdge
None
```

Place `Configurable` under advanced settings.

## 6.2 URL Matching

Support:

- Host-only rules
- Host and path rules
- HTTP and HTTPS
- Internal IP addresses
- Internal DNS hostnames
- Production, testing, and development environments

Normalize URLs before saving.

Examples:

```text
http://192.168.1.50/accounting
https://legacy.barbazamultipurpose.local/system
http://server-name:8080/old-app
```

Never store URL fragments.

## 6.3 Neutral Sites

Allow each registered application to define neutral authentication sites.

A neutral site must be generated as:

```xml
<site url="login.example.local">
  <open-in>None</open-in>
</site>
```

Use neutral sites when authentication redirects must preserve the current browser engine.

## 6.4 XML Requirements

The generator must:

- Use UTF-8 encoding.
- Escape XML correctly.
- Increment the version every time the published list changes.
- Validate the XML before publishing.
- Reject duplicate and conflicting rules.
- Write atomically using a temporary file and rename operation.
- Generate a SHA-256 hash for each published version.
- Preserve timestamped backups.
- Allow rollback to the last known working version.

---

# 7. Local Site List Windows Service

Create a small Windows service for standalone deployments.

Service name:

```text
BMPC Legacy Edge Site List Service
```

Default endpoint:

```text
http://127.0.0.1:8765/sitelist.xml
```

Health endpoint:

```text
http://127.0.0.1:8765/health
```

Example health response:

```json
{
  "status": "healthy",
  "siteListVersion": 12,
  "lastPublished": "2026-07-16T10:00:00+08:00"
}
```

## 7.1 Service Security

The service must:

- Bind only to `127.0.0.1`.
- Never bind to all network interfaces by default.
- Allow only GET and HEAD requests.
- Reject POST, PUT, PATCH, DELETE, TRACE, and CONNECT.
- Disable directory browsing.
- Serve only explicitly configured files.
- Return correct content types.
- Run under a restricted service identity.
- Store files under:

```text
C:\ProgramData\BMPC\LegacyEdgeLauncher\
```

Suggested folders:

```text
C:\ProgramData\BMPC\LegacyEdgeLauncher\Database
C:\ProgramData\BMPC\LegacyEdgeLauncher\SiteList
C:\ProgramData\BMPC\LegacyEdgeLauncher\Backups
C:\ProgramData\BMPC\LegacyEdgeLauncher\Logs
C:\ProgramData\BMPC\LegacyEdgeLauncher\Exports
```

---

# 8. Application Modes

Support two modes.

## 8.1 Administrator Mode

Administrator mode can:

- Add and edit applications
- Enable or disable applications
- Add neutral sites
- Generate and publish the site list
- Apply Edge policies
- Restart Edge after confirmation
- Back up and restore configuration
- Run full diagnostics
- View audit logs
- Export deployment scripts
- Configure service settings

## 8.2 Standard User Mode

Standard user mode can:

- View approved applications
- Launch approved applications
- Run read-only diagnostics
- View basic support instructions

Standard users must not be able to:

- Register arbitrary URLs
- Modify Edge policies
- Publish XML
- Change the service configuration
- Delete audit logs
- Restore backups

---

# 9. Main User Interface

Use a clean BMPC internal administration design.

Primary navigation:

```text
Dashboard
Applications
Site List
Policies
Diagnostics
Backups
Deployment
Audit Logs
Settings
About
```

Use MVVM and reusable controls.

## 9.1 Dashboard

Display status cards:

```text
IE Mode Policy
Site List Policy
Site List Service
Site List Version
Microsoft Edge Version
Registered Applications
Restart Required
Last Successful Diagnostic
```

Display approved application tiles.

Each tile must show:

```text
Application name
Environment
Base URL
Engine assignment
Configuration status
Last tested date
Open System button
Diagnose button
```

## 9.2 Applications Page

Provide:

- Search
- Environment filter
- Enabled/disabled filter
- Engine filter
- Add application
- Edit application
- Clone application
- Disable application
- Delete application with confirmation
- Test before publishing
- Publish pending changes

## 9.3 Add/Edit Application Form

Fields:

```text
Application Name
Environment
Base URL
Path Rule
Open-In Engine
Compatibility Mode
Neutral Authentication Sites
Owner
Department
Support Contact
Notes
Enabled
```

Environment options:

```text
Production
Testing
Development
```

Compatibility options:

```text
Default
IE7Enterprise
IE8Enterprise
IE5
IE7
IE8
IE9
IE10
IE11
```

Only expose compatibility modes that are valid for the selected schema and configuration.

## 9.4 Policies Page

Show:

```text
Policy name
Expected value
Current value
Source/scope
Status
Restart requirement
```

Actions:

```text
Apply Required Policies
Repair Configuration
Export Registry Backup
Restore Previous Policies
Open edge://policy
Open edge://compat
```

## 9.5 Diagnostics Page

Display checks as:

```text
Passed
Warning
Failed
Not Applicable
```

Each check must include:

```text
Check name
Result
Friendly explanation
Technical details
Recommended action
Copy details button
```

## 9.6 Backup Page

Display:

```text
Backup date
Site-list version
Policy snapshot
Database snapshot
Created by
Hash
Restore button
Export button
```

## 9.7 Audit Logs Page

Provide:

- Date range
- User filter
- Action filter
- Result filter
- Search
- CSV export
- JSON export

---

# 10. Core Workflows

## 10.1 First-Run Setup

```text
Application starts
    ↓
Detect administrator access
    ↓
Detect Windows version
    ↓
Detect Microsoft Edge
    ↓
Create ProgramData folders
    ↓
Initialize SQLite database
    ↓
Install or verify site-list service
    ↓
Configure site-list endpoint
    ↓
Back up existing Edge policies
    ↓
Apply required policies
    ↓
Create initial empty site list
    ↓
Ask administrator to register a system
```

## 10.2 Register Application

```text
Administrator enters application details
    ↓
Validate URL
    ↓
Normalize host and path
    ↓
Check duplicates and conflicts
    ↓
Optionally test network reachability
    ↓
Save as pending
    ↓
Generate XML preview
    ↓
Validate site list
    ↓
Publish new version
    ↓
Update audit log
    ↓
Mark Edge restart required
```

## 10.3 Open Application

```text
User clicks Open System
    ↓
Verify application is enabled
    ↓
Validate URL against approved database record
    ↓
Check Edge installation
    ↓
Check policy status
    ↓
Check site-list service
    ↓
Check URL exists in published XML
    ↓
Check whether restart is pending
    ↓
Launch Microsoft Edge with URL
    ↓
Write launch audit event
```

Do not block launch for noncritical warnings.

Block launch when:

- URL is not approved
- URL contains an unsafe scheme
- Edge is not installed
- Site record is disabled
- Site-list configuration is invalid
- Required policy is explicitly disabled

## 10.4 Repair Configuration

The repair action must:

1. Back up current registry values.
2. Validate ProgramData directories.
3. Validate database availability.
4. Validate service status.
5. Regenerate XML from database records.
6. Validate XML.
7. Republish XML atomically.
8. Restore required policies.
9. Confirm site-list endpoint is reachable.
10. Mark Edge restart required.
11. Write a detailed audit event.

## 10.5 Restart Edge

The tool must:

- Detect running Edge processes.
- Warn that unsaved tabs or forms may be lost.
- Require explicit confirmation.
- Attempt graceful closure first.
- Wait for processes to exit.
- Offer force close only after graceful close fails.
- Never terminate Edge silently.
- Relaunch the selected approved system afterward when requested.

---

# 11. Diagnostics

Create a diagnostic engine with the following checks:

```text
Windows version supported
64-bit operating system
Microsoft Edge installed
Edge executable path valid
Edge version detected
Administrator privileges
InternetExplorerIntegrationLevel policy
InternetExplorerIntegrationSiteList policy
Optional reload policy
Policy source conflict
Site-list service installed
Site-list service running
Health endpoint reachable
Site-list XML reachable
Site-list XML valid
Site-list schema valid
Site-list version valid
Target application exists in site list
Target URL normalized correctly
Duplicate site entries
Conflicting path entries
DNS resolution
TCP connection
HTTP/HTTPS response
Certificate validity
Redirect chain
Neutral authentication site recommendation
Proxy interference
Firewall interference
Edge restart pending
Database health
Backup directory writable
Audit directory writable
```

Generate a support report containing:

```text
Application version
Windows version
Edge version
Computer name
Current user
Policy values
Site-list endpoint
Site-list version
Diagnostic results
Recent relevant errors
Correlation ID
```

Do not include:

```text
Passwords
Cookies
Session tokens
Authorization headers
Private page contents
```

Allow the report to be exported as:

```text
TXT
JSON
ZIP support bundle
```

---

# 12. Data Model

Use SQLite with Entity Framework Core migrations.

## 12.1 LegacyApplication

```text
Id: Guid
Name: string
Environment: enum
BaseUrl: string
NormalizedHost: string
PathRule: string?
OpenIn: enum
CompatibilityMode: string
Owner: string?
Department: string?
SupportContact: string?
Notes: string?
IsEnabled: bool
LastTestedAt: DateTimeOffset?
LastTestStatus: string?
CreatedAt: DateTimeOffset
CreatedBy: string
UpdatedAt: DateTimeOffset
UpdatedBy: string
RowVersion: byte[]
```

## 12.2 NeutralSite

```text
Id: Guid
LegacyApplicationId: Guid
Url: string
NormalizedHost: string
Notes: string?
IsEnabled: bool
CreatedAt: DateTimeOffset
CreatedBy: string
```

## 12.3 SiteListVersion

```text
Id: Guid
VersionNumber: long
XmlHash: string
PublishedFilePath: string
PublishedAt: DateTimeOffset
PublishedBy: string
ValidationSucceeded: bool
ValidationMessage: string?
BackupFilePath: string?
```

## 12.4 PolicySnapshot

```text
Id: Guid
Scope: string
RegistryPath: string
ValueName: string
PreviousValue: string?
PreviousValueType: string?
NewValue: string?
NewValueType: string?
CapturedAt: DateTimeOffset
ChangedBy: string
RestoredAt: DateTimeOffset?
```

## 12.5 DiagnosticRun

```text
Id: Guid
ApplicationId: Guid?
StartedAt: DateTimeOffset
CompletedAt: DateTimeOffset?
OverallStatus: string
ComputerName: string
Username: string
CorrelationId: string
```

## 12.6 DiagnosticResult

```text
Id: Guid
DiagnosticRunId: Guid
CheckCode: string
CheckName: string
Status: string
FriendlyMessage: string
TechnicalDetails: string?
RecommendedAction: string?
```

## 12.7 AuditEvent

```text
Id: Guid
Timestamp: DateTimeOffset
Username: string
ComputerName: string
Action: string
EntityType: string?
EntityId: string?
OldValueJson: string?
NewValueJson: string?
Succeeded: bool
ErrorMessage: string?
CorrelationId: string
ApplicationVersion: string
```

---

# 13. Required Interfaces

Implement interfaces similar to:

```csharp
public interface IEdgePolicyService
{
    Task<EdgePolicyState> ReadStateAsync(CancellationToken cancellationToken);
    Task<PolicyChangeResult> ApplyAsync(
        EdgePolicyConfiguration configuration,
        CancellationToken cancellationToken);
    Task<PolicyChangeResult> RestoreAsync(
        Guid snapshotId,
        CancellationToken cancellationToken);
}

public interface ISiteListGenerator
{
    SiteListDocument Generate(
        IReadOnlyCollection<LegacyApplication> applications,
        IReadOnlyCollection<NeutralSite> neutralSites,
        long version);
}

public interface ISiteListValidator
{
    ValidationResult Validate(SiteListDocument document);
}

public interface ISiteListPublisher
{
    Task<PublishResult> PublishAsync(
        SiteListDocument document,
        CancellationToken cancellationToken);
}

public interface IEdgeLauncher
{
    string? FindEdgeExecutable();
    Task<LaunchResult> OpenAsync(
        Uri url,
        CancellationToken cancellationToken);
}

public interface IDiagnosticService
{
    Task<DiagnosticReport> RunAsync(
        Guid? applicationId,
        CancellationToken cancellationToken);
}

public interface IBackupService
{
    Task<BackupResult> CreateAsync(
        string reason,
        CancellationToken cancellationToken);
    Task<RestoreResult> RestoreAsync(
        Guid backupId,
        CancellationToken cancellationToken);
}

public interface IAuditService
{
    Task RecordAsync(
        AuditEvent auditEvent,
        CancellationToken cancellationToken);
}

public interface IPrivilegeService
{
    bool IsAdministrator();
    Task<ElevationResult> RelaunchElevatedAsync(
        string arguments,
        CancellationToken cancellationToken);
}
```

---

# 14. Security Requirements

## 14.1 URL Allowlist

Standard users may launch only URLs stored as enabled records.

Allow schemes:

```text
http
https
```

Reject:

```text
file
javascript
data
shell
cmd
powershell
ms-settings
ftp
```

Validate the final URI immediately before launch.

## 14.2 Elevation

Use a split-elevation model:

- Normal launching must not require administrator rights.
- Policy changes, service installation, and protected file changes require elevation.
- Use UAC.
- Pass only signed or validated commands to the elevated helper.
- Do not accept arbitrary shell commands from the UI.

## 14.3 File Permissions

Set restrictive ACLs for:

```text
Database
SiteList
Backups
Logs
Configuration
```

Standard users may read the published site list but must not modify it.

## 14.4 Secrets

Do not store:

```text
Legacy system passwords
Browser cookies
Windows credentials
Session tokens
Edge profile data
```

## 14.5 Signing

Prepare the build for:

- Authenticode signing
- Signed MSI
- Signed PowerShell scripts
- Versioned releases
- SHA-256 release hashes

---

# 15. Backup and Rollback

Before changing policies or publishing XML:

1. Export current Edge policy values.
2. Copy the current XML.
3. Back up the SQLite database.
4. Save application configuration.
5. Calculate hashes.
6. Create an audit event.

Keep:

```text
Latest 20 change-based backups
Monthly snapshots for 12 months
Manual backups until explicitly deleted
```

Restoration must:

- Validate backup integrity.
- Show what will change.
- Require administrator confirmation.
- Restore files atomically.
- Restore only policies owned or changed by this tool.
- Restart the service if required.
- Mark Edge restart required.
- Write an audit event.

---

# 16. Error Handling

Use typed result objects and centralized exception handling.

Every user-facing error must include:

```text
What failed
Likely cause
Recommended action
Technical details
Correlation ID
Copy button
```

Handle at least:

```text
Edge not installed
Edge executable not found
Access denied
Administrator permission required
Policy conflict
Policy controlled by organization
Invalid registry type
Site-list endpoint unavailable
Invalid XML
Invalid schema
Duplicate site rule
Conflicting site rule
Service not installed
Service failed to start
Port already in use
Database locked
Database migration failed
DNS failure
Connection timeout
TLS certificate error
Proxy failure
Application URL unreachable
Edge restart required
Launch failure
Backup failure
Rollback failure
```

Do not crash on expected configuration errors.

---

# 17. Logging

Use Serilog.

Log to:

```text
C:\ProgramData\BMPC\LegacyEdgeLauncher\Logs\
```

Use rolling daily files.

Recommended retention:

```text
90 days
```

Log:

```text
Startup
Shutdown
Configuration changes
Policy reads and writes
Site-list generation
Site-list publication
Service health
Diagnostics
Launch attempts
Backup and restore
Errors and warnings
```

Sanitize URLs where query parameters may contain sensitive information.

Never log passwords, cookies, tokens, or authorization headers.

---

# 18. CLI Requirements

Create a CLI for administrators and deployment automation.

Commands:

```powershell
BMPC.LegacyEdgeLauncher.Cli status
BMPC.LegacyEdgeLauncher.Cli policy show
BMPC.LegacyEdgeLauncher.Cli policy apply
BMPC.LegacyEdgeLauncher.Cli policy repair
BMPC.LegacyEdgeLauncher.Cli policy restore --snapshot <id>
BMPC.LegacyEdgeLauncher.Cli site list
BMPC.LegacyEdgeLauncher.Cli site add
BMPC.LegacyEdgeLauncher.Cli site validate
BMPC.LegacyEdgeLauncher.Cli site publish
BMPC.LegacyEdgeLauncher.Cli diagnose
BMPC.LegacyEdgeLauncher.Cli launch --application <id>
BMPC.LegacyEdgeLauncher.Cli backup create
BMPC.LegacyEdgeLauncher.Cli backup restore --id <id>
BMPC.LegacyEdgeLauncher.Cli export powershell
```

Exit codes:

```text
0 = Success
1 = Validation failure
2 = Permission denied
3 = Microsoft Edge not installed
4 = Policy conflict
5 = Site-list unavailable
6 = Invalid XML
7 = Launch failed
8 = Service failure
9 = Database failure
10 = Backup or restore failure
```

Support:

```text
--json
--quiet
--verbose
```

---

# 19. Installer Requirements

Build an MSI installer using WiX Toolset.

The installer must:

- Install the WPF desktop application.
- Install the CLI.
- Install the Windows service.
- Create ProgramData folders.
- Set required ACLs.
- Create Start Menu shortcuts.
- Optionally create a desktop shortcut.
- Register uninstall information.
- Support silent installation.
- Support repair installation.
- Preserve application data on upgrade.
- Ask whether to preserve data during uninstall.
- Never remove organization-managed Edge policies.
- Remove only policies created by the application when the user explicitly selects that option.

Silent install example:

```powershell
msiexec /i BMPC.LegacyEdgeLauncher.msi /qn
```

---

# 20. Testing Requirements

## 20.1 Unit Tests

Test:

```text
URL normalization
Unsafe scheme rejection
Duplicate detection
Conflict detection
XML generation
XML escaping
Site-list version increments
SHA-256 generation
Registry value conversion
Policy-state interpretation
Backup metadata
Rollback selection
Audit-event creation
Command validation
```

## 20.2 Integration Tests

Test on:

```text
Windows 10
Windows 11
Local administrator
Standard user
Domain-joined computer
Computer with conflicting Group Policy
Computer behind a proxy
Offline computer
Computer where port 8765 is occupied
Computer with Edge already running
Computer without Edge
```

## 20.3 Legacy Application Tests

For every registered application, create a repeatable checklist:

```text
Login
Logout
Menus
Forms
Validation
Printing
Pop-up windows
File upload
File download
Reports
Excel export
PDF export
Authentication redirects
Session timeout
Browser back
Browser forward
Multiple tabs
ActiveX dependency
JavaScript behavior
```

## 20.4 Acceptance Criteria

The MVP is complete only when:

1. An administrator can register a legacy URL.
2. The application generates valid Enterprise Mode Site List XML.
3. The application increments the site-list version.
4. The correct Edge IE mode policies are applied.
5. The local site-list endpoint is reachable.
6. The approved URL is present in the published XML.
7. The application detects when Edge must restart.
8. The approved URL opens through Microsoft Edge.
9. Edge renders the approved URL in IE mode after policy refresh/restart.
10. A standard user can launch an approved system.
11. A standard user cannot add an arbitrary URL.
12. The tool can repair deleted or damaged local configuration.
13. The tool can restore the previous working configuration.
14. Administrative actions are audited.
15. No passwords, cookies, or session tokens are stored.

---

# 21. Initial MVP Screens

Build these screens first:

```text
1. First Run Setup
2. Dashboard
3. Applications List
4. Add/Edit Application
5. Policy Status
6. Diagnostics
7. Backup and Restore
8. Settings
9. About
```

Do not build advanced central management before the local MVP works reliably.

---

# 22. Seed Configuration

Provide a safe seed configuration with placeholders.

```json
{
  "Application": {
    "Name": "BMPC Legacy Edge IE Mode Launcher",
    "Organization": "Barbaza Multi-Purpose Cooperative"
  },
  "SiteList": {
    "HostingMode": "LoopbackHttp",
    "Url": "http://127.0.0.1:8765/sitelist.xml",
    "Port": 8765,
    "RootDirectory": "C:\\ProgramData\\BMPC\\LegacyEdgeLauncher\\SiteList",
    "BackupDirectory": "C:\\ProgramData\\BMPC\\LegacyEdgeLauncher\\Backups"
  },
  "Edge": {
    "PolicyScope": "Machine",
    "AllowManualReload": false,
    "PromptBeforeRestart": true
  },
  "Security": {
    "AllowHttpForLoopbackOnly": true,
    "AllowedSchemes": [
      "http",
      "https"
    ]
  },
  "Logging": {
    "MinimumLevel": "Information",
    "RetentionDays": 90
  }
}
```

Do not hardcode a real production URL.

On first run, require the administrator to enter:

```text
System name
System URL
Environment
Application owner
Department
Optional neutral authentication domain
```

---

# 23. PowerShell Deployment Output

Add a feature that generates a PowerShell deployment script.

The generated script must:

- Require administrator rights.
- Back up existing Edge policy values.
- Create required registry keys safely.
- Set the required IE mode policy.
- Set the Enterprise Site List URL.
- Verify the written values.
- Print a warning that Edge must restart.
- Avoid deleting unrelated policies.
- Produce a rollback script.

Example logic:

```powershell
#Requires -RunAsAdministrator

$edgePolicyPath = "HKLM:\SOFTWARE\Policies\Microsoft\Edge"
$siteListUrl = "http://127.0.0.1:8765/sitelist.xml"

if (-not (Test-Path $edgePolicyPath)) {
    New-Item -Path $edgePolicyPath -Force | Out-Null
}

New-ItemProperty `
    -Path $edgePolicyPath `
    -Name "InternetExplorerIntegrationLevel" `
    -PropertyType DWord `
    -Value 1 `
    -Force | Out-Null

New-ItemProperty `
    -Path $edgePolicyPath `
    -Name "InternetExplorerIntegrationSiteList" `
    -PropertyType String `
    -Value $siteListUrl `
    -Force | Out-Null
```

The actual generated script must include backup, validation, and error handling.

---

# 24. Edge Verification Support

Add buttons that open:

```text
edge://policy
edge://compat
edge://compat/SiteListManager
```

Display instructions explaining what administrators should verify:

```text
InternetExplorerIntegrationLevel has no error.
InternetExplorerIntegrationSiteList contains the expected endpoint.
The Enterprise Mode Site List shows the current version.
The registered application URL appears in the list.
```

Do not scrape or automate these internal Edge pages.

---

# 25. Future Enterprise Features

Design extension points for:

```text
Central HTTPS-hosted site list
Active Directory Group Policy deployment
Microsoft Intune deployment
Microsoft 365 Cloud Site List Management
Multiple site-list profiles
Device inventory
Centralized audit collection
Role-based administration
Signed configuration packages
Remote health reporting
Application modernization tracking
```

Do not include these in the first MVP unless required by the current environment.

---

# 26. Modernization Tracking

IE mode is a compatibility solution, not a permanent modernization strategy.

Add optional fields:

```text
Legacy dependency
ActiveX dependency
Document mode dependency
Reason IE mode is required
Risk level
Modernization owner
Target replacement date
Last compatibility review
```

Create a future dashboard showing:

```text
Applications without owners
Applications without replacement dates
Applications using HTTP
Applications using ActiveX
Applications not tested recently
Applications approaching retirement
```

---

# 27. Coding Standards

Use:

```text
Nullable reference types
Async/await
CancellationToken
Dependency injection
Structured logging
Immutable result objects where appropriate
Centralized validation
Repository or service abstractions only where useful
EF Core migrations
Strongly typed configuration
XML documentation on public APIs
EditorConfig
Treat warnings as errors in CI
```

Avoid:

```text
God classes
Static service locators
Direct registry access from view models
Direct database access from views
Business logic in code-behind
Hardcoded URLs
Hardcoded registry paths scattered across files
Silent exception swallowing
Arbitrary Process.Start commands
```

---

# 28. CI Requirements

Create a GitHub Actions workflow that:

1. Restores NuGet packages.
2. Builds in Release mode.
3. Runs unit tests.
4. Runs XML tests.
5. Publishes test results.
6. Produces unsigned development artifacts.
7. Builds the MSI when running on a Windows runner.
8. Uploads artifacts.

Do not include secrets in the repository.

---

# 29. Documentation Deliverables

Create:

```text
README.md
docs/ADMINISTRATOR_GUIDE.md
docs/USER_GUIDE.md
docs/TROUBLESHOOTING.md
docs/DEPLOYMENT.md
docs/SECURITY.md
docs/ARCHITECTURE.md
docs/TEST_PLAN.md
```

The README must include:

```text
Purpose
Architecture
Prerequisites
Development setup
Build commands
Test commands
Installer creation
First-run setup
Policy behavior
Known limitations
Security notes
```

---

# 30. Implementation Order

Implement in this order:

## Phase 1 — Foundation

```text
Solution structure
Dependency injection
Configuration
Logging
SQLite database
Core models
Validation
```

## Phase 2 — Edge Integration

```text
Edge detection
Registry policy reader
Registry policy writer
Policy backup
Policy verification
Elevation helper
```

## Phase 3 — Site List

```text
XML generator
XML validator
Versioning
Atomic publication
Backup and rollback
Local site-list service
```

## Phase 4 — Desktop MVP

```text
Dashboard
Application management
Policy page
Diagnostics
Launch workflow
Restart workflow
```

## Phase 5 — Deployment

```text
CLI
PowerShell export
MSI installer
ACL setup
Uninstall behavior
```

## Phase 6 — Hardening

```text
Integration tests
Security review
Error handling
Support bundles
Documentation
CI pipeline
```

---

# 31. Required First Output From the Coding Agent

Before writing implementation code, produce:

1. A concise architecture summary.
2. A complete folder and project tree.
3. Database entity definitions.
4. Service interface definitions.
5. Policy-management design.
6. Site-list XML design.
7. Elevation and security design.
8. MVP screen map.
9. Development milestones.
10. Key risks and mitigations.

After presenting the plan, begin implementation without asking for confirmation.

---

# 32. Important Restrictions

Do not:

- Embed the Internet Explorer executable.
- Create an unsupported custom IE browser.
- Use Internet Explorer automation APIs as the primary solution.
- Use undocumented Edge switches.
- Store browser credentials.
- Disable Windows security controls.
- Disable certificate validation.
- Modify unrelated Edge policies.
- Terminate Edge without user confirmation.
- Allow arbitrary URL or command execution.
- Expose the local site-list service to the LAN by default.
- Assume every ActiveX control will work.
- Claim that IE mode can never be retired.
- Treat manual Edge IE-mode entries as permanent configuration.

---

# 33. Definition of Done

The project is done when a clean Windows computer can:

```text
Install the MSI
Open the launcher
Complete first-run setup
Register an approved legacy URL
Apply Edge IE mode policies
Publish a valid site list
Restart Edge through a confirmed workflow
Launch the approved system
Open the system in Edge IE mode
Run diagnostics
Repair configuration
Create a backup
Restore a backup
Uninstall safely
```

The code must be maintainable, testable, secure, documented, and suitable for controlled internal deployment at Barbaza Multi-Purpose Cooperative.

---

# 34. NATCCO EKoopBanker+ / CBS Application Profile

The first production application to support is:

```text
Application name: NATCCO EKoopBanker+ CASA
Organization/tenant: Barbaza Multi-Purpose Cooperative
Production URL: https://cbs.natcco.coop/barbazampc/logon.asp
Production host: cbs.natcco.coop
Production path: /barbazampc/logon.asp
Required browser: Microsoft Edge
Required rendering engine: Internet Explorer mode / IE11
```

Treat the production URL as a configurable seeded application record, not as an unchangeable hardcoded constant.

Use HTTPS in all application configuration, UI displays, bookmarks, diagnostics, and launches.

The application must normalize these inputs to the canonical production URL:

```text
cbs.natcco.coop/barbazampc/logon.asp
https://cbs.natcco.coop/barbazampc/logon.asp
https://cbs.natcco.coop/barbazampc/
```

The exact launch URL must remain:

```text
https://cbs.natcco.coop/barbazampc/logon.asp
```

Do not downgrade the URL to HTTP.

---

# 35. NATCCO Procedure Findings and Required Enhancements

The supplied NATCCO transition procedure requires more than IE mode alone. The implementation must support and validate these dependencies:

```text
1. Trusted Sites zone assignment
2. Microsoft Edge IE mode assignment
3. CASA module navigation and new-tab handling
4. Pop-up and redirect permission
5. Old bookmark cleanup guidance
6. DrayTek/internet connectivity verification
7. DNS and HTTPS connectivity diagnostics
8. Menu rendering troubleshooting
9. Internet Options security-zone validation
10. Support escalation details
```

The desktop application must include these items in the NATCCO application profile and diagnostic workflow.

Do not reproduce the procedure's broad instruction to enable every Internet Options security setting. That approach creates unnecessary security exposure.

Instead:

- Apply only the minimum settings that the application demonstrably requires.
- Scope settings to `cbs.natcco.coop`.
- Record every setting changed.
- Allow administrators to restore the previous configuration.
- Flag broad security-zone changes as unsafe.
- Never disable certificate validation, antivirus, SmartScreen, Windows Defender, or general browser protections.

---

# 36. Enterprise Mode Site List Rule for NATCCO CBS

Generate a production rule that covers the NATCCO CBS host and the Barbaza MPC path.

Preferred initial rule:

```xml
<site url="cbs.natcco.coop/barbazampc">
  <compat-mode>Default</compat-mode>
  <open-in>IE11</open-in>
</site>
```

If testing proves that the landing page or other CASA resources under the same host also require IE mode, support promotion to:

```xml
<site url="cbs.natcco.coop">
  <compat-mode>Default</compat-mode>
  <open-in>IE11</open-in>
</site>
```

Use the narrowest working rule.

Do not automatically assign unrelated NATCCO systems or the entire `natcco.coop` parent domain to IE mode.

The application must display a warning before widening a rule from:

```text
cbs.natcco.coop/barbazampc
```

to:

```text
cbs.natcco.coop
```

The warning must explain that all matching pages on the host will then use IE mode.

---

# 37. Trusted Sites Zone Management

The NATCCO procedure requires `cbs.natcco.coop` to be included in Internet Options Trusted Sites.

Add a dedicated service:

```csharp
public interface IInternetSecurityZoneService
{
    Task<SecurityZoneState> ReadAsync(
        Uri uri,
        CancellationToken cancellationToken);

    Task<SecurityZoneChangeResult> AddToTrustedSitesAsync(
        Uri uri,
        CancellationToken cancellationToken);

    Task<SecurityZoneChangeResult> RestoreAsync(
        Guid snapshotId,
        CancellationToken cancellationToken);
}
```

## 37.1 Required Behavior

The application must:

1. Detect whether `https://cbs.natcco.coop` is assigned to Trusted Sites.
2. Show the current zone assignment.
3. Back up the previous zone mapping.
4. Add only `cbs.natcco.coop` when an administrator selects repair.
5. Prefer HTTPS-specific assignment.
6. Avoid wildcarding the whole `*.natcco.coop` domain unless explicitly approved.
7. Detect Group Policy ownership.
8. Avoid silently overwriting organization-managed zone mappings.
9. support rollback.
10. write an audit event.

## 37.2 Policy-Based Enterprise Option

For domain-managed computers, support generating instructions or deployment output for:

```text
Site to Zone Assignment List
```

Use zone value:

```text
2 = Trusted Sites
```

The generated deployment profile should map:

```text
https://cbs.natcco.coop
```

to:

```text
2
```

Do not write broad zone mappings without explicit administrator approval.

## 37.3 Registry Compatibility Option

For standalone computers, the implementation may manage the appropriate Windows Internet Settings zone mapping after:

- creating a registry backup,
- validating the exact host,
- obtaining elevation where required,
- documenting the affected user or machine scope,
- and confirming that the value is not controlled by policy.

Keep registry implementation isolated in Infrastructure and covered by integration tests.

---

# 38. Trusted Sites Security Configuration

The original procedure says to enable all Trusted Sites settings except the pop-up blocker. Do not automate that broad configuration.

Implement a safer compatibility-assessment workflow:

```text
1. Confirm the host is in Trusted Sites.
2. Launch and test the system.
3. Detect or record the exact failed feature.
4. Recommend only the specific setting needed.
5. Require administrator approval.
6. Back up the prior setting.
7. Apply the setting only to the Trusted Sites zone.
8. Retest.
9. Keep a compatibility record.
```

Potential legacy dependencies to assess individually include:

```text
Active scripting
Binary and script behaviors
File download
Font download
Initialize and script ActiveX controls not marked as safe
Run ActiveX controls and plug-ins
Script ActiveX controls marked safe for scripting
Submit nonencrypted form data
Launching programs and files in an IFRAME
Cross-domain data access
Mixed-content display
Automatic prompting for file downloads
```

Do not enable any listed setting merely because it appears in this prompt.

For each setting, require:

```text
Setting name
Current value
Proposed value
Reason
Application feature affected
Risk level
Approved by
Date applied
Test result
Rollback snapshot
```

Mark these as high-risk and never enable automatically:

```text
Initialize and script ActiveX controls not marked as safe
Download unsigned ActiveX controls
Download signed ActiveX controls without validation
Launch applications and unsafe files without prompting
Disable certificate validation
```

---

# 39. Pop-Up and Redirect Management

The NATCCO CASA workflow may open modules in a new tab or pop-up.

Do not disable the Edge pop-up blocker globally.

Implement a site-specific allowlist for:

```text
https://cbs.natcco.coop
```

Add a service:

```csharp
public interface IEdgeContentPolicyService
{
    Task<ContentPolicyState> ReadAsync(
        Uri uri,
        CancellationToken cancellationToken);

    Task<ContentPolicyChangeResult> AllowPopupsForUrlAsync(
        Uri uri,
        CancellationToken cancellationToken);

    Task<ContentPolicyChangeResult> RestoreAsync(
        Guid snapshotId,
        CancellationToken cancellationToken);
}
```

Support the documented Edge enterprise policy:

```text
PopupsAllowedForUrls
```

The generated policy should allow only the NATCCO CBS origin.

Example conceptual allowlist entry:

```text
https://cbs.natcco.coop
```

Do not set a global "allow all pop-ups" policy.

The diagnostics page must test:

```text
Pop-up allowlist policy exists
NATCCO CBS URL matches the allowlist
No overly broad wildcard is configured
CASA module opens in the expected tab/window
Redirect remains on an approved NATCCO host
```

If the redirect target changes to another host, do not automatically trust or allow it. Capture the destination and ask an administrator to review it.

---

# 40. CASA Landing Page and Redirect Handling

The transition procedure states that users may open an EKoopBanker landing page and select the CASA Site, which opens a new tab.

The application must support two launch modes:

```text
Direct CASA Launch
Landing Page Launch
```

Default production behavior:

```text
Direct CASA Launch:
https://cbs.natcco.coop/barbazampc/logon.asp
```

Allow an administrator to configure a separate landing-page URL if NATCCO supplies one.

## 40.1 Redirect Diagnostics

Capture and report:

```text
Initial URL
Redirect count
Redirect destinations
HTTP status codes
Final URL
Whether the final host is approved
Whether the browser engine remains IE mode
Whether a new tab or pop-up was blocked
```

Do not log credentials, cookies, POST bodies, or authentication tokens.

## 40.2 Approved Hosts

Seed only:

```text
cbs.natcco.coop
```

Any additional host discovered during login must be marked:

```text
Pending administrator review
```

The tool may recommend either:

```text
IE11 site-list rule
MSEdge rule
Neutral site rule
Pop-up allowlist entry
Trusted Sites mapping
```

but must not apply the recommendation automatically without approval.

---

# 41. NATCCO Application Launch Tile

Seed a production application tile:

```text
NATCCO EKoopBanker+ CASA
Barbaza MPC
https://cbs.natcco.coop/barbazampc/logon.asp
```

Display:

```text
IE Mode: Required
Trusted Sites: Required
Pop-ups: Allowed for this site only
Environment: Production
Owner: Configurable
Support: Configurable
```

Actions:

```text
Open EKoopBanker+
Run NATCCO Diagnostics
Repair NATCCO Configuration
View Setup Status
Open Troubleshooting Guide
```

Before launching, validate:

```text
Edge installed
IE mode policy enabled
Enterprise Site List configured
Current site-list version loaded
Barbaza MPC path assigned to IE11
cbs.natcco.coop in Trusted Sites
Pop-up permission configured for the site
No Edge restart pending
DNS resolves
TCP 443 is reachable
TLS certificate is valid
```

Allow launch with a warning when a noncritical network diagnostic cannot be completed.

Do not launch when:

```text
TLS certificate is invalid
The URL resolves to an unexpected unsafe destination
The site-list XML is invalid
The target URL is not approved
Required IE mode policy is disabled
```

---

# 42. Network and DrayTek Diagnostics

The procedure requires users to confirm connection through the DrayTek router with internet access.

The tool cannot reliably prove the physical router model solely from a web request. Present router detection as an optional environment check, not an absolute claim.

Implement:

```text
Default gateway detection
Active network adapter detection
DNS server detection
DNS resolution for cbs.natcco.coop
TCP connection test to cbs.natcco.coop:443
HTTPS HEAD/GET connectivity test
TLS certificate chain validation
Proxy detection
Captive portal warning
Route trace option
Ping as a secondary test only
```

## 42.1 Ping Interpretation

Do not state that a failed ping proves the website is unavailable.

Some servers block ICMP while HTTPS remains functional.

Diagnostic priority:

```text
1. DNS resolution
2. TCP port 443
3. HTTPS response
4. TLS certificate
5. Redirect behavior
6. Ping as supplementary information
```

Display:

```text
Ping failed, but HTTPS is reachable
```

when applicable.

## 42.2 Optional Commands

Provide copyable commands:

```powershell
Resolve-DnsName cbs.natcco.coop
Test-NetConnection cbs.natcco.coop -Port 443
ping cbs.natcco.coop
tracert cbs.natcco.coop
```

Do not require users to interpret raw output when the application can provide a friendly result.

---

# 43. NATCCO Troubleshooting Decision Tree

Implement the following guided troubleshooting flow.

```text
Cannot open system
    |
    +-- Is a network adapter connected?
    |       |
    |       +-- No: Show local network guidance
    |
    +-- Does DNS resolve cbs.natcco.coop?
    |       |
    |       +-- No: Show DNS/router/support guidance
    |
    +-- Is TCP 443 reachable?
    |       |
    |       +-- No: Show firewall/router/provider guidance
    |
    +-- Is the TLS certificate valid?
    |       |
    |       +-- No: Block launch and escalate
    |
    +-- Does HTTPS respond?
    |       |
    |       +-- No: Generate support report
    |
    +-- Is IE mode policy active?
    |       |
    |       +-- No: Offer administrator repair
    |
    +-- Is the site in the Enterprise Site List?
    |       |
    |       +-- No: Republish and restart Edge
    |
    +-- Is cbs.natcco.coop in Trusted Sites?
    |       |
    |       +-- No: Offer administrator repair
    |
    +-- Are pop-ups allowed for this site?
    |       |
    |       +-- No: Offer site-specific allowlist repair
    |
    +-- Does login work but menus not display?
            |
            +-- Run compatibility diagnostics
            +-- Verify Trusted Sites mapping
            +-- Verify IE mode icon/state
            +-- Test active scripting
            +-- Check blocked ActiveX/content notifications
            +-- Check browser console only when useful
            +-- Generate NATCCO support bundle
```

---

# 44. Menus Not Showing Diagnostic

When the CASA page opens but menus are missing, test in this order:

```text
1. Confirm final URL remains under cbs.natcco.coop.
2. Confirm the page is actually rendered in IE mode.
3. Confirm the host is in Trusted Sites.
4. Confirm JavaScript/active scripting is available for Trusted Sites.
5. Check for blocked pop-ups or new windows.
6. Check for blocked ActiveX or legacy controls.
7. Check mixed-content warnings.
8. Check failed subresource requests.
9. Check authentication redirects and session cookies.
10. Compare with a known working workstation.
```

Do not immediately enable all Trusted Sites security settings.

Add a "Compare Working Computer" export/import tool that captures nonsecret configuration:

```text
Edge version
Windows version
Relevant Edge policy values
Site-list version and matching entry
Trusted Sites mapping
Pop-up allowlist
Selected Internet Options zone settings
Installed legacy controls by name/version
Proxy settings
```

Exclude credentials and browser profile data.

---

# 45. Bookmark Management

The NATCCO procedure instructs users to bookmark the new system and remove obsolete bookmarks.

The application should not directly manipulate a user's browser favorites in the MVP unless implemented through a documented enterprise policy.

Provide:

```text
Copy Production URL
Open System
Create Desktop Shortcut
Create Start Menu Shortcut
Bookmark Instructions
Old Bookmark Cleanup Instructions
```

Optional enterprise feature:

```text
ManagedFavorites policy generation
```

Do not delete user bookmarks automatically.

When generating managed favorites, label the entry:

```text
NATCCO EKoopBanker+ CASA — Barbaza MPC
```

URL:

```text
https://cbs.natcco.coop/barbazampc/logon.asp
```

---

# 46. Support Escalation

Seed the NATCCO support address from the procedure as a configurable support contact:

```text
itechline@natcco.coop
```

Do not automatically send email.

Generate a support summary that the user can copy or attach.

Suggested subject:

```text
EKoopBanker+ Access Issue — Barbaza MPC
```

Support report must include:

```text
Date and time
BMPC branch/workstation
Windows version
Edge version
Target URL
DNS result
TCP 443 result
HTTPS result
TLS result
IE mode policy status
Site-list status
Trusted Sites status
Pop-up allowlist status
Redirect summary
Diagnostic correlation ID
Error screenshots added manually by user
```

Exclude credentials, account numbers, member data, session tokens, cookies, and page contents.

---

# 47. NATCCO First-Run Setup Wizard

Add a dedicated setup template.

```text
Step 1 — Confirm application
NATCCO EKoopBanker+ CASA
https://cbs.natcco.coop/barbazampc/logon.asp

Step 2 — Verify Microsoft Edge
Detect installation and version

Step 3 — Configure IE mode
Enable InternetExplorerIntegrationLevel
Set InternetExplorerIntegrationSiteList

Step 4 — Publish site list
Assign cbs.natcco.coop/barbazampc to IE11

Step 5 — Configure Trusted Sites
Assign https://cbs.natcco.coop to zone 2

Step 6 — Configure pop-ups
Allow pop-ups only for https://cbs.natcco.coop

Step 7 — Test network
DNS, TCP 443, HTTPS, TLS

Step 8 — Restart Edge
Obtain confirmation and restart safely

Step 9 — Launch and verify
Open the exact production URL

Step 10 — Record outcome
Save pass/fail results and support notes
```

---

# 48. NATCCO Acceptance Tests

The NATCCO profile is accepted only when:

```text
1. The exact production URL is stored using HTTPS.
2. The URL opens in Microsoft Edge.
3. The Barbaza MPC site-list rule resolves to IE11.
4. Edge policy shows IE integration enabled.
5. Edge policy shows the expected site-list endpoint.
6. Edge compatibility status shows the published site-list version.
7. cbs.natcco.coop is assigned to Trusted Sites.
8. Pop-ups are allowed for cbs.natcco.coop only.
9. The login page displays correctly.
10. The user can authenticate using test credentials supplied outside the application.
11. The CASA module opens successfully.
12. The module may open a new tab/window without being blocked.
13. Menus display correctly.
14. Logout works.
15. No unrelated domain was assigned to IE mode.
16. No global pop-up permission was enabled.
17. No broad "enable all security settings" action was performed.
18. The diagnostic report can be generated without exposing secrets.
19. Configuration can be repaired after deleting the local XML.
20. Configuration can be rolled back to its previous state.
```

Use nonproduction or authorized test credentials during testing. Never include credentials in source code, test data, logs, screenshots, or documentation.

---

# 49. Revised Definition of Done for the First Production Deployment

The first production release is complete when a Barbaza MPC workstation can:

```text
Install the signed MSI
Open the BMPC launcher
Select NATCCO EKoopBanker+ CASA
Configure IE mode through supported Edge policy
Publish the Barbaza MPC site-list entry
Assign cbs.natcco.coop to Trusted Sites
Allow pop-ups only for cbs.natcco.coop
Restart Edge with user confirmation
Open https://cbs.natcco.coop/barbazampc/logon.asp
Render the application in IE mode
Open the CASA module
Display all required menus
Run network and compatibility diagnostics
Generate a NATCCO support report
Repair local configuration
Restore the previous configuration
```
