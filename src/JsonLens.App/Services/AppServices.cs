using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace JsonLens.App.Services;

/// <summary>Everything the view model needs from the outside world, so check and frames modes can stub it.</summary>
public interface IAppServices
{
    string? PickOpenFile();
    string? PickSaveFile(string suggestedName);
    bool TrySetClipboard(string text);
    void ShowAbout();
}

public sealed class WpfAppServices : IAppServices
{
    private readonly Func<Window?> _owner;

    public WpfAppServices(Func<Window?> owner) => _owner = owner;

    public string? PickOpenFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open JSON file",
            Filter = "JSON files (*.json;*.geojson;*.jsonc;*.har)|*.json;*.geojson;*.jsonc;*.har|All files (*.*)|*.*",
        };
        return dlg.ShowDialog(_owner()) == true ? dlg.FileName : null;
    }

    public string? PickSaveFile(string suggestedName)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save JSON as",
            FileName = suggestedName,
            DefaultExt = ".json",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
        };
        return dlg.ShowDialog(_owner()) == true ? dlg.FileName : null;
    }

    public bool TrySetClipboard(string text)
    {
        // The clipboard can be briefly locked by another app; retry a few times.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, copy: true);
                return true;
            }
            catch (COMException)
            {
                Thread.Sleep(30);
            }
        }
        return false;
    }

    public void ShowAbout()
    {
        var about = new AboutWindow { Owner = _owner() };
        about.ShowDialog();
    }
}

/// <summary>No dialogs, no real clipboard: used by --check and --frames.</summary>
public sealed class RecordingAppServices : IAppServices
{
    public string? OpenFileResult { get; set; }
    public string? SaveFileResult { get; set; }
    public string? ClipboardText { get; private set; }
    public int AboutCount { get; private set; }

    public string? PickOpenFile() => OpenFileResult;
    public string? PickSaveFile(string suggestedName) => SaveFileResult;

    public bool TrySetClipboard(string text)
    {
        ClipboardText = text;
        return true;
    }

    public void ShowAbout() => AboutCount++;
}
