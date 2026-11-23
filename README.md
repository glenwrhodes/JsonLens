# JSON Lens

[![Build and release](https://github.com/glenwrhodes/JsonLens/actions/workflows/release.yml/badge.svg)](https://github.com/glenwrhodes/JsonLens/actions/workflows/release.yml)

A fast JSON explorer for Windows with a dark IDE look: a tree that stays quick on large files, text search, JSONPath queries, copy path, and pretty or minified output.

Built with C# and WPF on .NET 8.

## Download

**[Download JsonLens-win-x64.zip](https://github.com/glenwrhodes/JsonLens/releases/latest/download/JsonLens-win-x64.zip)** (the latest build; no GitHub account needed). Older builds are on the [releases page](https://github.com/glenwrhodes/JsonLens/releases).

Unzip it anywhere and run `JsonLens.exe`. There is nothing to install.

**What it needs**
- Windows 10 or 11, 64-bit. Windows on ARM runs it through x64 emulation.
- No .NET install. The .NET 8 runtime is built into the exe, which is why it's about 70 MB.
- About 40 MB of free space in `%LOCALAPPDATA%\JsonLens`, and only if you use *Open demo*, which generates its sample file once.

The exe isn't code-signed, so the first time you run it Windows SmartScreen may show "Windows protected your PC". Click **More info**, then **Run anyway**.

Every push to `main` builds a new release on a clean Windows runner. The build runs the unit tests, then runs `JsonLens.exe --check` on the exact exe that goes into the zip, and publishes only if both pass. See [the workflow](.github/workflows/release.yml).

## Features

- **Fast tree for large files.** Files are parsed with `System.Text.Json.JsonDocument`, and the tree is a flat list of only the visible rows, shown in a virtualized list. Containers with more than 100 children are split into groups (`[0 … 99]`, or `[0 … 9999]` with groups inside), so opening a 400,000-item array is instant. A 384 MB file with 13.6 million values opens in about 1.4 s, and searching it takes 0.1–0.2 s (see [Large files](#large-files)). Files load in the background with a progress bar and a Cancel button, so the window never freezes.
- **Search** in keys and values, with Match case (`Aa`), Enter / F3 for the next match, Shift+Enter / Shift+F3 for the previous one. The tree opens to show each match. Search runs in the background and stops at 10,000 matches.
- **JSONPath queries** (the core of RFC 9535): `$`, `.name`, `['name']`, `[n]`, `[-n]`, `*`, `..`, slices `[start:end:step]`, lists `[0,2]`, and filters such as `[?@.price < 10 && @.category == 'fiction']` with `== != < <= > >=`, existence tests `[?@.isbn]`, `&&`, `||`, `!` and parentheses. The old `[?(...)]` form works too. Results are listed with their paths; click one to jump to it in the tree. Errors give a message, and a red marker points at the character where the query went wrong. Function extensions such as `length()` are not supported.
- **Selected node:** its JSONPath (for example `$.store.book[0]['odd key']`), Copy path, Copy as JSON Pointer, Copy value, and a Pretty / Minify switch. Numbers are written exactly as they appear in the file. The preview stops at 50 KB (more would make the window stutter on every big selection), but Copy value always copies everything.
- **Save as** writes the whole document, pretty or minified.
- **Opening files:** Open (Ctrl+O), drag and drop, a file path on the command line, or *Open demo* for the generated large file.
- **Duplicate keys.** JSON allows the same key twice in one object, as in `{"a": 1, "a": 2}`. JSON Lens shows every copy in file order. Search, query results and the tree always open the exact copy you picked.
  - **Text paths mean the last copy.** A path such as `$.a` has no way to say which copy, so it means the last one. That's the copy JSON.parse, System.Text.Json and Python keep, and the one JSONPath name selectors like `$.a` return here.
  - **Earlier copies are marked as overridden.** They show a strikethrough key and a note in the tree, and the details pane warns that their path and pointer lead to the later value. Copying the path of an overridden copy, or of anything inside one, shows the same warning.

### Controls

| Mouse | Action |
| --- | --- |
| Click a row's arrow, or double-click the row | Expand or collapse |
| Click a row | Select it and show it in the details pane |
| Click a query result | Jump to it in the tree |
| Drag a file onto the window | Open it |
| Drag the splitters | Resize the tree, results and details panes |

| Keys | Action |
| --- | --- |
| Ctrl+O | Open file |
| Ctrl+S | Save as (current pretty or minified format) |
| Ctrl+F / Ctrl+L | Search box / JSONPath box |
| Enter, F3 / Shift+Enter, Shift+F3 | Next / previous match |
| Enter (in the JSONPath box) | Run the query |
| → / ← in the tree | Expand, or go to the first child / collapse, or go to the parent |
| Enter, Space, double-click | Expand or collapse the row |
| Ctrl+C (in the tree) | Copy value (strings as plain text, everything else as JSON) |
| Ctrl+Shift+C | Copy path |

## Build and run

You need the .NET 8 SDK or newer (it builds `net8.0` / `net8.0-windows`) and the .NET 8 Desktop Runtime.

```powershell
dotnet build
dotnet test
dotnet run --project src/JsonLens.App              # opens the window
dotnet run --project src/JsonLens.App -- file.json
```

The app is `src/JsonLens.App/bin/Debug/net8.0-windows/JsonLens.exe`.

## Check mode (no window)

```powershell
.\JsonLens.exe --check | Out-Host
# or
dotnet src/JsonLens.App/bin/Debug/net8.0-windows/JsonLens.dll --check
```

Starts the app without opening a window. It builds the real UI off-screen and runs it through the main features:

- the theme and layout
- the built-in sample, expand and collapse
- search, JSONPath and JSONPath errors
- copy, duplicate keys, pretty and minify
- on the large demo file (it is generated if missing): parse, reveal, search, filter and capped expand-all
- a background load with progress, which fails if the UI thread is ever blocked for 250 ms or more, and Cancel
- one off-screen render

It prints `PASS` or `FAIL` for each step and exits with 0 when every step passes, or 1 when any step fails. Add `--log check.log` to also write the report to a file. JsonLens.exe is a GUI program, so pipe it to `Out-Host` in PowerShell to make the prompt wait for the output.

## Frames mode (for the video)

```powershell
.\JsonLens.exe --frames | Out-Host                        # Videos\JsonLens-frames
.\JsonLens.exe --frames D:\clips\lens --fps 60 --size 1920x1080
```

Plays a scripted demo of about 40 seconds against the real UI, with no window, and saves every frame as `frame_00001.png`, `frame_00002.png` and so on. The frames are drawn by WPF's own renderer (`RenderTargetBitmap`), not captured from the screen. The demo:

1. opens the 37 MB demo file
2. browses into an order
3. searches for "moose is friendly" and steps through the matches
4. runs a JSONPath filter and opens a result
5. copies the path
6. switches between Minify and Pretty
7. runs a recursive-descent query
8. collapses the tree

A mouse pointer, click ripples, a text caret and key hints are drawn over the UI. Timing comes from the frame count, so each run gives the same frames, except for the parse and query times the app measures live. Old `frame_*.png` files in the output folder are deleted first. At the end it prints the ffmpeg command:

```powershell
ffmpeg -framerate 30 -i "%USERPROFILE%\Videos\JsonLens-frames\frame_%05d.png" -c:v libx264 -pix_fmt yuv420p -crf 16 jsonlens-demo.mp4
```

At 1600x900 and 30 fps, rendering takes about 75 seconds.

## Large files

These numbers come from `--bench` on a 384 MB file: 400,000 orders, 13.6 million values, from `DemoData`. The machine has 20 logical CPUs, a Release build and .NET 8. Every result fingerprint was identical before and after.

| Step | Before (1.0.1) | After |
| --- | ---: | ---: |
| Open (read + parse + count) | 5,151 ms | **1,364 ms** |
| … read file | 708 ms | 134 ms |
| … parse (`JsonDocument`) | 3,858 ms | 1,106 ms |
| … count values | 581 ms | 123 ms |
| Search, rare phrase | 4,593 ms | **173 ms** |
| Search, word not in the file (full scan) | 4,006 ms | **95 ms** |
| Search, match case | 4,235 ms | **120 ms** |
| JSONPath `$..customers[?@.tier == 'gold'].name` | 3,380 ms | **375 ms** |
| Working set after opening | 757 MB | 749 MB |

What made the difference, measured:

1. **Windows power throttling.** This was the biggest cost, and it isn't in the code. On hybrid (P-core/E-core) CPUs, Windows 11 moves a process it considers background work to slower cores, and windowless runs such as `--check` and `--bench` count as background. After about two seconds of work, parsing and even a bare token scan ran 3–4 times slower. JSON Lens now opts out of this throttling (`SetProcessInformation` with `ProcessPowerThrottling`), because it only uses CPU while the user is waiting for it. With that alone, the old code opens the file in 1,513 ms and searches it in 0.9–1.2 s. `--eco` keeps the old behaviour for comparisons.
2. **Search runs in parallel, with a subtree pre-check.**
   - The elements of big arrays and objects (512 or more children) are searched on all cores, in ranges whose results are joined back in document order.
   - Before searching an element, its raw JSON is checked for the term. If the raw JSON has no backslash, every key, string, number and `true`/`false`/`null` in it appears there verbatim, so a miss there means a miss inside. Elements with escapes always get the full search.
   - Tests check that the results are identical to the plain one-thread search for escapes, duplicate keys, keys only or values only, the result limit and cancellation.
3. **Counting values runs in parallel**, and the file is read in 8 MB chunks (for progress) into a buffer that isn't zeroed first.
4. **The JSONPath `..` walk skips plain values**, because a selector can only select from objects and arrays. That saves an object and a path link for most values.
5. **The details pane preview is limited to 50 KB.** A WPF text box needed 260 ms on the UI thread to lay out the 190 KB preview of a big node. That was the last thing freezing the window after a load.

Where the time and memory still go:

- **Parsing takes about 1.1 s.** That is `System.Text.Json`'s `JsonDocument`, at about 330–400 MB/s. It can't run in parallel or report progress, so the progress bar is measured while reading and estimated while parsing. To get faster than this, JSON Lens would need its own parser and index.
- **Memory is about twice the file size.** `JsonDocument` keeps the file's bytes, here 367 MiB, because it reads strings from them. It also keeps a 12-byte index entry per token: 28.2 million tokens, 323 MiB. With the .NET runtime, that makes the 749 MB working set. Its index buffer is rounded up to 512 MB, but the untouched part only counts as committed memory, not RAM.
- **Opening a second big file** (64 MB or more) now closes the current one first, so two big documents are never in memory together.

To repeat the measurement:

```powershell
# Generates the file first if it's missing (about 2 s), then times everything.
dotnet src/JsonLens.App/bin/Release/net8.0-windows/JsonLens.dll --bench "$env:LOCALAPPDATA\JsonLens\bench-400k.json" --orders 400000 --runs 3
```

`--bench` prints the time for each open phase, then the tree, search and JSONPath steps, then memory and GC counts. Each search and query also gets a fingerprint, a hash of every result's path and value. Build in Release first (`dotnet build -c Release`); a Debug build says so at the top. `--check --demo-file <path>` runs every check against your own big file, including one that loads it the way the window does and measures the longest UI-thread pause.

## Demo data

The large demo file is a fictional shop's order export, about 37 MB. It is never committed. `DemoData` generates it from a fixed seed, so it is byte-for-byte the same every time, at `%LOCALAPPDATA%\JsonLens\large-demo-v1.json`. Check mode, frames mode and the *Open demo* button create it when it is missing; use `--demo-file <path>` to put it somewhere else. The only sample in the repo is the small `samples/sample.json`, which is embedded in the app.

## Project layout

```
src/JsonLens.Core/          no WPF: everything that is tested
  JsonDoc.cs                parse files (UTF-8/16, BOM, comments, trailing commas), node count, error line/column
  Tree/FlatTree.cs          visible-row model: expand, collapse, groups, reveal path, capped expand-all
  JsonPath/                 RFC 9535 parser and evaluator (no leading zeros in numbers, as the RFC says)
  JsonSearcher.cs           key/value search with limit and cancellation (parallel over big containers)
  ParallelWalk.cs           splitting big arrays/objects into ranges for parallel search and counting
  JsonFormatter.cs          pretty/minify that keeps numbers exactly, bounded previews
  PathSegment.cs            JSONPath and JSON Pointer text for a node
  DemoData.cs               seeded large-file generator
src/JsonLens.App/           WPF app (assembly JsonLens.exe)
  Themes/Dark.xaml          palette and control styles
  Views/MainView.xaml       the whole UI, title bar included (rendered off-screen in check/frames modes)
  ViewModels/               MainViewModel, RowList (cheap reset-only list for the virtualized tree)
  Modes/                    --check, --frames, --bench, the demo script and overlay, console output
  PowerThrottling.cs        opts the process out of Windows EcoQoS throttling (see Large files)
tests/JsonLens.Core.Tests/  xUnit tests for Core
samples/sample.json         small sample (embedded in the app)
packaging/README.txt        the read-me that goes into the release zip
.github/workflows/release.yml   build, test, --check, zip and release on every push to main
```

© 2026 Tiny Moosh Games Inc.
