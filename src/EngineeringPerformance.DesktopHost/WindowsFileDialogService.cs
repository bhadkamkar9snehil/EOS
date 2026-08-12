using EngineeringPerformance.Application;
using Microsoft.Win32;

namespace EngineeringPerformance.DesktopHost;

public sealed class WindowsFileDialogService : IFileDialogService
{
    public string? PickWorkbookOrZip() => Pick("Excel or ZIP files|*.xlsx;*.xlsm;*.zip|All files|*.*");
    public IReadOnlyList<string> PickReviewWorkbooksOrZip()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "EOS review workbooks or ZIP files|*.xlsx;*.xlsm;*.zip|All files|*.*",
            CheckFileExists = true,
            Multiselect = true,
            Title = "Add returned employee review files"
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }
    public string? PickWorkbook() => Pick("Excel workbooks|*.xlsx;*.xlsm|All files|*.*");
    public string? PickSaveWorkbook(string suggestedFileName)
    {
        var dialog = new SaveFileDialog { Filter = "Excel workbook|*.xlsx", FileName = suggestedFileName, AddExtension = true, DefaultExt = ".xlsx" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
    public string? PickFolder(string title)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static string? Pick(string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
