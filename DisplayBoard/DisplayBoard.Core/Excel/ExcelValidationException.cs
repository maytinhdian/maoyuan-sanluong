namespace DisplayBoard.Core.Excel;

/// <summary>
/// The workbook cannot be used at all (file missing, DATA sheet missing, required columns missing...).
/// Row-level problems are not thrown; they are reported as <see cref="ExcelReadResult.Warnings"/>.
/// </summary>
public sealed class ExcelValidationException : Exception
{
    public ExcelValidationException(string message, IReadOnlyList<string>? missingColumns = null, Exception? innerException = null)
        : base(message, innerException)
    {
        MissingColumns = missingColumns ?? [];
    }

    /// <summary>Required columns that were not found in the header row, using their canonical names.</summary>
    public IReadOnlyList<string> MissingColumns { get; }
}
