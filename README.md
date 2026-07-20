# BMPC Legacy Edge IE Mode Launcher

<img src="coop.png" alt="Cooperative logo" width="220">

A Windows desktop tool that lets approved legacy web systems open in **Microsoft Edge using
Internet Explorer mode**, via Edge's supported, policy-based Enterprise Mode Site List — not a
custom browser, not embedded IE, and not undocumented Edge flags.

Built for **Barbaza Multi-Purpose Cooperative** to run the **NATCCO EKoopBanker+ CASA** core
banking system (`https://cbs.natcco.coop/barbazampc/logon.asp`) in IE mode.

See [System Features and User Guide](SYSTEM_FEATURES.md) for complete functional documentation,
setup instructions, security controls, and system limitations.

## How it works

```
Register URL → Generate Enterprise Mode Site List XML → Apply Edge IE-mode policies
   → Restart Edge → Launch URL normally → Edge matches the site list → opens in IE mode
```

## Prerequisites

- Windows 10 or 11 (64-bit)
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Microsoft Edge installed
- **Administrator rights** are required only for policy/registry changes (see *Modes* below). Normal
  launching does not need elevation.

## Solution layout

| Project | Purpose |
|---|---|
| `src/BMPC.LegacyEdgeLauncher.Core` | Models, enums, interfaces, validation, URL normalization |
| `src/BMPC.LegacyEdgeLauncher.Infrastructure` | Registry/policy, site-list generation & publishing, Edge launcher, diagnostics, backup, audit, EF Core/SQLite |
| `src/BMPC.LegacyEdgeLauncher.Desktop` | WPF (MVVM) desktop application |
| `src/BMPC.LegacyEdgeLauncher.Cli` | `bmpc-launcher` command-line tool for admins/automation |
| `src/BMPC.LegacyEdgeLauncher.SiteListService` | Local loopback service that hosts `sitelist.xml` |
| `tests/BMPC.LegacyEdgeLauncher.UnitTests` | URL normalization, scheme rejection, application validation |
| `tests/BMPC.LegacyEdgeLauncher.XmlTests` | Site-list XML generation, escaping, versioning, validation |

Application data lives under `C:\ProgramData\BMPC\LegacyEdgeLauncher\` (database, site list,
backups, logs, exports).

## Build

```powershell
dotnet build BMPC.LegacyEdgeLauncher.sln
```

## Run the desktop app

From the repository root:

```powershell
dotnet run --project src/BMPC.LegacyEdgeLauncher.Desktop
```

On first run it creates the SQLite database and seeds the NATCCO EKoopBanker+ CASA application, so
the dashboard already shows one tile.

### Browser choice

Each application has an independent launch-browser option:

- **Edge (IE mode)** — recommended. The URL is opened in Edge and the published Enterprise Mode
  Site List selects the IE11 rendering engine.
- **Microsoft Edge (normal mode)** — opens the URL in Edge without assigning it to IE mode.
- **Internet Explorer (legacy/unsupported)** — attempts the same
  `InternetExplorer.Application` COM automation used by older VBScript launchers. It works only
  when Windows still exposes that component; current Windows 10 and Windows 11 installations may
  disable or remove it. The application reports a clear error instead of silently changing browser.

The launcher invokes the legacy COM component directly and does not execute uploaded or external
VBScript files. All three choices retain the same `http`/`https` URL safety validation.

## Run the CLI

```powershell
dotnet run --project src/BMPC.LegacyEdgeLauncher.Cli -- status
dotnet run --project src/BMPC.LegacyEdgeLauncher.Cli -- apps
dotnet run --project src/BMPC.LegacyEdgeLauncher.Cli -- diagnose
dotnet run --project src/BMPC.LegacyEdgeLauncher.Cli -- help
```

Everything after `--` is passed to the tool.

## Modes: standard user vs. administrator

The app detects elevation at startup:

- **Standard user** (launched normally): view applications, launch approved systems, run read-only
  diagnostics. Add/Edit/Delete, Publish, and all policy actions are disabled.
- **Administrator**: everything above, plus apply Edge IE-mode policies, publish the site list, add
  `cbs.natcco.coop` to Trusted Sites, allow pop-ups for that site only, and restart Edge.

When the administrator adds the application to Trusted Sites, the tool also configures the current
user's Trusted Sites zone for the legacy .NET behaviors shown in Internet Properties: XAML browser
applications, XPS documents, and Loose XAML are enabled, while manifest components remain at
**High Safety**. These settings affect every site in the Trusted Sites zone. The tool does not modify
the Internet zone and refuses to override organization-managed zone policy.

To use administrator features, build once and run the produced executable from an **elevated**
terminal (policy changes write to `HKLM`):

```powershell
dotnet build src/BMPC.LegacyEdgeLauncher.Desktop
# then, in a terminal opened "Run as administrator":
& "src\BMPC.LegacyEdgeLauncher.Desktop\bin\Debug\net8.0-windows\BMPC.LegacyEdgeLauncher.exe"
```

The equivalent CLI admin commands (`apply-policies`, `publish`, `allow-popups`, `restart-edge`,
`trust`, backup/restore) also require an elevated prompt.

## Test

```powershell
dotnet test tests/BMPC.LegacyEdgeLauncher.UnitTests
dotnet test tests/BMPC.LegacyEdgeLauncher.XmlTests
```

## Policy behavior

The tool reads, applies, backs up, and restores these documented Edge policies under
`HKLM\SOFTWARE\Policies\Microsoft\Edge`:

- `InternetExplorerIntegrationLevel` = `1` (IE mode)
- `InternetExplorerIntegrationSiteList` = the configured site-list URL (default
  `http://127.0.0.1:8765/sitelist.xml`)
- `InternetExplorerIntegrationReloadInIEModeAllowed` — supported, off by default

It never deletes the Edge policy key, never silently overwrites organization-managed (Group Policy)
values, and restores only values it changed.

## Known limitations

- IE mode is a compatibility measure, not a permanent modernization strategy.
- Direct Internet Explorer launch cannot be forced when Windows has disabled the retired IE11
  desktop application. Use Edge (IE mode) for supported compatibility on current Windows systems.
- Router/DrayTek detection is an optional environment check, not a guaranteed hardware claim.
- The release includes Inno Setup installers for initial machine-wide deployment and per-user
  updates. A WiX MSI package and PowerShell deployment export are not included.

## Security notes

- Standard users may launch only enabled, stored records; `http`/`https` schemes only, validated
  again immediately before launch.
- No passwords, cookies, session tokens, or browser profile data are ever stored or logged.
- Trusted Sites and pop-up allowances are scoped to the single approved host — never wildcarded.
