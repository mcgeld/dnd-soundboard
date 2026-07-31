using System;
using System.Collections.Generic;
using System.Linq;

namespace SoundBoard.Models;

public enum AssignmentStep
{
    ModeChoice,       // Page 1: "Stem" vs "Preset"
    CategorySelection, // Category wheel (when "Stem" is selected)
    StemSelection,     // Stem wheel (when Category is confirmed)
    PresetSelection    // Preset wheel (when "Preset" is selected)
}

/// <summary>
/// State machine tracking the interactive Channel Assignment Wizard workflow for a channel.
/// </summary>
public class StemAssignmentWizard
{
    public int TargetChannelIndex { get; }
    public AssignmentStep CurrentStep { get; private set; } = AssignmentStep.ModeChoice;

    // Page 1 Options: 0 = "Stem", 1 = "Preset"
    public int SelectedModeIndex { get; private set; } = 0; // 0 = Stem, 1 = Preset

    public List<Category> Categories { get; }
    public int SelectedCategoryIndex { get; private set; } = 0;

    public List<Preset> Presets { get; }
    public int SelectedPresetIndex { get; private set; } = 0;

    public List<Stem> CurrentStems => SelectedCategoryIndex >= 0 && SelectedCategoryIndex < Categories.Count
        ? Categories[SelectedCategoryIndex].Stems
        : new List<Stem>();

    public int SelectedStemIndex { get; private set; } = 0;

    public Category? CurrentCategory => SelectedCategoryIndex >= 0 && SelectedCategoryIndex < Categories.Count
        ? Categories[SelectedCategoryIndex]
        : null;

    public Stem? SelectedStem => SelectedStemIndex >= 0 && SelectedStemIndex < CurrentStems.Count
        ? CurrentStems[SelectedStemIndex]
        : null;

    public Preset? SelectedPreset => SelectedPresetIndex >= 0 && SelectedPresetIndex < Presets.Count
        ? Presets[SelectedPresetIndex]
        : null;

    public StemAssignmentWizard(int targetChannelIndex, List<Category> categories, List<Preset> presets, float initialFaderValue)
    {
        TargetChannelIndex = targetChannelIndex;
        Categories = categories;
        Presets = presets;

        // Default to CategorySelection if no presets exist, else start at ModeChoice
        if (Presets.Count == 0)
        {
            CurrentStep = AssignmentStep.CategorySelection;
        }
        else
        {
            CurrentStep = AssignmentStep.ModeChoice;
        }

        UpdateFaderPosition(initialFaderValue);
    }

    public void UpdateFaderPosition(float faderValue)
    {
        float inverted = 1.0f - Math.Clamp(faderValue, 0.0f, 1.0f);

        if (CurrentStep == AssignmentStep.ModeChoice)
        {
            // 2 options: 0 = Stem, 1 = Preset
            SelectedModeIndex = inverted < 0.5f ? 0 : 1;
        }
        else if (CurrentStep == AssignmentStep.CategorySelection)
        {
            if (Categories.Count == 0) return;
            int idx = (int)Math.Floor(inverted * Categories.Count);
            SelectedCategoryIndex = Math.Clamp(idx, 0, Categories.Count - 1);
        }
        else if (CurrentStep == AssignmentStep.StemSelection)
        {
            var stems = CurrentStems;
            if (stems.Count == 0) return;
            int idx = (int)Math.Floor(inverted * stems.Count);
            SelectedStemIndex = Math.Clamp(idx, 0, stems.Count - 1);
        }
        else if (CurrentStep == AssignmentStep.PresetSelection)
        {
            if (Presets.Count == 0) return;
            int idx = (int)Math.Floor(inverted * Presets.Count);
            SelectedPresetIndex = Math.Clamp(idx, 0, Presets.Count - 1);
        }
    }

    public bool ConfirmNextStep(float currentFaderVal, out Stem? finalStem, out Preset? finalPreset)
    {
        finalStem = null;
        finalPreset = null;

        if (CurrentStep == AssignmentStep.ModeChoice)
        {
            if (SelectedModeIndex == 0)
            {
                // Selected "Stem"
                CurrentStep = AssignmentStep.CategorySelection;
                UpdateFaderPosition(currentFaderVal);
                return false;
            }
            else
            {
                // Selected "Preset"
                if (Presets.Count > 0)
                {
                    CurrentStep = AssignmentStep.PresetSelection;
                    UpdateFaderPosition(currentFaderVal);
                    return false;
                }
                else
                {
                    // Fallback if no presets exist
                    CurrentStep = AssignmentStep.CategorySelection;
                    UpdateFaderPosition(currentFaderVal);
                    return false;
                }
            }
        }
        else if (CurrentStep == AssignmentStep.CategorySelection)
        {
            if (CurrentStems.Count > 0)
            {
                CurrentStep = AssignmentStep.StemSelection;
                UpdateFaderPosition(currentFaderVal);
                return false;
            }
            else
            {
                finalStem = null;
                return true;
            }
        }
        else if (CurrentStep == AssignmentStep.StemSelection)
        {
            finalStem = SelectedStem;
            return true;
        }
        else if (CurrentStep == AssignmentStep.PresetSelection)
        {
            finalPreset = SelectedPreset;
            return true;
        }

        return false;
    }

    public bool GoBackOrCancel(float currentFaderVal)
    {
        if (CurrentStep == AssignmentStep.PresetSelection)
        {
            CurrentStep = AssignmentStep.ModeChoice;
            UpdateFaderPosition(currentFaderVal);
            return false;
        }
        else if (CurrentStep == AssignmentStep.CategorySelection)
        {
            if (Presets.Count > 0)
            {
                CurrentStep = AssignmentStep.ModeChoice;
                UpdateFaderPosition(currentFaderVal);
                return false;
            }
            else
            {
                return true; // Cancel if no presets exist and already on CategorySelection
            }
        }
        else if (CurrentStep == AssignmentStep.StemSelection)
        {
            CurrentStep = AssignmentStep.CategorySelection;
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
