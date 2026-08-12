namespace EngineeringPerformance.DesktopHost;

/// <summary>
/// Central place to configure the Velopack update feed. Change <see cref="FeedUrl"/> when a real
/// release feed exists (a local file share UNC/path, or a GitHub Releases URL of the form
/// "https://github.com/&lt;owner&gt;/&lt;repo&gt;" work with Velopack's UpdateManager out of the box).
/// </summary>
public static class UpdateSettings
{
    /// <summary>
    /// PLACEHOLDER — replace with the real update feed before shipping auto-update.
    /// Examples:
    ///   Local/network share: @"\\fileserver\EOS-Releases"
    ///   GitHub Releases:      "https://github.com/your-org/EOS"
    /// Until this points at a real feed, update checks fail harmlessly and are swallowed
    /// (see App.xaml.cs's CheckForUpdatesAsync).
    /// </summary>
    public const string FeedUrl = "https://example.invalid/eos-update-feed";
}
