using System.Runtime.InteropServices;

namespace MoveMentorChess.Persistence;

internal sealed class SqliteStatement : IDisposable
{
    private static readonly IntPtr SqliteTransient = new(-1);

    private readonly SqliteDatabase database;

    public SqliteStatement(SqliteDatabase database, IntPtr handle)
    {
        this.database = database;
        Handle = handle;
    }

    public IntPtr Handle { get; }

    public void BindText(int index, string value)
    {
        int result = SqliteNativeMethods.BindText(Handle, index, value, -1, SqliteTransient);
        SqliteNativeMethods.ThrowIfError(result, database.Handle, $"bind text parameter {index}");
    }

    public void BindNullableText(int index, string? value)
    {
        if (value is null)
        {
            BindNull(index);
            return;
        }

        BindText(index, value);
    }

    public void BindNull(int index)
    {
        int bindNullResult = SqliteNativeMethods.BindNull(Handle, index);
        SqliteNativeMethods.ThrowIfError(bindNullResult, database.Handle, $"bind null parameter {index}");
    }

    public void BindInt(int index, int value)
    {
        int result = SqliteNativeMethods.BindInt(Handle, index, value);
        SqliteNativeMethods.ThrowIfError(result, database.Handle, $"bind int parameter {index}");
    }

    public int Step()
    {
        int result = SqliteNativeMethods.Step(Handle);
        if (result is SqliteResult.Row or SqliteResult.Done)
        {
            return result;
        }

        SqliteNativeMethods.ThrowIfError(result, database.Handle, "execute statement");
        return result;
    }

    public void StepUntilDone()
    {
        int result = Step();
        if (result != SqliteResult.Done)
        {
            throw new InvalidOperationException("SQLite statement returned rows when no rows were expected.");
        }
    }

    public void Reset()
    {
        int resetResult = SqliteNativeMethods.Reset(Handle);
        SqliteNativeMethods.ThrowIfError(resetResult, database.Handle, "reset statement");

        int clearResult = SqliteNativeMethods.ClearBindings(Handle);
        SqliteNativeMethods.ThrowIfError(clearResult, database.Handle, "clear statement bindings");
    }

    public int GetInt(int columnIndex)
    {
        return SqliteNativeMethods.GetColumnInt(Handle, columnIndex);
    }

    public int? GetNullableInt(int columnIndex)
    {
        return SqliteNativeMethods.GetColumnType(Handle, columnIndex) == SqliteResult.Null
            ? null
            : SqliteNativeMethods.GetColumnInt(Handle, columnIndex);
    }

    public string? GetText(int columnIndex)
    {
        if (SqliteNativeMethods.GetColumnType(Handle, columnIndex) == SqliteResult.Null)
        {
            return null;
        }

        IntPtr textPointer = SqliteNativeMethods.GetColumnText(Handle, columnIndex);
        return textPointer == IntPtr.Zero
            ? null
            : Marshal.PtrToStringUni(textPointer);
    }

    public void Dispose()
    {
        SqliteNativeMethods.Finalize(Handle);
    }
}
