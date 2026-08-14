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
3. Use stable `Microsoft.WindowsAppSDK 2.3.1` (MIT, Microsoft-supported current
   channel at audit time). Target Windows 10 build 19041 as the existing app does.
4. Use Mica only when supported. Windows 10 uses the normal theme background.
5. Do not use `CommunityToolkit.WinUI.UI.Controls.DataGrid` 7.1.2: the package's
   last stable update was in 2021. The port-table POC uses WinUI's virtualizing
   `ListView` and a deterministic grid row. Re-evaluate `WinUI.TableView` only if
   sorting, column resizing, or accessibility requirements outgrow this POC.
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

### Stage 2 — SSH and application lifecycle parity

- Move tunnel presentation state out of the service and expose asynchronous host
  key confirmation instead of a synchronous UI callback.
- Implement add/edit, trust reset, connect/stop/start-all, reconnect status,
  logs, validation, and accessible ContentDialogs in WinUI.
- Add single-instance activation, close/minimize policy, tray icon, notifications,
  unhandled-error reporting, and configuration migration tests.

### Stage 3 — inspection and settings parity

- Complete port filtering, refresh policy, process details/termination, and
  accessible keyboard navigation; measure virtualization with large snapshots.
- Add file/folder pickers, drag/drop, cancellation, open-location and terminate
  workflows to the path-lock page.
- Finish all settings, system theme change observation, and persisted navigation.

### Stage 4 — release cutover

- Add WinUI ZIP and Inno Setup outputs beside WPF, then test clean install,
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
