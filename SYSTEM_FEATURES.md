# BMPC Legacy Edge IE Mode Launcher

<img src="coop.png" alt="Cooperative logo" width="220">

## System Features and User Guide

**Organization:** Barbaza Multi-Purpose Cooperative  
**Version:** 1.0.0  
**Platform:** Windows 10 and Windows 11, 64-bit

## 1. System purpose

BMPC Legacy Edge IE Mode Launcher helps approved legacy web applications operate on modern
Windows computers. Its primary purpose is to open systems such as NATCCO EKoopBanker+ CASA in
Microsoft Edge using Internet Explorer compatibility mode.

The system does not replace Microsoft Edge and does not contain its own web browser. It manages
the Microsoft Edge Enterprise Mode Site List and the Windows policies that tell Edge which sites
must use the IE11 compatibility engine.

The system includes:

- A Windows desktop application for normal users and administrators.
- A command-line tool for administration and automation.
- A local SQLite configuration database.
- Enterprise Mode Site List generation and publishing.
- Diagnostics, audit records, backups, and recovery commands.

## 2. Main features

### 2.1 Application dashboard

The Dashboard displays all registered applications and provides an **Open System** button.
Each application tile shows:

- Application name and environment.
- Launch URL.
- Selected browser.
- Enabled or disabled status.
- Date of the last recorded compatibility test, when available.

Only enabled applications can be launched. The URL is validated again immediately before the
browser starts.

### 2.2 Browser selection

An administrator can select a browser independently for every application:

| Browser option | Behavior | Recommended use |
|---|---|---|
| **Edge (IE mode)** | Opens the URL in Edge. The published site list assigns the IE11 compatibility engine. | Recommended for legacy systems on Windows 10 and 11. |
| **Microsoft Edge (normal mode)** | Opens the URL in the normal Edge rendering engine. | Applications that no longer require IE compatibility. |
| **Internet Explorer (legacy/unsupported)** | Attempts to start the retired `InternetExplorer.Application` Windows COM component. | Exceptional testing on computers where legacy IE is still available. |

The legacy Internet Explorer option provides behavior equivalent to the older VBScript launcher,
but the system calls Windows directly and does not execute a `.vbs` file. Windows may block or
remove standalone Internet Explorer. If it is unavailable, the launcher displays an error and does
not silently open a different browser.

### 2.3 Application management

Administrators can add, edit, enable, disable, and delete application records. Each record contains:

- Application name.
- Environment: Production, Testing, or Development.
- Base URL.
- Optional path-specific site-list rule.
- Browser selection.
- Compatibility mode.
- Enabled status.

After changing an application, the administrator must publish the site list. Edge may also need to
be restarted before the new configuration takes effect.

### 2.4 Enterprise Mode Site List

The system generates Microsoft Enterprise Mode Site List XML from enabled application records.
It provides:

- Host-level or path-specific matching rules.
- IE11 or normal Edge rendering assignments.
- Compatibility-mode values.
- Duplicate-rule prevention and XML validation.
- Increasing site-list version numbers.
- SHA-256 hashes for published versions.
- Backup of the previous site list for rollback.

The default published file is:

```text
C:\ProgramData\BMPC\LegacyEdgeLauncher\SiteList\sitelist.xml
```

Edge can read the local file directly. The optional site-list service can expose the file through
the loopback URL `http://127.0.0.1:8765/sitelist.xml`.

### 2.5 Edge IE-mode policy setup

The **Policies & Setup** page can read and apply these Microsoft Edge policies:

- Enable Internet Explorer integration mode.
- Configure the Enterprise Mode Site List location.
- Optionally allow users to reload sites in IE mode.

The page displays the expected value, current value, configuration status, and whether a setting
is controlled by Group Policy. The system refuses to silently override organization-managed
policy.

### 2.6 Trusted Sites and legacy .NET configuration

For approved legacy applications, an administrator can add the exact host to the Windows Trusted
Sites zone. The system does not add a wildcard domain.

It can also configure the Trusted Sites settings required by older .NET browser technologies:

- XAML browser applications.
- Loose XAML.
- XPS documents.
- High Safety permissions for components with manifests.

These settings apply to every website placed in the user's Trusted Sites zone. The system does not
change the Internet zone and reports when Group Policy controls the configuration.

### 2.7 Pop-up allowlist

Some legacy screens open functions in a new browser window. The system can add the application's
exact origin to Edge's `PopupsAllowedForUrls` policy. The allowance is scoped to the configured
scheme, host, and non-default port rather than all websites.

### 2.8 Diagnostics

Standard users and administrators can run diagnostics. The report checks:

- Microsoft Edge installation and version.
- IE-mode policy configuration.
- Published site-list existence and valid XML.
- Site-list file or service availability.
- Configuration database availability.
- Whether an Edge restart is pending.
- Presence of the application's published rule.
- Trusted Sites assignment.
- Required Trusted Sites legacy .NET settings.
- Edge pop-up allowance.

Each result is marked **Passed**, **Warning**, **Failed**, or **Not Applicable** and includes a
recommended action when appropriate. Every diagnostic run receives a correlation reference and is
stored in the database. The desktop application can copy a secret-free report to the clipboard for
support.

### 2.9 Backup and recovery

The command-line tool can:

- Back up the SQLite database and published site list.
- List available backups.
- Verify SHA-256 file hashes before restoration.
- Reject unexpected files, incomplete backups, and reparse points.
- Restore a selected backup.
- Roll back the current site list to the preceding published version.

Backups are stored under:

```text
C:\ProgramData\BMPC\LegacyEdgeLauncher\Backups
```

### 2.10 Audit logging

Important operations create audit records, including:

- Application creation, update, and deletion.
- Browser launches and their selected browser.
- Policy application.
- Trusted Sites and pop-up changes.
- Site-list publishing.
- Backup and restore operations.
- Edge restart requests.

Audit records include the action, user, computer, timestamp, affected entity, and success or
failure status. Recent records can be queried with the CLI `audit` command.

### 2.11 Standard-user and administrator modes

The application separates everyday use from configuration changes.

**Standard users can:**

- View registered applications.
- Open enabled applications.
- View current status.
- Run and copy diagnostics.

**Administrators can additionally:**

- Add, edit, enable, disable, or delete applications.
- Publish and roll back site lists.
- Apply Edge policies.
- Configure Trusted Sites and legacy .NET settings.
- Configure the pop-up allowlist.
- Restart Edge after configuration changes.
- Create and restore backups.
- Query audit records.

Administrative operations verify elevation rather than relying only on disabled user-interface
buttons.

## 3. Typical setup workflow

1. Run the desktop application as administrator.
2. Open **Applications** and add or review the legacy application's URL and browser choice.
3. Select **Edge (IE mode)** when the application requires Internet Explorer compatibility.
4. Click **Publish site list**.
5. Open **Policies & Setup** and click **Apply IE mode policies**.
6. Configure **Trusted Site + legacy .NET** if the application requires those Windows settings.
7. Select **Allow pop-ups for site** if legacy screens open new windows.
8. Restart Edge when the header reports that a restart is required.
9. Run **Diagnostics** and resolve any failed checks.
10. Return to the Dashboard and click **Open System**.

## 4. Command-line features

The CLI uses the same database and services as the desktop application.

```powershell
bmpc-launcher status
bmpc-launcher apps
bmpc-launcher launch "NATCCO EKoopBanker+ CASA"
bmpc-launcher diagnose "NATCCO EKoopBanker+ CASA"
```

Administrator commands include:

```powershell
bmpc-launcher apply-policies
bmpc-launcher publish
bmpc-launcher trust "NATCCO EKoopBanker+ CASA"
bmpc-launcher allow-popups "NATCCO EKoopBanker+ CASA"
bmpc-launcher restart-edge
bmpc-launcher backup "Before configuration change"
bmpc-launcher backups
bmpc-launcher restore-backup <backup-id>
bmpc-launcher rollback-sitelist
bmpc-launcher audit
```

Run administrator commands from an elevated terminal.

## 5. Security features

- Only `http` and `https` application URLs can be launched.
- Edge is located through the machine registry or standard installation directories; a
  user-controlled executable path is not trusted.
- Browser URLs are passed as process arguments without command-shell execution.
- External VBScript files are not executed.
- Disabled applications cannot be launched.
- Site-list XML is validated before publishing.
- Configuration changes are scoped to documented registry values.
- Existing policy and site-list state is backed up before supported changes.
- Passwords, cookies, authentication tokens, and browser profile data are not stored or logged.
- Backup restoration includes integrity and path-safety checks.

## 6. Data locations

| Data | Location |
|---|---|
| Configuration database | `C:\ProgramData\BMPC\LegacyEdgeLauncher\Database\launcher.db` |
| Published site list | `C:\ProgramData\BMPC\LegacyEdgeLauncher\SiteList\sitelist.xml` |
| Backups | `C:\ProgramData\BMPC\LegacyEdgeLauncher\Backups` |
| Logs | `C:\ProgramData\BMPC\LegacyEdgeLauncher\Logs` |
| Exports | `C:\ProgramData\BMPC\LegacyEdgeLauncher\Exports` |

## 7. Limitations

- Standalone Internet Explorer is retired and may be disabled or unavailable on Windows 10 and
  Windows 11. The system cannot override an operating-system removal or organizational policy.
- Edge IE mode requires correctly applied policies, a published matching site-list rule, and an
  Edge restart after configuration changes.
- IE mode improves compatibility but cannot guarantee that every ActiveX control, obsolete
  browser plug-in, or server application will function.
- Trusted Sites and security-zone changes may be controlled by domain Group Policy.
- IE mode should be treated as a migration aid while legacy applications are modernized.

## 8. Support checklist

If an application opens but some buttons do not work:

1. Confirm that **Edge (IE mode)** is selected for the application.
2. Republish the site list.
3. Apply the IE-mode policies.
4. Configure Trusted Sites and legacy .NET settings.
5. Allow pop-ups for the application's exact origin.
6. Restart Edge.
7. Run Diagnostics and copy the report for technical support.
