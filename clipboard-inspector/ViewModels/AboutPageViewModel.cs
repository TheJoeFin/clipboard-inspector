using clipboard_inspector.Models;
using System.Reflection;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace clipboard_inspector.ViewModels;

public sealed class AboutPageViewModel
{
    private const string FallbackAppName = "Clipboard Inspector";
    private const string UnknownValue = "Unknown";

    public AboutPageViewModel()
    {
        Package? package = TryGetCurrentPackage();

        AppName = ResolveAppName(package);
        VersionText = ResolveVersionText(package);
        VersionSummary = package is null
            ? $"Version {VersionText} (unpackaged)"
            : $"Version {VersionText}";
        InfoItems = BuildInfoItems(package);
    }

    public string AppName { get; }

    public string VersionText { get; }

    public string VersionSummary { get; }

    public IReadOnlyList<AppInfoItem> InfoItems { get; }

    private static Package? TryGetCurrentPackage()
    {
        try
        {
            return Package.Current;
        }
        catch (Exception)
        {
            // Package.Current is unavailable when the app runs unpackaged.
            return null;
        }
    }

    private static string ResolveAppName(Package? package)
    {
        if (package is not null && !string.IsNullOrWhiteSpace(package.DisplayName))
        {
            return package.DisplayName;
        }

        return Assembly.GetExecutingAssembly()
                   .GetCustomAttribute<AssemblyProductAttribute>()?.Product
               ?? FallbackAppName;
    }

    private static string ResolveVersionText(Package? package)
    {
        if (package is not null)
        {
            PackageVersion version = package.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }

        return Assembly.GetExecutingAssembly()
                   .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
               ?? UnknownValue;
    }

    private IReadOnlyList<AppInfoItem> BuildInfoItems(Package? package) =>
    [
        new("Version", VersionText),
        new("Package", package?.Id.FamilyName ?? "Not running from an MSIX package"),
        new("Architecture", RuntimeInformation.ProcessArchitecture.ToString()),
        new(".NET runtime", RuntimeInformation.FrameworkDescription),
        new("Windows", RuntimeInformation.OSDescription)
    ];
}
