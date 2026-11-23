using System.IO;
using JsonLens.Core;

namespace JsonLens.App;

public static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\JsonLens: generated demo data lives here, never in the repo.</summary>
    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JsonLens");

    public static string DefaultDemoFile => Path.Combine(DataDir, DemoData.DefaultFileName);

    /// <summary>Default --frames output folder: Videos\JsonLens-frames (outside the repo).</summary>
    public static string DefaultFramesDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "JsonLens-frames");

    public static string FormatBytes(long bytes)
    {
        string[] units = { "bytes", "KB", "MB", "GB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1)
        {
            v /= 1024;
            u++;
        }
        return u == 0 ? $"{bytes:N0} bytes" : $"{v:0.0} {units[u]}";
    }
}
