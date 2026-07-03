using Microsoft.Data.Sqlite;
using Weaver.Core;

namespace Weaver.Core.Tests;

// The sandbox runs untrusted agent SQL — these tests hammer the guards, not the
// happy path. Each test builds a throwaway read-only db so nothing depends on the
// generated telemetry.
public class SqlSandboxTests : IDisposable
{
    readonly string _db;

    public SqlSandboxTests()
    {
        _db = Path.Combine(Path.GetTempPath(), $"weaver-sandbox-{Guid.NewGuid():N}.db");
        using var conn = new SqliteConnection($"Data Source={_db}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE t (a INTEGER, b TEXT);
            INSERT INTO t VALUES (1,'x'),(2,'y'),(3,'z');";
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();          // release the file handle before delete
        try { File.Delete(_db); } catch { }
    }

    SqlResult Run(string sql, int timeoutMs = SqlSandbox.DefaultTimeoutMs, int maxRows = SqlSandbox.DefaultMaxRows)
        => SqlSandbox.Run(sql, _db, timeoutMs, maxRows);

    // --- happy path ------------------------------------------------------

    [Fact]
    public void Select_returns_columns_and_rows()
    {
        var r = Run("SELECT a, b FROM t ORDER BY a");
        Assert.Equal(new[] { "a", "b" }, r.Columns);
        Assert.Equal(3, r.Rows.Count);
        Assert.Equal(1L, r.Rows[0][0]);
        Assert.Equal("x", r.Rows[0][1]);
        Assert.False(r.Truncated);
    }

    [Fact]
    public void With_cte_is_allowed()
    {
        var r = Run("WITH hi AS (SELECT a FROM t WHERE a > 1) SELECT count(*) AS n FROM hi");
        Assert.Equal(2L, r.Rows[0][0]);
    }

    [Fact]
    public void Null_cells_come_back_as_null()
    {
        var r = Run("SELECT NULL AS n");
        Assert.Null(r.Rows[0][0]);
    }

    // --- statement-shape gate -------------------------------------------

    [Theory]
    [InlineData("INSERT INTO t VALUES (9,'q')")]
    [InlineData("UPDATE t SET a = 9")]
    [InlineData("DELETE FROM t")]
    [InlineData("DROP TABLE t")]
    [InlineData("CREATE TABLE u (x)")]
    [InlineData("PRAGMA table_info(t)")]
    [InlineData("ATTACH DATABASE 'boards.db' AS b")]
    [InlineData("EXPLAIN SELECT * FROM t")]
    [InlineData("(SELECT a FROM t)")]
    [InlineData("")]
    [InlineData("   ")]
    public void Non_select_is_rejected(string sql)
        => Assert.Throws<SqlSandboxException>(() => Run(sql));

    [Fact]
    public void Selectfoo_is_not_mistaken_for_select()
        => Assert.Throws<SqlSandboxException>(() => Run("SELECTED FROM t"));

    // --- multi-statement gate -------------------------------------------

    [Theory]
    [InlineData("SELECT 1; DROP TABLE t")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT a FROM t; ATTACH DATABASE 'x' AS y")]
    public void Multiple_statements_are_rejected(string sql)
        => Assert.Throws<SqlSandboxException>(() => Run(sql));

    [Fact]
    public void Single_trailing_semicolon_is_fine()
    {
        var r = Run("SELECT a FROM t ORDER BY a;");
        Assert.Equal(3, r.Rows.Count);
    }

    [Fact]
    public void Semicolon_inside_a_string_literal_is_not_a_statement_break()
    {
        var r = Run("SELECT ';' AS s, ';drop' AS s2");
        Assert.Equal(";", r.Rows[0][0]);
        Assert.Equal(";drop", r.Rows[0][1]);
    }

    [Fact]
    public void Comment_hiding_a_second_statement_is_still_one_statement()
    {
        // the ';' lives in a line comment → blanked → single statement
        var r = Run("SELECT a FROM t -- ; DROP TABLE t\n ORDER BY a");
        Assert.Equal(3, r.Rows.Count);
    }

    // --- read-only enforcement at the engine ----------------------------

    [Fact]
    public void Write_is_blocked_even_if_it_slipped_the_gate()
    {
        // The gate already rejects this, but prove the connection itself is
        // read-only: run the write through a bare Mode=ReadOnly connection.
        using var conn = new SqliteConnection($"Data Source={_db};Mode=ReadOnly");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO t VALUES (9,'q')";
        Assert.Throws<SqliteException>(() => cmd.ExecuteNonQuery());
    }

    // --- row cap ---------------------------------------------------------

    [Fact]
    public void Row_cap_truncates_and_flags()
    {
        var r = Run(
            "WITH RECURSIVE seq(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM seq WHERE n < 100) SELECT n FROM seq",
            maxRows: 10);
        Assert.Equal(10, r.Rows.Count);
        Assert.True(r.Truncated);
    }

    // --- wall-clock cancel ----------------------------------------------

    [Fact]
    public void Runaway_query_is_cancelled()
    {
        // Infinite recursion aggregated into a single count(): produces no rows
        // (so the row cap can't save us), scans forever → must be interrupted.
        var ex = Assert.Throws<SqlSandboxException>(() => Run(
            "WITH RECURSIVE bomb(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM bomb) SELECT count(*) FROM bomb",
            timeoutMs: 500));
        Assert.Contains("cancelled", ex.Message);
    }
}
