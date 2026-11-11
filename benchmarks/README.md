# Performance review, 10 November 2026

Compared the feature-complete baseline `2c91663` with the optimized folder scanner and JSON reader. The later Desktop commit `cfe58fa` reverted preview features; restoring those features is a separate integration change, not a measured optimization.

## Results

Medians of seven warm operations, after two warmups, Release x86, .NET 9.0.22, Windows 11 build 26200, on this computer. Fixture creation and explicit garbage collections are outside the timer. Final measurements run outside the tool sandbox. The last pair ran the optimized version first, then the baseline, to check order effects. Raw results are in `before.json` and `after.json`.

| Operation | Before | After | Managed allocation before / after |
| --- | ---: | ---: | ---: |
| Scan a flat folder with 2,000 files | 119.85 ms | 0.63 ms | 596,192 / 196 bytes |
| Read and format 1,053,891-byte JSON, 15,000 objects | 39.10 ms | 19.16 ms | 8,573,388 / 6,792,888 bytes |
| Construct JSON preview controls | 244.14 ms | 306.35 ms | 19,928,452 / 19,928,440 bytes |
| Decode a 4000 × 3000 JPEG | 19.60 ms | 22.71 ms | 4,188 / 4,188 bytes |

Folder scanning took 99.5% less time on this fixture and nearly eliminated per-file managed allocations. JSON reading/formatting took 51% less time and allocated 20.8% less memory. These are local warm-cache results, not guarantees for other machines, disks or network folders.

JSON control construction and JPEG decoding are unchanged controls. Their elapsed times varied, with overlapping before/after ranges; no speed improvement is claimed. The rendering experiment was removed after inconsistent results. Native image allocations are not included in the managed allocation counter.

Process CPU medians were 125 ms before for the folder scan and below the timer's resolution afterward; JSON was 46.875 / 15.625 ms. Windows process CPU accounting is coarse, so zero does not mean zero work. Allocation figures measure bytes allocated on the test thread per operation, not retained memory or application working set. This suite does not measure cold startup, first Shell enumeration, end-to-end visible preview latency, or long-running idle CPU.

The initial sandbox measurements are retained separately in `before-sandbox.json` and `after-sandbox.json`. They were diagnostic runs during development and are not the final comparison.

## Changes and verification

- Folder scanning consumes the metadata returned by Windows enumeration, without opening each file again or creating a path string and FileInfo object for each file. Directory paths are allocated only when needed for traversal. Hidden files, cancellation, partial-result reporting, reparse-point exclusion and the entry cap retain their existing behavior.
- JSON formatting streams a bounded prefix and stops serialization after enough UTF-8 text is available. The parsed document remains available for the tree. The entire source still must be parsed for validation, and a single large JSON string can still require a large temporary writer buffer.
- Large-file text sampling no longer splits a UTF-16 surrogate pair at the display boundary. A regression test failed on the baseline and passes with the fix.
- Added tests for empty and nested folders, zero and exact entry limits, hidden and Unicode filenames, cancellation, reference-equivalent JSON formatting, very long strings, excessive nesting, portrait image sizing, frozen bitmap handoff and released file handles. The complete suite passes 18 groups, including Desktop selection, preview lifecycle and opening Paint.

## Repeat the measurements

```powershell
dotnet build .\tests\BetterQuickLook.Tests.csproj -c Release -r win-x86
.\tests\bin\Release\net9.0-windows\win-x86\BetterQuickLook.Tests.exe --benchmark .\benchmarks\run.json
.\tests\bin\Release\net9.0-windows\win-x86\BetterQuickLook.Tests.exe --open-with-smoke
```

For a new baseline, check out the desired revision in a separate folder, copy `tests/Performance.cs` there and add the same `--benchmark` dispatch from `tests/Program.cs`. Use the same fixture code, build target, permissions and machine for both versions. Run one executable at a time without a debugger. Timings are recorded rather than asserted as brittle test thresholds.

## Next areas to measure

1. JSON control construction remains expensive at roughly 20 MB allocated per capped preview. Profile WPF layout and token runs before changing highlighting or introducing virtualization.
2. Native Office/Shell preview handlers can perform work on the UI thread. Measure slow or unavailable handlers before choosing timeouts or process isolation.
3. Rapid file switching can leave obsolete image, JSON and document reads running. Measure a sustained switching workload before adding cancellation or a concurrency limit.
4. Test cold startup, idle working set, large real photos, network directories, inaccessible folders and reparse-point loops on representative PCs. The deterministic tests here do not establish performance across those environments.

## Follow-up review, 11 November 2026

Compared the source in the Desktop checkout at `61b984c` with the local, uncommitted changes. Both executables used the same expanded fixtures in `tests/Performance.cs` and `tests/ExcelFixtures.cs`. Release x86, .NET 9.0.22, Windows 11 build 26200; two warmups and seven timed samples per operation, outside the sandbox, with no debugger. Raw final measurements are in `speed-before.json` and `speed-after.json`. Fixture generation and forced GC are excluded.

| Operation | Before median | After median | Managed allocation before / after |
| --- | ---: | ---: | ---: |
| Word fallback, one 10-million-character text run | 75.33 ms | 0.52 ms | 100,078,180 / 180,164 bytes |
| Extract the same program icon 100 times | 284.02 ms | 3.20 ms | 58,828 / 51,628 bytes |
| Excel fallback, 100,000 shared strings, numeric-only preview | 65.21 ms | 0.40 ms | 18,603,444 / 20,736 bytes |
| Excel fallback, shared-string reference 15 | 43.46 ms | 0.42 ms | 18,603,872 / 35,868 bytes |
| Excel fallback, shared-string reference 99,999 | 56.88 ms | 33.11 ms | 18,603,892 / 35,900 bytes |
| Read/format JSON, 15,000 objects | 31.13 ms | 16.06 ms | 6,792,888 / 3,613,764 bytes |
| Construct JSON preview controls | 278.33 ms | 135.41 ms | 19,928,464 / 17,842,792 bytes |

The Word improvement fixes a limit bypass: the old reader returned the entire long text run despite a nominal 40,000-character preview cap. The new reader stops collecting at that cap, without splitting surrogate pairs. Long Excel cells obey the same output limit. Word and Excel figures measure the bundled text fallback, not installed Office preview handlers.

Excel now finds the displayed cells first, skips unused shared text, and retains only referenced values. A late reference still requires scanning earlier XML entries; its speedup will depend on the workbook. The early-reference and numeric-only fixtures show the biggest gain. Icon extraction reuses frozen images across file types, checks file modification time/size, and bounds its cache to 128 entries. The warm icon benchmark excludes the first extraction and does not measure opening an external application.

JSON parses ordinary UTF-8 files directly from bytes, preserving BOM-based UTF-16/UTF-32 support. Highlighting inserts runs directly into a single text container within a [WPF change block](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.primitives.textboxbase.beginchange), shares frozen brushes, and disables undo storage for the read-only view. Text, colors, the 5,000-token highlighting cap, selection, tree view, and scrolling remain available. JSON parsing allocations fell 46.8%; display construction allocations fell 10.5%.

Timings vary with background activity and filesystem caches. JSON display ranges overlap (183–514 ms before, 114–305 ms after), so the median is a local measurement, not a guaranteed speedup. The unchanged folder and JPEG controls also varied; no improvement is claimed for their individual operations. Allocation measurements exclude native memory and measure bytes allocated per operation, not idle RAM. CPU medians are in the raw reports; zero values are below Windows' accounting resolution, not zero CPU use.

Rapid switching now permits one expensive image/JSON/document fallback read at a time. A deterministic test holds the first read open, cancels 32 queued requests, then submits the final request: only the first and final readers execute, and the discarded disposable result is released. UI tests verify that canceled reads and deferred Office callbacks cannot replace a newer preview or reopen a closed window. An in-flight native decode cannot be interrupted; the next read waits for it to finish. Folder scans retain their existing independent cancellation and entry limit.

Explorer COM selections, native preview hosts, failed native-handler initialization, and external-process handles are released explicitly rather than waiting for garbage collection. No polling loop or persistent worker thread was added.

Validation: 24 test groups pass with `--open-with-smoke`, covering Desktop/Explorer rules, images, corrupt files, repeated opening/closing, JSON encodings and exact displayed text, cancellation, icons, Word/Excel edge cases, themed controls, Ctrl+Enter, Opening feedback, and Open once/remember using Paint. The installer separately passed install, repair, uninstall, and reinstall checks for the quoted current-user startup registration. `tests/Test-Installer.ps1` repeats those checks in a clean test account; it refuses to replace an existing installation. Startup is registered through WiX's [RegistryValue element](https://docs.firegiant.com/wix/schema/wxs/registryvalue/).

Remaining platform-dependent costs include native Office preview handlers, third-party app launch times, cold runtime extraction, and slow/network storage. These need representative-machine measurements or a larger isolation/rendering redesign; this pass does not claim to eliminate them.

## Tray memory and CPU, 11 November 2026

This baseline is the installed build from the preceding optimization pass, including its uncommitted performance fixes. Executable hashes are recorded in the sample files. Tests used the same Windows 11 machine, x86 self-contained compressed single-file builds, and 16 logical processors. No forced garbage collections or working-set trimming were used.

The main change is `IncludeAllContentForSelfExtract=true` for single-file publication. The runtime caches managed assemblies on disk before loading them, avoiding decompressed assembly copies held privately in memory. Microsoft documents [single-file extraction and compression behavior](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview). The installer remains a single MSI with everything needed; no separately installed .NET runtime is required. Preview-only scrollbar resources now load with the first preview instead of during tray startup.

| State | Original resident / private committed | Updated resident / private committed |
| --- | ---: | ---: |
| Fresh launch, settled in tray | about 178 / 85 MiB | about 51 / 14 MiB |
| After showing and closing synthetic image and JSON previews | 270.6 / 174.2 MiB | 148.7 / 99.7 MiB |

Fresh-tray resident memory fell about 71%, and private commit about 84%. The post-preview lifecycle measurement fell about 45% resident and 43% private commit. Resident memory includes shared code pages and is not identical to Task Manager's default private working-set column. Private commit measures committed private address space, not necessarily physical RAM.

Fresh-process measurements are made externally by `tests/Measure-Tray.ps1`. The matched samples `tray-before-3.json`, `tray-after-1.json`, and `tray-after-2.json` settle for 20 seconds and then collect counters for 20 seconds. Earlier repeat samples and the theme-only experiment are retained separately. `tray-extracted-1.json` includes initial cache extraction and is not used to describe steady idle CPU.

Idle CPU was already near zero. In the first matched settled comparison, the original used 31.25 ms CPU over 20.37 seconds and the candidate used 62.5 ms over 20.32 seconds, approximately 0.01% and 0.02% of this machine's total CPU capacity. These short, quantized samples do not establish a CPU improvement. The final installed-build verification in `tray-after-2.json` recorded no measurable CPU increase over 20.32 seconds, 51.04 MiB resident memory, and 13.84 MiB private commit. No polling timer, periodic GC, or memory-trimming loop was added to the application.

The lifecycle harness runs the real `App` startup, tray icon and keyboard hook, shows an 800×600 PNG and a 300-object JSON file, hides both, and samples idle counters. It then reopens the image to verify the preview still works. Reports are `tray-lifecycle-before.json` and `tray-lifecycle-after.json`. This harness uses a timer to collect counters, so its CPU values include measurement overhead. It did not force a GC; managed live estimates were about 5.7–5.9 MiB after the previews in both builds. Native graphics resources and loaded UI libraries explain why post-preview process memory exceeds fresh-start memory.

Tradeoffs: the first launch of each new build extracts more files and uses additional per-user disk cache space. Later launches reuse the cache. The first-image lifecycle samples were 284 ms before and 340 ms after; these are single observations, not a latency benchmark. The memory change shifts preview resource loading to first use and does not claim faster first-preview startup.

Validation: all 24 regression groups passed in the new self-contained packaging, including scrollbar styling without global application resources, image/JSON previews, cancellation, window reuse, Ctrl+Enter, and Open With. The actual installer upgraded the existing installation; its executable hash and sign-in startup registration were verified. The updated app is left installed and running.

To repeat in a test session, exit BetterQuickLook first:

```powershell
.\tests\Measure-Tray.ps1 -Executable .\dist\BetterQuickLook.exe -OutputPath .\benchmarks\tray-run.json -SettleSeconds 20 -Samples 20
dotnet publish .\tests\BetterQuickLook.Tests.csproj -c Release -r win-x86 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o .\dist\tray-tests
.\dist\tray-tests\BetterQuickLook.Tests.exe --tray-lifecycle .\benchmarks\tray-lifecycle-run.json
```

The external sampler stops only the process it launched. The lifecycle test uses synthetic temporary files. Neither changes startup preferences or commits files.

## Opening BetterScreenShot — 2026-11-11

BetterQuickLook 1.0.1 forwards image paths directly to the existing BetterScreenShot image pipe when the selected executable or Shell handler is an absolute path ending in BetterScreenShot.exe. Other apps retain their normal launch behavior. A missing resident instance goes straight to the normal launch path; an unavailable pipe gets a bounded 150 ms connection attempt. Once a request may have been sent, errors are reported rather than launching a duplicate. This adds no background polling or preloading.

BetterScreenShot 1.2.11 retains decoded imported pixels in memory and no longer encodes an unused temporary PNG before creating the editor. Saving still encodes the full-resolution image; disposal never targets the original.

| Measurement | Before | After |
| --- | ---: | ---: |
| Resident editor: launch request to visible window, median of 5 | 2,058 ms | 316 ms |
| Resident editor: launch request handoff, median of 5 | 425 ms | 17 ms |
| Fresh editor process: launch request to visible window, one sample | 2,623 ms | 2,110 ms |
| 4K PNG import only, median of 5 | 745 ms | 144 ms |
| 4K JPEG import only, median of 5 | 597 ms | 88 ms |

The full launch test uses a synthetic 3840×2160 PNG with deterministic opaque random pixels and a unique Unicode filename for each open. The baseline uses the previous Shell handler invocation with installed BetterScreenShot 1.2.10; the final run uses direct delivery with installed 1.2.11. Window visibility is polled every 10 ms: this is time until a visible editor window exists, not proof that every pixel has reached the display. Handoff means acceptance, not rendering completion. Only benchmark windows are closed. The cold test requests a graceful editor exit before launching once; it does not clear Windows caches, reboot, or measure first-ever package extraction. Its single sample is indicative, not a statistically reliable improvement estimate.

Raw launch results: `launch-before.json`, `launch-after.json`, `launch-cold-before.json`, and `launch-cold-after.json`. `launch-direct-old-editor.json` isolates direct delivery with the old editor: handoff improved but visible-window times varied substantially, so the transport-only experiment is not used to claim a rendering improvement. Import results live in BetterScreenShot's `benchmarks/import-before.json` and `import-after.json`.

Validation: 25 BetterQuickLook regression groups pass, including normal Open With, Ctrl+Enter, Unicode pipe paths, disconnected or unresponsive peers, bad acknowledgements, and duplicate-open protection. BetterScreenShot's final full run passed 194 checks. An initial run had an obsolete temporary-file assertion (updated for in-memory imports) and a video-duration timing failure; the unchanged original's recording tests and the final full suite passed. Existing ImageSharp dependency audit warnings remain in the editor build.

Both installed executable hashes match the published builds. Sign-in startup remains enabled. Source changes are uncommitted.

To repeat the launch measurement with BetterScreenShot installed:

```powershell
dotnet build .\tests\BetterQuickLook.Tests.csproj -c Release
.\tests\bin\Release\net9.0-windows\BetterQuickLook.Tests.exe --launch-benchmark "$env:LOCALAPPDATA\Programs\BetterScreenShot\BetterScreenShot.exe" direct .\benchmarks\launch-repeat.json
```
