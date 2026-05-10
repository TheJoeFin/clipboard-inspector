using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Graphics.Imaging;

namespace clipboard_inspector.Services;

public sealed class ExternalContentLauncher
{
    private readonly string _exportDirectory = Path.Combine(Path.GetTempPath(), "ClipboardInspector");

    public async Task OpenTextInNotepadAsync(string content, string extension)
    {
        Directory.CreateDirectory(_exportDirectory);
        var filePath = Path.Combine(
            _exportDirectory,
            $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss-fff}{extension}");

        await File.WriteAllTextAsync(filePath, content, Encoding.UTF8);
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{filePath}\"") { UseShellExecute = true });
    }

    public async Task OpenImageAsync(SoftwareBitmap bitmap)
    {
        Directory.CreateDirectory(_exportDirectory);
        var filePath = Path.Combine(
            _exportDirectory,
            $"clipboard-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");

        await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var randomAccessStream = fileStream.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, randomAccessStream);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();

        Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
    }

    public void OpenUri(string uri) =>
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });

    public void RevealPath(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
}
