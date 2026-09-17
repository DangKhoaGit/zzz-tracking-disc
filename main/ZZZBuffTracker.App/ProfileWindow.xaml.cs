using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;

namespace ZZZBuffTracker.App;

public partial class ProfileWindow : Window
{
    private readonly ProfileViewModel viewModel;
    public ProfileWindow(ProfileViewModel viewModel) { InitializeComponent(); this.viewModel = viewModel; DataContext = viewModel; }
    private void SavePreset(object sender, RoutedEventArgs e)
    {
        if (!BuffGrid.CommitEdit(DataGridEditingUnit.Cell, true) || !BuffGrid.CommitEdit(DataGridEditingUnit.Row, true))
        { viewModel.ReportError(new ArgumentException("Sửa giá trị không hợp lệ trong bảng buff trước khi lưu.")); return; }
        viewModel.SaveCommand.Execute(null);
    }
    private async void DeletePreset(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, $"Xóa preset {viewModel.CharacterId}?", "Xóa preset", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await Run(viewModel.DeleteSelectedAsync);
    }
    private async void PreviewImport(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog { Filter = "Profile JSON|*.json" };
        if (picker.ShowDialog(this) == true) await Run(() => viewModel.PreviewImportAsync(picker.FileName));
    }
    private async void ApplyImport(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, viewModel.ImportSummary + "\nTiếp tục?", "Import profile", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await Run(viewModel.ApplyImportAsync);
    }
    private async void Export(object sender, RoutedEventArgs e)
    {
        var picker = new SaveFileDialog { Filter = "Profile JSON|*.json", FileName = "zzz-profile.json", OverwritePrompt = true };
        if (picker.ShowDialog(this) == true) await Run(() => viewModel.ExportAsync(picker.FileName, true));
    }
    private async Task Run(Func<Task> action)
    {
        IsEnabled = false;
        try { await action(); }
        catch (Exception ex) { viewModel.ReportError(ex); }
        finally { IsEnabled = true; }
    }
}
