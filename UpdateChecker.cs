using Newtonsoft.Json.Linq;

namespace Orbital;

/// <summary>
/// Pure logic for the GitHub-based auto-update flow: version comparison and
/// release-asset selection. Kept separate from App so it can be unit tested
/// without WPF or network access.
/// </summary>
internal static class UpdateChecker
{
    /// <summary>True when the candidate version is strictly newer than the current one.</summary>
    internal static bool IsNewerVersion(string candidate, string current)
    {
        if (!System.Version.TryParse(candidate, out var c)) return false;
        if (!System.Version.TryParse(current, out var cur)) return false;
        return c > cur;
    }

    /// <summary>
    /// Returns the browser_download_url of the Setup.exe asset for the given
    /// version, or null when the release JSON has no matching asset.
    /// </summary>
    internal static string? FindSetupAssetUrl(JObject release, string version)
    {
        var assets = release["assets"] as JArray;
        if (assets == null) return null;

        string target = $"Orbital-{version}-Setup.exe";
        foreach (var asset in assets)
        {
            if ((string?)asset["name"] == target)
                return (string?)asset["browser_download_url"];
        }
        return null;
    }
}
