using Newtonsoft.Json.Linq;
using Orbital;
using Xunit;

namespace Orbit.Tests;

public class UpdateCheckerTests
{
    [Theory]
    [InlineData("0.4.9", "0.4.8", true)]
    [InlineData("0.5.0", "0.4.8", true)]
    [InlineData("1.0.0", "0.4.8", true)]
    [InlineData("0.4.8", "0.4.8", false)]
    [InlineData("0.4.7", "0.4.8", false)]
    [InlineData("garbage", "0.4.8", false)]
    [InlineData("0.4.9", "garbage", false)]
    public void IsNewerVersion_CompareSemverStrings(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, UpdateChecker.IsNewerVersion(candidate, current));
    }

    [Fact]
    public void FindSetupAssetUrl_ReturnsUrlForMatchingAsset()
    {
        var release = JObject.Parse("""
            {
              "tag_name": "v0.4.9",
              "assets": [
                { "name": "Orbital-0.4.9-Setup.exe", "browser_download_url": "https://example.com/setup" },
                { "name": "Orbital-0.4.9-Portable.zip", "browser_download_url": "https://example.com/portable" }
              ]
            }
            """);

        Assert.Equal("https://example.com/setup", UpdateChecker.FindSetupAssetUrl(release, "0.4.9"));
    }

    [Fact]
    public void FindSetupAssetUrl_ReturnsNullWhenNoMatchingAsset()
    {
        var release = JObject.Parse("""
            {
              "assets": [
                { "name": "Orbital-0.4.9-Portable.zip", "browser_download_url": "https://example.com/p" }
              ]
            }
            """);

        Assert.Null(UpdateChecker.FindSetupAssetUrl(release, "0.4.9"));
    }

    [Fact]
    public void FindSetupAssetUrl_ReturnsNullWithEmptyAssets()
    {
        var release = JObject.Parse("{ \"assets\": [] }");

        Assert.Null(UpdateChecker.FindSetupAssetUrl(release, "0.4.9"));
    }
}
