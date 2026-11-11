# BetterQuickLook

BetterQuickLook opens a preview when you select one item on the Desktop or in File Explorer and press Space. Press Space or Escape to close it. The app stays in the notification area; right-click its icon and choose **Exit** to quit.

While the preview is open, click another file or use the arrow keys in Explorer or on the Desktop. The same preview window updates automatically without moving or taking keyboard focus. It follows one selected file or folder; empty and multiple selections clear the content. Click inside the preview to interact with its controls. Ctrl+Enter also works while the Explorer file list retains focus.

Selection tracking listens to Windows events only while the preview is visible. Events are coalesced over 75 ms, one background Shell query runs at a time, and obsolete results cannot replace the latest selection or reopen a closed window. There is no selection polling while the app sits in the tray.

## Windows installer

Exit any running copy of BetterQuickLook from the notification area, then double-click `dist/BetterQuickLook-Setup.msi` and choose **Install**. Setup installs for your Windows account and registers BetterQuickLook to start quietly in the notification area whenever you sign in. Open BetterQuickLook from the Start menu to use it immediately after setup. You can disable automatic startup in **Settings → Apps → Startup**. To remove it, use **Settings → Apps → Installed apps**; uninstall also removes its startup registration. Portable copies do not register themselves for startup.

To build the installer, run `./installer/Build.ps1` with the .NET 9 SDK and .NET 6 Runtime installed. The build restores the WiX SDK through NuGet and packages a fresh copy of the app.

## Single-file build

The published `BetterQuickLook.exe` is portable. Copy that one file to a Windows 10 or 11 PC and run it. No .NET installation is needed. The first launch of a build may take several seconds while .NET extracts its bundled runtime and assemblies into a per-user cache. Later launches reuse that cache. Loading assemblies from the cache reduces tray memory usage; it uses more cache space on disk. .NET recreates the cache if temporary files are cleaned up.

Build the distributable file with the .NET 9 SDK:

From the repository root:

```powershell
dotnet publish .\src\BetterQuickLook.csproj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o .\dist
```

The x86 build runs natively on x86 Windows and through Windows compatibility support on x64 and ARM64 Windows. It was tested on x64 Windows 11; other configurations still need hands-on testing.

## Preview coverage

- Images use the image decoders installed in Windows. Common JPEG, PNG, GIF, BMP, TIFF, ICO, and other formats with installed codecs can be displayed. A codec is required for formats Windows cannot decode.
- **Open in [app]** launches your preferred program. Press **Ctrl+Enter** from the preview to do the same. If you have no saved program, **Choose app** and **Ctrl+Enter** open the app picker. The sliders button opens the picker at any time. Choose a program, then select **Open once** or **Open and remember**. You can also browse for an `.exe`. BetterQuickLook remembers separate choices for images, JSON, Word documents, Excel workbooks, and other file extensions. These choices are local to BetterQuickLook and your Windows account.
- `.docx` and `.xlsx` use the installed Windows preview handler when available. A bundled reader shows document text or the first worksheet's first 100 rows and 16 columns when no handler is available.
- Older `.doc`, `.xls`, and some macro-enabled Office formats require a Windows preview handler, usually supplied by Microsoft Office.
- Folders show their location, total file size, and counts of files and subfolders. Size is calculated in the background. The scan stops after 100,000 items; linked and inaccessible items are excluded, and partial results are labeled.
- `.json` and `.jsonc` files show formatted, syntax-colored text and a lazily expandable tree. Invalid JSON shows the line and byte position of the error. Files over 2 MB show a bounded text sample; formatted text is limited to 100,000 characters and tree nodes show up to 500 children at a time.
- Other files show their name, type, size, modified time, and path.

The bundled Word and Excel reader is a content preview, not a full layout renderer. Exact page formatting, charts, formulas, and every image format would require additional rendering engines and a much larger executable.

## Tests

See the [performance review](benchmarks/README.md) and [app picker measurements](benchmarks/app-picker.md) for reproducible before/after results.

The tests cover Desktop and Explorer shortcut eligibility, active Explorer tabs, PNG and JPEG decoding, corrupt images, preferred apps by file type, Windows Open With listings and menus, Word and Excel content, malformed documents, JSON formatting and parse errors, folder sizes and scan limits, Desktop Shell selection, focus changes, and reopening after the title bar close button is used. Run the visible-window tests with `--open-with-smoke` to also launch and close Paint with a temporary image.

```powershell
dotnet build .\tests\BetterQuickLook.Tests.csproj -c Release -r win-x86
.\tests\bin\Release\net9.0-windows\win-x86\BetterQuickLook.Tests.exe
```

Use `--no-ui` when running the tests without an interactive Windows desktop. That skips the Desktop Shell and visible-window checks.

The selection-following regression tests cover repeated events, duplicate paths, rapid changes, concurrent-query limits, empty selections, close/reopen during a query, Explorer failures, other foreground apps, and the program picker. Visible-window tests also verify focus/position preservation and files deleted between selection and loading.

For live Shell integration checks, run each command separately on an interactive desktop. The first opens and closes its own temporary Explorer window, exercises selection events, Down-arrow navigation, and Ctrl+Enter. The second briefly shows the Desktop, creates and removes two temporary fixtures, tests single/multiple selection, and restores the previous Desktop selection.

```powershell
.\tests\bin\Release\net9.0-windows\win-x86\BetterQuickLook.Tests.exe --selection-smoke
.\tests\bin\Release\net9.0-windows\win-x86\BetterQuickLook.Tests.exe --desktop-selection-smoke
```
