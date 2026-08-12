namespace EngineeringPerformance.DesktopHost;

/// <summary>
/// Central place to configure the Velopack update feed. Change <see cref="FeedUrl"/> when a real
/// release feed exists (a local file share UNC/path, or a GitHub Releases URL of the form
/// "https://github.com/&lt;owner&gt;/&lt;repo&gt;" work with Velopack's UpdateManager out of the box).
/// </summary>
public static class UpdateSettings
{
    /// <summary>
    /// Points at this repo's GitHub Releases, published by .github/workflows/release.yml on every
    /// "vX.Y.Z" tag push. Until at least one release exists there, update checks fail harmlessly
    /// and are swallowed (see App.xaml.cs's CheckForUpdatesAsync).
    /// </summary>
    public const string FeedUrl = "https://github.com/bhadkamkar9snehil/EOS";
}
