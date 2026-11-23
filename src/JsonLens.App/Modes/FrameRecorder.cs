using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using JsonLens.App.Services;
using JsonLens.App.ViewModels;
using JsonLens.App.Views;
using JsonLens.Core;

namespace JsonLens.App.Modes;

/// <summary>
/// --frames: plays <see cref="DemoScript"/> against the real UI with no window and writes every frame
/// to a PNG with WPF's own renderer (RenderTargetBitmap). Nothing is captured from the screen, and
/// timing comes from the frame count, so the run is the same every time (apart from the measured
/// parse and query times shown in the UI).
/// </summary>
public sealed class FrameRecorder
{
    private readonly ConsoleOutput _out;
    private readonly CommandLine _cl;

    public FrameRecorder(ConsoleOutput output, CommandLine cl)
    {
        _out = output;
        _cl = cl;
    }

    public int Run()
    {
        var total = Stopwatch.StartNew();
        string dir = Path.GetFullPath(_cl.FramesDir ?? AppPaths.DefaultFramesDir);
        string demoPath = _cl.DemoFile ?? AppPaths.DefaultDemoFile;
        int width = _cl.Width, height = _cl.Height, fps = _cl.Fps;
        var size = new Size(width, height);

        _out.Line("JSON Lens frame recorder  ·  © 2026 Tiny Moosh Games Inc.");
        _out.Line($"Output: {dir}  ({width}x{height} at {fps} fps)");

        try
        {
            Directory.CreateDirectory(dir);
            int removed = 0;
            foreach (var old in Directory.EnumerateFiles(dir, "frame_*.png"))
            {
                File.Delete(old);
                removed++;
            }
            if (removed > 0) _out.Line($"Removed {removed} frames from a previous run.");

            if (!File.Exists(demoPath))
            {
                _out.Line($"Generating demo file {demoPath} …");
                DemoData.EnsureFile(demoPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _out.Line("Error: " + ex.Message);
            return 1;
        }

        var services = new RecordingAppServices { OpenFileResult = demoPath };
        using var vm = new MainViewModel(services, synchronous: true);
        var view = new MainView { DataContext = vm };
        UiState.SetForceActiveSelection(view, true);
        var host = new DemoHost(view);
        Offscreen.Settle(host, size);

        var script = new DemoScript(host, vm, fps, size, demoPath);
        int stride = width * 4;
        int workers = Math.Max(2, Environment.ProcessorCount - 1);
        using var slots = new SemaphoreSlim(workers);
        var writes = new List<Task>();
        Exception? writeError = null;
        int frame = 0;

        foreach (var _ in script.Run())
        {
            vm.Tick(1.0 / fps);
            Offscreen.Settle(host, size);
            script.BeforeRender();
            Offscreen.Layout(host, size);

            var bmp = Offscreen.Render(host, width, height);
            var pixels = new byte[stride * height];
            bmp.CopyPixels(pixels, stride, 0);

            frame++;
            string path = Path.Combine(dir, $"frame_{frame:D5}.png");
            slots.Wait();
            writes.Add(Task.Run(() =>
            {
                try
                {
                    var src = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(src));
                    using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
                    encoder.Save(fs);
                }
                catch (Exception ex)
                {
                    writeError ??= ex;
                }
                finally
                {
                    slots.Release();
                }
            }));

            if (frame % (fps * 5) == 0) _out.Line($"  {frame / (double)fps,5:0.0} s  ({frame} frames)");
            if (writeError is not null) break;
        }

        Task.WaitAll(writes.ToArray());
        if (writeError is not null)
        {
            _out.Line("Error writing frames: " + writeError.Message);
            return 1;
        }

        _out.Line();
        _out.Line($"Wrote {frame} frames ({frame / (double)fps:0.0} s of video) in {total.Elapsed.TotalSeconds:0.0} s.");
        _out.Line("Make a video with ffmpeg:");
        _out.Line($"  ffmpeg -framerate {fps} -i \"{Path.Combine(dir, "frame_%05d.png")}\" -c:v libx264 -pix_fmt yuv420p -crf 16 jsonlens-demo.mp4");
        return 0;
    }
}
