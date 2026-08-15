# WinUI 3 migration

## Audited baseline (2026-08-14)

- Repository: `Wind134/SSH-Tunnel-Manager`; default branch `main`.
- Baseline commit: `e55fa62429cb2e25c7a8f415c5aa4a1ebd86d673` (PR #3 merged).
- Open pull requests at audit time: none.
- Latest published tag: `v1.0.0`; its two recorded `Build & Release` runs succeeded.
- Stable UI: `src/TinyTools` (WPF shell), with WPF feature libraries in
  `src/SSHTunnelManager` and `src/HandleViewer`.
- Stable release: .NET 8 `win-x64`, unpackaged, self-contained, single file,
  ZIP plus Inno Setup installer.
- Migration branch: `codex/winui3-migration` from the audited `origin/main`.

## Decisions

1. Keep the WPF app and current installer untouched until the WinUI acceptance
   matrix is green. WinUI publishes as a preview artifact in parallel.
2. `TinyTools.Core` owns UI-neutral models and services. Stage 1 links the
   proven source files into the new assembly so assembly ownership changes
   without a risky namespace-and-file move in the same commit.
3. Use the official component graph shipped by `Microsoft.WindowsAppSDK 2.3.1`:
   WinUI 2.3.0, Foundation 2.3.5 and InteractiveExperiences 2.1.3 (Microsoft
   Windows App SDK license, Microsoft-maintained current channel at audit time).
   Referencing the required components directly avoids shipping unused AI, ML,
   Widgets and DWrite payloads from the meta-package. Target Windows 10 build
   19041 as the existing app does.
4. Use Mica only when supported. Windows 10 uses the normal theme background.
5. Do not use `CommunityToolkit.WinUI.UI.Controls.DataGrid` 7.1.2: the package's
   last stable update was in 2021. The production port table uses WinUI's
   virtualizing `ListView` and a deterministic grid row without adding a UI
   dependency. Re-evaluate `WinUI.TableView` only if sorting, column resizing,
   or accessibility requirements outgrow this implementation.
6. Keep unpackaged + self-contained deployment. The publish profile carries the
   Windows App SDK single-file requirements; first launch extracts bundled
   content to a temporary location by design.
7. Upgrade the existing SSH.NET dependency from 2020.0.2 to 2026.0.0 in Core.
   It remains MIT-licensed and maintained, restores modern key-format and
   algorithm support, and is the patched release for GHSA-q939-rpr3-3284.
   Tunnel regression tests and a real SSH smoke test remain required before the
   WinUI app becomes stable.

## Staged implementation plan

### Stage 1 — parallel foundation (this branch)

- Establish latest Git/GitHub baseline and a dedicated branch.
- Create `TinyTools.Core`; make both WPF feature libraries consume it.
- Add an unpackaged, self-contained WinUI app with native title bar,
  NavigationView, Mica gating, restrained entrance transitions, theme switching,
  read-only tunnel configuration, live port inspection, and path-lock querying.
- Validate native virtualizing ListView as the zero-dependency port table.
- Add WinUI single-file preview publishing while keeping WPF release output.

### Stage 2 — SSH and application lifecycle parity (completed 2026-08-15)

- Move tunnel presentation state out of the service and expose asynchronous host
  key confirmation instead of a synchronous UI callback.
- Implement add/edit, trust reset, connect/stop/start-all, reconnect status,
  logs, validation, and accessible ContentDialogs in WinUI.
- Add single-instance activation, close/minimize policy, tray icon, notifications,
  unhandled-error reporting, and configuration migration tests.

Verified checkpoint:

- The WinUI tunnel page supports create/edit/delete, SSH config import,
  start/stop/start-all, reconnect state, shared logs and asynchronous first-use
  or changed Host Key confirmation. WPF consumes the same new Core contract.
- Application-level services survive page navigation. The WinUI preview has an
  independent single-instance channel so it can run beside the WPF rollback.
- The tray icon and context menu use Win32 `Shell_NotifyIcon` directly; no
  Windows Forms or third-party tray dependency is shipped.
- Single-file configuration is explicitly rooted beside the distributed EXE,
  not in the temporary bundle extraction directory, so upgrades retain data.
- Release WPF and WinUI builds complete with zero warnings; 19 automated tests
  pass. The verified `win-x64` self-contained single file is 161.84 MiB and its
  real-process smoke test covers first launch, second-instance activation,
  close-to-tray, restore and crash-log absence.

### Stage 3 — inspection, process actions and updates (completed 2026-08-15)

- Completed port filtering, refresh policy, virtualized results, copy/open
  actions, and guarded process-tree termination.
- Added file/folder pickers, page-wide drag/drop, cancellable recursive scans,
  detailed results, and the same process actions to the path-lock page.
- Removed the migration-oriented overview page. SSH tunnels are now the default
  product entry, with independent persisted navigation for ports and file locks.
- Replaced the physical-pixel startup size with a DPI-aware logical size policy.
  The app clamps restored windows to a usable minimum and the current display
  work area, then remembers the last non-maximized size with debounced writes.
- Added a dependency-free GitHub Release checker/downloader. It accepts only
  `TinyTools-WinUI-*` assets and requires SHA-256 verification before making a
  download available to the user. ZIP updates use a guided manual replacement;
  a future WinUI installer can be launched only after explicit confirmation.
- The release workflow now publishes a versioned WinUI ZIP and checksum beside
  the stable WPF fallback artifacts.

Verified checkpoint:

- Release WinUI build completes with zero warnings and all 33 automated tests
  pass, including window-size edge cases, update selection and tamper rejection.
- Remaining Stage 3 acceptance work is live system-theme change observation,
  large-snapshot performance/accessibility testing, and full manual UI coverage.

### Stage 4 — release cutover

- Add the WinUI Inno Setup output beside the existing verified ZIP, then test clean install,
  first-launch extraction, in-place upgrade, uninstall, and config preservation.
- Run the complete acceptance matrix on Windows 10 and Windows 11.
- Switch release defaults only after approval; keep the WPF source/tag as a
  rollback line. Evaluate package identity/MSIX only for a concrete API need.

## Acceptance matrix

- Release restore/build and automated tests.
- WinUI launch on clean Windows 10 and Windows 11 machines.
- Light/dark/system theme and live theme changes.
- Window resize, DPI changes, keyboard navigation and screen-reader labels.
- Tray restore/exit, single instance, notifications and close policies.
- SSH password/private-key flow, Host Key first-use/change/reject, reconnect and
  shutdown cancellation.
- IPv4/IPv6 port list, refresh/filter, permission failures and process action.
- File and recursive folder lock queries, inaccessible paths and cancellation.
- Self-contained single-file first launch, ZIP portability, Inno clean install,
  upgrade over v1.0.0, uninstall and configuration preservation.
