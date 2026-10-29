namespace DPIShift.Core.Models;

public sealed class DiscoveredApp
{
    /// <summary>
    /// Stable normalized identifier key (canonical executable path or Squirrel app root + process name).
    /// </summary>
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string ExecutablePath { get; init; }
    public string? Publisher { get; init; }
    public bool IsRunning { get; set; }
    public List<int> RunningProcessIds { get; set; } = [];
    public List<IntPtr> MainWindowHandles { get; set; } = [];
    public bool IsInstalled { get; set; }
    public CompatibilityReport Compatibility { get; set; } = new();
}
