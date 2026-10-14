using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EOS.Infrastructure.Persistence;

/// <summary>Lets `dotnet ef migrations` build a context at design time. The connection string is never used at runtime.</summary>
public sealed class EosDbContextFactory : IDesignTimeDbContextFactory<EosDbContext>
{
    public EosDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<EosDbContext>().UseSqlite("Data Source=design-time.db").Options);
}

public static class EosDatabase
{
    /// <summary>Creates a context on a SQLite file with WAL enabled (spec §25.5). Use ":memory:"-style names only in tests via <see cref="InMemory"/>.</summary>
    public static EosDbContext Open(string databasePath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var options = new DbContextOptionsBuilder<EosDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        var db = new EosDbContext(options);
        db.Database.Migrate();
        db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
        return db;
    }

    /// <summary>A fresh in-memory database for tests; keep the returned connection open for the context's lifetime.</summary>
    public static (EosDbContext Db, Microsoft.Data.Sqlite.SqliteConnection Connection) InMemory()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new EosDbContext(new DbContextOptionsBuilder<EosDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        return (db, connection);
    }
}
