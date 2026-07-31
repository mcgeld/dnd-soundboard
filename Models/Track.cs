namespace SoundBoard.Models;

/// <summary>
/// Represents a single audio track file on disk.
/// </summary>
public class Track
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}
