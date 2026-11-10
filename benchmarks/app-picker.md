# App picker performance

The full Windows app list loads on the first picker request for each extension. Later requests reuse the list. At the time of this benchmark, a preview with a saved program looked up its icon separately. The sliders button introduced later uses a fixed vector icon.

## Controlled comparison

Both builds use the new UI. The eager control adds a single `ShellFileHandlers.ListAsync(ext)` call immediately after `_currentFilePath = path` in `ShowFile`. The deferred build omits that call.

| Median | Eager, no saved app | Deferred, no saved app | Eager, saved app | Deferred, saved app |
| --- | ---: | ---: | ---: | ---: |
| Preview image laid out | 47.3 ms | 39.4 ms | 94.8 ms | 83.3 ms |
| Process CPU during first 500 ms | 203.1 ms | 109.4 ms | 250.0 ms | 156.3 ms |
| Managed bytes allocated during first 500 ms | 180,968 | 176,948 | 319,116 | 293,840 |
| First picker populated | 161.1 ms | 260.4 ms | 135.6 ms | 241.1 ms |

Preview latency ranges overlap. The observed median improved by 7.9 ms without a saved app and 11.5 ms with one. CPU fell by about 94 ms in both scenarios. Opening the picker for the first time took about 100 ms longer as the app list and icons loaded on demand.

The original UI and eager startup were also measured before editing the app. Median preview times were 45.0 ms without a saved app and 76.4 ms with one. The final version measured 39.4 ms and 83.3 ms respectively. Those runs include the UI changes, so use the controlled pair to assess the effect of deferring the list.

## Reproduce

```powershell
dotnet build .\tests\BetterQuickLook.Tests.csproj -c Release -r win-x86
.\tests\bin\Release\net9.0-windows\win-x86\BetterQuickLook.Tests.exe --benchmark-app-picker .\benchmarks\picker-run.json
```

Run the benchmark on an interactive Windows desktop with PNG handlers installed. Each of the nine samples per scenario uses a fresh process. The harness warms WPF and the PNG decoder first, then times showing a 640 × 480 image until the preview image has been laid out. It records process CPU and managed allocations for the first 500 ms, then measures how long the picker takes to populate. The parent process queries handlers to choose a real saved app, so the in-process app list starts empty in each child while Windows may have warmed its own caches. CPU time is measured for all process threads. Managed allocations exclude native memory.

This run used Windows 11, Release x86, .NET 9.0.22, 13 PNG handlers and BetterScreenShot as the saved program. Results vary with installed apps and machine load. Raw samples are in [original](picker-before.json), [eager control](picker-eager-control.json), and [deferred](picker-after.json).
