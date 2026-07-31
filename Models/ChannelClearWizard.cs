using SoundBoard.Models;

namespace SoundBoard.Models;

/// <summary>
/// State tracking for the Clear Channel Confirmation Wizard.
/// </summary>
public class ChannelClearWizard
{
    public int TargetChannelIndex { get; }
    public Stem? LoadedStem { get; }

    public ChannelClearWizard(int targetChannelIndex, Stem? loadedStem)
    {
        TargetChannelIndex = targetChannelIndex;
        LoadedStem = loadedStem;
    }
}
