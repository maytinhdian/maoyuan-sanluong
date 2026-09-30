namespace DisplayBoard.Core.Models;

public enum ExcelValidationError
{
    FileNotFound,
    FileLocked,
    InvalidWorkbook,
    MissingDataSheet,
    MissingColumns,
}

/// <summary>The workbook as a whole cannot be used. Row-level problems are <see cref="RowWarning"/>s instead.</summary>
public sealed class ExcelValidationException : Exception
{
    public ExcelValidationException(
        ExcelValidationError error,
        string message,
        IReadOnlyList<string>? missingColumns = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
        MissingColumns = missingColumns ?? [];
    }

    public ExcelValidationError Error { get; }

    public IReadOnlyList<string> MissingColumns { get; }
}
