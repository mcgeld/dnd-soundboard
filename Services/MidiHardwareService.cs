using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Multimedia;
using SoundBoard.Models;

namespace SoundBoard.Services;

/// <summary>
/// Hardware MIDI listener, volume sync, LED feedback controller, and interactive Stem & Preset Assignment / Save Manager.
/// </summary>
public class MidiHardwareService : IDisposable
{
    private const string StateFilePath = "./hardware_state.json";

    private InputDevice? _inputDevice;
    private readonly List<OutputDevice> _outputDevices = new();
    private readonly AudioEngine _audioEngine;
    private readonly HudService? _hudService;
    private readonly PresetStorageService _presetStorageService;
    private List<Category> _categories = new();
    private readonly Timer _flashTimer;
    private bool _flashPhase = false;

    private FourBitNumber _hardwareMidiChannel = (FourBitNumber)0;

    // Interactive Channel Assignment Wizard State
    private StemAssignmentWizard? _activeWizard;
    private readonly Timer?[] _longPressTimers = new Timer?[8];
    private readonly bool[] _isOperationHeld = new bool[8];
    private readonly bool[] _wasLongPressHandled = new bool[8];

    // Interactive Clear Channel Wizard State
    private ChannelClearWizard? _activeClearWizard;
    private readonly Timer?[] _muteLongPressTimers = new Timer?[8];
    private readonly bool[] _isMuteHeld = new bool[8];
    private readonly bool[] _wasMuteLongPressHandled = new bool[8];

    // Interactive Preset Creation / Save State (Note 107)
    private bool _isPresetSaveActive = false;
    private bool _isPresetNamingStep = false;
    private readonly bool[] _presetSelectedChannels = new bool[8];

    // Hardware Control Dirty Flags & Motion Soft-Catch State
    private readonly bool[] _isFaderDirty = new bool[8];
    private readonly bool[] _isFaderMoving = new bool[8];
    private readonly Timer?[] _faderMotionPauseTimers = new Timer?[8];

    private readonly bool[][] _isKnobDirty = new bool[8][];
    private readonly bool[][] _isKnobMoving = new bool[8][];
    private readonly Timer?[][] _knobMotionPauseTimers = new Timer?[8][];

    // Last known hardware positions
    private readonly float[] _lastFaderVol = new float[8];
    private readonly float[][] _lastKnobVol = new float[8][];
    private readonly bool[] _hasHardwarePosition = new bool[8];

    // LED Velocity / Color Constants
    public const byte LedOff = 0;
    public const byte LedRedFull = 11;    // Velocity 11 = Clean Red
    public const byte LedGreenFull = 60;  // Velocity 60 = Clean Green
    public const byte LedAmberFull = 62;  // Velocity 62 = Clean Amber / Yellow

    public MidiHardwareService(AudioEngine audioEngine, HudService? hudService = null, PresetStorageService? presetStorageService = null)
    {
        _audioEngine = audioEngine;
        _hudService = hudService;
        _presetStorageService = presetStorageService ?? new PresetStorageService();
        _flashTimer = new Timer(OnFlashTimerTick, null, 400, 400);

        if (_hudService != null)
        {
            _hudService.OnPresetSaveSubmitted += OnPresetNameSubmitted;
            _hudService.OnPresetSaveCancelled += CancelPresetSave;
        }

        for (int i = 0; i < 8; i++)
        {
            _lastFaderVol[i] = 0.0f;
            _lastKnobVol[i] = new float[3] { 1.0f, 1.0f, 1.0f };

            _isKnobDirty[i] = new bool[3];
            _isKnobMoving[i] = new bool[3];
            _knobMotionPauseTimers[i] = new Timer?[3];
        }

        LoadHardwareState();
    }

    public void SetCategories(List<Category> categories)
    {
        _categories = categories;
    }

    public bool Start(string deviceNamePattern = "Launch Control")
    {
        try
        {
            var inDevices = InputDevice.GetAll().ToList();
            var outDevices = OutputDevice.GetAll().ToList();

            if (inDevices.Count == 0)
            {
                Console.WriteLine("[MIDI] No MIDI input devices found on system.");
                return false;
            }

            _inputDevice = inDevices.FirstOrDefault(d => d.Name.Contains(deviceNamePattern, StringComparison.OrdinalIgnoreCase))
                ?? inDevices.FirstOrDefault();

            if (_inputDevice != null)
            {
                Console.WriteLine($"[MIDI] Connected Input: '{_inputDevice.Name}'");
                _inputDevice.EventReceived += OnMidiEventReceived;
                _inputDevice.StartEventsListening();
            }

            foreach (var outDev in outDevices)
            {
                if (outDev.Name.Contains(deviceNamePattern, StringComparison.OrdinalIgnoreCase) ||
                    outDev.Name.Contains("Launch", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        Console.WriteLine($"[MIDI] Connected Output Port for LED: '{outDev.Name}'");
                        _outputDevices.Add(outDev);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[MIDI Warning] Could not open output port '{outDev.Name}': {ex.Message}");
                    }
                }
            }

            if (_outputDevices.Count == 0 && outDevices.Count > 0)
            {
                _outputDevices.Add(outDevices[0]);
                Console.WriteLine($"[MIDI Fallback] Connected Output Port: '{outDevices[0].Name}'");
            }

            RequestHardwareStateDump();
            UpdateAllLeds();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MIDI Error] Initialization failed: {ex.Message}");
            return false;
        }
    }

    private void RequestHardwareStateDump()
    {
        if (_outputDevices.Count == 0) return;

        try
        {
            Console.WriteLine("[MIDI] Requesting physical control position dump from hardware...");
            var dumpQuery = new byte[] { 0xF0, 0x00, 0x20, 0x29, 0x02, 0x11, 0x77, 0xF7 };
            var idQuery = new byte[] { 0xF0, 0x7E, 0x7F, 0x06, 0x01, 0xF7 };

            foreach (var outDev in _outputDevices)
            {
                outDev.SendEvent(new NormalSysExEvent(dumpQuery));
                outDev.SendEvent(new NormalSysExEvent(idQuery));
            }
        }
        catch { }
    }

    public void SyncChannelHardwareVolume(int chIdx)
    {
        if (chIdx < 0 || chIdx >= 8) return;

        _isFaderDirty[chIdx] = false;
        _isFaderMoving[chIdx] = false;
        for (int t = 0; t < 3; t++)
        {
            _isKnobDirty[chIdx][t] = false;
            _isKnobMoving[chIdx][t] = false;
        }

        float faderVal = _lastFaderVol[chIdx];
        float k0 = _lastKnobVol[chIdx][0];
        float k1 = _lastKnobVol[chIdx][1];
        float k2 = _lastKnobVol[chIdx][2];

        _audioEngine.SetMasterVolume(chIdx, faderVal, immediate: true);
        _audioEngine.SetTrackVolume(chIdx, 0, k0, immediate: true);
        _audioEngine.SetTrackVolume(chIdx, 1, k1, immediate: true);
        _audioEngine.SetTrackVolume(chIdx, 2, k2, immediate: true);

        Console.WriteLine($"[MIDI] Channel {chIdx} synced volume state: Master={faderVal * 100:F0}%, Dials=[{k0 * 100:F0}%, {k1 * 100:F0}%, {k2 * 100:F0}%]");
    }

    private void OnMidiEventReceived(object? sender, MidiEventReceivedEventArgs e)
    {
        switch (e.Event)
        {
            case ControlChangeEvent cc:
                _hardwareMidiChannel = cc.Channel;
                HandleControlChange((int)cc.ControlNumber, (int)cc.ControlValue);
                break;

            case NoteOnEvent noteOn:
                _hardwareMidiChannel = noteOn.Channel;
                HandleNote((int)noteOn.NoteNumber, noteOn.Velocity > 0);
                break;

            case NoteOffEvent noteOff:
                _hardwareMidiChannel = noteOff.Channel;
                HandleNote((int)noteOff.NoteNumber, false);
                break;
        }
    }

    private bool CancelActiveWizardsIfOtherControlTouched(int sourceChannelIndex, bool isTargetChannelControl)
    {
        bool cancelledAny = false;

        if (_activeWizard != null && (_activeWizard.TargetChannelIndex != sourceChannelIndex || !isTargetChannelControl))
        {
            Console.WriteLine($"[Wizard] Control touched outside active assignment channel -> Cancelling wizard for Channel {_activeWizard.TargetChannelIndex + 1}");
            _activeWizard = null;
            _hudService?.CloseAssignmentWizard();
            cancelledAny = true;
        }

        if (_activeClearWizard != null && (_activeClearWizard.TargetChannelIndex != sourceChannelIndex || !isTargetChannelControl))
        {
            Console.WriteLine($"[Clear Wizard] Control touched outside active clear channel -> Cancelling clear confirmation for Channel {_activeClearWizard.TargetChannelIndex + 1}");
            _activeClearWizard = null;
            _hudService?.CloseClearConfirmation();
            cancelledAny = true;
        }

        if (_isPresetSaveActive)
        {
            CancelPresetSave();
            cancelledAny = true;
        }

        if (cancelledAny)
        {
            UpdateAllLeds();
        }

        return cancelledAny;
    }

    private void HandleControlChange(int cc, int value)
    {
        float floatVal = value / 127.0f;

        // Faders 1..8 (CC 77..84)
        if (cc >= 77 && cc <= 84)
        {
            int chIdx = cc - 77;
            _lastFaderVol[chIdx] = floatVal;
            _hasHardwarePosition[chIdx] = true;

            if (_activeWizard != null)
            {
                if (_activeWizard.TargetChannelIndex == chIdx)
                {
                    _activeWizard.UpdateFaderPosition(floatVal);
                    _hudService?.UpdateAssignmentWizard(_activeWizard);
                    SaveHardwareState();
                    return;
                }
                else
                {
                    CancelActiveWizardsIfOtherControlTouched(chIdx, isTargetChannelControl: false);
                }
            }

            if (_activeClearWizard != null || _isPresetSaveActive)
            {
                CancelActiveWizardsIfOtherControlTouched(chIdx, isTargetChannelControl: false);
            }

            var ch = _audioEngine.Channels[chIdx];

            // Dirty Fader Soft-Catch Handling
            if (_isFaderDirty[chIdx])
            {
                if (!_isFaderMoving[chIdx])
                {
                    _isFaderMoving[chIdx] = true;
                    UpdateChannelLeds(chIdx);
                }

                _hudService?.ShowChannelOverview(
                    chIdx,
                    ch,
                    activeControl: "fader",
                    isFaderDirty: _isFaderDirty[chIdx],
                    isFaderMoving: _isFaderMoving[chIdx],
                    isKnobDirty: _isKnobDirty[chIdx],
                    isKnobMoving: _isKnobMoving[chIdx],
                    lastFaderVol: _lastFaderVol,
                    lastKnobVol: _lastKnobVol,
                    dismissDelayMs: 1000
                );

                _faderMotionPauseTimers[chIdx]?.Dispose();
                _faderMotionPauseTimers[chIdx] = new Timer(_ =>
                {
                    float startVol = _audioEngine.Channels[chIdx].MasterVolume;
                    float targetVol = _lastFaderVol[chIdx];

                    Console.WriteLine($"[Soft-Catch] Fader {chIdx + 1} motion paused >= 1s -> Fading Master Vol from {startVol * 100:F0}% to physical fader {targetVol * 100:F0}% over 1.0s");

                    _audioEngine.FadeMasterVolume(chIdx, startVol, targetVol, 1000, onComplete: () =>
                    {
                        _isFaderMoving[chIdx] = false;
                        _isFaderDirty[chIdx] = false;
                        UpdateChannelLeds(chIdx);
                        Console.WriteLine($"[Soft-Catch] Fader {chIdx + 1} soft-catch fade complete! Control turns Solid Green.");
                    });
                }, null, 1000, Timeout.Infinite);

                SaveHardwareState();
                return;
            }

            _audioEngine.SetMasterVolume(chIdx, floatVal, immediate: true);
            _hudService?.ShowChannelOverview(
                chIdx,
                ch,
                activeControl: "fader",
                isFaderDirty: _isFaderDirty[chIdx],
                isFaderMoving: _isFaderMoving[chIdx],
                isKnobDirty: _isKnobDirty[chIdx],
                isKnobMoving: _isKnobMoving[chIdx],
                lastFaderVol: _lastFaderVol,
                lastKnobVol: _lastKnobVol,
                dismissDelayMs: 1000
            );

            Console.WriteLine($"[MIDI] Channel {chIdx} ({ch.LoadedStem?.Name ?? "Unassigned"}) Fader -> Master Vol {ch.MasterVolume:F2}");
            SaveHardwareState();
            return;
        }

        // Knob Row 3 (Bottom Row, CC 49..56) -> Track 0
        if (cc >= 49 && cc <= 56)
        {
            int chIdx = cc - 49;
            _lastKnobVol[chIdx][0] = floatVal;
            _hasHardwarePosition[chIdx] = true;

            CancelActiveWizardsIfOtherControlTouched(chIdx, isTargetChannelControl: false);

            var ch = _audioEngine.Channels[chIdx];

            if (_isKnobDirty[chIdx][0])
            {
                if (!_isKnobMoving[chIdx][0])
                {
                    _isKnobMoving[chIdx][0] = true;
                    UpdateChannelLeds(chIdx);
                }

                _hudService?.ShowChannelOverview(
                    chIdx,
                    ch,
                    activeControl: "knob_0",
                    isFaderDirty: _isFaderDirty[chIdx],
                    isFaderMoving: _isFaderMoving[chIdx],
                    isKnobDirty: _isKnobDirty[chIdx],
                    isKnobMoving: _isKnobMoving[chIdx],
                    lastFaderVol: _lastFaderVol,
                    lastKnobVol: _lastKnobVol,
                    dismissDelayMs: 1000
                );

                _knobMotionPauseTimers[chIdx][0]?.Dispose();
                _knobMotionPauseTimers[chIdx][0] = new Timer(_ =>
                {
                    float startVol = _audioEngine.Channels[chIdx].TrackVolumes[0];
                    float targetVol = _lastKnobVol[chIdx][0];

                    Console.WriteLine($"[Soft-Catch] Knob 3 (Ch {chIdx + 1}) motion paused >= 1s -> Fading Track 0 Vol from {startVol * 100:F0}% to physical knob {targetVol * 100:F0}% over 1.0s");

                    _audioEngine.FadeTrackVolume(chIdx, 0, startVol, targetVol, 1000, onComplete: () =>
                    {
                        _isKnobMoving[chIdx][0] = false;
                        _isKnobDirty[chIdx][0] = false;
                        UpdateChannelLeds(chIdx);
                        Console.WriteLine($"[Soft-Catch] Knob 3 (Ch {chIdx + 1}) soft-catch fade complete! Control turns Solid Green.");
                    });
                }, null, 1000, Timeout.Infinite);

                SaveHardwareState();
                return;
            }

            _audioEngine.SetTrackVolume(chIdx, 0, floatVal, immediate: true);

            _hudService?.ShowChannelOverview(
                chIdx,
                ch,
                activeControl: "knob_0",
                isFaderDirty: _isFaderDirty[chIdx],
                isFaderMoving: _isFaderMoving[chIdx],
                isKnobDirty: _isKnobDirty[chIdx],
                isKnobMoving: _isKnobMoving[chIdx],
                lastFaderVol: _lastFaderVol,
                lastKnobVol: _lastKnobVol,
                dismissDelayMs: 1000
            );

            SaveHardwareState();
            return;
        }

        // Knob Row 2 (Middle Row, CC 29..36) -> Track 1
        if (cc >= 29 && cc <= 36)
        {
            int chIdx = cc - 29;
            _lastKnobVol[chIdx][1] = floatVal;
            _hasHardwarePosition[chIdx] = true;

            CancelActiveWizardsIfOtherControlTouched(chIdx, isTargetChannelControl: false);

            var ch = _audioEngine.Channels[chIdx];

            if (_isKnobDirty[chIdx][1])
            {
                if (!_isKnobMoving[chIdx][1])
                {
                    _isKnobMoving[chIdx][1] = true;
                    UpdateChannelLeds(chIdx);
                }

                _hudService?.ShowChannelOverview(
                    chIdx,
                    ch,
                    activeControl: "knob_1",
                    isFaderDirty: _isFaderDirty[chIdx],
                    isFaderMoving: _isFaderMoving[chIdx],
                    isKnobDirty: _isKnobDirty[chIdx],
                    isKnobMoving: _isKnobMoving[chIdx],
                    lastFaderVol: _lastFaderVol,
                    lastKnobVol: _lastKnobVol,
                    dismissDelayMs: 1000
                );

                _knobMotionPauseTimers[chIdx][1]?.Dispose();
                _knobMotionPauseTimers[chIdx][1] = new Timer(_ =>
                {
                    float startVol = _audioEngine.Channels[chIdx].TrackVolumes[1];
                    float targetVol = _lastKnobVol[chIdx][1];

                    Console.WriteLine($"[Soft-Catch] Knob 2 (Ch {chIdx + 1}) motion paused >= 1s -> Fading Track 1 Vol from {startVol * 100:F0}% to physical knob {targetVol * 100:F0}% over 1.0s");

                    _audioEngine.FadeTrackVolume(chIdx, 1, startVol, targetVol, 1000, onComplete: () =>
                    {
                        _isKnobMoving[chIdx][1] = false;
                        _isKnobDirty[chIdx][1] = false;
                        UpdateChannelLeds(chIdx);
                        Console.WriteLine($"[Soft-Catch] Knob 2 (Ch {chIdx + 1}) soft-catch fade complete! Control turns Solid Green.");
                    });
                }, null, 1000, Timeout.Infinite);

                SaveHardwareState();
                return;
            }

            _audioEngine.SetTrackVolume(chIdx, 1, floatVal, immediate: true);

            _hudService?.ShowChannelOverview(
                chIdx,
                ch,
                activeControl: "knob_1",
                isFaderDirty: _isFaderDirty[chIdx],
                isFaderMoving: _isFaderMoving[chIdx],
                isKnobDirty: _isKnobDirty[chIdx],
                isKnobMoving: _isKnobMoving[chIdx],
                lastFaderVol: _lastFaderVol,
                lastKnobVol: _lastKnobVol,
                dismissDelayMs: 1000
            );

            SaveHardwareState();
            return;
        }

        // Knob Row 1 (Top Row, CC 13..20) -> Track 2
        if (cc >= 13 && cc <= 20)
        {
            int chIdx = cc - 13;
            _lastKnobVol[chIdx][2] = floatVal;
            _hasHardwarePosition[chIdx] = true;

            CancelActiveWizardsIfOtherControlTouched(chIdx, isTargetChannelControl: false);

            var ch = _audioEngine.Channels[chIdx];

            if (_isKnobDirty[chIdx][2])
            {
                if (!_isKnobMoving[chIdx][2])
                {
                    _isKnobMoving[chIdx][2] = true;
                    UpdateChannelLeds(chIdx);
                }

                _hudService?.ShowChannelOverview(
                    chIdx,
                    ch,
                    activeControl: "knob_2",
                    isFaderDirty: _isFaderDirty[chIdx],
                    isFaderMoving: _isFaderMoving[chIdx],
                    isKnobDirty: _isKnobDirty[chIdx],
                    isKnobMoving: _isKnobMoving[chIdx],
                    lastFaderVol: _lastFaderVol,
                    lastKnobVol: _lastKnobVol,
                    dismissDelayMs: 1000
                );

                _knobMotionPauseTimers[chIdx][2]?.Dispose();
                _knobMotionPauseTimers[chIdx][2] = new Timer(_ =>
                {
                    float startVol = _audioEngine.Channels[chIdx].TrackVolumes[2];
                    float targetVol = _lastKnobVol[chIdx][2];

                    Console.WriteLine($"[Soft-Catch] Knob 1 (Ch {chIdx + 1}) motion paused >= 1s -> Fading Track 2 Vol from {startVol * 100:F0}% to physical knob {targetVol * 100:F0}% over 1.0s");

                    _audioEngine.FadeTrackVolume(chIdx, 2, startVol, targetVol, 1000, onComplete: () =>
                    {
                        _isKnobMoving[chIdx][2] = false;
                        _isKnobDirty[chIdx][2] = false;
                        UpdateChannelLeds(chIdx);
                        Console.WriteLine($"[Soft-Catch] Knob 1 (Ch {chIdx + 1}) soft-catch fade complete! Control turns Solid Green.");
                    });
                }, null, 1000, Timeout.Infinite);

                SaveHardwareState();
                return;
            }

            _audioEngine.SetTrackVolume(chIdx, 2, floatVal, immediate: true);

            _hudService?.ShowChannelOverview(
                chIdx,
                ch,
                activeControl: "knob_2",
                isFaderDirty: _isFaderDirty[chIdx],
                isFaderMoving: _isFaderMoving[chIdx],
                isKnobDirty: _isKnobDirty[chIdx],
                isKnobMoving: _isKnobMoving[chIdx],
                lastFaderVol: _lastFaderVol,
                lastKnobVol: _lastKnobVol,
                dismissDelayMs: 1000
            );

            SaveHardwareState();
            return;
        }
    }

    private void HandleNote(int note, bool isNoteOn)
    {
        // DEVICE / TRACK SELECT ▼ BUTTON (Note 105) -> Cycle Target HUD Monitor Display
        if (note == 105)
        {
            if (isNoteOn)
            {
                if (_hudService != null && _hudService.MonitorCount > 1)
                {
                    Console.WriteLine("[MIDI] Device Button (Note 105) Pressed -> Displaying/Cycling Target HUD Monitor Display");
                    CancelActiveWizardsIfOtherControlTouched(-1, isTargetChannelControl: false);
                    int newMonIdx = _hudService.ShowOrCycleTargetMonitor();
                    SaveHardwareState();
                }
                else
                {
                    Console.WriteLine("[MIDI] Device Button (Note 105) Pressed -> Single monitor system, cycling skipped.");
                }
            }
            return;
        }

        // GLOBAL MASTER MUTE MACRO BUTTON (Note 106)
        if (note == 106)
        {
            if (isNoteOn)
            {
                Console.WriteLine("[MIDI] Global Master MUTE Button (Note 106) Pressed -> Muting all assigned channels!");
                CancelActiveWizardsIfOtherControlTouched(-1, isTargetChannelControl: false);
                _audioEngine.MuteAllChannels();
                UpdateAllLeds();
            }
            return;
        }

        // PRESET CREATION / SAVE BUTTON (Note 107)
        if (note == 107)
        {
            if (isNoteOn)
            {
                if (!_isPresetSaveActive)
                {
                    // Step 1: Start Preset Creation Mode
                    Console.WriteLine("[Preset Save] Note 107 Pressed -> Entering Preset Creation Mode");
                    CancelActiveWizardsIfOtherControlTouched(-1, isTargetChannelControl: false);

                    _isPresetSaveActive = true;
                    _isPresetNamingStep = false;

                    // By default, select all channels that have loaded stems
                    for (int c = 0; c < 8; c++)
                    {
                        _presetSelectedChannels[c] = _audioEngine.Channels[c].LoadedStem != null;
                    }

                    UpdateAllLeds();
                    _hudService?.ShowPresetSaveWindow(_audioEngine.Channels, _presetSelectedChannels);
                }
                else if (!_isPresetNamingStep)
                {
                    // Step 2: Confirm Channel Selection & Transition to Auto-Focused Naming Step
                    Console.WriteLine("[Preset Save] Note 107 Pressed -> Confirming Channel Selection & Transitioning to Naming Step");

                    // Verify at least 1 channel is selected
                    bool hasAny = false;
                    for (int c = 0; c < 8; c++)
                    {
                        if (_presetSelectedChannels[c]) { hasAny = true; break; }
                    }

                    if (!hasAny)
                    {
                        Console.WriteLine("[Preset Save Warning] No channels selected to include in preset.");
                        return;
                    }

                    _isPresetNamingStep = true;
                    UpdateAllLeds();
                    _hudService?.TransitionPresetSaveToNaming();
                }
                else
                {
                    // Step 3: Confirm Preset Name Submission
                    Console.WriteLine("[Preset Save] Note 107 Pressed -> Submitting Preset Name");
                    _hudService?.SubmitPresetSaveName();
                }
            }
            return;
        }

        // TOP ROW BUTTONS (MUTE / BACK / CANCEL / CLEAR LONG-PRESS) -> Notes 41..44 (Ch 0..3) & 57..60 (Ch 4..7)
        int muteChIdx = -1;
        if (note >= 41 && note <= 44) muteChIdx = note - 41;
        else if (note >= 57 && note <= 60) muteChIdx = note - 57 + 4;

        if (muteChIdx >= 0 && muteChIdx < 8)
        {
            if (isNoteOn)
            {
                _isMuteHeld[muteChIdx] = true;
                _wasMuteLongPressHandled[muteChIdx] = false;

                _muteLongPressTimers[muteChIdx]?.Dispose();
                _muteLongPressTimers[muteChIdx] = new Timer(_ =>
                {
                    if (_isMuteHeld[muteChIdx])
                    {
                        _wasMuteLongPressHandled[muteChIdx] = true;
                        OnMuteButtonLongPress(muteChIdx);
                    }
                }, null, 600, Timeout.Infinite);
            }
            else
            {
                _isMuteHeld[muteChIdx] = false;
                _muteLongPressTimers[muteChIdx]?.Dispose();
                _muteLongPressTimers[muteChIdx] = null;

                if (_wasMuteLongPressHandled[muteChIdx])
                {
                    _wasMuteLongPressHandled[muteChIdx] = false;
                    return;
                }

                OnMuteButtonShortPress(muteChIdx);
            }
            return;
        }

        // BOTTOM ROW BUTTONS (OPERATION / CONFIRM / PRESET SELECTION TOGGLE / SWAP MOVE GESTURE) -> Notes 73..76 (Ch 0..3) & 89..92 (Ch 4..7)
        int operChIdx = -1;
        if (note >= 73 && note <= 76) operChIdx = note - 73;
        else if (note >= 89 && note <= 96) operChIdx = note - 89 + 4;

        if (operChIdx >= 0 && operChIdx < 8)
        {
            if (isNoteOn)
            {
                // PRESET CREATION MODE: Toggle channel selection via Operation Buttons!
                if (_isPresetSaveActive && !_isPresetNamingStep)
                {
                    if (_audioEngine.Channels[operChIdx].LoadedStem != null)
                    {
                        _presetSelectedChannels[operChIdx] = !_presetSelectedChannels[operChIdx];
                        Console.WriteLine($"[Preset Save] Channel {operChIdx + 1} Selection Toggled -> {(_presetSelectedChannels[operChIdx] ? "INCLUDED (Green)" : "REMOVED (Amber)")}");
                        UpdateAllLeds();
                        _hudService?.UpdatePresetSaveWindow(_audioEngine.Channels, _presetSelectedChannels);
                    }
                    return;
                }

                // CHECK FOR CHANNEL MOVE/SWAP GESTURE: Is another Operation button held down right now?
                int heldCh = -1;
                for (int c = 0; c < 8; c++)
                {
                    if (c != operChIdx && _isOperationHeld[c])
                    {
                        heldCh = c;
                        break;
                    }
                }

                if (heldCh >= 0)
                {
                    // SWAP / MOVE GESTURE TRIGGERED! Move Stem from heldCh -> operChIdx!
                    _isOperationHeld[operChIdx] = false;
                    _isOperationHeld[heldCh] = false;

                    _longPressTimers[heldCh]?.Dispose();
                    _longPressTimers[heldCh] = null;
                    _wasLongPressHandled[heldCh] = true;
                    _wasLongPressHandled[operChIdx] = true;

                    CancelActiveWizardsIfOtherControlTouched(-1, isTargetChannelControl: false);

                    var chSrc = _audioEngine.Channels[heldCh];
                    var stemToMove = chSrc.LoadedStem;

                    if (stemToMove != null)
                    {
                        Console.WriteLine($"[MIDI MOVE GESTURE] Moving Stem '[{stemToMove.Name}]' from Channel {heldCh + 1} -> Channel {operChIdx + 1}");

                        bool wasMuted = chSrc.IsMuted;
                        float masterVol = chSrc.MasterVolume;
                        float k0 = chSrc.TrackVolumes[0];
                        float k1 = chSrc.TrackVolumes[1];
                        float k2 = chSrc.TrackVolumes[2];

                        _audioEngine.LoadStemToChannel(heldCh, null);
                        _isFaderDirty[heldCh] = false;
                        _isFaderMoving[heldCh] = false;
                        for (int t = 0; t < 3; t++)
                        {
                            _isKnobDirty[heldCh][t] = false;
                            _isKnobMoving[heldCh][t] = false;
                        }

                        _audioEngine.LoadStemToChannel(operChIdx, stemToMove);

                        var chDst = _audioEngine.Channels[operChIdx];
                        chDst.IsMuted = wasMuted;
                        chDst.MasterVolume = masterVol;
                        chDst.TrackVolumes[0] = k0;
                        chDst.TrackVolumes[1] = k1;
                        chDst.TrackVolumes[2] = k2;

                        _audioEngine.UpdateChannelEffectiveVolumes(operChIdx, immediate: true);

                        _isFaderDirty[operChIdx] = true;
                        _isFaderMoving[operChIdx] = false;

                        int trackCount = stemToMove.Tracks.Count;
                        for (int t = 0; t < 3; t++)
                        {
                            _isKnobDirty[operChIdx][t] = t < trackCount;
                            _isKnobMoving[operChIdx][t] = false;
                        }

                        UpdateAllLeds();
                        _hudService?.ShowChannelOverview(
                            operChIdx,
                            chDst,
                            activeControl: "",
                            isFaderDirty: _isFaderDirty[operChIdx],
                            isFaderMoving: _isFaderMoving[operChIdx],
                            isKnobDirty: _isKnobDirty[operChIdx],
                            isKnobMoving: _isKnobMoving[operChIdx],
                            lastFaderVol: _lastFaderVol,
                            lastKnobVol: _lastKnobVol,
                            dismissDelayMs: 3000
                        );
                    }
                    else
                    {
                        Console.WriteLine($"[MIDI MOVE GESTURE] Channel {heldCh + 1} is empty, no stem to move.");
                    }
                    return;
                }

                _isOperationHeld[operChIdx] = true;
                _wasLongPressHandled[operChIdx] = false;

                _longPressTimers[operChIdx]?.Dispose();
                _longPressTimers[operChIdx] = new Timer(_ =>
                {
                    if (_isOperationHeld[operChIdx])
                    {
                        _wasLongPressHandled[operChIdx] = true;
                        OnOperationButtonLongPress(operChIdx);
                    }
                }, null, 1000, Timeout.Infinite);
            }
            else
            {
                _isOperationHeld[operChIdx] = false;
                _longPressTimers[operChIdx]?.Dispose();
                _longPressTimers[operChIdx] = null;

                if (_wasLongPressHandled[operChIdx])
                {
                    _wasLongPressHandled[operChIdx] = false;
                    return;
                }

                OnOperationButtonShortPress(operChIdx);
            }
            return;
        }

        // SIDEBOARD MACRO BUTTONS (Notes 104, 108)
        if (isNoteOn && (_activeWizard != null || _activeClearWizard != null))
        {
            CancelActiveWizardsIfOtherControlTouched(-1, isTargetChannelControl: false);
        }
    }

    private void OnMuteButtonShortPress(int chIdx)
    {
        if (_activeWizard != null)
        {
            if (_activeWizard.TargetChannelIndex == chIdx)
            {
                bool cancelled = _activeWizard.GoBackOrCancel(_lastFaderVol[chIdx]);
                if (cancelled)
                {
                    Console.WriteLine($"[Wizard] Cancelled channel assignment for Channel {_activeWizard.TargetChannelIndex + 1}.");
                    _activeWizard = null;
                    _hudService?.CloseAssignmentWizard();
                    UpdateAllLeds();
                }
                else
                {
                    Console.WriteLine($"[Wizard] Went back in assignment wizard for Channel {_activeWizard.TargetChannelIndex + 1}.");
                    _hudService?.UpdateAssignmentWizard(_activeWizard);
                }
                return;
            }
            else
            {
                CancelActiveWizardsIfOtherControlTouched(chIdx, isTargetChannelControl: false);
            }
        }

        if (_activeClearWizard != null)
        {
            if (_activeClearWizard.TargetChannelIndex == chIdx)
            {
                Console.WriteLine($"[Clear Wizard] Cancelled clear channel operation for Channel {chIdx + 1}.");
                _activeClearWizard = null;
                _hudService?.CloseClearConfirmation();
                UpdateAllLeds();
                return;
            }
            else
            {
                CancelActiveWizardsIfOtherControlTouched(chIdx, isTargetChannelControl: false);
            }
        }

        if (_isPresetSaveActive)
        {
            CancelPresetSave();
            return;
        }

        bool muted = _audioEngine.ToggleMute(chIdx);
        var ch = _audioEngine.Channels[chIdx];
        string stemName = ch.LoadedStem?.Name ?? "Unassigned";
        Console.WriteLine($"[MIDI] Channel {chIdx} ({stemName}) Top Button (Mute) Short-Pressed -> Muted: {muted}");
        UpdateChannelLeds(chIdx);
    }

    private void OnMuteButtonLongPress(int chIdx)
    {
        var ch = _audioEngine.Channels[chIdx];
        if (ch.LoadedStem == null)
        {
            Console.WriteLine($"[Clear Wizard] Channel {chIdx + 1} is already unassigned.");
            return;
        }

        Console.WriteLine($"[Clear Wizard] Channel {chIdx + 1} Mute Button Long-Pressed (>=600ms) -> Launching Clear Channel Confirmation");

        CancelActiveWizardsIfOtherControlTouched(chIdx, isTargetChannelControl: false);

        _activeClearWizard = new ChannelClearWizard(chIdx, ch.LoadedStem);

        UpdateAllLeds();
        _hudService?.ShowClearConfirmation(chIdx, ch.LoadedStem);
    }

    private void OnOperationButtonShortPress(int chIdx)
    {
        if (_activeClearWizard != null)
        {
            if (_activeClearWizard.TargetChannelIndex == chIdx)
            {
                int targetCh = _activeClearWizard.TargetChannelIndex;
                Console.WriteLine($"[Clear Wizard] Channel {targetCh + 1} confirmed clear! Unloading stem and going dark.");

                _audioEngine.LoadStemToChannel(targetCh, null);
                SyncChannelHardwareVolume(targetCh);

                _activeClearWizard = null;
                _hudService?.CloseClearConfirmation();
                UpdateAllLeds();

                _hudService?.ShowChannelOverview(
                    targetCh,
                    _audioEngine.Channels[targetCh],
                    activeControl: "",
                    isFaderDirty: _isFaderDirty[targetCh],
                    isFaderMoving: _isFaderMoving[targetCh],
                    isKnobDirty: _isKnobDirty[targetCh],
                    isKnobMoving: _isKnobMoving[targetCh],
                    lastFaderVol: _lastFaderVol,
                    lastKnobVol: _lastKnobVol,
                    dismissDelayMs: 3000
                );
                return;
            }
            else
            {
                CancelActiveWizardsIfOtherControlTouched(chIdx, isTargetChannelControl: false);
            }
        }

        if (_activeWizard != null)
        {
            if (_activeWizard.TargetChannelIndex == chIdx)
            {
                bool finished = _activeWizard.ConfirmNextStep(_lastFaderVol[chIdx], out Stem? finalStem, out Preset? finalPreset);

                if (!finished)
                {
                    Console.WriteLine($"[Wizard] Channel {chIdx + 1} confirmed wizard step. Moving forward.");
                    _hudService?.UpdateAssignmentWizard(_activeWizard);
                }
                else
                {
                    int targetCh = _activeWizard.TargetChannelIndex;

                    if (finalPreset != null)
                    {
                        // LOAD MULTI-CHANNEL PRESET SEQUENTIALLY STARTING AT targetCh
                        Console.WriteLine($"[Wizard] Channel {targetCh + 1} loading Preset '[{finalPreset.Name}]' with {finalPreset.ChannelSnapshots.Count} channel snapshot(s)...");

                        foreach (var snap in finalPreset.ChannelSnapshots)
                        {
                            int destCh = targetCh + snap.RelativeChannelIndex;
                            if (destCh >= 8) break; // Exceeds board channels

                            // Find matching stem in categories
                            Category? cat = _categories.FirstOrDefault(c => c.Name.Equals(snap.CategoryName, StringComparison.OrdinalIgnoreCase));
                            Stem? stemToLoad = cat?.Stems.FirstOrDefault(s => s.Name.Equals(snap.StemName, StringComparison.OrdinalIgnoreCase));

                            if (stemToLoad != null)
                            {
                                _audioEngine.LoadStemToChannel(destCh, stemToLoad);
                                var chDest = _audioEngine.Channels[destCh];
                                chDest.IsMuted = snap.IsMuted;
                                chDest.MasterVolume = snap.MasterVolume;
                                for (int t = 0; t < 3; t++)
                                {
                                    if (t < snap.TrackVolumes.Length)
                                    {
                                        chDest.TrackVolumes[t] = snap.TrackVolumes[t];
                                    }
                                }
                                _audioEngine.UpdateChannelEffectiveVolumes(destCh, immediate: true);

                                // Mark controls dirty for soft-catch
                                _isFaderDirty[destCh] = true;
                                _isFaderMoving[destCh] = false;
                                int tCount = stemToLoad.Tracks.Count;
                                for (int t = 0; t < 3; t++)
                                {
                                    _isKnobDirty[destCh][t] = t < tCount;
                                    _isKnobMoving[destCh][t] = false;
                                }

                                Console.WriteLine($"[Preset Load] Loaded '{stemToLoad.Name}' -> Channel {destCh + 1}");
                            }
                        }

                        _activeWizard = null;
                        _hudService?.CloseAssignmentWizard();
                        UpdateAllLeds();

                        _hudService?.ShowChannelOverview(
                            targetCh,
                            _audioEngine.Channels[targetCh],
                            activeControl: "",
                            isFaderDirty: _isFaderDirty[targetCh],
                            isFaderMoving: _isFaderMoving[targetCh],
                            isKnobDirty: _isKnobDirty[targetCh],
                            isKnobMoving: _isKnobMoving[targetCh],
                            lastFaderVol: _lastFaderVol,
                            lastKnobVol: _lastKnobVol,
                            dismissDelayMs: 3000
                        );
                    }
                    else if (finalStem != null)
                    {
                        // LOAD SINGLE STEM
                        Console.WriteLine($"[Wizard] Channel {targetCh + 1} assigning Stem '[{finalStem.Name}]' ({finalStem.CategoryName}).");
                        _audioEngine.LoadStemToChannel(targetCh, finalStem);
                        SyncChannelHardwareVolume(targetCh);

                        _activeWizard = null;
                        _hudService?.CloseAssignmentWizard();
                        UpdateAllLeds();

                        _hudService?.ShowChannelOverview(
                            targetCh,
                            _audioEngine.Channels[targetCh],
                            activeControl: "",
                            isFaderDirty: _isFaderDirty[targetCh],
                            isFaderMoving: _isFaderMoving[targetCh],
                            isKnobDirty: _isKnobDirty[targetCh],
                            isKnobMoving: _isKnobMoving[targetCh],
                            lastFaderVol: _lastFaderVol,
                            lastKnobVol: _lastKnobVol,
                            dismissDelayMs: 3000
                        );
                    }
                    else
                    {
                        _activeWizard = null;
                        _hudService?.CloseAssignmentWizard();
                        UpdateAllLeds();
                    }
                }
                return;
            }
            else
            {
                CancelActiveWizardsIfOtherControlTouched(chIdx, isTargetChannelControl: false);
            }
        }

        var ch2 = _audioEngine.Channels[chIdx];
        string stemName2 = ch2.LoadedStem?.Name ?? "Unassigned";
        Console.WriteLine($"[MIDI] Channel {chIdx} ({stemName2}) Bottom Button (Operation) Short-Pressed -> Showing Unified Channel Overview HUD");
        _hudService?.ShowChannelOverview(
            chIdx,
            ch2,
            activeControl: "",
            isFaderDirty: _isFaderDirty[chIdx],
            isFaderMoving: _isFaderMoving[chIdx],
            isKnobDirty: _isKnobDirty[chIdx],
            isKnobMoving: _isKnobMoving[chIdx],
            lastFaderVol: _lastFaderVol,
            lastKnobVol: _lastKnobVol,
            dismissDelayMs: 3000
        );
    }

    private void OnOperationButtonLongPress(int chIdx)
    {
        Console.WriteLine($"[Wizard] Channel {chIdx + 1} Operation Button Long-Pressed (>=1000ms) -> Launching 3D Wheel Channel Assignment Wizard");

        CancelActiveWizardsIfOtherControlTouched(chIdx, isTargetChannelControl: false);

        var presets = _presetStorageService.GetAlphabetizedPresets();
        _activeWizard = new StemAssignmentWizard(chIdx, _categories, presets, _lastFaderVol[chIdx]);

        UpdateAllLeds();
        _hudService?.ShowAssignmentWizard(_activeWizard);
    }

    private void OnPresetNameSubmitted(string presetName)
    {
        if (!_isPresetSaveActive) return;

        int firstChannelIdx = -1;
        for (int c = 0; c < 8; c++)
        {
            if (_presetSelectedChannels[c]) { firstChannelIdx = c; break; }
        }

        if (firstChannelIdx < 0)
        {
            Console.WriteLine("[Preset Save Error] No channels selected.");
            CancelPresetSave();
            return;
        }

        var preset = new Preset
        {
            Name = presetName,
            CreatedAt = DateTime.Now
        };

        for (int c = 0; c < 8; c++)
        {
            if (_presetSelectedChannels[c])
            {
                var ch = _audioEngine.Channels[c];
                if (ch.LoadedStem != null)
                {
                    preset.ChannelSnapshots.Add(new ChannelSnapshot
                    {
                        RelativeChannelIndex = c - firstChannelIdx,
                        CategoryName = ch.LoadedStem.CategoryName,
                        StemName = ch.LoadedStem.Name,
                        MasterVolume = ch.MasterVolume,
                        TrackVolumes = (float[])ch.TrackVolumes.Clone(),
                        IsMuted = ch.IsMuted
                    });
                }
            }
        }

        _presetStorageService.SavePreset(preset);
        Console.WriteLine($"[Preset Save Success] Saved Preset '[{preset.Name}]' with {preset.ChannelSnapshots.Count} channel(s)!");

        _isPresetSaveActive = false;
        _isPresetNamingStep = false;
        _hudService?.ClosePresetSaveWindow();
        UpdateAllLeds();
    }

    private void CancelPresetSave()
    {
        if (!_isPresetSaveActive) return;
        Console.WriteLine("[Preset Save] Cancelled preset creation mode.");
        _isPresetSaveActive = false;
        _isPresetNamingStep = false;
        _hudService?.ClosePresetSaveWindow();
        UpdateAllLeds();
    }

    private void OnFlashTimerTick(object? state)
    {
        _flashPhase = !_flashPhase;

        if (_isPresetSaveActive)
        {
            SendRawLed(107, _flashPhase ? (_isPresetNamingStep ? LedGreenFull : LedAmberFull) : LedOff);
        }

        if (_activeWizard != null)
        {
            int activeCh = _activeWizard.TargetChannelIndex;
            byte operButtonId = (byte)(activeCh < 4 ? 73 + activeCh : 89 + (activeCh - 4));
            byte muteButtonId = (byte)(activeCh < 4 ? 41 + activeCh : 57 + (activeCh - 4));

            SendRawLed(operButtonId, _flashPhase ? LedGreenFull : LedOff);
            SendRawLed(muteButtonId, _flashPhase ? LedRedFull : LedOff);
        }

        if (_activeClearWizard != null)
        {
            int activeCh = _activeClearWizard.TargetChannelIndex;
            int baseId = activeCh * 16;
            byte topKnobId = (byte)(13 + baseId);
            byte midKnobId = (byte)(14 + baseId);
            byte botKnobId = (byte)(15 + baseId);
            byte muteButtonId = (byte)(activeCh < 4 ? 41 + activeCh : 57 + (activeCh - 4));
            byte operButtonId = (byte)(activeCh < 4 ? 73 + activeCh : 89 + (activeCh - 4));

            byte redVal = _flashPhase ? LedRedFull : LedOff;
            byte greenVal = _flashPhase ? LedGreenFull : LedOff;

            SendRawLed(operButtonId, greenVal);
            SendRawLed(muteButtonId, redVal);
            SendRawLed(topKnobId, redVal);
            SendRawLed(midKnobId, redVal);
            SendRawLed(botKnobId, redVal);
        }
    }

    public void UpdateAllLeds()
    {
        for (int i = 0; i < 8; i++)
        {
            UpdateChannelLeds(i);
        }

        SendRawLed(104, LedOff);

        // Device Button (Note 105) LED: Lit Solid Green if multiple monitors exist, else OFF
        if (_hudService != null && _hudService.MonitorCount > 1)
        {
            SendRawLed(105, LedGreenFull);
        }
        else
        {
            SendRawLed(105, LedOff);
        }

        // Global Master MUTE Button LED (Note 106): ALWAYS LIT Solid Green
        SendRawLed(106, LedGreenFull);

        // Preset Save Button LED (Note 107): Flashing Amber (Channel Select) or Green (Naming), else OFF
        if (_isPresetSaveActive)
        {
            SendRawLed(107, _flashPhase ? (_isPresetNamingStep ? LedGreenFull : LedAmberFull) : LedOff);
        }
        else
        {
            SendRawLed(107, LedOff);
        }

        SendRawLed(108, LedOff);
    }

    public void UpdateChannelLeds(int channelIndex)
    {
        if (channelIndex < 0 || channelIndex >= 8 || _outputDevices.Count == 0) return;

        var ch = _audioEngine.Channels[channelIndex];

        int baseId = channelIndex * 16;
        byte topKnobId = (byte)(13 + baseId);
        byte midKnobId = (byte)(14 + baseId);
        byte botKnobId = (byte)(15 + baseId);

        byte muteButtonId = (byte)(channelIndex < 4 ? 41 + channelIndex : 57 + (channelIndex - 4));
        byte operButtonId = (byte)(channelIndex < 4 ? 73 + channelIndex : 89 + (channelIndex - 4));

        // PRESET SAVE MODE: Operation Buttons light Green (Selected) or Amber (Unselected) for assigned channels!
        if (_isPresetSaveActive)
        {
            if (ch.LoadedStem != null)
            {
                bool isSelected = _presetSelectedChannels[channelIndex];
                SendRawLed(operButtonId, isSelected ? LedGreenFull : LedAmberFull);
            }
            else
            {
                SendRawLed(operButtonId, LedOff);
            }
            SendRawLed(muteButtonId, LedOff);
            SendRawLed(topKnobId, LedOff);
            SendRawLed(midKnobId, LedOff);
            SendRawLed(botKnobId, LedOff);
            return;
        }

        if (_activeClearWizard != null && _activeClearWizard.TargetChannelIndex == channelIndex)
        {
            byte redVal = _flashPhase ? LedRedFull : LedOff;
            byte greenVal = _flashPhase ? LedGreenFull : LedOff;

            SendRawLed(operButtonId, greenVal);
            SendRawLed(muteButtonId, redVal);
            SendRawLed(topKnobId, redVal);
            SendRawLed(midKnobId, redVal);
            SendRawLed(botKnobId, redVal);
            return;
        }

        if (_activeWizard != null && _activeWizard.TargetChannelIndex == channelIndex)
        {
            SendRawLed(operButtonId, _flashPhase ? LedGreenFull : LedOff);
            SendRawLed(muteButtonId, _flashPhase ? LedRedFull : LedOff);
            SendRawLed(topKnobId, LedOff);
            SendRawLed(midKnobId, LedOff);
            SendRawLed(botKnobId, LedOff);
            return;
        }

        if (ch.LoadedStem == null)
        {
            SendRawLed(topKnobId, LedOff);
            SendRawLed(midKnobId, LedOff);
            SendRawLed(botKnobId, LedOff);
            SendRawLed(muteButtonId, LedOff);
            SendRawLed(operButtonId, LedOff);
            return;
        }

        int trackCount = ch.LoadedStem.Tracks.Count;

        // Knob 3 (Bottom -> Track 0)
        if (trackCount >= 1)
        {
            if (_isKnobDirty[channelIndex][0])
            {
                SendRawLed(botKnobId, _isKnobMoving[channelIndex][0] ? LedAmberFull : LedRedFull);
            }
            else
            {
                SendRawLed(botKnobId, LedGreenFull);
            }
        }
        else
        {
            SendRawLed(botKnobId, LedOff);
        }

        // Knob 2 (Middle -> Track 1)
        if (trackCount >= 2)
        {
            if (_isKnobDirty[channelIndex][1])
            {
                SendRawLed(midKnobId, _isKnobMoving[channelIndex][1] ? LedAmberFull : LedRedFull);
            }
            else
            {
                SendRawLed(midKnobId, LedGreenFull);
            }
        }
        else
        {
            SendRawLed(midKnobId, LedOff);
        }

        // Knob 1 (Top -> Track 2)
        if (trackCount >= 3)
        {
            if (_isKnobDirty[channelIndex][2])
            {
                SendRawLed(topKnobId, _isKnobMoving[channelIndex][2] ? LedAmberFull : LedRedFull);
            }
            else
            {
                SendRawLed(topKnobId, LedGreenFull);
            }
        }
        else
        {
            SendRawLed(topKnobId, LedOff);
        }

        // Operation Button LED (representing Master Fader)
        if (_isFaderDirty[channelIndex])
        {
            SendRawLed(operButtonId, _isFaderMoving[channelIndex] ? LedAmberFull : LedRedFull);
        }
        else
        {
            SendRawLed(operButtonId, LedGreenFull);
        }

        // Mute LED: OFF when unmuted, Solid Red (11) when Muted
        byte muteVal = ch.IsMuted ? LedRedFull : LedOff;
        SendRawLed(muteButtonId, muteVal);
    }

    private void SendRawLed(byte index, byte colorValue)
    {
        if (_outputDevices.Count == 0) return;

        try
        {
            FourBitNumber[] targetChannels = new FourBitNumber[] { _hardwareMidiChannel, (FourBitNumber)0, (FourBitNumber)8 };
            foreach (var outDev in _outputDevices)
            {
                foreach (var ch in targetChannels)
                {
                    outDev.SendEvent(new NoteOnEvent((SevenBitNumber)index, (SevenBitNumber)colorValue) { Channel = ch });
                    outDev.SendEvent(new ControlChangeEvent((SevenBitNumber)index, (SevenBitNumber)colorValue) { Channel = ch });
                }
            }
        }
        catch { }
    }

    private void SaveHardwareState()
    {
        try
        {
            var dto = new HardwareStateDto
            {
                FaderVolumes = (float[])_lastFaderVol.Clone(),
                KnobVolumes = _lastKnobVol.Select(arr => (float[])arr.Clone()).ToArray(),
                TargetMonitorIndex = _hudService?.TargetMonitorIndex ?? 0
            };
            string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StateFilePath, json);
        }
        catch { }
    }

    private void LoadHardwareState()
    {
        try
        {
            if (File.Exists(StateFilePath))
            {
                string json = File.ReadAllText(StateFilePath);
                var dto = JsonSerializer.Deserialize<HardwareStateDto>(json);
                if (dto != null)
                {
                    if (dto.FaderVolumes != null && dto.FaderVolumes.Length == 8)
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            _lastFaderVol[i] = dto.FaderVolumes[i];
                            if (dto.KnobVolumes != null && dto.KnobVolumes.Length == 8 && dto.KnobVolumes[i].Length == 3)
                            {
                                _lastKnobVol[i] = dto.KnobVolumes[i];
                            }
                            _hasHardwarePosition[i] = true;
                        }
                    }

                    _hudService?.SetTargetMonitorIndex(dto.TargetMonitorIndex);
                    Console.WriteLine($"[MIDI State] Restored last known hardware control positions and target monitor ({dto.TargetMonitorIndex + 1}).");
                }
            }
        }
        catch { }
    }

    public void Dispose()
    {
        _flashTimer.Dispose();
        for (int i = 0; i < 8; i++)
        {
            _longPressTimers[i]?.Dispose();
            _muteLongPressTimers[i]?.Dispose();
            _faderMotionPauseTimers[i]?.Dispose();

            for (int t = 0; t < 3; t++)
            {
                _knobMotionPauseTimers[i][t]?.Dispose();
            }
        }
        SaveHardwareState();

        foreach (var outDev in _outputDevices)
        {
            try { outDev.Dispose(); } catch { }
        }
        _outputDevices.Clear();

        if (_inputDevice != null)
        {
            _inputDevice.EventReceived -= OnMidiEventReceived;
            if (_inputDevice.IsListeningForEvents)
            {
                _inputDevice.StopEventsListening();
            }
            _inputDevice.Dispose();
            _inputDevice = null;
        }

        Console.WriteLine("[MIDI] Disconnected hardware device.");
    }
}
