namespace SoundBoard.Services;

/// <summary>
/// DTO for persisting last known hardware positions between application runs.
/// </summary>
public class HardwareStateDto
{
    public float[] FaderVolumes { get; set; } = new float[8];
    public float[][] KnobVolumes { get; set; } = new float[8][];
}
