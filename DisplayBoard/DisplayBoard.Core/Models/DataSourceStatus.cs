namespace DisplayBoard.Core.Models;

/// <summary>State of the Excel data source as shown on the main window.</summary>
public enum DataSourceStatus
{
    Ready,
    Reading,
    Updated,
    FileInUse,
    InvalidData,
    FileNotFound,
}
