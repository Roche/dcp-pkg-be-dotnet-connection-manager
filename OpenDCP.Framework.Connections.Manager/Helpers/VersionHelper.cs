using NuGet.Versioning;

namespace OpenDCP.Framework.Connections.Manager.Helpers;

public static class VersionHelper
{
    public static NuGetVersion ParseFlexible(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Version string cannot be empty.", nameof(input));

        input = input.Trim();

        // Try NuGet-style first (semver)
        if (NuGetVersion.TryParse(input, out var nugetVersion))
            return nugetVersion;

        // Fall back to System.Version (4-part)
        if (Version.TryParse(input, out var sysVersion))
        {
            // Map System.Version → NuGetVersion (only first 3 parts)
            return new NuGetVersion(
                sysVersion.Major,
                sysVersion.Minor,
                sysVersion.Build < 0 ? 0 : sysVersion.Build
            );
        }

        throw new ArgumentException($"'{input}' is not a valid version string.", nameof(input));
    }
}
