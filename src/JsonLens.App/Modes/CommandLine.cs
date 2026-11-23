using System.Globalization;

namespace JsonLens.App.Modes;

public enum RunMode { Normal, Check, Frames, Help }

/// <summary>
/// JsonLens.exe [file]
/// JsonLens.exe --check [--demo-file path] [--log path]
/// JsonLens.exe --frames [dir] [--fps 30] [--size 1600x900] [--demo-file path] [--log path]
/// </summary>
public sealed class CommandLine
{
    public RunMode Mode { get; private set; } = RunMode.Normal;
    public string? FilePath { get; private set; }
    public string? FramesDir { get; private set; }
    public int Fps { get; private set; } = 30;
    public int Width { get; private set; } = 1600;
    public int Height { get; private set; } = 900;
    public string? DemoFile { get; private set; }
    public string? LogFile { get; private set; }
    public string? Error { get; private set; }

    public const string Usage = """
        JSON Lens © 2026 Tiny Moosh Games Inc.

        Usage:
          JsonLens.exe [file.json]
          JsonLens.exe --check  [--demo-file path] [--log path]
              Starts up without opening a window, exercises the UI and core logic, prints a report,
              exits with 0 (all passed) or 1.
          JsonLens.exe --frames [output-dir] [--fps 30] [--size 1600x900] [--demo-file path]
              Renders a scripted ~40 second demo of the real UI off-screen to PNG frames
              (frame_00001.png, ...). Default output: Videos\JsonLens-frames.

        The demo file is generated from a fixed seed if it is missing
        (default: %LOCALAPPDATA%\JsonLens\large-demo-v1.json).
        From PowerShell, pipe to Out-Host to wait and see the output:  .\JsonLens.exe --check | Out-Host
        """;

    public static CommandLine Parse(string[] args)
    {
        var cl = new CommandLine();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string? Next() => i + 1 < args.Length ? args[++i] : null;

            switch (a.ToLowerInvariant())
            {
                case "--check":
                    cl.Mode = RunMode.Check;
                    break;
                case "--frames":
                    cl.Mode = RunMode.Frames;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) cl.FramesDir = args[++i];
                    break;
                case "--fps":
                    if (!int.TryParse(Next(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fps) || fps < 1 || fps > 120)
                        return cl.Fail("--fps needs a number from 1 to 120");
                    cl.Fps = fps;
                    break;
                case "--size":
                    var size = Next()?.Split('x', 'X');
                    if (size is not { Length: 2 } ||
                        !int.TryParse(size[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) ||
                        !int.TryParse(size[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) ||
                        w < 800 || h < 500 || w > 7680 || h > 4320)
                        return cl.Fail("--size needs WIDTHxHEIGHT, at least 800x500, for example 1600x900");
                    cl.Width = w;
                    cl.Height = h;
                    break;
                case "--demo-file":
                    cl.DemoFile = Next() ?? (string?)null;
                    if (cl.DemoFile is null) return cl.Fail("--demo-file needs a path");
                    break;
                case "--log":
                    cl.LogFile = Next();
                    if (cl.LogFile is null) return cl.Fail("--log needs a path");
                    break;
                case "--help":
                case "-h":
                case "/?":
                    cl.Mode = RunMode.Help;
                    break;
                default:
                    if (a.StartsWith("--", StringComparison.Ordinal)) return cl.Fail($"Unknown option {a}");
                    cl.FilePath = a;
                    break;
            }
        }
        return cl;
    }

    private CommandLine Fail(string message)
    {
        Error = message;
        Mode = RunMode.Help;
        return this;
    }
}
