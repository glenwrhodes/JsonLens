using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using JsonLens.App.ViewModels;

namespace JsonLens.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Icon = AppIcon.Create();

        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (_, _) => SystemCommands.MinimizeWindow(this)));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (_, _) =>
        {
            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
            else SystemCommands.MaximizeWindow(this);
        }));
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (_, _) => SystemCommands.CloseWindow(this)));

        StateChanged += (_, _) => UpdateFrameForState();
        UpdateFrameForState();
    }

    private void UpdateFrameForState()
    {
        bool max = WindowState == WindowState.Maximized;
        // A maximized chrome-less window hangs over the screen edge by the resize border; pad it back in.
        Frame.BorderThickness = max ? new Thickness(0) : new Thickness(1);
        Frame.Padding = max ? new Thickness(7) : new Thickness(0);
        View.MaximizeButton.Content = max ? "" : "";
        View.MaximizeButton.ToolTip = max ? "Restore" : "Maximize";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        _vm.Dispose();
    }
}
