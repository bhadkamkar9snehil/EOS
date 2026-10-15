using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EOS.Infrastructure.Persistence;

public sealed class EosDbContextFactory : IDesignTimeDbContextFactory<EosDbContext>
{
    public EosDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<EosDbContext>().UseSqlite("Data Source=design-time.db").Options);
}

public static class EosDatabase
{
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
        EOS.Infrastructure.Configuration.ConfigurationService.SeedDefaults(db);
        return db;
    }

    public static (EosDbContext Db, Microsoft.Data.Sqlite.SqliteConnection Connection) InMemory()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new EosDbContext(new DbContextOptionsBuilder<EosDbContext>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();
        EOS.Infrastructure.Configuration.ConfigurationService.SeedDefaults(db);
        return (db, connection);
    }
}
