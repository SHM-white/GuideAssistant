using System.Reflection;
using System.Runtime.InteropServices;
using Windows.ApplicationModel;

namespace GuideAssistant.Services;

/// <summary>Reads version information from the actual running installation.</summary>
public static class VersionInfo
{
    public static string GetDisplayVersion()
    {
        try
        {
            var version = Package.Current.Id.Version;
            return FormatVersion(version.Major, version.Minor, version.Build, version.Revision);
        }
        catch (InvalidOperationException)
        {
            // Unpackaged Visual Studio / portable build.
        }
        catch (COMException)
        {
            // Process without package identity.
        }

        var assembly = typeof(VersionInfo).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0];
        if (!string.IsNullOrWhiteSpace(informational))
            return $"v{informational}";

        var v = assembly.GetName().Version ?? new Version(0, 0, 0);
        return FormatVersion(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision));
    }

    private static string FormatVersion(int major, int minor, int build, int revision)
        => revision == 0 ? $"v{major}.{minor}.{build}" : $"v{major}.{minor}.{build}.{revision}";
}
