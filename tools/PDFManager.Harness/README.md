# PDFManager harness

Drives the real `PDFManager.exe` through its own buttons and real Windows dialogs, then closes it the way the title-bar X does and records how the process ended. It covers what the unit tests can't: the actual Open, Save As and message-box dialogs, App's crash handler, and crashes while the process exits.

```powershell
.\tools\PDFManager.Harness\run.ps1                       # the smoke preset, once
.\tools\PDFManager.Harness\run.ps1 -Steps exit -Times 3  # look for a crash at exit
.\tools\PDFManager.Harness\run.ps1 -Steps split_open,gc  # any steps, in order
```

The app window is on screen for the length of a run. Don't use the mouse or keyboard meanwhile.

## How it works

`run.ps1` builds the app and the harness, then starts `bin\Debug\net9.0-windows10.0.19041.0\PDFManager.exe` with `PDFManager.Harness.dll` as a .NET startup hook (`DOTNET_STARTUP_HOOKS`). The exe itself is unchanged.

- **Before `Main`:** the hook points `AppSettings.Folder` and `ErrorLog.Folder` at a fresh folder under `%TEMP%\PDFManager.Harness`, so your own settings and logs are never touched. `run.ps1` warns if your `settings.json` changes anyway.
- **When the window is up:** each step runs on the UI thread. Buttons are pressed through UI Automation's Invoke, which goes through the real command binding. No mouse or keyboard input is ever sent.
- **Dialogs:** a background thread answers them. It types file names into Open and Save As, and posts the button a step expects. A dialog no step expected is closed with No/Cancel and fails the run.
- **At the end:** the window gets `WM_CLOSE` and the app exits on its own.

The hook does nothing unless `run.ps1` set `PDFMANAGER_HARNESS_REPORT`.

## Reading the results

Each run prints one line:

```
Run 1: exit 0, checks passed, crash none
```

A run fails when any check fails, the exit code isn't 0, or Windows logged a crash for that process (Application log, event 1000). A failed run also prints its failing lines and the path of its report. The report is in `%TEMP%\PDFManager.Harness\reports` and has one PASS or FAIL line per check, every dialog with its text, and "ProcessExit raised" once the app's own shutdown has run. A crash after that line happened during native teardown. The script's exit code is the number of failed runs.

## Presets and steps

| Preset | Steps |
|---|---|
| `smoke` (default) | `split_open,split_run,merge_open,merge_saveas,merge_run,rotate_flow,batch_flow,reorder_flow,sidebar_fits,theme_light` |
| `exit` | `split_load,gc`. Before the fix in `WindowsPageRenderer`, this crashed in the Qualcomm graphics driver at exit. |
| `crash` | `crash_ui,rotate_flow` |

| Step | What it does |
|---|---|
| `split_open` | Split: pick a.pdf (4 pages) in the Open dialog and wait for its page previews |
| `split_load` | Split: load a.pdf without a dialog and wait for its page previews |
| `split_run` | Split: overwrite No, then Yes, then the success prompt |
| `merge_open` | Merge: pick a.pdf and b.pdf in the Open dialog |
| `merge_saveas` | Merge: pick the output in the Save As dialog |
| `merge_run` | Merge: the same-path warning, overwrite No, then Yes |
| `rotate_flow` | Rotate: Open, Save As, overwrite No, then Yes, then the success prompt |
| `batch_flow` | Batch: pick both PDFs in the Open dialog, run, answer the summary |
| `reorder_flow` | Reorder: Open, press page 4's thumbnail and Move to start, overwrite No, then Yes, then the success prompt; checks the saved page order |
| `sidebar_fits` | Every sidebar entry fits without scrolling at the default 1100x720, all sections expanded. Logs the room left: a new tool needs about 32 px |
| `crash_ui` | Throw on the UI thread: App logs it, shows the error and keeps running |
| `theme_light` | Switch to the light theme with `ThemeManager.Apply` |
| `theme_ui` | Switch to the light theme through Home's appearance choice |
| `nav_home`, `nav_split`, `nav_reorder` | Open that page |
| `screenshot` | Save what the window shows (its WPF content, without the title bar) as `<report>-shotN.png` next to the report |
| `gc` | Force a full garbage collection |
| `idle` | Wait three seconds |

A misspelled step fails the run and lists the real ones.

## What it can't do

- **Folder pickers:** typing a path and pressing OK in the folder dialog navigates into the folder instead of choosing it. Check Batch's and PDF to image's Browse by hand.
- **Drag and drop:** it needs real mouse input. That includes dragging pages in Reorder; the unit tests cover where dropped pages land (`ReorderViewModel.MovePages`), but not the dragging itself.
- **Timing:** a property change re-checks the commands' CanExecute at Background priority, so press buttons through `Ui.ClickAsync`, which waits for the dispatcher to go idle first. A button pressed before that may still be disabled, and Invoke then throws `ElementNotEnabledException`. This is not an app bug.

## Adding a step

Add a method to `Session` and register it in the `_steps` table with a one-line description, then add it to the table above. A step should open its own page first (`Open("navX")`), so it works in any order. Register every dialog it causes with `_dialogs.Expect(name, (title, text) => ..., answer)` *before* the click that opens it, and wait with `await _dialogs.AllHandled()`. Match a dialog by its title and text, never by order alone. An OK-only message box needs `Answer.IDCANCEL`.
