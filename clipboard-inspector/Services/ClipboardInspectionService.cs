using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using clipboard_inspector.Models;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace clipboard_inspector.Services;

public sealed class ClipboardInspectionService
{
    public async Task<ClipboardInspectionResult> AnalyzeAsync()
    {
        var dataView = Clipboard.GetContent();
        var availableFormats = dataView.AvailableFormats
            .Select(NormalizeFormatName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(format => format, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (availableFormats.Count == 0)
        {
            return new ClipboardInspectionResult
            {
                Title = "Clipboard is empty",
                SummaryBody = "No supported clipboard formats are currently available.",
                OutlineItems =
                [
                    new ClipboardOutlineItem(
                        "No payload detected",
                        "Copy text, code, HTML, an image, or file paths to populate this inspector.")
                ],
                ActionItems =
                [
                    new ClipboardActionItem(
                        "Refresh clipboard inspection",
                        "The app automatically re-analyzes when the clipboard changes, or you can refresh manually.")
                ],
                Fingerprint = HashString("empty-clipboard")
            };
        }

        var plainText = dataView.Contains(StandardDataFormats.Text)
            ? NormalizeLineEndings(await dataView.GetTextAsync())
            : string.Empty;

        var htmlText = dataView.Contains(StandardDataFormats.Html)
            ? NormalizeLineEndings(await dataView.GetHtmlFormatAsync())
            : string.Empty;

        var rtfText = dataView.Contains(StandardDataFormats.Rtf)
            ? NormalizeLineEndings(await dataView.GetRtfAsync())
            : string.Empty;

        var uri = await GetUriAsync(dataView, plainText);
        var files = dataView.Contains(StandardDataFormats.StorageItems)
            ? await ReadFilesAsync(dataView)
            : [];
        var image = dataView.Contains(StandardDataFormats.Bitmap)
            ? await ReadImageAsync(dataView)
            : null;

        var textAnalysis = AnalyzeText(plainText);
        var htmlAnalysis = AnalyzeHtml(htmlText);
        var actionItems = BuildActionItems(plainText, htmlText, uri, files, image);
        var summaryItems = BuildSummaryItems(
            availableFormats,
            textAnalysis,
            htmlAnalysis,
            rtfText,
            files,
            image,
            uri);
        var outlineItems = BuildOutlineItems(
            textAnalysis,
            htmlAnalysis,
            rtfText,
            files,
            image,
            uri);
        var title = BuildTitle(textAnalysis, htmlText, files, image, uri);
        var summary = BuildSummary(textAnalysis, htmlText, files, image, uri);
        var fingerprint = BuildFingerprint(
            availableFormats,
            plainText,
            htmlText,
            rtfText,
            files,
            image,
            uri?.ToString());

        return new ClipboardInspectionResult
        {
            Title = title,
            SummaryBody = summary,
            AvailableFormats = availableFormats,
            SummaryItems = summaryItems,
            OutlineItems = outlineItems,
            Files = files,
            ActionItems = actionItems,
            PlainText = plainText,
            HtmlText = htmlText,
            RtfText = rtfText,
            FileListText = BuildFileListText(files),
            ImageDetailsText = BuildImageDetailsText(image),
            ImageData = image,
            UriText = uri?.ToString(),
            Fingerprint = fingerprint
        };
    }

    private static async Task<Uri?> GetUriAsync(DataPackageView dataView, string plainText)
    {
        if (dataView.Contains(StandardDataFormats.WebLink))
        {
            return await dataView.GetWebLinkAsync();
        }

        return TryParseAbsoluteUri(plainText);
    }

    private static async Task<IReadOnlyList<ClipboardFileItem>> ReadFilesAsync(DataPackageView dataView)
    {
        var items = await dataView.GetStorageItemsAsync();
        var results = new List<ClipboardFileItem>(items.Count);

        foreach (var item in items)
        {
            switch (item)
            {
                case StorageFile file:
                {
                    var properties = await file.GetBasicPropertiesAsync();
                    results.Add(new ClipboardFileItem(
                        file.Name,
                        file.Path,
                        string.IsNullOrWhiteSpace(file.FileType) ? "File" : $"{file.FileType} file",
                        FormatBytes((long)properties.Size)));
                    break;
                }
                case StorageFolder folder:
                    results.Add(new ClipboardFileItem(folder.Name, folder.Path, "Folder", "n/a"));
                    break;
                default:
                    results.Add(new ClipboardFileItem(item.Name, item.Path, "Storage item", "n/a"));
                    break;
            }
        }

        return results;
    }

    private static async Task<ClipboardImageData> ReadImageAsync(DataPackageView dataView)
    {
        var streamReference = await dataView.GetBitmapAsync();
        using var stream = await streamReference.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var pixelBytes = pixelData.DetachPixelData();
        var convertedBitmap = SoftwareBitmap.CreateCopyFromBuffer(
            pixelBytes.AsBuffer(),
            BitmapPixelFormat.Bgra8,
            (int)decoder.PixelWidth,
            (int)decoder.PixelHeight,
            BitmapAlphaMode.Premultiplied);
        var previewSource = new SoftwareBitmapSource();
        await previewSource.SetBitmapAsync(convertedBitmap);

        return new ClipboardImageData
        {
            Bitmap = convertedBitmap,
            PreviewSource = previewSource,
            PixelWidth = decoder.PixelWidth,
            PixelHeight = decoder.PixelHeight,
            PixelFormat = convertedBitmap.BitmapPixelFormat.ToString(),
            AlphaMode = convertedBitmap.BitmapAlphaMode.ToString(),
            ContentHash = Convert.ToHexString(SHA256.HashData(pixelBytes)).ToLowerInvariant()
        };
    }

    private static IReadOnlyList<ClipboardActionItem> BuildActionItems(
        string plainText,
        string htmlText,
        Uri? uri,
        IReadOnlyList<ClipboardFileItem> files,
        ClipboardImageData? image)
    {
        var items = new List<ClipboardActionItem>();

        if (!string.IsNullOrWhiteSpace(plainText))
        {
            items.Add(new ClipboardActionItem(
                "Open text in Notepad",
                "Write the plain-text clipboard payload to a temporary UTF-8 file and open it in Notepad."));
        }

        if (!string.IsNullOrWhiteSpace(htmlText))
        {
            items.Add(new ClipboardActionItem(
                "Open HTML payload",
                "Write the raw CF_HTML payload to a temporary file so you can inspect the clipboard markup directly."));
        }

        if (uri is not null)
        {
            items.Add(new ClipboardActionItem(
                "Open URI target",
                $"Launch {uri} with the default handler."));
        }

        if (files.Count > 0)
        {
            items.Add(new ClipboardActionItem(
                "Reveal first file-system item",
                $"Open Explorer and select {files[0].Path}."));
        }

        if (image is not null)
        {
            items.Add(new ClipboardActionItem(
                "Open image preview",
                "Export the bitmap payload as a PNG file and open it with the default image viewer."));
        }

        if (items.Count == 0)
        {
            items.Add(new ClipboardActionItem(
                "No external action available",
                "This clipboard payload exposes formats the app can inspect but not launch externally."));
        }

        return items;
    }

    private static IReadOnlyList<ClipboardSummaryItem> BuildSummaryItems(
        IReadOnlyList<string> availableFormats,
        TextInspection text,
        HtmlInspection? html,
        string rtfText,
        IReadOnlyList<ClipboardFileItem> files,
        ClipboardImageData? image,
        Uri? uri)
    {
        var items = new List<ClipboardSummaryItem>
        {
            new("Primary payload", BuildPrimaryPayloadLabel(text, html, files, image, uri)),
            new("Formats", string.Join(", ", availableFormats)),
            new("Captured", DateTimeOffset.Now.ToString("u", CultureInfo.InvariantCulture))
        };

        if (text.HasText)
        {
            items.Add(new("Text classifier", text.Classification));
            items.Add(new("Text metrics", $"{text.CharacterCount:N0} chars, {text.LineCount:N0} lines, {text.Utf8Bytes:N0} UTF-8 bytes"));
            items.Add(new("Text SHA-256", text.Sha256));

            if (text.Json is not null)
            {
                items.Add(new("JSON structure", text.Json.Summary));
            }

            if (text.Xml is not null)
            {
                items.Add(new("XML root", text.Xml.RootElement));
            }

            if (text.Code is not null)
            {
                items.Add(new("Code language", text.Code.LanguageHint));
            }
        }

        if (html is not null)
        {
            items.Add(new("HTML payload", $"{html.CharacterCount:N0} chars"));
            items.Add(new("HTML SHA-256", html.Sha256));

            if (!string.IsNullOrWhiteSpace(html.Title))
            {
                items.Add(new("HTML title", html.Title!));
            }

            if (html.FragmentLength > 0)
            {
                items.Add(new("HTML fragment", $"{html.FragmentLength:N0} chars"));
            }
        }

        if (image is not null)
        {
            items.Add(new("Bitmap", $"{image.PixelWidth} x {image.PixelHeight} px"));
            items.Add(new("Bitmap SHA-256", image.ContentHash));
        }

        if (files.Count > 0)
        {
            items.Add(new("File list", $"{files.Count:N0} item(s)"));
        }

        if (uri is not null)
        {
            items.Add(new("URI", uri.ToString()));
        }

        if (!string.IsNullOrEmpty(rtfText))
        {
            items.Add(new("RTF payload", $"{rtfText.Length:N0} chars"));
            items.Add(new("RTF SHA-256", HashString(rtfText)));
        }

        return items;
    }

    private static IReadOnlyList<ClipboardOutlineItem> BuildOutlineItems(
        TextInspection text,
        HtmlInspection? html,
        string rtfText,
        IReadOnlyList<ClipboardFileItem> files,
        ClipboardImageData? image,
        Uri? uri)
    {
        var items = new List<ClipboardOutlineItem>();

        if (text.HasText)
        {
            items.Add(new ClipboardOutlineItem(
                "Text payload",
                $"{text.Classification}; {text.CharacterCount:N0} characters across {text.LineCount:N0} line(s); SHA-256 {ShortHash(text.Sha256)}."));

            if (text.Json is not null)
            {
                items.Add(new ClipboardOutlineItem(
                    "JSON structure",
                    text.Json.Details));
            }

            if (text.Xml is not null)
            {
                items.Add(new ClipboardOutlineItem(
                    "XML structure",
                    $"{text.Xml.RootElement} root with {text.Xml.DescendantCount:N0} descendant element(s)."));
            }

            if (text.StackTrace is not null)
            {
                items.Add(new ClipboardOutlineItem(
                    "Stack trace",
                    $"{text.StackTrace.ExceptionLine} | top frame: {text.StackTrace.TopFrame}"));
            }

            if (text.Code is not null)
            {
                items.Add(new ClipboardOutlineItem(
                    "Code signal",
                    $"{text.Code.LanguageHint}; {text.Code.NamespaceCount:N0} namespace/import block(s), {text.Code.TypeCount:N0} type declaration(s), {text.Code.MemberCount:N0} callable/member pattern(s)."));
            }
        }

        if (uri is not null)
        {
            var queryParameterCount = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Length;

            items.Add(new ClipboardOutlineItem(
                "URI candidate",
                $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath} with {queryParameterCount:N0} query parameter(s)."));
        }

        if (html is not null)
        {
            var detail = new StringBuilder();
            detail.Append($"{html.CharacterCount:N0} characters of CF_HTML data");

            if (!string.IsNullOrWhiteSpace(html.Title))
            {
                detail.Append($", title \"{html.Title}\"");
            }

            if (html.FragmentLength > 0)
            {
                detail.Append($", fragment {html.FragmentLength:N0} chars");
            }

            if (html.SourceUrl is not null)
            {
                detail.Append($", source {html.SourceUrl}");
            }

            detail.Append('.');

            items.Add(new ClipboardOutlineItem("HTML payload", detail.ToString()));
        }

        if (!string.IsNullOrEmpty(rtfText))
        {
            items.Add(new ClipboardOutlineItem(
                "Rich text payload",
                $"{rtfText.Length:N0} characters of RTF source; SHA-256 {ShortHash(HashString(rtfText))}."));
        }

        if (image is not null)
        {
            items.Add(new ClipboardOutlineItem(
                "Bitmap preview",
                $"{image.PixelWidth} x {image.PixelHeight} px, {image.PixelFormat}, alpha {image.AlphaMode}."));
        }

        if (files.Count > 0)
        {
            items.Add(new ClipboardOutlineItem(
                "File drop list",
                files.Count == 1
                    ? $"1 item: {files[0].Path}"
                    : $"{files.Count:N0} items; first entry is {files[0].Path}; extensions {DescribeFileExtensions(files)}."));
        }

        if (items.Count == 0)
        {
            items.Add(new ClipboardOutlineItem(
                "Unsupported clipboard payload",
                "The clipboard exposes formats that this inspector does not decode yet."));
        }

        return items;
    }

    private static string BuildTitle(
        TextInspection text,
        string htmlText,
        IReadOnlyList<ClipboardFileItem> files,
        ClipboardImageData? image,
        Uri? uri)
    {
        if (image is not null && (text.HasText || files.Count > 0 || !string.IsNullOrEmpty(htmlText)))
        {
            return "Mixed clipboard payload";
        }

        if (text.HasText)
        {
            return $"{text.Classification} clipboard payload";
        }

        if (!string.IsNullOrEmpty(htmlText))
        {
            return "HTML clipboard payload";
        }

        if (image is not null)
        {
            return "Bitmap clipboard payload";
        }

        if (files.Count > 0)
        {
            return "File drop clipboard payload";
        }

        if (uri is not null)
        {
            return "URI clipboard payload";
        }

        return "Clipboard payload detected";
    }

    private static string BuildSummary(
        TextInspection text,
        string htmlText,
        IReadOnlyList<ClipboardFileItem> files,
        ClipboardImageData? image,
        Uri? uri)
    {
        var parts = new List<string>();

        if (text.HasText)
        {
            parts.Add($"{text.Classification.ToLowerInvariant()} ({text.CharacterCount:N0} chars, {text.LineCount:N0} lines)");
        }

        if (!string.IsNullOrEmpty(htmlText))
        {
            parts.Add($"CF_HTML payload ({htmlText.Length:N0} chars)");
        }

        if (image is not null)
        {
            parts.Add($"bitmap image ({image.PixelWidth} x {image.PixelHeight})");
        }

        if (files.Count > 0)
        {
            parts.Add(files.Count == 1 ? "1 file-system item" : $"{files.Count:N0} file-system items");
        }

        if (uri is not null)
        {
            parts.Add($"URI target {uri}");
        }

        return parts.Count == 0
            ? "Clipboard formats were detected, but no supported payload could be decoded."
            : $"Detected {string.Join(", ", parts)}.";
    }

    private static string BuildFileListText(IReadOnlyList<ClipboardFileItem> files)
    {
        if (files.Count == 0)
        {
            return "No file-system items were detected.";
        }

        var builder = new StringBuilder();

        foreach (var file in files)
        {
            builder.AppendLine($"{file.Kind,-12} {file.Size,-10} {file.Path}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildImageDetailsText(ClipboardImageData? image)
    {
        if (image is null)
        {
            return "No bitmap payload detected.";
        }

        return string.Join(Environment.NewLine,
        [
            $"Dimensions: {image.PixelWidth} x {image.PixelHeight}",
            $"Pixel format: {image.PixelFormat}",
            $"Alpha mode: {image.AlphaMode}",
            $"SHA-256: {image.ContentHash}"
        ]);
    }

    private static string BuildFingerprint(
        IReadOnlyList<string> availableFormats,
        string plainText,
        string htmlText,
        string rtfText,
        IReadOnlyList<ClipboardFileItem> files,
        ClipboardImageData? image,
        string? uriText)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join('|', availableFormats));
        builder.AppendLine(plainText);
        builder.AppendLine(htmlText);
        builder.AppendLine(rtfText);
        builder.AppendLine(uriText ?? string.Empty);
        builder.AppendLine(image?.ContentHash ?? string.Empty);

        foreach (var file in files)
        {
            builder.AppendLine($"{file.Kind}|{file.Size}|{file.Path}");
        }

        return HashString(builder.ToString());
    }

    private static string BuildPrimaryPayloadLabel(
        TextInspection text,
        HtmlInspection? html,
        IReadOnlyList<ClipboardFileItem> files,
        ClipboardImageData? image,
        Uri? uri)
    {
        if (image is not null && (text.HasText || html is not null || files.Count > 0))
        {
            return "Mixed payload";
        }

        if (text.HasText)
        {
            return text.Classification;
        }

        if (html is not null)
        {
            return "HTML";
        }

        if (image is not null)
        {
            return "Bitmap image";
        }

        if (files.Count > 0)
        {
            return "File drop list";
        }

        if (uri is not null)
        {
            return "Absolute URI";
        }

        return "Unknown";
    }

    private static TextInspection AnalyzeText(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return TextInspection.Empty;
        }

        var trimmed = plainText.Trim();
        var uriCandidate = TryParseAbsoluteUri(trimmed);
        var json = TryAnalyzeJson(trimmed);
        var xml = TryAnalyzeXml(trimmed);
        var stackTrace = TryAnalyzeStackTrace(trimmed);
        var code = TryAnalyzeCode(trimmed);
        var classification = ClassifyText(uriCandidate, json, xml, stackTrace, code, trimmed);

        return new TextInspection(
            true,
            classification,
            plainText.Length,
            CountLines(plainText),
            Encoding.UTF8.GetByteCount(plainText),
            HashString(plainText),
            TrimForSingleLine(FirstNonEmptyLine(plainText)),
            json,
            xml,
            stackTrace,
            code);
    }

    private static HtmlInspection? AnalyzeHtml(string htmlText)
    {
        if (string.IsNullOrWhiteSpace(htmlText))
        {
            return null;
        }

        var metadata = ParseCfHtmlMetadata(htmlText);

        return new HtmlInspection(
            htmlText.Length,
            HashString(htmlText),
            ExtractHtmlTitle(htmlText),
            metadata.HeaderLength,
            metadata.FragmentLength,
            metadata.SourceUrl);
    }

    private static JsonInspection? TryAnalyzeJson(string text)
    {
        if (!(text.StartsWith('{') && text.EndsWith('}')) && !(text.StartsWith('[') && text.EndsWith(']')))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var rootKind = root.ValueKind switch
            {
                JsonValueKind.Object => "Object",
                JsonValueKind.Array => "Array",
                _ => root.ValueKind.ToString()
            };

            if (root.ValueKind == JsonValueKind.Object)
            {
                var propertyNames = root.EnumerateObject()
                    .Select(property => property.Name)
                    .Take(5)
                    .ToList();
                var propertyCount = root.EnumerateObject().Count();
                var sampleKeys = propertyNames.Count == 0 ? "none" : string.Join(", ", propertyNames);

                return new JsonInspection(
                    $"{rootKind} with {propertyCount:N0} top-level propert{(propertyCount == 1 ? "y" : "ies")}",
                    $"{rootKind} with {propertyCount:N0} top-level propert{(propertyCount == 1 ? "y" : "ies")}; sample keys: {sampleKeys}.");
            }

            if (root.ValueKind == JsonValueKind.Array)
            {
                var itemCount = root.GetArrayLength();
                return new JsonInspection(
                    $"{rootKind} with {itemCount:N0} top-level item(s)",
                    $"{rootKind} with {itemCount:N0} top-level item(s).");
            }

            return new JsonInspection(rootKind, $"{rootKind} JSON value.");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static XmlInspection? TryAnalyzeXml(string text)
    {
        if (!text.StartsWith('<') || !text.EndsWith('>'))
        {
            return null;
        }

        try
        {
            var document = XDocument.Parse(text, LoadOptions.None);
            var root = document.Root;

            if (root is null)
            {
                return null;
            }

            return new XmlInspection(root.Name.LocalName, root.Descendants().Count());
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static StackTraceInspection? TryAnalyzeStackTrace(string text)
    {
        if (!text.Contains("Exception", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var lines = text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var exceptionLine = lines.FirstOrDefault(line => line.Contains("Exception", StringComparison.OrdinalIgnoreCase));
        var topFrame = lines.FirstOrDefault(line => line.StartsWith("at ", StringComparison.Ordinal))
            ?? lines.FirstOrDefault(line => line.Contains(" at ", StringComparison.Ordinal));

        return string.IsNullOrWhiteSpace(exceptionLine) || string.IsNullOrWhiteSpace(topFrame)
            ? null
            : new StackTraceInspection(
                TrimForSingleLine(exceptionLine),
                TrimForSingleLine(topFrame));
    }

    private static CodeInspection? TryAnalyzeCode(string text)
    {
        if (!LooksLikeSourceCode(text))
        {
            return null;
        }

        var languageHint = DetectCodeLanguage(text);
        var namespaceCount = CountMatches(text, @"\b(namespace|using|import|package)\b");
        var typeCount = CountMatches(text, @"\b(class|struct|interface|enum|record)\b");
        var memberCount = CountMatches(
            text,
            @"\b(function|def|func)\b|=>|[A-Za-z_][A-Za-z0-9_<>,\[\]\?]*\s+[A-Za-z_][A-Za-z0-9_]*\s*\(");

        return new CodeInspection(languageHint, namespaceCount, typeCount, memberCount);
    }

    private static string ClassifyText(
        Uri? uriCandidate,
        JsonInspection? json,
        XmlInspection? xml,
        StackTraceInspection? stackTrace,
        CodeInspection? code,
        string trimmedText)
    {
        if (uriCandidate is not null)
        {
            return "Absolute URI";
        }

        if (json is not null)
        {
            return "JSON document";
        }

        if (xml is not null)
        {
            return "XML document";
        }

        if (stackTrace is not null)
        {
            return "Stack trace";
        }

        if (code is not null)
        {
            return $"Source code ({code.LanguageHint})";
        }

        return trimmedText.Contains('\n', StringComparison.Ordinal) ? "Multiline text" : "Plain text";
    }

    private static bool LooksLikeSourceCode(string text) =>
        Regex.IsMatch(
            text,
            @"\b(namespace|class|struct|interface|public|private|internal|using|import|function|const|let|var|SELECT|INSERT|UPDATE|DELETE|def|func|package)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string DetectCodeLanguage(string text)
    {
        if (Regex.IsMatch(text, @"^\s*(using\s+[A-Z][A-Za-z0-9_.]*;|namespace\s+[A-Za-z0-9_.]+;?)", RegexOptions.Multiline))
        {
            return "C#";
        }

        if (Regex.IsMatch(text, @"^\s*(import\s+.+\s+from\s+['""][^'""]+['""]|export\s+(class|function|const)|interface\s+\w+)", RegexOptions.Multiline))
        {
            return "TypeScript";
        }

        if (Regex.IsMatch(text, @"^\s*(function\s+\w+\s*\(|const\s+\w+\s*=|let\s+\w+\s*=|var\s+\w+\s*=)", RegexOptions.Multiline))
        {
            return "JavaScript";
        }

        if (Regex.IsMatch(text, @"^\s*(def\s+\w+\s*\(|from\s+\w+\s+import|import\s+\w+)", RegexOptions.Multiline))
        {
            return "Python";
        }

        if (Regex.IsMatch(text, @"^\s*(package\s+\w+|func\s+\w+\s*\()", RegexOptions.Multiline))
        {
            return "Go";
        }

        if (Regex.IsMatch(text, @"\b(SELECT|INSERT|UPDATE|DELETE|FROM|WHERE|JOIN)\b", RegexOptions.IgnoreCase))
        {
            return "SQL";
        }

        return "Generic";
    }

    private static HtmlMetadata ParseCfHtmlMetadata(string htmlText)
    {
        var sourceUrl = TryExtractCfHtmlSourceUrl(htmlText);
        var startHtml = TryExtractCfHtmlOffset(htmlText, "StartHTML");
        var startFragment = TryExtractCfHtmlOffset(htmlText, "StartFragment");
        var endFragment = TryExtractCfHtmlOffset(htmlText, "EndFragment");
        var fragmentLength = startFragment is not null && endFragment is not null && endFragment > startFragment
            ? endFragment.Value - startFragment.Value
            : 0;

        return new HtmlMetadata(startHtml ?? 0, fragmentLength, sourceUrl);
    }

    private static Uri? TryExtractCfHtmlSourceUrl(string htmlText)
    {
        var match = Regex.Match(
            htmlText,
            @"(?im)^SourceURL:(?<url>.+)$",
            RegexOptions.CultureInvariant);

        return match.Success && Uri.TryCreate(match.Groups["url"].Value.Trim(), UriKind.Absolute, out var uri)
            ? uri
            : null;
    }

    private static int? TryExtractCfHtmlOffset(string htmlText, string fieldName)
    {
        var match = Regex.Match(
            htmlText,
            $@"(?im)^{fieldName}:(?<value>\d+)$",
            RegexOptions.CultureInvariant);

        return match.Success && int.TryParse(match.Groups["value"].Value, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string? ExtractHtmlTitle(string htmlText)
    {
        if (string.IsNullOrWhiteSpace(htmlText))
        {
            return null;
        }

        var match = Regex.Match(
            htmlText,
            @"<title>\s*(?<title>.*?)\s*</title>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        return match.Success ? match.Groups["title"].Value : null;
    }

    private static Uri? TryParseAbsoluteUri(string candidate) =>
        Uri.TryCreate(candidate.Trim(), UriKind.Absolute, out var uri) ? uri : null;

    private static string NormalizeFormatName(string formatName) =>
        formatName switch
        {
            "Text" => "Text",
            "Html" => "HTML",
            "HTML Format" => "HTML Format",
            "Rich Text Format" => "RTF",
            "Rtf" => "RTF",
            "Bitmap" => "Bitmap",
            "StorageItems" => "Storage items",
            "UniformResourceLocatorW" => "URI",
            "UniformResourceLocator" => "URI",
            _ => formatName
        };

    private static string NormalizeLineEndings(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static int CountLines(string value) =>
        string.IsNullOrEmpty(value) ? 0 : value.Count(character => character == '\n') + 1;

    private static int CountMatches(string value, string pattern) =>
        Regex.Matches(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).Count;

    private static string DescribeFileExtensions(IReadOnlyList<ClipboardFileItem> files)
    {
        var extensions = files
            .Select(file => Path.GetExtension(file.Name))
            .Where(extension => !string.IsNullOrWhiteSpace(extension))
            .Select(extension => extension!.ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return extensions.Count == 0 ? "none" : string.Join(", ", extensions);
    }

    private static string HashString(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ShortHash(string hash) =>
        hash.Length <= 12 ? hash : hash[..12];

    private static string FirstNonEmptyLine(string value) =>
        value.Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0)
        ?? string.Empty;

    private static string FormatBytes(long byteCount)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = byteCount;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return $"{size:0.##} {units[unitIndex]}";
    }

    private static string TrimForSingleLine(string value)
    {
        var singleLine = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        return singleLine.Length <= 96 ? singleLine : $"{singleLine[..93]}...";
    }

    private sealed record TextInspection(
        bool HasText,
        string Classification,
        int CharacterCount,
        int LineCount,
        int Utf8Bytes,
        string Sha256,
        string PreviewLine,
        JsonInspection? Json,
        XmlInspection? Xml,
        StackTraceInspection? StackTrace,
        CodeInspection? Code)
    {
        public static TextInspection Empty { get; } = new(
            false,
            "No text payload",
            0,
            0,
            0,
            string.Empty,
            string.Empty,
            null,
            null,
            null,
            null);
    }

    private sealed record JsonInspection(string Summary, string Details);

    private sealed record XmlInspection(string RootElement, int DescendantCount);

    private sealed record StackTraceInspection(string ExceptionLine, string TopFrame);

    private sealed record CodeInspection(string LanguageHint, int NamespaceCount, int TypeCount, int MemberCount);

    private sealed record HtmlInspection(
        int CharacterCount,
        string Sha256,
        string? Title,
        int HeaderLength,
        int FragmentLength,
        Uri? SourceUrl);

    private sealed record HtmlMetadata(int HeaderLength, int FragmentLength, Uri? SourceUrl);
}
