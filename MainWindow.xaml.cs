using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ValCC;

public partial class MainWindow : Window
{
    private enum StatusKind
    {
        Info,
        Success,
        Error
    }

    private readonly RiotApi riotApi = new();
    private CrosshairData? crosshairData;
    private CrosshairProfile? selectedProfile;
    private TaskCompletionSource<bool>? dialogResult;
    private string connectedStatus = "";
    private int lastProfileIndex;
    private bool isBusy;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        DrawStars();
        await LoadSettingsAsync();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DialogOverlay.Visibility == Visibility.Visible)
        {
            CloseDialog(false);
            e.Handled = true;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (crosshairData != null && crosshairData.GetChanges().Count > 0)
        {
            bool discard = await AskAsync(
                "refresh",
                "you have unsaved outline colors. refreshing loads your settings from the server again and drops them.",
                "refresh");
            if (!discard)
            {
                return;
            }
        }

        await LoadSettingsAsync();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (crosshairData == null)
        {
            return;
        }

        List<OutlineChange> changes = crosshairData.GetChanges();
        if (changes.Count == 0)
        {
            return;
        }

        if (IsGameRunning())
        {
            bool saveAnyway = await AskAsync(
                "valorant is running",
                "close valorant before saving. while the game is open it can overwrite your settings with its own copy.",
                "save anyway");
            if (!saveAnyway)
            {
                return;
            }
        }

        SetBusy(true);
        try
        {
            ShowStatus("connecting…");
            await riotApi.ConnectAsync();

            ShowStatus("backing up your current settings…");
            JsonNode latestSettings = await riotApi.FetchSettingsAsync();
            string backupName = BackupStore.Write(latestSettings);

            CrosshairData latestData = CrosshairData.Read(latestSettings);
            latestData.Apply(changes);

            ShowStatus("saving…");
            await riotApi.SaveSettingsAsync(latestSettings);

            ShowStatus("checking that it stuck…");
            JsonNode savedSettings = await riotApi.FetchSettingsAsync();
            List<OutlineChange> missing = CrosshairData.Read(savedSettings).FindMissingChanges(changes);
            ShowSettings(savedSettings);

            if (missing.Count == 0)
            {
                ShowStatus($"saved and verified · backed up as {backupName}", StatusKind.Success);
            }
            else
            {
                string names = string.Join(", ", missing.Select(change => change.Description));
                ShowStatus($"saved, but the server did not keep: {names}", StatusKind.Error);
            }
        }
        catch (Exception error)
        {
            ShowStatus("not saved: " + error.Message, StatusKind.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BackupsButton_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsButton.IsChecked == true)
        {
            ShowBackups();
        }
        else
        {
            ShowEditor();
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(BackupStore.Folder);
        Process.Start(new ProcessStartInfo { FileName = BackupStore.Folder, UseShellExecute = true });
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not BackupFile backup)
        {
            return;
        }

        string message = $"restore all your valorant settings from {backup.DisplayName}? this replaces everything, not only crosshairs. your current settings are backed up first.";
        if (IsGameRunning())
        {
            message += " valorant is running, close it first or the game may overwrite the restore.";
        }

        bool confirmed = await AskAsync("restore backup", message, "restore");
        if (!confirmed)
        {
            return;
        }

        SetBusy(true);
        try
        {
            JsonNode backupSettings = BackupStore.Read(backup.FilePath);

            ShowStatus("connecting…");
            await riotApi.ConnectAsync();

            ShowStatus("backing up your current settings…");
            JsonNode currentSettings = await riotApi.FetchSettingsAsync();
            BackupStore.Write(currentSettings);

            ShowStatus("restoring…");
            await riotApi.SaveSettingsAsync(backupSettings);

            ShowStatus("checking that it stuck…");
            JsonNode restoredSettings = await riotApi.FetchSettingsAsync();
            bool verified = JsonNode.DeepEquals(restoredSettings, backupSettings);
            ShowSettings(restoredSettings);
            LoadBackupList();

            if (verified)
            {
                ShowStatus($"restored {backup.DisplayName} and verified", StatusKind.Success);
            }
            else
            {
                ShowStatus("restored, but the server returned something different from the backup", StatusKind.Error);
            }
        }
        catch (Exception error)
        {
            ShowStatus("not restored: " + error.Message, StatusKind.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        selectedProfile = ProfileList.SelectedItem as CrosshairProfile;
        if (selectedProfile != null)
        {
            lastProfileIndex = ProfileList.SelectedIndex;
            ShowEditor();
        }

        UpdateEditor();
    }

    private void PrimaryPicker_ColorChanged(object? sender, EventArgs e)
    {
        ChangeOutline(CrosshairPart.Primary, PrimaryPicker.SelectedColor);
    }

    private void AdsPicker_ColorChanged(object? sender, EventArgs e)
    {
        ChangeOutline(CrosshairPart.Ads, AdsPicker.SelectedColor);
    }

    private void Preview_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawPreviews();
    }

    private void DialogConfirmButton_Click(object sender, RoutedEventArgs e)
    {
        CloseDialog(true);
    }

    private void DialogCancelButton_Click(object sender, RoutedEventArgs e)
    {
        CloseDialog(false);
    }

    private async Task LoadSettingsAsync()
    {
        SetBusy(true);
        try
        {
            ShowStatus("connecting…");
            await riotApi.ConnectAsync();

            ShowStatus("loading your settings…");
            JsonNode settings = await riotApi.FetchSettingsAsync();
            ShowSettings(settings);

            int count = crosshairData?.Profiles.Count ?? 0;
            connectedStatus = count switch
            {
                0 => "connected · no crosshair profiles found",
                1 => "connected · 1 crosshair profile",
                _ => $"connected · {count} crosshair profiles"
            };
            ShowStatus(connectedStatus);
        }
        catch (Exception error)
        {
            ShowStatus(error.Message, StatusKind.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowSettings(JsonNode settings)
    {
        crosshairData = CrosshairData.Read(settings);
        ProfileList.ItemsSource = crosshairData.Profiles;
        if (BackupsPanel.Visibility != Visibility.Visible)
        {
            ShowEditor();
        }
    }

    private void ShowEditor()
    {
        BackupsButton.IsChecked = false;
        BackupsPanel.Visibility = Visibility.Collapsed;
        EditorPanel.Visibility = Visibility.Visible;
        if (ProfileList.SelectedIndex < 0 && ProfileList.Items.Count > 0)
        {
            ProfileList.SelectedIndex = Math.Min(lastProfileIndex, ProfileList.Items.Count - 1);
        }
    }

    private void ShowBackups()
    {
        BackupsButton.IsChecked = true;
        EditorPanel.Visibility = Visibility.Collapsed;
        BackupsPanel.Visibility = Visibility.Visible;
        ProfileList.SelectedIndex = -1;
        LoadBackupList();
    }

    private void LoadBackupList()
    {
        List<BackupFile> backups = BackupStore.List();
        BackupList.ItemsSource = backups;
        NoBackupsText.Visibility = backups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateEditor()
    {
        EditorPanel.IsEnabled = selectedProfile != null;
        if (selectedProfile != null)
        {
            PrimaryPicker.ShowColor(selectedProfile.GetOutline(CrosshairPart.Primary));
            AdsPicker.ShowColor(selectedProfile.GetOutline(CrosshairPart.Ads));
            PrimaryLabel.Text = OutlineLabel("primary outline", selectedProfile.Primary);
            AdsLabel.Text = selectedProfile.UsesPrimaryForAds
                ? "ads outline · unused, ads uses primary"
                : OutlineLabel("ads outline", selectedProfile.Ads);
        }

        DrawPreviews();
    }

    private static string OutlineLabel(string title, JsonObject? crosshair)
    {
        return JsonHelper.Flag(crosshair, "bHasOutline") ? title : title + " · outlines are off";
    }

    private static bool IsGameRunning()
    {
        Process[] processes = Process.GetProcessesByName("VALORANT-Win64-Shipping");
        foreach (Process process in processes)
        {
            process.Dispose();
        }

        return processes.Length > 0;
    }

    private void ChangeOutline(CrosshairPart part, Color color)
    {
        if (selectedProfile == null)
        {
            return;
        }

        selectedProfile.SetOutline(part, color);
        DrawPreviews();

        int count = crosshairData?.GetChanges().Count ?? 0;
        SaveButton.IsEnabled = count > 0 && !isBusy;
        if (count == 0)
        {
            ShowStatus(connectedStatus);
        }
        else
        {
            ShowStatus(count == 1 ? "1 unsaved outline color" : $"{count} unsaved outline colors");
        }
    }

    private void DrawPreviews()
    {
        if (selectedProfile == null)
        {
            CrosshairPreview.Draw(PrimaryPreview, null, 2);
            CrosshairPreview.Draw(AdsPreview, null, 2);
            PrimaryZoomText.Text = "";
            AdsZoomText.Text = "";
            return;
        }

        int scale = CrosshairPreview.ChooseScale(PrimaryPreview.ActualWidth, PrimaryPreview.ActualHeight, selectedProfile.Primary, selectedProfile.Ads);
        CrosshairPreview.Draw(PrimaryPreview, selectedProfile.Primary, scale);
        CrosshairPreview.Draw(AdsPreview, selectedProfile.Ads, scale);
        PrimaryZoomText.Text = $"×{scale}";
        AdsZoomText.Text = $"×{scale}";
    }

    private void DrawStars()
    {
        StarCanvas.Children.Clear();
        var random = new Random();
        Brush starBrush = (Brush)FindResource("StarBrush");
        double width = StarCanvas.ActualWidth;
        double height = StarCanvas.ActualHeight;
        int count = (int)(width * height / 18000);

        for (int index = 0; index < count; index++)
        {
            double radius = random.NextDouble() * 0.7 + 0.5;
            var star = new Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = starBrush,
                Opacity = random.NextDouble() * 0.3 + 0.1
            };
            Canvas.SetLeft(star, random.NextDouble() * width - radius);
            Canvas.SetTop(star, random.NextDouble() * height - radius);
            StarCanvas.Children.Add(star);
        }
    }

    private void SetBusy(bool busy)
    {
        isBusy = busy;
        BodyGrid.IsEnabled = !busy;
        BackupsButton.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy && crosshairData?.GetChanges().Count > 0;
    }

    private void ShowStatus(string message, StatusKind kind = StatusKind.Info)
    {
        string brushName = kind switch
        {
            StatusKind.Success => "AccentBrush",
            StatusKind.Error => "ErrorBrush",
            _ => "TextDimBrush"
        };
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource(brushName);
    }

    private Task<bool> AskAsync(string title, string message, string confirmText)
    {
        DialogTitle.Text = title;
        DialogMessage.Text = message;
        DialogConfirmButton.Content = confirmText;
        DialogOverlay.Visibility = Visibility.Visible;
        DialogCancelButton.Focus();
        dialogResult = new TaskCompletionSource<bool>();
        return dialogResult.Task;
    }

    private void CloseDialog(bool confirmed)
    {
        DialogOverlay.Visibility = Visibility.Collapsed;
        dialogResult?.TrySetResult(confirmed);
        dialogResult = null;
    }
}
