namespace DisplayBoard.Core.Interfaces;

/// <summary>A dashboard view that can be hosted on any screen or in the preview.</summary>
public interface IDisplayViewDefinition
{
    string Id { get; }
    string Name { get; }
}
