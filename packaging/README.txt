JSON Lens
=========

A fast JSON explorer: tree view that stays quick on big files, search, JSONPath queries,
copy path, and pretty or minified output.

Run
---
Double-click JsonLens.exe. Nothing to install. It needs Windows 10 or 11 (64-bit); the .NET
runtime is built into the exe. You can also drag a .json file onto JsonLens.exe or onto the window.

Windows may show "Windows protected your PC" the first time, because the exe is not code-signed.
Click "More info", then "Run anyway".

Controls
--------
Ctrl+O                   Open a file (or drop one on the window)
Click the arrow, Enter, Space, double-click   Expand or collapse a row
Right / Left             Expand, or go to the first child / collapse, or go to the parent
Ctrl+F                   Search keys and values
Enter, F3 / Shift+F3     Next / previous match
Ctrl+L                   JSONPath box; Enter runs the query, click a result to jump to it
Ctrl+C (in the tree)     Copy the selected value
Ctrl+Shift+C             Copy the selected node's JSONPath
Pretty / Minify          Switch how the selected value is shown and copied
Ctrl+S                   Save the whole document, pretty or minified
"Open demo"              Generates (once) and opens a 37 MB sample file

Command line
------------
JsonLens.exe file.json      Open a file
JsonLens.exe --check        Self-test without opening a window (exit code 0 = all passed)
JsonLens.exe --frames       Render a ~40 s demo to PNG frames in Videos\JsonLens-frames
JsonLens.exe --help         All options

https://github.com/glenwrhodes/JsonLens

(c) 2026 Tiny Moosh Games Inc.
