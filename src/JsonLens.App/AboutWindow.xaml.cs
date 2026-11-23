using System.Reflection;
using System.Windows;
using System.Windows.Input;

namespace JsonLens.App;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is not null) VersionText.Text = $"Version {version.Major}.{version.Minor}.{version.Build}";
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
