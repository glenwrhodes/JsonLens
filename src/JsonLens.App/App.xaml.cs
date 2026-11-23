using System.Windows;
using System.Windows.Threading;
using JsonLens.App.Modes;
using JsonLens.App.Services;
using JsonLens.App.ViewModels;

namespace JsonLens.App;

public partial class App : Application
{
    private ConsoleOutput? _output;
    private bool _headless;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;

        var cl = CommandLine.Parse(e.Args);
        switch (cl.Mode)
        {
            case RunMode.Help:
                using (var help = new ConsoleOutput(cl.LogFile))
                {
                    if (cl.Error is not null) help.Line("Error: " + cl.Error + Environment.NewLine);
                    help.Line(CommandLine.Usage);
                }
                Shutdown(cl.Error is null ? 0 : 2);
                return;

            case RunMode.Check:
                _headless = true;
                _output = new ConsoleOutput(cl.LogFile);
                int checkCode = new CheckMode(_output, cl).Run();
                _output.Dispose();
                Shutdown(checkCode);
                return;

            case RunMode.Frames:
                _headless = true;
                _output = new ConsoleOutput(cl.LogFile);
                int framesCode = new FrameRecorder(_output, cl).Run();
                _output.Dispose();
                Shutdown(framesCode);
                return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        MainWindow? window = null;
        var vm = new MainViewModel(new WpfAppServices(() => window));
        window = new MainWindow(vm);
        MainWindow = window;
        window.Show();
        if (cl.FilePath is not null) _ = vm.OpenFileAsync(cl.FilePath);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_headless)
        {
            _output?.Line("FAIL  unhandled exception: " + e.Exception);
            _output?.Dispose();
            e.Handled = true;
            Shutdown(1);
            return;
        }
        MessageBox.Show(MainWindow, e.Exception.Message, "JSON Lens: something went wrong", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
