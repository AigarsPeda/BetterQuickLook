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
