using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Imaging;

namespace clipboard_inspector.Models;

public sealed record ClipboardSummaryItem(string Label, string Value);

public sealed record ClipboardOutlineItem(string Title, string Detail);

public sealed record ClipboardFileItem(string Name, string Path, string Kind, string Size);

public sealed record ClipboardActionItem(string Title, string Detail);

public sealed class ClipboardHistoryItem
{
    public required string Fingerprint { get; init; }

    public required string Title { get; init; }

    public required string Detail { get; init; }

    public required string FormatsText { get; init; }

    public required string CapturedText { get; init; }

    public required string SourceText { get; init; }

    public required DateTimeOffset CapturedAt { get; init; }

    public required ClipboardInspectionResult Snapshot { get; init; }
}

public sealed class ClipboardImageData
{
    public required SoftwareBitmap Bitmap { get; init; }

    public required ImageSource PreviewSource { get; init; }

    public required uint PixelWidth { get; init; }

    public required uint PixelHeight { get; init; }

    public required string PixelFormat { get; init; }

    public required string AlphaMode { get; init; }

    public required string ContentHash { get; init; }
}

public sealed class ClipboardInspectionResult
{
    public string Title { get; init; } = "Clipboard ready";

    public string SummaryBody { get; init; } = "Open the app to inspect the current clipboard payload.";

    public IReadOnlyList<string> AvailableFormats { get; init; } = Array.Empty<string>();

    public IReadOnlyList<ClipboardSummaryItem> SummaryItems { get; init; } = Array.Empty<ClipboardSummaryItem>();

    public IReadOnlyList<ClipboardOutlineItem> OutlineItems { get; init; } = Array.Empty<ClipboardOutlineItem>();

    public IReadOnlyList<ClipboardFileItem> Files { get; init; } = Array.Empty<ClipboardFileItem>();

    public IReadOnlyList<ClipboardActionItem> ActionItems { get; init; } = Array.Empty<ClipboardActionItem>();

    public string PlainText { get; init; } = string.Empty;

    public string HtmlText { get; init; } = string.Empty;

    public string RtfText { get; init; } = string.Empty;

    public string FileListText { get; init; } = string.Empty;

    public string ImageDetailsText { get; init; } = string.Empty;

    public ClipboardImageData? ImageData { get; init; }

    public string? UriText { get; init; }

    public string Fingerprint { get; init; } = string.Empty;
}
