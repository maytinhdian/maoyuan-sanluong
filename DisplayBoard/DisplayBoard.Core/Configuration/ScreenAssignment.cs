namespace DisplayBoard.Core.Configuration;

/// <summary>Which view is hosted on which monitor.</summary>
public sealed record ScreenAssignment(string MonitorId, string ViewId);
