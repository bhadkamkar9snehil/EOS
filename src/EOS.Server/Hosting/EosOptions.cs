namespace EOS.Server.Hosting;

/// <summary>
/// Server settings. Environment variables (EOS__DatabasePath ...) or appsettings override the defaults. Production paths live under
/// /var/lib/eos (spec §25.9). Nothing secret is stored here.
/// </summary>
public sealed record EosOptions(string DataDirectory, string DatabasePath, string BackupDirectory, bool DevNoAuth)
{
    public static EosOptions From(IConfiguration c)
    {
        var data = c["EOS:DataDirectory"] ?? Environment.GetEnvironmentVariable("EOS_DATA")
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EOS_V2");
        return new EosOptions(
            data,
            c["EOS:DatabasePath"] ?? Environment.GetEnvironmentVariable("EOS_DB") ?? Path.Combine(data, "eos.db"),
            c["EOS:BackupDirectory"] ?? Path.Combine(data, "backups"),
            string.Equals(c["EOS:DevNoAuth"], "true", StringComparison.OrdinalIgnoreCase) || Environment.GetEnvironmentVariable("EOS_DEV_NOAUTH") == "1");
    }
}
