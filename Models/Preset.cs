using System;
using System.Collections.Generic;

namespace SoundBoard.Models;

/// <summary>
/// Named collection of channel snapshots persisted to disk.
/// </summary>
public class Preset
{
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<ChannelSnapshot> ChannelSnapshots { get; set; } = new();
}
