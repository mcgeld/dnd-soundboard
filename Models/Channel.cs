namespace SoundBoard.Models;

/// <summary>
/// Domain model representing one of the 8 physical hardware mixer channels.
/// Default MasterVolume = 0.0f (0% startup safety).
/// Default TrackVolumes = 1.0f (100% track capacity so moving Master slider plays audio immediately).
/// </summary>
public class Channel
{
    public int ChannelIndex { get; }
    public Stem? LoadedStem { get; set; }
    public float MasterVolume { get; set; } = 0.0f;
    public float[] TrackVolumes { get; set; } = new float[3] { 1.0f, 1.0f, 1.0f };
    public bool IsMuted { get; set; } = false;

    public Channel(int channelIndex)
    {
        ChannelIndex = channelIndex;
    }
}
