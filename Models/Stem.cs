using System.Collections.Generic;

namespace SoundBoard.Models;

/// <summary>
/// A folder containing up to 3 individual Tracks that make up a sub-mix or ambient set (e.g., "Thunderstorm").
/// </summary>
public class Stem
{
    public string Name { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public List<Track> Tracks { get; set; } = new();
}
