# Security Best-Practices Audit

**Project:** BMPC.LegacyEdgeLauncher  
**Audit date:** 2026-07-16  
**Scope:** C# source and project files in `src/`, the solution, and the master requirements document. Generated `bin/` and `obj/` content was excluded from source review.

## Executive summary

The project has several good safeguards, including a strict HTTP/HTTPS launch-scheme allowlist, shell-free Edge URL argument handling, loopback-only site-list binding, parameterized database access, and no obvious embedded secrets. However, it is **not ready for a privileged production deployment** without remediation.

The audit found **four high**, **four medium**, and **two low** findings. The most important risks are: an elevated process can launch an executable selected through the current user's registry; privileged registry restore methods trust target paths and value names read from the SQLite database; the repository contains no implementation that provisions or verifies restrictive ACLs on the shared ProgramData state; and the resolved dependency graph contains high-severity vulnerable packages.

No critical finding was confirmed. Some high-risk attack chains depend on the deployed filesystem ACLs or on an administrator running the launcher elevated. Those conditions are realistic for this application's intended policy-management workflow and should be treated as security boundaries, not assumptions.

**Remediation progress (2026-07-16):** SEC-001, SEC-002, SEC-004, SEC-005, SEC-006, SEC-008, and SEC-009 are remediated in source. SEC-007 is partially remediated. SEC-003 and SEC-010 remain open. Administrator-only CLI commands are also now centrally authorization-gated.

## High severity

### SEC-001 — Elevated launcher trusts an executable path from HKCU

**Status:** Remediated in source on 2026-07-16. Executable discovery now uses HKLM and trusted Program Files locations only; HKCU is no longer consulted.

`EdgeLauncher.FindEdgeExecutable()` accepts `msedge.exe` from both HKLM and the current user's `App Paths` registry key, then `OpenAsync()` starts that path. An unprivileged user can control HKCU. If an administrator launches this application or CLI elevated and invokes `launch`, a pre-positioned HKCU value can select an attacker-controlled executable that will inherit the elevated token.

**Evidence:**

- [`EdgeLauncher.cs:27`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Edge/EdgeLauncher.cs#L27) searches machine and user registry hives.
- [`EdgeLauncher.cs:29`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Edge/EdgeLauncher.cs#L29) explicitly includes `Registry.CurrentUser`.
- [`EdgeLauncher.cs:84`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Edge/EdgeLauncher.cs#L84) starts the selected executable.

**Recommendation:** When elevated, never use HKCU executable discovery. Prefer a fixed path under trusted Program Files locations or HKLM App Paths, and verify the resolved file is not a reparse-point escape and has a valid Microsoft Authenticode signature before launch. Keep normal URL launching in the unelevated process.

### SEC-002 — Registry restore targets are trusted from mutable database records

**Status:** Remediated in source on 2026-07-16. All three restore paths now enforce service-owned hives/paths, value-name and type constraints, expected record semantics, and one-time restoration.

The three restore implementations use `RegistryPath`, `ValueName`, value type, and prior value directly from a database record. They do not verify the snapshot's scope, permitted registry path, permitted value-name allowlist, expected type, whether it was already restored, or that it was authentically created by this tool. In an elevated process, a forged or modified snapshot can redirect a restore to another HKLM policy value.

**Evidence:**

- [`EdgePolicyService.cs:102`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Registry/EdgePolicyService.cs#L102) loads a snapshot by ID; [`EdgePolicyService.cs:118`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Registry/EdgePolicyService.cs#L118) opens the persisted path after the new guard approves it.
- [`EdgeContentPolicyService.cs:94`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Registry/EdgeContentPolicyService.cs#L94) follows the same pattern; [`EdgeContentPolicyService.cs:102`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Registry/EdgeContentPolicyService.cs#L102) opens the stored path.
- [`TrustedSitesService.cs:87`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Registry/TrustedSitesService.cs#L87) also restores a stored path and value without binding it to the expected per-user ZoneMap location.
- [`ApplicationRepository.cs:122`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Database/ApplicationRepository.cs#L122) returns snapshots without integrity or semantic validation.

**Recommendation:** Give each restore service a hardcoded path and value-name allowlist. Require the expected scope/type, reject already-restored records, validate values strictly, and store privileged rollback records in an administrator-only location. Consider an authenticated snapshot format if records cross a writable trust boundary.

### SEC-003 — Restrictive ProgramData ACLs are required but not provisioned or verified

The application creates its database, site list, backup, and flag directories with plain `Directory.CreateDirectory()`. There is no installer or code in this repository that sets or verifies ACLs. These files influence elevated registry changes, executable behavior, rollback, audit history, and the Windows service. A standard user who can pre-create or modify this hierarchy can tamper with trusted state; the first-user directory creation case is particularly important under `C:\ProgramData`.

This also conflicts with the project's own explicit requirement that standard users may read the site list but must not modify it.

**Evidence:**

- [`ServiceCollectionExtensions.cs:22`](src/BMPC.LegacyEdgeLauncher.Infrastructure/ServiceCollectionExtensions.cs#L22) selects the shared database and line 26 creates its directory without ACL enforcement.
- [`SiteListPublisher.cs:54`](src/BMPC.LegacyEdgeLauncher.Infrastructure/SiteList/SiteListPublisher.cs#L54), [`BackupService.cs:44`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Backup/BackupService.cs#L44), and [`RestartFlag.cs:17`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Registry/RestartFlag.cs#L17) create or write shared privileged state without checking ownership or ACLs.
- [`AppConstants.cs:31`](src/BMPC.LegacyEdgeLauncher.Core/Constants/AppConstants.cs#L31) places all state under a single machine-wide root.
- [`BMPC_Legacy_Edge_IE_Mode_Launcher_Master_Prompt_Enhanced_NATCCO.md:1077`](BMPC_Legacy_Edge_IE_Mode_Launcher_Master_Prompt_Enhanced_NATCCO.md#L1077) requires restrictive ACLs.

**Recommendation:** Provision directories from a signed elevated installer before first use. Disable inherited write access and assign explicit least-privilege ACLs: administrators/system own and modify the database/backups/configuration; the service identity receives read access only to the published site list; standard users receive only the narrowly required read/execute access. Fail closed at startup if ownership or ACLs are unsafe. Separate per-user editable data from administrator-controlled machine policy state.

### SEC-004 — Resolved dependency graph contains high-severity vulnerable packages

**Status:** Remediated in source on 2026-07-16. The .NET 8 servicing baseline and bundled SQLite dependency were updated; NuGet audit now reports no known vulnerable packages for all five projects.

`dotnet list package --vulnerable --include-transitive` reported the following resolved packages:

| Package | Resolved | Projects | Advisory | Practical note |
|---|---:|---|---|---|
| `SQLitePCLRaw.lib.e_sqlite3` | 2.1.6 | Infrastructure, CLI, Desktop | [CVE-2025-6965](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) | Memory-corruption issue in bundled SQLite before 3.50.2. The database is local, but it is a trust-boundary input in this application. |
| `Microsoft.Extensions.Caching.Memory` | 8.0.0 | Infrastructure, CLI, Desktop | [CVE-2024-43483](https://github.com/advisories/GHSA-qj66-m88j-hmgj) | Hash-flooding denial of service; patched package version is 8.0.1. No direct hostile cache-key flow was identified. |
| `System.Text.Json` | 8.0.4 | Infrastructure, CLI, Desktop | [CVE-2024-43485](https://github.com/advisories/GHSA-8g4q-xg66-9fp4) | Patched in 8.0.5. Current code does not use `JsonExtensionData`, reducing known exploitability. |
| `System.Text.Json` | 8.0.0 | SiteListService | [CVE-2024-30105](https://github.com/advisories/GHSA-hh2w-p6rv-4g7w), [CVE-2024-43485](https://github.com/advisories/GHSA-8g4q-xg66-9fp4) | The service does not deserialize request JSON, but deployment should not ship a known-vulnerable runtime graph. |

**Evidence:** Old baseline package references appear in [`BMPC.LegacyEdgeLauncher.Infrastructure.csproj:13`](src/BMPC.LegacyEdgeLauncher.Infrastructure/BMPC.LegacyEdgeLauncher.Infrastructure.csproj#L13), [`BMPC.LegacyEdgeLauncher.SiteListService.csproj:12`](src/BMPC.LegacyEdgeLauncher.SiteListService/BMPC.LegacyEdgeLauncher.SiteListService.csproj#L12), and [`BMPC.LegacyEdgeLauncher.Cli.csproj:13`](src/BMPC.LegacyEdgeLauncher.Cli/BMPC.LegacyEdgeLauncher.Cli.csproj#L13).

**Recommendation:** Update all Microsoft packages to the latest supported .NET 8 servicing versions as one consistent baseline, update EF Core SQLite to a release that resolves to SQLite 3.50.2 or newer, regenerate lock/assets data, and rerun NuGet audit. Do not jump to .NET 10 packages without intentionally retargeting and compatibility testing.

## Medium severity

### SEC-005 — Elevated site-list policy accepts an arbitrary URL

**Status:** Remediated in source on 2026-07-16. Site-list policy now permits HTTPS or loopback-only HTTP, rejects credentials/fragments, and is validated at both the CLI and privileged registry sink.

The CLI accepts any string for `--sitelist-url`, and `EdgePolicyService` writes it to HKLM without URI parsing or a scheme/host policy. This permits accidental or malicious configuration of `file:`, UNC, non-loopback HTTP, credential-bearing, or attacker-controlled site-list locations. An attacker-controlled Enterprise Mode Site List can expand which sites receive legacy IE-mode handling.

**Evidence:** [`CommandHandlers.cs:251`](src/BMPC.LegacyEdgeLauncher.Cli/Commands/CommandHandlers.cs#L251) accepts the option; [`EdgePolicyService.cs:67`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Registry/EdgePolicyService.cs#L67) writes it without validation.

**Recommendation:** Default to the exact loopback endpoint. If remote lists are required, allow only HTTPS and explicitly approved hosts, reject credentials/fragments/UNC/file URLs, and optionally pin the expected path. Validate again immediately before the registry write.

### SEC-006 — Elevation API accepts a raw argument string rather than validated commands

**Status:** Remediated in source on 2026-07-16. The elevation contract now accepts an allowlisted command enum, bounds arguments, and passes each argument as a separate token without logging argument contents.

`RelaunchElevatedAsync(string arguments, ...)` passes a raw command line to the elevated copy of the application. It does not invoke a shell, so this is not direct shell injection, but it creates a fragile confused-deputy boundary and does not satisfy the requirement to pass only validated commands to the elevated helper.

**Evidence:** [`PrivilegeService.cs:31`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Security/PrivilegeService.cs#L31) accepts the string; [`PrivilegeService.cs:39`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Security/PrivilegeService.cs#L39) passes it as a command line; [`Interfaces.cs:72`](src/BMPC.LegacyEdgeLauncher.Core/Interfaces/Interfaces.cs#L72) exposes the raw-string contract.

**Recommendation:** Replace the string contract with a small command enum plus typed, validated parameters. Populate `ProcessStartInfo.ArgumentList` one token at a time. The elevated side must independently authorize the command and revalidate every parameter.

### SEC-007 — Backup hashes do not authenticate backup sets

**Status:** Partially remediated in source on 2026-07-16. Restore now accepts only the two expected leaf files, verifies canonical containment and hash format, and rejects reparse points. Cryptographic authenticity still depends on resolving SEC-003 or adding a protected signing/MAC key.

The manifest and backed-up files live together under the same directory, and the manifest's SHA-256 values are trusted without a signature or keyed MAC. Anyone who can alter the backup directory can replace both a database and its hash. Manifest file names are also combined with the backup path without restricting them to the two expected leaf names.

**Evidence:** [`BackupService.cs:76`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Backup/BackupService.cs#L76) writes the unauthenticated manifest; [`BackupService.cs:126`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Backup/BackupService.cs#L126) trusts it; lines 130–137 resolve and hash manifest-controlled names.

**Recommendation:** First enforce administrator-only ACLs. Also reject any manifest key except exact known leaf names, canonicalize and verify paths remain under the backup root, reject reparse points, and authenticate manifests if backups can be transported or written by another principal. Label plain hashes as corruption detection, not authenticity.

### SEC-008 — Loopback HTTP service does not restrict Host headers

**Status:** Remediated in source on 2026-07-16. The service now enforces the exact `127.0.0.1:<configured-port>` Host value, validates the configured port, and adds `nosniff` and no-referrer response headers.

Binding to `127.0.0.1` prevents direct network access, but the application leaves host filtering at its permissive default. A DNS-rebinding-capable webpage can potentially address the loopback service using an attacker-controlled Host name and read the internal site list or health metadata under the attacker's web origin.

**Evidence:** [`Program.cs:13`](src/BMPC.LegacyEdgeLauncher.SiteListService/Program.cs#L13) binds to loopback, while the remainder of the service config does not set `AllowedHosts` or perform an explicit Host check; [`Program.cs:19`](src/BMPC.LegacyEdgeLauncher.SiteListService/Program.cs#L19) serves the site list without authentication.

**Recommendation:** Allow only the exact expected Host values (`127.0.0.1:8765`, with the configured port) and reject all others before endpoint execution. Keep CORS disabled, add `X-Content-Type-Options: nosniff`, and consider whether `/health` needs to disclose the full hash and timestamp.

## Low severity

### SEC-009 — Detailed exception messages are returned to local users

**Status:** Remediated in source on 2026-07-16. Normal CLI and desktop errors now use safe messages and support references; CLI technical details require both administrator context and an explicit diagnostic environment switch.

Several service results expose `ex.Message`, and the CLI prints `TechnicalDetails`. These messages can reveal local paths, database details, registry information, or internal state to users who only need an operational error.

**Evidence:** [`CommandHandlers.cs:480`](src/BMPC.LegacyEdgeLauncher.Cli/Commands/CommandHandlers.cs#L480), [`BackupService.cs:86`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Backup/BackupService.cs#L86), and [`App.xaml.cs:46`](src/BMPC.LegacyEdgeLauncher.Desktop/App.xaml.cs#L46).

**Recommendation:** Show a correlation ID and safe message by default; retain detailed exceptions in a protected administrator-readable log. A deliberate diagnostic mode may expose details to authorized support users.

### SEC-010 — Security regression coverage and build coverage are absent

No test projects or CI workflow are present. The Desktop project is omitted from the solution, and building it directly fails because referenced `Views`/view-model types are absent. Consequently, the successful solution build does not cover the desktop security boundary, and there are no automated tests for URL validation, executable discovery, registry target allowlists, backup traversal, ACL checks, or service Host filtering.

**Evidence:** [`BMPC.LegacyEdgeLauncher.sln`](BMPC.LegacyEdgeLauncher.sln) lists Core, Infrastructure, SiteListService, and CLI but not Desktop. [`App.xaml.cs:2`](src/BMPC.LegacyEdgeLauncher.Desktop/App.xaml.cs#L2) references missing view-model/view namespaces.

**Recommendation:** Add the complete Desktop project and security-focused test projects to the solution and CI. Make Release build, tests, `dotnet list package --vulnerable --include-transitive`, and signing verification required release gates.

## Positive controls observed

- Launch URLs are restricted to HTTP/HTTPS and rechecked immediately before launch: [`UrlNormalizer.cs:73`](src/BMPC.LegacyEdgeLauncher.Core/Validation/UrlNormalizer.cs#L73).
- Edge receives the URL through `ProcessStartInfo.ArgumentList` with `UseShellExecute = false`, avoiding URL-to-command-line injection: [`EdgeLauncher.cs:81`](src/BMPC.LegacyEdgeLauncher.Infrastructure/Edge/EdgeLauncher.cs#L81).
- The site-list service binds specifically to `127.0.0.1`: [`Program.cs:13`](src/BMPC.LegacyEdgeLauncher.SiteListService/Program.cs#L13).
- Site-list XML is constructed with LINQ to XML, which safely escapes values: [`SiteListGenerator.cs:37`](src/BMPC.LegacyEdgeLauncher.Infrastructure/SiteList/SiteListGenerator.cs#L37).
- Database queries use EF Core or bound SQLite parameters; no SQL string interpolation from user input was found.
- Desktop execution is `asInvoker` with `uiAccess=false`: [`app.manifest:7`](src/BMPC.LegacyEdgeLauncher.Desktop/app.manifest#L7).
- A repository scan found no obvious embedded passwords, API keys, tokens, or private keys.

## Verification performed

- `dotnet build BMPC.LegacyEdgeLauncher.sln -c Release --no-restore` — passed with 0 warnings and 0 errors, but Desktop is not in the solution.
- Direct Desktop Release build — failed due to missing `BMPC.LegacyEdgeLauncher.Desktop.Views` (and related incomplete UI source).
- Initial `dotnet list ... package --vulnerable --include-transitive` — found the advisories documented in SEC-004. After remediation, the same audit reports no known vulnerable packages for the solution or Desktop.
- `dotnet list ... package --outdated --include-transitive` — confirmed the dependency baseline is substantially behind current servicing releases.
- Secret-pattern scan — no obvious embedded secrets found.

## Recommended remediation order

1. Decide and implement the ACL/service-identity/audit-write model in SEC-003.
2. Complete SEC-007 authenticity using the protected storage model from SEC-003.
3. Complete the Desktop project and add security regression tests and release gates for SEC-010.
