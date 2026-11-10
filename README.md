# BetterQuickLook

BetterQuickLook opens a preview when you select one item on the Desktop or in File Explorer and press Space. Press Space or Escape to close it. The app stays in the notification area; right-click its icon and choose **Exit** to quit.

## Single-file build

The published `BetterQuickLook.exe` is portable. Copy that one file to a Windows 10 or 11 PC and run it. No .NET installation is needed. The first launch may take longer while Windows extracts the bundled desktop runtime; later launches reuse that cache.

Build the distributable file with the .NET 9 SDK:

From the repository root:

```powershell
dotnet publish .\src\BetterQuickLook.csproj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o .\dist
```

The x86 build runs natively on x86 Windows and through Windows compatibility support on x64 and ARM64 Windows. It was tested on x64 Windows 11; other configurations still need hands-on testing.

## Preview coverage

- Images use the image decoders installed in Windows. Common JPEG, PNG, GIF, BMP, TIFF, ICO, and other formats with installed codecs can be displayed. A codec is required for formats Windows cannot decode.
- Image previews have an **Edit image** button. The first click lets you choose Paint or browse to an editor `.exe`. BetterQuickLook saves the choice for the current Windows user, so later clicks open that editor directly. Use **Change editor** to pick a different program. The preview closes when the editor starts.
- `.docx` and `.xlsx` use the installed Windows preview handler when available. A bundled reader shows document text or the first worksheet's first 100 rows and 16 columns when no handler is available.
- Older `.doc`, `.xls`, and some macro-enabled Office formats require a Windows preview handler, usually supplied by Microsoft Office.
- Folders show their location, total file size, and counts of files and subfolders. Size is calculated in the background. The scan stops after 100,000 items; linked and inaccessible items are excluded, and partial results are labeled.
- `.json` and `.jsonc` files show formatted, syntax-colored text and a lazily expandable tree. Invalid JSON shows the line and byte position of the error. Files over 2 MB show a bounded text sample; formatted text is limited to 100,000 characters and tree nodes show up to 500 children at a time.
- Other files show their name, type, size, modified time, and path.

The bundled Word and Excel reader is a content preview, not a full layout renderer. Exact page formatting, charts, formulas, and every image format would require additional rendering engines and a much larger executable.

## Tests

The tests cover Desktop and Explorer shortcut eligibility, active Explorer tabs, PNG and JPEG decoding, corrupt images, editor preferences and launch arguments, Word and Excel content, malformed documents, JSON formatting and parse errors, folder sizes and scan limits, Desktop Shell selection, focus changes, and reopening after the title bar close button is used.

```powershell
dotnet build .\tests\BetterQuickLook.Tests.csproj -c Release -r win-x86
.\tests\bin\Release\net9.0-windows\win-x86\BetterQuickLook.Tests.exe
```

Use `--no-ui` when running the tests without an interactive Windows desktop. That skips the Desktop Shell and visible-window checks.
