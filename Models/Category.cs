using System.Collections.Generic;

namespace SoundBoard.Models;

/// <summary>
/// A top-level folder grouping related Stems (e.g., "Weather", "Env_Wilderness").
/// </summary>
public class Category
{
    public string Name { get; set; } = string.Empty;
    public List<Stem> Stems { get; set; } = new();
}
