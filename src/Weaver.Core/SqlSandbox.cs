using System.Text;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Weaver.Core;

/// <summary>
/// Runs an agent-authored SQL query against the read-only telemetry DB and
/// returns a plain {columns, rows} table. The query is untrusted (the agent
/// wrote it), so this is a hardened sandbox, not a convenience wrapper:
///
///   - a dedicated Mode=ReadOnly connection + PRAGMA query_only → no writes/DDL
///   - single-statement gate (one SELECT/WITH, no stray ';')    → no ATTACH,
///     PRAGMA, or multi-statement chaining
///   - wall-clock cancel via sqlite3_interrupt (SqliteCommand.Cancel) → no
///     runaway scan / recursive-CTE bomb
///   - a hard row cap                                           → bounded memory
///
/// Extension loading stays off (Microsoft.Data.Sqlite's default). This
/// enumerates facts only; it interprets nothing (analysis-architecture.md).
/// </summary>
public static class SqlSandbox
{
    public const int DefaultTimeoutMs = 2000;
    public const int DefaultMaxRows = 5000;

    public static SqlResult Run(string sql, string? dbPath = null,
        int timeoutMs = DefaultTimeoutMs, int maxRows = DefaultMaxRows)
    {
        var trimmed = (sql ?? "").Trim();
        if (trimmed.Length == 0) throw new SqlSandboxException("empty query");
        Gate(trimmed);

        var path = dbPath ?? WeaverDatabase.Locate();
        using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        conn.Open();
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA query_only=ON;";
            pragma.ExecuteNonQuery();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = trimmed;

        // CommandTimeout only bounds lock-waits and SqliteCommand.Cancel() is a
        // no-op — neither stops a runaway scan. So enforce the wall clock via the
        // native progress handler: it runs on the query thread every ~1000 VM ops
        // and interrupts (returns non-zero) once we're past the deadline. No
        // dependence on a timer thread getting scheduled off a pinned core.
        var deadline = Environment.TickCount64 + timeoutMs;
        var timedOut = false;
        delegate_progress onProgress = _ =>
        {
            if (Environment.TickCount64 < deadline) return 0;
            timedOut = true;
            return 1;   // → SQLITE_INTERRUPT
        };
        raw.sqlite3_progress_handler(conn.Handle!, 1000, onProgress, null);

        try
        {
            using var reader = cmd.ExecuteReader();
            var columns = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++) columns[i] = reader.GetName(i);

            var rows = new List<IReadOnlyList<object?>>();
            var truncated = false;
            while (reader.Read())
            {
                if (rows.Count >= maxRows) { truncated = true; break; }
                var cells = new object?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                    cells[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(cells);
            }
            return new SqlResult(columns, rows, truncated);
        }
        catch (SqliteException) when (timedOut)
        {
            throw new SqlSandboxException($"query exceeded {timeoutMs}ms and was cancelled");
        }
        catch (SqliteException ex)
        {
            throw new SqlSandboxException(ex.Message);
        }
        finally
        {
            raw.sqlite3_progress_handler(conn.Handle!, 0, null, null);
        }
    }

    // Exactly one statement, and it must start with SELECT or WITH. We inspect a
    // "code-only" copy (string/identifier literals and comments blanked) so a ';'
    // or keyword *inside* a literal can't fool the scan — but we execute the
    // original text.
    static void Gate(string sql)
    {
        var code = StripLiteralsAndComments(sql);
        var head = code.TrimStart();
        if (!(StartsWithWord(head, "SELECT") || StartsWithWord(head, "WITH")))
            throw new SqlSandboxException("only a single SELECT (or WITH … SELECT) query is allowed");

        var body = code.TrimEnd();
        if (body.EndsWith(';')) body = body[..^1];   // one optional trailing ';'
        if (body.Contains(';'))
            throw new SqlSandboxException("only one statement is allowed (no ';')");
    }

    static bool StartsWithWord(string s, string word) =>
        s.Length >= word.Length
        && s.AsSpan(0, word.Length).Equals(word, StringComparison.OrdinalIgnoreCase)
        && (s.Length == word.Length || (!char.IsLetterOrDigit(s[word.Length]) && s[word.Length] != '_'));

    // Blank the contents of comments and quoted spans ('…' "…" [-…-] `…`) so the
    // structural scan sees only real SQL syntax. Doubled quotes ('' "") are the
    // SQL escape and stay inside the span.
    static string StripLiteralsAndComments(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')      // -- line comment
            {
                while (i < sql.Length && sql[i] != '\n') { sb.Append(' '); i++; }
                if (i < sql.Length) sb.Append('\n');
                continue;
            }
            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')      // /* block comment */
            {
                sb.Append("  "); i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) { sb.Append(' '); i++; }
                if (i + 1 < sql.Length) { sb.Append("  "); i++; }         // consume the closing '*/'
                continue;
            }
            if (c is '\'' or '"' or '`' or '[')                          // quoted span
            {
                var close = c == '[' ? ']' : c;
                sb.Append(' '); i++;
                while (i < sql.Length)
                {
                    if (sql[i] == close)
                    {
                        if ((close == '\'' || close == '"') && i + 1 < sql.Length && sql[i + 1] == close)
                        { sb.Append("  "); i += 2; continue; }           // doubled-quote escape
                        sb.Append(' ');
                        break;                                            // closing quote
                    }
                    sb.Append(' '); i++;
                }
                continue;
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}

public record SqlResult(
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<object?>> Rows,
    bool Truncated);

public class SqlSandboxException : Exception
{
    public SqlSandboxException(string message) : base(message) { }
}
