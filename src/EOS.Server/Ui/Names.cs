namespace EOS.Server.Ui;

public static class Names
{
    private static readonly string[] Prefixes = ["SITC of X-Force ", "SITC of ", "Implementation of "];

    public static string Short(string name)
    {
        foreach (var p in Prefixes)
            if (name.StartsWith(p, StringComparison.OrdinalIgnoreCase) && name.Length > p.Length + 6) return name[p.Length..];
        return name;
    }
}
