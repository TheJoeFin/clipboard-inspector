using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using clipboard_inspector.Services;
using clipboard_inspector.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace clipboard_inspector.Pages;

public sealed partial class HomePage : Page
{
    private const string DefaultTextSelectionDetails = "Select text to inspect characters, words, and lines.";
    private bool _isClipboardSubscribed;
    private bool _isViewModelSubscribed;

    public HomePageViewModel ViewModel { get; } = App.HomePageViewModel;

    public HomePage()
    {
        InitializeComponent();
        Loaded += HomePage_Loaded;
        Unloaded += HomePage_Unloaded;
    }

    private async void HomePage_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isViewModelSubscribed)
        {
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            _isViewModelSubscribed = true;
        }

        if (!_isClipboardSubscribed)
        {
            Clipboard.ContentChanged += Clipboard_ContentChanged;
            _isClipboardSubscribed = true;
        }

        await ViewModel.InitializeAsync();
        SelectDefaultDetailsTab();
        UpdateTextSelectionInspector();
    }

    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isViewModelSubscribed)
        {
            ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
            _isViewModelSubscribed = false;
        }

        if (!_isClipboardSubscribed)
        {
            return;
        }

        Clipboard.ContentChanged -= Clipboard_ContentChanged;
        _isClipboardSubscribed = false;
    }

    private void Clipboard_ContentChanged(object? sender, object e)
    {
        DispatcherQueue.TryEnqueue(async () => await ViewModel.RefreshFromClipboardChangeAsync());
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(HomePageViewModel.SelectedHistoryItem), StringComparison.Ordinal))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            SelectDefaultDetailsTab();
            UpdateTextSelectionInspector();
        });
    }

    private void SelectDefaultDetailsTab()
    {
        Bindings.Update();

        DetailsPanel.SelectedItem = GetFirstVisibleDetailsTab();
    }

    private TabViewItem? GetFirstVisibleDetailsTab()
    {
        foreach (var tab in new[]
                 {
                     TextDetailsTab,
                     HtmlDetailsTab,
                     RtfDetailsTab,
                     FilesDetailsTab,
                     ImageDetailsTab
                 })
        {
            if (tab.Visibility == Visibility.Visible)
            {
                return tab;
            }
        }

        return null;
    }

    private void TextPayloadTextBox_SelectionChanged(object sender, RoutedEventArgs e) =>
        UpdateTextSelectionInspector();

    private void UpdateTextSelectionInspector()
    {
        if (TextSelectionDetailsText is null || TextPayloadTextBox is null)
        {
            return;
        }

        TextSelectionDetailsText.Text = DescribeSelection(TextPayloadTextBox.SelectedText);
    }

    private static string DescribeSelection(string? selection)
    {
        if (string.IsNullOrEmpty(selection))
        {
            return DefaultTextSelectionDetails;
        }

        int characterCount = CountTextElements(selection);
        if (characterCount == 1)
        {
            return DescribeSingleCharacter(selection);
        }

        int wordCount = CountWords(selection);
        int lineCount = CountLines(selection);
        return $"{characterCount} characters, {wordCount} words, {lineCount} lines";
    }

    private static string DescribeSingleCharacter(string selection)
    {
        Rune[] runes = [.. selection.EnumerateRunes()];
        if (runes.Length == 1)
        {
            return $"Unicode {DescribeRune(runes[0])}";
        }

        string codePoints = string.Join(
            "; ",
            runes.Select(DescribeRune));

        return $"1 character, {runes.Length} code points: {codePoints}";
    }

    private static string DescribeRune(Rune rune)
    {
        if (UnicodeNameLookup.GetSpecialCharacterInfo(rune) is { } specialCharacter)
        {
            return $"U+{rune.Value:X4} {specialCharacter.Description} ({specialCharacter.Abbreviation}, dec {specialCharacter.Decimal}, oct {specialCharacter.Octal})";
        }

        return $"U+{rune.Value:X4} {UnicodeNameLookup.GetName(rune)}";
    }

    private static int CountTextElements(string selection) =>
        StringInfo.ParseCombiningCharacters(selection).Length;

    private static int CountWords(string selection) =>
        Regex.Matches(selection, @"\S+").Count;

    private static int CountLines(string selection) =>
        Regex.Matches(selection, @"\r\n|\r|\n").Count + 1;

    private static class UnicodeNameLookup
    {
        private const int UnicodeCharName = 0;
        private const int BufferOverflowError = 15;
        private static readonly IReadOnlyDictionary<int, SpecialCharacterInfo> SpecialCharacters = new Dictionary<int, SpecialCharacterInfo>
        {
            [0x0000] = new(0, "000", "Null character", "NUL"),
            [0x0001] = new(1, "001", "Start of Heading", "SOH / Ctrl-A"),
            [0x0002] = new(2, "002", "Start of Text", "STX / Ctrl-B"),
            [0x0003] = new(3, "003", "End-of-text character", "ETX / Ctrl-C"),
            [0x0004] = new(4, "004", "End-of-transmission character", "EOT / Ctrl-D"),
            [0x0005] = new(5, "005", "Enquiry character", "ENQ / Ctrl-E"),
            [0x0006] = new(6, "006", "Acknowledge character", "ACK / Ctrl-F"),
            [0x0007] = new(7, "007", "Bell character", "BEL / Ctrl-G"),
            [0x0008] = new(8, "010", "Backspace", "BS / Ctrl-H"),
            [0x0009] = new(9, "011", "Horizontal tab", "HT / Ctrl-I"),
            [0x000A] = new(10, "012", "Line feed", "LF / Ctrl-J"),
            [0x000B] = new(11, "013", "Vertical tab", "VT / Ctrl-K"),
            [0x000C] = new(12, "014", "Form feed", "FF / Ctrl-L"),
            [0x000D] = new(13, "015", "Carriage return", "CR / Ctrl-M"),
            [0x000E] = new(14, "016", "Shift Out", "SO / Ctrl-N"),
            [0x000F] = new(15, "017", "Shift In", "SI / Ctrl-O"),
            [0x0010] = new(16, "020", "Data Link Escape", "DLE / Ctrl-P"),
            [0x0011] = new(17, "021", "Device Control 1", "DC1 / Ctrl-Q"),
            [0x0012] = new(18, "022", "Device Control 2", "DC2 / Ctrl-R"),
            [0x0013] = new(19, "023", "Device Control 3", "DC3 / Ctrl-S"),
            [0x0014] = new(20, "024", "Device Control 4", "DC4 / Ctrl-T"),
            [0x0015] = new(21, "025", "Negative-acknowledge character", "NAK / Ctrl-U"),
            [0x0016] = new(22, "026", "Synchronous Idle", "SYN / Ctrl-V"),
            [0x0017] = new(23, "027", "End of Transmission Block", "ETB / Ctrl-W"),
            [0x0018] = new(24, "030", "Cancel character", "CAN / Ctrl-X"),
            [0x0019] = new(25, "031", "End of Medium", "EM / Ctrl-Y"),
            [0x001A] = new(26, "032", "Substitute character", "SUB / Ctrl-Z"),
            [0x001B] = new(27, "033", "Escape character", "ESC"),
            [0x001C] = new(28, "034", "File Separator", "FS"),
            [0x001D] = new(29, "035", "Group Separator", "GS"),
            [0x001E] = new(30, "036", "Record Separator", "RS"),
            [0x001F] = new(31, "037", "Unit Separator", "US"),
            [0x007F] = new(127, "0177", "Delete", "DEL"),
            [0x0080] = new(128, "0302 0200", "Padding Character", "PAD"),
            [0x0081] = new(129, "0302 0201", "High Octet Preset", "HOP"),
            [0x0082] = new(130, "0302 0202", "Break Permitted Here", "BPH"),
            [0x0083] = new(131, "0302 0203", "No Break Here", "NBH"),
            [0x0084] = new(132, "0302 0204", "Index", "IND"),
            [0x0085] = new(133, "0302 0205", "Next Line", "NEL"),
            [0x0086] = new(134, "0302 0206", "Start of Selected Area", "SSA"),
            [0x0087] = new(135, "0302 0207", "End of Selected Area", "ESA"),
            [0x0088] = new(136, "0302 0210", "Character Tabulation Set", "HTS"),
            [0x0089] = new(137, "0302 0211", "Character Tabulation with Justification", "HTJ"),
            [0x008A] = new(138, "0302 0212", "Line Tabulation Set", "VTS"),
            [0x008B] = new(139, "0302 0213", "Partial Line Forward", "PLD"),
            [0x008C] = new(140, "0302 0214", "Partial Line Backward", "PLU"),
            [0x008D] = new(141, "0302 0215", "Reverse Line Feed", "RI"),
            [0x008E] = new(142, "0302 0216", "Single-Shift Two", "SS2"),
            [0x008F] = new(143, "0302 0217", "Single-Shift Three", "SS3"),
            [0x0090] = new(144, "0302 0220", "Device Control String", "DCS"),
            [0x0091] = new(145, "0302 0221", "Private Use 1", "PU1"),
            [0x0092] = new(146, "0302 0222", "Private Use 2", "PU2"),
            [0x0093] = new(147, "0302 0223", "Set Transmit State", "STS"),
            [0x0094] = new(148, "0302 0224", "Cancel character", "CCH"),
            [0x0095] = new(149, "0302 0225", "Message Waiting", "MW"),
            [0x0096] = new(150, "0302 0226", "Start of Protected Area", "SPA"),
            [0x0097] = new(151, "0302 0227", "End of Protected Area", "EPA"),
            [0x0098] = new(152, "0302 0230", "Start of String", "SOS"),
            [0x0099] = new(153, "0302 0231", "Single Graphic Character Introducer", "SGCI"),
            [0x009A] = new(154, "0302 0232", "Single Character Intro Introducer", "SCI"),
            [0x009B] = new(155, "0302 0233", "Control Sequence Introducer", "CSI"),
            [0x009C] = new(156, "0302 0234", "String Terminator", "ST"),
            [0x009D] = new(157, "0302 0235", "Operating System Command", "OSC"),
            [0x009E] = new(158, "0302 0236", "Private Message", "PM"),
            [0x009F] = new(159, "0302 0237", "Application Program Command", "APC")
        };

        [DllImport("icu.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, EntryPoint = "u_charName")]
        private static extern int UCharName(
            int codePoint,
            int nameChoice,
            byte[] buffer,
            int bufferLength,
            out int errorCode);

        public static string GetName(Rune rune)
        {
            byte[] buffer = new byte[128];
            int length = UCharName(rune.Value, UnicodeCharName, buffer, buffer.Length, out int errorCode);

            if (errorCode == BufferOverflowError && length > buffer.Length)
            {
                buffer = new byte[length];
                length = UCharName(rune.Value, UnicodeCharName, buffer, buffer.Length, out errorCode);
            }

            if (errorCode != 0 || length <= 0)
            {
                return "UNNAMED CHARACTER";
            }

            return Encoding.ASCII.GetString(buffer, 0, length);
        }

        public static SpecialCharacterInfo? GetSpecialCharacterInfo(Rune rune) =>
            SpecialCharacters.TryGetValue(rune.Value, out SpecialCharacterInfo? info) ? info : null;
    }

    private sealed record SpecialCharacterInfo(int Decimal, string Octal, string Description, string Abbreviation);
}
