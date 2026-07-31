using System;
using System.Collections.Generic;
using System.Linq;

namespace SoundBoard.Models;

public enum AssignmentStep
{
    CategorySelection,
    StemSelection
}

/// <summary>
/// State machine tracking the interactive Stem Assignment Wizard workflow for a channel.
/// </summary>
public class StemAssignmentWizard
{
    public int TargetChannelIndex { get; }
    public AssignmentStep CurrentStep { get; private set; } = AssignmentStep.CategorySelection;

    public List<Category> Categories { get; }
    public int SelectedCategoryIndex { get; private set; } = 0;

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

    public StemAssignmentWizard(int targetChannelIndex, List<Category> categories, float initialFaderValue)
    {
        TargetChannelIndex = targetChannelIndex;
        Categories = categories;
        UpdateFaderPosition(initialFaderValue);
    }

    public void UpdateFaderPosition(float faderValue)
    {
        float inverted = 1.0f - Math.Clamp(faderValue, 0.0f, 1.0f);

        if (CurrentStep == AssignmentStep.CategorySelection)
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
    }

    public bool ConfirmNextStep(float currentFaderVal, out Stem? finalStem)
    {
        finalStem = null;

        if (CurrentStep == AssignmentStep.CategorySelection)
        {
            if (CurrentStems.Count > 0)
            {
                CurrentStep = AssignmentStep.StemSelection;
                // Immediately evaluate SelectedStemIndex from current physical slider position
                UpdateFaderPosition(currentFaderVal);
                return false; // Wizard continues to Stem selection
            }
            else
            {
                finalStem = null;
                return true; // Wizard complete (empty category)
            }
        }
        else if (CurrentStep == AssignmentStep.StemSelection)
        {
            finalStem = SelectedStem;
            return true; // Wizard complete
        }

        return false;
    }

    public bool GoBackOrCancel(float currentFaderVal)
    {
        if (CurrentStep == AssignmentStep.StemSelection)
        {
            CurrentStep = AssignmentStep.CategorySelection;
            // Immediately evaluate SelectedCategoryIndex from current physical slider position
            UpdateFaderPosition(currentFaderVal);
            return false; // Returned to category selection
        }
        else
        {
            return true; // Cancelled wizard
        }
    }
}
