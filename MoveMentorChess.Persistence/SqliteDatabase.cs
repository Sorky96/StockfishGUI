namespace MoveMentorChess.Persistence;

internal sealed class SqliteDatabase : IDisposable
{
    public SqliteDatabase(string path)
    {
        int result = SqliteNativeMethods.Open(path, out IntPtr handle);
        if (result != SqliteResult.Ok)
        {
            string message = handle == IntPtr.Zero ? "unknown error" : SqliteNativeMethods.GetErrorMessage(handle);
            if (handle != IntPtr.Zero)
            {
                SqliteNativeMethods.Close(handle);
            }

            throw new InvalidOperationException($"Unable to open SQLite database '{path}': {message}");
        }

        Handle = handle;
    }

    public IntPtr Handle { get; }

    public void ExecuteNonQuery(string sql)
    {
        using SqliteStatement statement = Prepare(sql);
        statement.StepUntilDone();
    }

    public void ExecuteNonQuery(string sql, Action<SqliteStatement> bind)
    {
        using SqliteStatement statement = Prepare(sql);
        bind(statement);
        statement.StepUntilDone();
    }

    public bool Exists(string sql, Action<SqliteStatement> bind)
    {
        using SqliteStatement statement = Prepare(sql);
        bind(statement);
        return statement.Step() == SqliteResult.Row;
    }

    public SqliteStatement Prepare(string sql)
    {
        int result = SqliteNativeMethods.Prepare(Handle, sql, -1, out IntPtr statement, IntPtr.Zero);
        SqliteNativeMethods.ThrowIfError(result, Handle, $"prepare SQL '{sql}'");
        return new SqliteStatement(this, statement);
    }

    public void Dispose()
    {
        SqliteNativeMethods.Close(Handle);
    }
}
