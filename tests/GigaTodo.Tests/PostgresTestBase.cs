using Microsoft.EntityFrameworkCore;
using GigaTodo;
using Xunit;

namespace GigaTodo.Tests;

[CollectionDefinition("Postgres", DisableParallelization = true)]
public sealed class PostgresCollection;

public abstract class PostgresTestBase
{
    protected PostgresTestBase()
    {
        var connection = Environment.GetEnvironmentVariable("GIGA_TEST_DATABASE");
        if (string.IsNullOrEmpty(connection)) return;
        using var db = new Database(new DbContextOptionsBuilder<Database>().UseNpgsql(connection).Options);
        db.Database.Migrate();
        // Clear unfinished runs of earlier test cases, never arbitrary application records.
        db.Database.ExecuteSqlRaw("UPDATE runs SET \"State\" = 3 WHERE \"State\" = 0 AND \"TaskId\" IN (SELECT t.\"Id\" FROM tasks t JOIN boards b ON t.\"BoardId\" = b.\"Id\" WHERE b.\"Name\" IN ('Integration test', 'MCP integration'))");
    }
}
