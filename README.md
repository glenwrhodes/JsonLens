# JSON Lens

A fast JSON explorer for Windows with a dark IDE look: a tree that stays quick on large files, text search, JSONPath queries, copy path, and pretty or minified output.

Built with C# and WPF on .NET 8.

## Features

- **Fast tree for large files.** Files are parsed with `System.Text.Json.JsonDocument`, and the tree is a flat list of only the visible rows, shown in a virtualized list. Containers with more than 100 children are split into groups (`[0 … 99]`, or `[0 … 9999]` with groups inside), so opening a 40,000-item array is instant. The 37 MB demo file (1.37 million nodes) parses in well under a second.
- **Search** in keys and values, with Match case (`Aa`), Enter / F3 for the next match, Shift+Enter / Shift+F3 for the previous one. The tree opens to show each match. Search runs in the background and stops at 10,000 matches.
- **JSONPath queries** (the core of RFC 9535): `$`, `.name`, `['name']`, `[n]`, `[-n]`, `*`, `..`, slices `[start:end:step]`, lists `[0,2]`, and filters such as `[?@.price < 10 && @.category == 'fiction']` with `== != < <= > >=`, existence tests `[?@.isbn]`, `&&`, `||`, `!` and parentheses. The old `[?(...)]` form works too. Results are listed with their paths; click one to jump to it in the tree. Errors give a message, and a red marker points at the character where the query went wrong. Function extensions such as `length()` are not supported.
- **Selected node:** its JSONPath (for example `$.store.book[0]['odd key']`), Copy path, Copy as JSON Pointer, Copy value, and a Pretty / Minify switch. Numbers are written exactly as they appear in the file. The preview stops at 200 KB, but Copy value always copies everything.
- **Save as** writes the whole document, pretty or minified.
- **Opening files:** Open (Ctrl+O), drag and drop, a file path on the command line, or *Open demo* for the generated large file.

### Keyboard

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
- copy, pretty and minify
- on the large demo file (it is generated if missing): parse, reveal, search, filter and capped expand-all
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

## Demo data

The large demo file is a fictional shop's order export, about 37 MB. It is never committed. `DemoData` generates it from a fixed seed, so it is byte-for-byte the same every time, at `%LOCALAPPDATA%\JsonLens\large-demo-v1.json`. Check mode, frames mode and the *Open demo* button create it when it is missing; use `--demo-file <path>` to put it somewhere else. The only sample in the repo is the small `samples/sample.json`, which is embedded in the app.

## Project layout

```
src/JsonLens.Core/          no WPF: everything that is tested
  JsonDoc.cs                parse files (UTF-8/16, BOM, comments, trailing commas), node count, error line/column
  Tree/FlatTree.cs          visible-row model: expand, collapse, groups, reveal path, capped expand-all
  JsonPath/                 RFC 9535 parser and evaluator
  JsonSearcher.cs           key/value search with limit and cancellation
  JsonFormatter.cs          pretty/minify that keeps numbers exactly, bounded previews
  PathSegment.cs            JSONPath and JSON Pointer text for a node
  DemoData.cs               seeded large-file generator
src/JsonLens.App/           WPF app (assembly JsonLens.exe)
  Themes/Dark.xaml          palette and control styles
  Views/MainView.xaml       the whole UI, title bar included (rendered off-screen in check/frames modes)
  ViewModels/               MainViewModel, RowList (cheap reset-only list for the virtualized tree)
  Modes/                    --check, --frames, the demo script and overlay, console output
tests/JsonLens.Core.Tests/  xUnit tests for Core
samples/sample.json         small sample (embedded in the app)
```

© 2026 Tiny Moosh Games Inc.
