namespace SoundBoard.Models;

/// <summary>
/// A lightweight, serializable DTO representing the saved state of a single active channel inside a preset.
/// </summary>
public class ChannelSnapshot
{
    public int RelativeChannelIndex { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public string StemName { get; set; } = string.Empty;
    public float MasterVolume { get; set; } = 1.0f;
    public float[] TrackVolumes { get; set; } = new float[3] { 1.0f, 1.0f, 1.0f };
    public bool IsMuted { get; set; } = false;
}
