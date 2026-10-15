namespace EOS.Server.Hosting;

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
