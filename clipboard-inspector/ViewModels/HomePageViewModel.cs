using clipboard_inspector.Models;
using clipboard_inspector.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security;
using Windows.Graphics.Imaging;

namespace clipboard_inspector.ViewModels;

public partial class HomePageViewModel : ObservableObject
{
    private const int MaxHistoryItems = 25;
    private readonly ClipboardInspectionService _clipboardInspectionService;
    private readonly ExternalContentLauncher _externalContentLauncher = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private ClipboardRefreshSource? _pendingRefreshSource;
    private SoftwareBitmap? _imageBitmap;
    private bool _initialized;

    public HomePageViewModel(ClipboardInspectionService clipboardInspectionService)
    {
        _clipboardInspectionService = clipboardInspectionService;
    }

    public ObservableCollection<ClipboardSummaryItem> SummaryItems { get; } = [];

    public ObservableCollection<ClipboardOutlineItem> OutlineItems { get; } = [];

    public ObservableCollection<ClipboardFileItem> FileItems { get; } = [];

    public ObservableCollection<ClipboardActionItem> ActionItems { get; } = [];

    public ObservableCollection<ClipboardHistoryItem> HistoryItems { get; } = [];

    public Visibility TextTabVisibility => HasText ? Visibility.Visible : Visibility.Collapsed;

    public Visibility HtmlTabVisibility => HasHtml ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RtfTabVisibility => HasRtf ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FilesTabVisibility => HasFiles ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ImageTabVisibility => HasImage ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    public partial string ContentTitle { get; set; } = "Clipboard ready";

    [ObservableProperty]
    public partial string SummaryBody { get; set; } = "Open the app to analyze the current clipboard payload automatically.";

    [ObservableProperty]
    public partial string DetectedFormatsText { get; set; } = "Available formats: none";

    [ObservableProperty]
    public partial string LastAnalyzedText { get; set; } = "Last analyzed: not yet";

    [ObservableProperty]
    public partial string MonitoringText { get; set; } = "Live monitoring is on while this window stays open.";

    [ObservableProperty]
    public partial string HistoryStatusText { get; set; } = "Session history is empty. Clipboard snapshots stay in memory only and clear on app close.";

    [ObservableProperty]
    public partial string PlainText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string HtmlText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string RtfText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string FileListText { get; set; } = "No file-system items detected.";

    [ObservableProperty]
    public partial string ImageDetailsText { get; set; } = "No bitmap payload detected.";

    [ObservableProperty]
    public partial ImageSource? ImagePreviewSource { get; set; }

    [ObservableProperty]
    public partial bool HasText { get; set; }

    [ObservableProperty]
    public partial bool HasHtml { get; set; }

    [ObservableProperty]
    public partial bool HasRtf { get; set; }

    [ObservableProperty]
    public partial bool HasFiles { get; set; }

    [ObservableProperty]
    public partial bool HasImage { get; set; }

    [ObservableProperty]
    public partial bool HasUri { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsStatusOpen { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity StatusSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial ClipboardHistoryItem? SelectedHistoryItem { get; set; }

    public string? UriText { get; private set; }

    public string? PrimaryFilePath { get; private set; }

    public void ClearSessionCache()
    {
        _pendingRefreshSource = null;
        HistoryItems.Clear();
        SelectedHistoryItem = null;
        UpdateHistoryTelemetry();
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await RequestRefreshAsync(ClipboardRefreshSource.Startup);
    }

    public Task RefreshFromClipboardChangeAsync() =>
        RequestRefreshAsync(ClipboardRefreshSource.ClipboardChanged);

    [RelayCommand]
    private Task RefreshAsync() =>
        RequestRefreshAsync(ClipboardRefreshSource.Manual);

    [RelayCommand(CanExecute = nameof(CanOpenTextInNotepad))]
    private async Task OpenTextInNotepadAsync()
    {
        try
        {
            await _externalContentLauncher.OpenTextInNotepadAsync(PlainText, ".txt");
        }
        catch (IOException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to open text", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to open text", ex.Message);
        }
        catch (SecurityException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to open text", ex.Message);
        }
        catch (Win32Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to open text", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenHtmlInNotepad))]
    private async Task OpenHtmlInNotepadAsync()
    {
        try
        {
            await _externalContentLauncher.OpenTextInNotepadAsync(HtmlText, ".html");
        }
        catch (IOException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to open HTML", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to open HTML", ex.Message);
        }
        catch (SecurityException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to open HTML", ex.Message);
        }
        catch (Win32Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to open HTML", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenUri))]
    private void OpenUri()
    {
        try
        {
            _externalContentLauncher.OpenUri(UriText!);
        }
        catch (Win32Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to open URI", ex.Message);
        }
        catch (ArgumentException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to open URI", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRevealFirstItem))]
    private void RevealFirstItem()
    {
        try
        {
            _externalContentLauncher.RevealPath(PrimaryFilePath!);
        }
        catch (Win32Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to reveal path", ex.Message);
        }
        catch (ArgumentException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to reveal path", ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenImageExternally))]
    private async Task OpenImageExternallyAsync()
    {
        try
        {
            await _externalContentLauncher.OpenImageAsync(_imageBitmap!);
        }
        catch (IOException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to open image", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to open image", ex.Message);
        }
        catch (SecurityException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to open image", ex.Message);
        }
        catch (COMException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to open image", ex.Message);
        }
        catch (Win32Exception ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Unable to open image", ex.Message);
        }
    }

    private async Task RequestRefreshAsync(ClipboardRefreshSource source)
    {
        if (!await _refreshGate.WaitAsync(0))
        {
            _pendingRefreshSource = source;
            return;
        }

        try
        {
            ClipboardRefreshSource currentSource = source;

            while (true)
            {
                await RefreshCoreAsync(currentSource);

                if (_pendingRefreshSource is not ClipboardRefreshSource nextSource)
                {
                    break;
                }

                _pendingRefreshSource = null;
                currentSource = nextSource;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task RefreshCoreAsync(ClipboardRefreshSource source)
    {
        try
        {
            IsBusy = true;

            if (source != ClipboardRefreshSource.Manual)
            {
                CloseStatus();
            }

            ClipboardInspectionResult result = await _clipboardInspectionService.AnalyzeAsync();
            ClipboardHistoryItem entry = CacheHistory(result, source, DateTimeOffset.Now);
            SelectedHistoryItem = entry;

            if (source == ClipboardRefreshSource.Manual)
            {
                ShowStatus(InfoBarSeverity.Informational, "Clipboard analyzed", result.SummaryBody);
            }
        }
        catch (COMException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Clipboard analysis failed", ex.Message);
        }
        catch (IOException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Clipboard analysis failed", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Clipboard analysis failed", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            ShowStatus(InfoBarSeverity.Error, "Clipboard analysis failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
            NotifyActionCommands();
        }
    }

    partial void OnSelectedHistoryItemChanged(ClipboardHistoryItem? value)
    {
        if (value is null)
        {
            return;
        }

        ApplySnapshot(value.Snapshot, value.CapturedAt, value.SourceText);
    }

    private ClipboardHistoryItem CacheHistory(
        ClipboardInspectionResult result,
        ClipboardRefreshSource source,
        DateTimeOffset capturedAt)
    {
        ClipboardHistoryItem entry = new()
        {
            Fingerprint = result.Fingerprint,
            Title = result.Title,
            Detail = result.SummaryBody,
            FormatsText = result.AvailableFormats.Count == 0
                ? "Formats: none"
                : $"Formats: {string.Join(", ", result.AvailableFormats)}",
            CapturedText = capturedAt.ToString("u"),
            SourceText = DescribeSource(source),
            CapturedAt = capturedAt,
            Snapshot = result
        };

        if (HistoryItems.Count > 0 && string.Equals(HistoryItems[0].Fingerprint, entry.Fingerprint, StringComparison.Ordinal))
        {
            HistoryItems[0] = entry;
        }
        else
        {
            HistoryItems.Insert(0, entry);

            while (HistoryItems.Count > MaxHistoryItems)
            {
                HistoryItems.RemoveAt(HistoryItems.Count - 1);
            }
        }

        UpdateHistoryTelemetry();
        return entry;
    }

    private void ApplySnapshot(ClipboardInspectionResult result, DateTimeOffset capturedAt, string sourceText)
    {
        ContentTitle = result.Title;
        SummaryBody = result.SummaryBody;
        DetectedFormatsText = result.AvailableFormats.Count == 0
            ? "Available formats: none"
            : $"Available formats: {string.Join(", ", result.AvailableFormats)}";
        LastAnalyzedText = $"Last analyzed: {capturedAt:u} via {sourceText}";
        PlainText = result.PlainText;
        HtmlText = result.HtmlText;
        RtfText = result.RtfText;
        FileListText = result.FileListText;
        ImageDetailsText = result.ImageDetailsText;
        ImagePreviewSource = result.ImageData?.PreviewSource;
        _imageBitmap = result.ImageData?.Bitmap;
        UriText = result.UriText;
        PrimaryFilePath = result.Files.FirstOrDefault()?.Path;

        HasText = !string.IsNullOrWhiteSpace(result.PlainText);
        HasHtml = !string.IsNullOrWhiteSpace(result.HtmlText);
        HasRtf = !string.IsNullOrWhiteSpace(result.RtfText);
        HasFiles = result.Files.Count > 0;
        HasImage = result.ImageData is not null;
        HasUri = !string.IsNullOrWhiteSpace(result.UriText);

        ReplaceItems(SummaryItems, result.SummaryItems);
        ReplaceItems(OutlineItems, result.OutlineItems);
        ReplaceItems(FileItems, result.Files);
        ReplaceItems(ActionItems, result.ActionItems);
    }

    private void UpdateHistoryTelemetry()
    {
        MonitoringText = $"Live monitoring is on. Session history keeps the most recent {MaxHistoryItems} items in memory only and clears on app close.";
        HistoryStatusText = HistoryItems.Count == 0
            ? "Session history is empty. Clipboard snapshots stay in memory only and clear on app close."
            : $"Session history: {HistoryItems.Count} item(s) cached in memory for this app session.";
    }

    private static string DescribeSource(ClipboardRefreshSource source) =>
        source switch
        {
            ClipboardRefreshSource.Startup => "startup",
            ClipboardRefreshSource.Manual => "manual refresh",
            ClipboardRefreshSource.ClipboardChanged => "clipboard change",
            _ => "unknown source"
        };

    partial void OnHasTextChanged(bool value) => OnPropertyChanged(nameof(TextTabVisibility));

    partial void OnHasHtmlChanged(bool value) => OnPropertyChanged(nameof(HtmlTabVisibility));

    partial void OnHasRtfChanged(bool value) => OnPropertyChanged(nameof(RtfTabVisibility));

    partial void OnHasFilesChanged(bool value) => OnPropertyChanged(nameof(FilesTabVisibility));

    partial void OnHasImageChanged(bool value) => OnPropertyChanged(nameof(ImageTabVisibility));

    private bool CanOpenTextInNotepad() => HasText;

    private bool CanOpenHtmlInNotepad() => HasHtml;

    private bool CanOpenUri() => HasUri && UriText is not null;

    private bool CanRevealFirstItem() => HasFiles && !string.IsNullOrWhiteSpace(PrimaryFilePath);

    private bool CanOpenImageExternally() => HasImage && _imageBitmap is not null;

    private void NotifyActionCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        OpenTextInNotepadCommand.NotifyCanExecuteChanged();
        OpenHtmlInNotepadCommand.NotifyCanExecuteChanged();
        OpenUriCommand.NotifyCanExecuteChanged();
        RevealFirstItemCommand.NotifyCanExecuteChanged();
        OpenImageExternallyCommand.NotifyCanExecuteChanged();
    }

    private void ShowStatus(InfoBarSeverity severity, string title, string message)
    {
        StatusSeverity = severity;
        StatusTitle = title;
        StatusMessage = message;
        IsStatusOpen = true;
    }

    private void CloseStatus()
    {
        IsStatusOpen = false;
        StatusTitle = string.Empty;
        StatusMessage = string.Empty;
    }

    private static void ReplaceItems<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();

        foreach (T? item in source)
        {
            target.Add(item);
        }
    }

    private enum ClipboardRefreshSource
    {
        Startup,
        Manual,
        ClipboardChanged
    }
}
