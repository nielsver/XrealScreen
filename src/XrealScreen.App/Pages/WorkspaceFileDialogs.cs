using Microsoft.Windows.Storage.Pickers;

namespace XrealScreen.App.Pages;

/// <summary>Import / export of the settings file, shared by the Home pages of all looks.</summary>
internal static class WorkspaceFileDialogs
{
    public static async Task ImportAsync()
    {
        try
        {
            var picker = new FileOpenPicker(App.MainWindow.AppWindow.Id);
            picker.FileTypeFilter.Add(".json");
            var result = await picker.PickSingleFileAsync();
            if (result is not null)
            {
                await App.Session.ImportSettingsAsync(result.Path).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Import", ex);
        }
    }

    public static async Task ExportAsync()
    {
        try
        {
            var picker = new FileSavePicker(App.MainWindow.AppWindow.Id)
            {
                SuggestedFileName = $"XrealScreen settings {DateTime.Now:yyyy-MM-dd}",
            };
            picker.FileTypeChoices.Add("XrealScreen settings", [".json"]);
            var result = await picker.PickSaveFileAsync();
            if (result is not null)
            {
                App.Session.ExportSettings(result.Path);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Export", ex);
        }
    }
}
