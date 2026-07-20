# BMPC IE Launcher — Deployment & Update Guide

This app is deployed to ~200 **standalone** PCs (no domain / no MDM) and **updates itself**
from an internal web location. There are two installers and one publish workflow.

---

## The two installers

| Installer | When | Admin? | Installs to |
|---|---|---|---|
| `BMPC-IE-Launcher-Setup-<ver>.exe` | **First-time** setup on a machine | **Yes (UAC)** | `Program Files` |
| `BMPC-IE-Launcher-Update-<ver>.exe` | **Auto-updates** (run by the app) | **No** | `%LOCALAPPDATA%\Programs\BMPC\IE Launcher` |

- The **Setup** installer is machine-wide and is where IT applies the Edge IE-mode policies,
  `.dprt` auto-open, and IE emulation (all need admin, once per machine).
- The **Update** installer is per-user and silent — this is what each unit downloads and runs
  on its own when a new version is published. No admin, no UAC.

---

## One-time per machine (IT, with admin)

1. Run `BMPC-IE-Launcher-Setup-<ver>.exe`.
2. In the app: gear → password → **Policies & Setup** → **Apply IE mode policies**,
   **Auto-open reports in viewer**, and (if using Trusted Sites) **Configure Trusted Site**.
3. Confirm `C:\ProgramData\BMPC\LegacyEdgeLauncher\update-config.json` exists and points at
   your update server (see below). Edit it if needed.

After this, the unit updates itself — no more manual reinstalling.

---

## The update server (host once, internal)

Pick any internal web location every unit can reach over HTTP, e.g.
`http://updates.bmpc.local/bmpc/`. Host two files there:

```
http://updates.bmpc.local/bmpc/
    version.json                          <- the manifest the app checks
    BMPC-IE-Launcher-Update-1.1.0.exe     <- the per-user update installer
```

Set that base URL in **`update-config.json`** on each unit (deployed with the Setup installer):

```json
{
  "updateUrl": "http://updates.bmpc.local/bmpc/",
  "mode": "Prompt",
  "timeoutSeconds": 5
}
```

`mode` can be: `Prompt` (ask user — default), `Silent` (auto-install), `NotifyOnly`
(just tell them), or `Disabled` (no checking).

---

## Publishing an update (the whole point — do this ONCE to patch all 200 units)

1. **Build** the new version:
   - Bump `ApplicationVersion` in `src/…Core/Constants/AppConstants.cs` and the two `.iss`
     `MyAppVersion` values to the new number (e.g. `1.1.0`).
   - `dotnet publish …Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\Desktop`
   - Compile the **per-user** installer: `ISCC.exe installer\BMPC.IE.Launcher.PerUser.iss`
     → produces `installer\Output\BMPC-IE-Launcher-Update-1.1.0.exe`.

2. **Hash it** (this exact value goes in version.json):
   ```powershell
   (Get-FileHash installer\Output\BMPC-IE-Launcher-Update-1.1.0.exe -Algorithm SHA256).Hash.ToLower()
   ```

3. **Fill in `version.json`** (copy `version.json.template`):
   - `version` = new version, `url` = the update exe filename, `sha256` = the hash from step 2,
     `notes` = what changed.

4. **Upload** the update exe and `version.json` to the update server folder.

That's it. Each unit checks `version.json` on next launch, sees the newer version, prompts the
user, verifies the SHA-256, and self-installs. **No reinstalling on 200 machines.**

---

## Safety notes

- The update check is **best-effort**: if the server is down/slow, the app starts normally and
  simply doesn't update.
- The downloaded installer is **rejected unless its SHA-256 matches** `version.json` — so a
  corrupt or tampered file will not run.
- Keep the update server access-controlled: anyone who can write to that folder can push code
  to all units. Treat it like a software-distribution point.
