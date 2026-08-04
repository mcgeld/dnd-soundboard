namespace SoundBoard.Services;

/// <summary>
/// DTO for persisting last known hardware positions and preferred target monitor index between application runs.
/// </summary>
public class HardwareStateDto
{
    public float[] FaderVolumes { get; set; } = new float[8];
    public float[][] KnobVolumes { get; set; } = new float[8][];
    public int TargetMonitorIndex { get; set; } = 0;
    public float GlobalMasterVolume { get; set; } = 1.0f;
}
