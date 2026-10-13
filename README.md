# CoolMaconWindows

A native Windows dock with a mac-inspired floating shelf. Windows apps keep their own icons and behavior. This first version focuses on the dock.

## Run

Double-click **launch.cmd**, or run **bin/CoolMacDock.exe** after building. The EXE opens a control window showing **Dock is on** or **Dock is off**, with separate **Turn on dock** and **Turn off dock** buttons. Opening the EXE again brings that same control window forward.

**Turn off dock** immediately restores the Windows taskbar and keeps the controls open. The app remembers this choice: reopening it after switching off leaves the dock off until you turn it on. Closing the control window while the dock is on keeps the dock running; closing it while the dock is off exits the app. **Exit app** turns the dock off and closes the app.

The exact EXE location in this checkout is `C:\Users\ferro\Desktop\Project AI\CoolMaconWindows\bin\CoolMacDock.exe`. The control window also has an **Open EXE folder** button.

The dock does not install a service or configure startup. It runs as the current user without administrator privileges. Preferences are stored in `%LOCALAPPDATA%\CoolMaconWindows\settings.json`.

## Folder icons

Click **Folder icons** in the control window. This works whether the dock is on or off.

1. Choose **Classic blue**, **Soft teal**, or **Lavender**.
2. Choose a folder with **Choose folder…**, or drag a folder into the window.
3. Select an embossed symbol or use **Automatic** for familiar folder names.
4. Click **Apply icon**. The icon appears in Windows Explorer and in pinned folder shortcuts on the dock.
5. Select the folder again and click **Restore original** to remove CoolMac's styling.

Folder icons stay applied when the app closes or the dock is switched off. They are independent of the dock's on/off control. **Styled folders** lists folders managed by the app. **Open in Explorer** opens the currently selected folder.

The artwork is original vector art inspired by macOS folder shapes, supplied in ten Windows icon sizes from 16 to 256 pixels. Windows' documented `desktop.ini` folder customization is used; this feature styles selected folders rather than globally replacing the Windows default folder icon.

Each selected folder receives a hidden icon file and a hidden `.coolmac-folder.json` backup. An index is kept in `%LOCALAPPDATA%\CoolMaconWindows\FolderBackups`. The backup preserves original `desktop.ini` bytes and attributes. Restoring after later changes preserves unrelated metadata and icons set by another application. Icon paths are relative, and backups travel with a folder when it is renamed or moved. Do not delete these backup files before restoring.

Folder customization requires a writable local folder. Drive roots and linked folders are excluded. Explorer can take a moment to refresh a changed icon; reopen its parent folder or press F5 if necessary. The app does not restart Explorer or clear the system icon cache.

The `Folder Preview` directory contains styled Projects, Documents, and Downloads examples. The preview folders have no personal files.

## Use

- Click an app to launch it, restore its window, or switch to it. Click an already active, single-window app to minimize it.
- Click an app with several windows repeatedly to cycle between them. Right-click for a list of its windows.
- Shift-click to request a new instance. Some single-instance apps will reuse their existing window.
- Drag an app, shortcut, file, or folder onto the dock to pin it.
- Right-click an icon to remove it, move it left/right, or keep a running app in the dock.
- Click the **…** button for settings. You can also press **Ctrl+Alt+D**, unless another program already owns that shortcut.
- Right-click the dock and choose **Dock controls…** to reach the on/off buttons, or choose **Turn off dock** directly. The tray icon opens the controls on double-click.
- Use **Add apps** to search Start menu shortcuts, or browse for an executable or shortcut.
- Adjust icon size, magnification, transparency, auto-hide, reduced motion, and the dock's display in settings.
- When auto-hide is enabled, move the pointer to the bottom edge to reveal the dock. Fullscreen applications hide it automatically.
- The dock follows the Windows reduced-animation preference as well as its own Reduce motion option.

To use the original Start menu, press the Windows key or choose **Windows Start menu** from the dock's background menu. To access Windows tray icons, the clock, and taskbar controls, right-click the dock and select **Show Windows taskbar**. Turn **Replace Windows taskbar** back on in settings to return to the dock.

## Restoration

Taskbar replacement only begins after a separate recovery process has acknowledged startup. On normal exit, the dock removes its AppBar reservation, restores the previous auto-hide setting using the actual Windows taskbar window handle, and shows every taskbar window. Stale hidden-window snapshots are not reapplied. If the dock process exits unexpectedly, the recovery process removes the reservation and restores the taskbar. It exits when its work is complete.

If an older build left the taskbar in auto-hide mode, run `tools/restore-taskbar.ps1` in your Windows session. This shows the taskbar and disables auto-hide without restarting Explorer.

There are no Explorer DLL patches, icon replacements in Windows system files, or process injections. The taskbar's auto-hide state is changed temporarily while replacement is enabled and restored on exit. Icon artwork comes from the installed apps; Settings and utility buttons use Windows' Segoe MDL2 Assets glyphs.

## Build

Requires 64-bit Windows with .NET Framework 4.8. Windows 11 includes it. No SDK download or NuGet packages are required for this build path.

```powershell
.\build.ps1
.\build.ps1 -Verify
.\build.ps1 -Run
```

The script uses the framework compiler in `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319`. `bin/CoolMacDock.exe` is the portable output. The original app icon source is `assets/icon.svg`; `tools/make-icon.ps1` generates the matching Windows icon.

## Verification

`build.ps1 -Verify` checks shell icon extraction, pin handling, persisted preferences, corrupt-settings fallback, and animation calculations. It also applies/restores folder styles inside unique test fixtures under `artifacts/`, checks original metadata and attributes, preserves later edits, tests moved-folder restoration and filename collisions, and confirms Windows resolves the new folder icon. Actual WPF controls and all icon variants are rendered into `artifacts/` for visual inspection.

To additionally check the real taskbar in an interactive Windows session:

```powershell
bin\CoolMacDock.exe --verify "C:\absolute\path\to\verification-output" --live
```

This briefly hides/restores the taskbar, checks the work area reserved for maximized windows, and verifies recovery after an abrupt child-process exit. It also verifies the taskbar's physical screen position, waits for delayed Explorer updates, and tests recovery from a stale hidden-window snapshot. It restores the original taskbar state after checking. Do not run integration verification alongside a running copy of the dock.

## Current scope

- One dock on the selected display. Taskbar replacement applies to all Windows taskbars; per-display independent docks are future work.
- Running apps are grouped by executable path. Some Store apps and application hosts may need dedicated identity handling, and elevated apps can restrict window switching.
- The shelf uses layered transparency and a gradient; it does not apply desktop blur.
- Window thumbnails, taskbar progress badges, Jump Lists, a tray/clock replacement, and startup installation are future work.
- Folder icon customization applies to selected filesystem folders. Explorer's overall layout, app title bars, and system window animations are outside this version's scope.

## Implementation

- `src/DockWindow.cs`: dock layout, hover animation, app launching/window switching, menus, drag-and-drop, auto-hide.
- `src/Native.cs`: Win32 window discovery, high-resolution Windows shell icons, monitor positioning, AppBar reservation, taskbar recovery.
- `src/SettingsWindow.cs`: preferences and installed-app picker.
- `src/Models.cs`: pin data, normalization, atomic settings persistence, and animation math.
- `src/Program.cs`: application lifecycle, single-instance guard, recovery helper, and verification entry points.
- `src/ControlWindow.cs`: running status, on/off controls, remembered enabled state, and reopening the running app.
- `src/FolderArt.cs`: original folder artwork, color variants, embossed symbols, and multi-size ICO generation.
- `src/FolderIcons.cs`: per-folder customization, durable backups, restoration, and shell refresh notifications.
- `src/FolderWindow.cs`: previews, folder picker, and managed-folder controls.
- `src/FolderVerification.cs`: folder styling and restoration verification.

Sources used for native integration: [Microsoft AppBars](https://learn.microsoft.com/en-us/windows/win32/shell/application-desktop-toolbars), [shell icons](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shgetfileinfow), and [window activation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow).

Folder customization follows [Microsoft's desktop.ini documentation](https://learn.microsoft.com/en-us/windows/win32/shell/how-to-customize-folders-with-desktop-ini).
