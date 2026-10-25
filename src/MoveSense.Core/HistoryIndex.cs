using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MoveSense.Core;

// Journals and original bytes are authoritative. SQLite is a disposable, rebuildable index.
[SupportedOSPlatform("windows")]
public sealed class HistoryIndex : IDisposable
{
    private IntPtr database;
    public HistoryIndex(string path)
    {
        Paths.RejectLinks(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Check(sqlite3_open_v2(path, out database, 2 | 4, IntPtr.Zero));
        Execute("CREATE TABLE IF NOT EXISTS operations (id TEXT PRIMARY KEY, started TEXT NOT NULL, source TEXT NOT NULL, destination TEXT NOT NULL, status TEXT NOT NULL)");
    }
    public void Rebuild(IEnumerable<OperationRecord> records)
    {
        Execute("BEGIN IMMEDIATE");
        try
        {
            Execute("DELETE FROM operations");
            foreach (var r in records)
            {
                Check(sqlite3_prepare_v2(database, "INSERT INTO operations VALUES (?1,?2,?3,?4,?5)", -1, out var statement, IntPtr.Zero));
                try
                {
                    var values = new[] { r.Id.ToString("N"), r.StartedAt.ToString("O"), r.Source, r.Destination, r.Status };
                    for (var i = 0; i < values.Length; i++) Check(sqlite3_bind_text(statement, i + 1, values[i], -1, new IntPtr(-1)));
                    if (sqlite3_step(statement) != 101) throw new IOException("Cannot update the history index.");
                }
                finally { sqlite3_finalize(statement); }
            }
            Execute("COMMIT");
        }
        catch { Execute("ROLLBACK"); throw; }
    }
    private void Execute(string sql) => Check(sqlite3_exec(database, sql, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero));
    private static void Check(int code) { if (code != 0) throw new IOException("SQLite error " + code); }
    public void Dispose() { if (database != IntPtr.Zero) { sqlite3_close(database); database = IntPtr.Zero; } }
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)] string filename, out IntPtr db, int flags, IntPtr vfs);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, IntPtr callback, IntPtr arg, IntPtr error);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_prepare_v2(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_bind_text(IntPtr statement, int index, [MarshalAs(UnmanagedType.LPUTF8Str)] string text, int length, IntPtr destructor);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3", CallingConvention = CallingConvention.Cdecl)] private static extern int sqlite3_close(IntPtr db);
}

