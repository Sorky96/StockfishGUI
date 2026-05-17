using System.Runtime.InteropServices;

namespace MoveMentorChess.Persistence;

internal static partial class SqliteNativeMethods
{
    public static int Open(string filename, out IntPtr db) => sqlite3_open16(filename, out db);

    public static int Close(IntPtr db) => sqlite3_close(db);

    public static int Prepare(IntPtr db, string sql, int numBytes, out IntPtr statement, IntPtr tail)
        => sqlite3_prepare16_v2(db, sql, numBytes, out statement, tail);

    public static int Step(IntPtr statement) => sqlite3_step(statement);

    public static int Reset(IntPtr statement) => sqlite3_reset(statement);

    public static int ClearBindings(IntPtr statement) => sqlite3_clear_bindings(statement);

    public static int Finalize(IntPtr statement) => sqlite3_finalize(statement);

    public static int BindText(IntPtr statement, int index, string value, int length, IntPtr destructor)
        => sqlite3_bind_text16(statement, index, value, length, destructor);

    public static int BindNull(IntPtr statement, int index) => sqlite3_bind_null(statement, index);

    public static int BindInt(IntPtr statement, int index, int value) => sqlite3_bind_int(statement, index, value);

    public static int GetColumnInt(IntPtr statement, int columnIndex) => sqlite3_column_int(statement, columnIndex);

    public static int GetColumnType(IntPtr statement, int columnIndex) => sqlite3_column_type(statement, columnIndex);

    public static IntPtr GetColumnText(IntPtr statement, int columnIndex) => sqlite3_column_text16(statement, columnIndex);

    public static void ThrowIfError(int result, IntPtr databaseHandle, string operation)
    {
        if (result == SqliteResult.Ok)
        {
            return;
        }

        throw new InvalidOperationException($"SQLite failed to {operation}: {GetErrorMessage(databaseHandle)}");
    }

    public static string GetErrorMessage(IntPtr databaseHandle)
    {
        IntPtr pointer = sqlite3_errmsg16(databaseHandle);
        return pointer == IntPtr.Zero
            ? "unknown error"
            : Marshal.PtrToStringUni(pointer) ?? "unknown error";
    }

    [DllImport("winsqlite3", CharSet = CharSet.Unicode, EntryPoint = "sqlite3_open16")]
    private static extern int sqlite3_open16(string filename, out IntPtr db);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_close")]
    private static extern int sqlite3_close(IntPtr db);

    [DllImport("winsqlite3", CharSet = CharSet.Unicode, EntryPoint = "sqlite3_prepare16_v2")]
    private static extern int sqlite3_prepare16_v2(IntPtr db, string sql, int numBytes, out IntPtr statement, IntPtr tail);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_step")]
    private static extern int sqlite3_step(IntPtr statement);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_reset")]
    private static extern int sqlite3_reset(IntPtr statement);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_clear_bindings")]
    private static extern int sqlite3_clear_bindings(IntPtr statement);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_finalize")]
    private static extern int sqlite3_finalize(IntPtr statement);

    [DllImport("winsqlite3", CharSet = CharSet.Unicode, EntryPoint = "sqlite3_bind_text16")]
    private static extern int sqlite3_bind_text16(IntPtr statement, int index, string value, int length, IntPtr destructor);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_bind_null")]
    private static extern int sqlite3_bind_null(IntPtr statement, int index);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_bind_int")]
    private static extern int sqlite3_bind_int(IntPtr statement, int index, int value);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_column_int")]
    private static extern int sqlite3_column_int(IntPtr statement, int columnIndex);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_column_type")]
    private static extern int sqlite3_column_type(IntPtr statement, int columnIndex);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_column_text16")]
    private static extern IntPtr sqlite3_column_text16(IntPtr statement, int columnIndex);

    [DllImport("winsqlite3", EntryPoint = "sqlite3_errmsg16")]
    private static extern IntPtr sqlite3_errmsg16(IntPtr db);
}
