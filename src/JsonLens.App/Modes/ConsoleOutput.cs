using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace JsonLens.App.Modes;

/// <summary>
/// Console output for a GUI (WinExe) app. Uses redirected stdout when there is one (pipes, files),
/// otherwise attaches to the parent console. Optionally copies every line to a log file.
/// </summary>
public sealed class ConsoleOutput : IDisposable
{
    private readonly TextWriter? _console;
    private readonly StreamWriter? _log;

    public ConsoleOutput(string? logPath)
    {
        _console = OpenConsole();
        if (logPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
            _log = new StreamWriter(logPath, append: false, new UTF8Encoding(false)) { AutoFlush = true };
        }
    }

    public void Line(string text = "")
    {
        _console?.WriteLine(text);
        _log?.WriteLine(text);
    }

    public void Dispose()
    {
        _console?.Flush();
        _log?.Dispose();
    }

    private static TextWriter? OpenConsole()
    {
        try
        {
            var handle = GetStdHandle(StdOutputHandle);
            if (handle != IntPtr.Zero && handle != new IntPtr(-1) && GetFileType(handle) != FileTypeUnknown)
            {
                // Redirected to a pipe or file (e.g. "| Out-Host", "> out.txt").
                return new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            }

            if (AttachConsole(AttachParentProcess))
            {
                var conout = CreateFile("CONOUT$", GenericWrite, FileShareWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                if (!conout.IsInvalid)
                {
                    Console.OutputEncoding = Encoding.UTF8;
                    var writer = new StreamWriter(new FileStream(conout, FileAccess.Write), new UTF8Encoding(false)) { AutoFlush = true };
                    writer.WriteLine(); // start below the prompt the shell already printed
                    return writer;
                }
            }
        }
        catch (Exception)
        {
            // No console available: the log file (if any) still gets everything.
        }
        return null;
    }

    private const int StdOutputHandle = -11;
    private const int AttachParentProcess = -1;
    private const uint FileTypeUnknown = 0;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareWrite = 0x2;
    private const uint OpenExisting = 3;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    private static extern uint GetFileType(IntPtr hFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);
}
