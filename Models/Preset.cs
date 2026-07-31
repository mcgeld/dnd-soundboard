using System.Collections.Generic;

namespace SoundBoard.Models;

/// <summary>
/// A named global snapshot of saved active channel states across the board.
/// </summary>
public class Preset
{
    public string Name { get; set; } = string.Empty;
    public List<ChannelSnapshot> SavedChannels { get; set; } = new();
}
