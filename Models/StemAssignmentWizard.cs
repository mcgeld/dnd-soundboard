using System;
using System.Collections.Generic;
using System.Linq;

namespace SoundBoard.Models;

public enum AssignmentStep
{
    ModeChoice,   // Page 1: "Music" (0), "Stem" (1), "Preset" (2)
    ItemSelection // Page 2: Final Selection wheel for Music folder, Stem folder, or Preset
}

/// <summary>
/// State machine tracking the interactive 2-Step Channel Assignment Wizard workflow.
/// </summary>
public class StemAssignmentWizard
{
    public int TargetChannelIndex { get; }
    public AssignmentStep CurrentStep { get; private set; } = AssignmentStep.ModeChoice;

    // Page 1 Options: 0 = "Music", 1 = "Stem", 2 = "Preset"
    public int SelectedModeIndex { get; private set; } = 0;

    public List<Category> Categories { get; }
    public List<Preset> Presets { get; }

    public List<Stem> MusicFolders => Categories.FirstOrDefault(c => c.Name.Equals("Music", StringComparison.OrdinalIgnoreCase))?.Stems ?? new List<Stem>();
    public List<Stem> StemFolders => Categories.FirstOrDefault(c => c.Name.Equals("Stems", StringComparison.OrdinalIgnoreCase) || c.Name.Equals("Stem", StringComparison.OrdinalIgnoreCase))?.Stems ?? new List<Stem>();

    public int SelectedItemIndex { get; private set; } = 0;

    public Stem? SelectedMusicFolder => SelectedItemIndex >= 0 && SelectedItemIndex < MusicFolders.Count
        ? MusicFolders[SelectedItemIndex]
        : null;

    public Stem? SelectedStemFolder => SelectedItemIndex >= 0 && SelectedItemIndex < StemFolders.Count
        ? StemFolders[SelectedItemIndex]
        : null;

    public Preset? SelectedPreset => SelectedItemIndex >= 0 && SelectedItemIndex < Presets.Count
        ? Presets[SelectedItemIndex]
        : null;

    public StemAssignmentWizard(int targetChannelIndex, List<Category> categories, List<Preset> presets, float initialFaderValue)
    {
        TargetChannelIndex = targetChannelIndex;
        Categories = categories;
        Presets = presets;

        CurrentStep = AssignmentStep.ModeChoice;
        UpdateFaderPosition(initialFaderValue);
    }

    public void UpdateFaderPosition(float faderValue)
    {
        float inverted = 1.0f - Math.Clamp(faderValue, 0.0f, 1.0f);

        if (CurrentStep == AssignmentStep.ModeChoice)
        {
            // 3 options: 0 = Music, 1 = Stem, 2 = Preset
            int idx = (int)Math.Floor(inverted * 3.0f);
            SelectedModeIndex = Math.Clamp(idx, 0, 2);
        }
        else if (CurrentStep == AssignmentStep.ItemSelection)
        {
            int count = 0;
            if (SelectedModeIndex == 0) count = MusicFolders.Count;
            else if (SelectedModeIndex == 1) count = StemFolders.Count;
            else if (SelectedModeIndex == 2) count = Presets.Count;

            if (count == 0)
            {
                SelectedItemIndex = 0;
                return;
            }

            int idx = (int)Math.Floor(inverted * count);
            SelectedItemIndex = Math.Clamp(idx, 0, count - 1);
        }
    }

    public bool ConfirmNextStep(float currentFaderVal, out Stem? finalStem, out Preset? finalPreset)
    {
        finalStem = null;
        finalPreset = null;

        if (CurrentStep == AssignmentStep.ModeChoice)
        {
            SelectedItemIndex = 0;
            CurrentStep = AssignmentStep.ItemSelection;
            UpdateFaderPosition(currentFaderVal);
            return false;
        }
        else if (CurrentStep == AssignmentStep.ItemSelection)
        {
            if (SelectedModeIndex == 0)
            {
                finalStem = SelectedMusicFolder;
                return true;
            }
            else if (SelectedModeIndex == 1)
            {
                finalStem = SelectedStemFolder;
                return true;
            }
            else if (SelectedModeIndex == 2)
            {
                finalPreset = SelectedPreset;
                return true;
            }
        }

        return false;
    }

    public bool GoBackOrCancel(float currentFaderVal)
    {
        if (CurrentStep == AssignmentStep.ItemSelection)
        {
            CurrentStep = AssignmentStep.ModeChoice;
            UpdateFaderPosition(currentFaderVal);
            return false;
        }
        else if (CurrentStep == AssignmentStep.ModeChoice)
        {
            return true; // Cancel wizard
        }

        return true;
    }
}
