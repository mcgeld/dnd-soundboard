using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;
using SoundBoard.Helpers;
using SoundBoard.Models;
using SoundBoard.UI;

namespace SoundBoard.Services;

/// <summary>
/// Thread-safe WPF HUD manager running a background STA Thread and managing TopMost translucent HUD overlays.
/// </summary>
public class HudService : IDisposable
{
    private Thread? _uiThread;
    private Dispatcher? _dispatcher;
    private HudOverlayWindow? _hudWindow;
    private StemAssignmentWindow? _assignmentWindow;
    private ChannelClearWindow? _clearWindow;
    private MonitorSelectionWindow? _monitorWindow;
    private PresetSaveWindow? _presetSaveWindow;
    private MuteToggleWindow? _muteToggleWindow;

    private readonly List<DisplayMonitorInfo> _monitors;
    private int _targetMonitorIndex = 0;
    private bool _isMonitorWindowShowing = false;

    private Timer? _dismissTimer;
    private Timer? _monitorDismissTimer;
    private readonly object _lock = new();

    public event Action<string>? OnPresetSaveSubmitted;
    public event Action? OnPresetSaveCancelled;

    public int MonitorCount => _monitors.Count;
    public int TargetMonitorIndex => _targetMonitorIndex;

    public HudService()
    {
        _monitors = DisplayMonitorHelper.GetDisplayMonitors();
        InitializeUiCore();
    }

    private void InitializeUiCore()
    {
        var readyEvent = new ManualResetEvent(false);

        _uiThread = new Thread(() =>
        {
            try
            {
                var app = System.Windows.Application.Current ?? new System.Windows.Application
                {
                    ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
                };

                _dispatcher = Dispatcher.CurrentDispatcher;
                _hudWindow = new HudOverlayWindow();
                _assignmentWindow = new StemAssignmentWindow();
                _clearWindow = new ChannelClearWindow();
                _monitorWindow = new MonitorSelectionWindow();
                _presetSaveWindow = new PresetSaveWindow();
                _muteToggleWindow = new MuteToggleWindow();

                _presetSaveWindow.OnPresetSaveSubmitted += name => OnPresetSaveSubmitted?.Invoke(name);
                _presetSaveWindow.OnPresetSaveCancelled += () => OnPresetSaveCancelled?.Invoke();

                ApplyTargetMonitorToAllWindows();

                readyEvent.Set();
                app.Run();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Initialization Error] {ex.Message}");
                readyEvent.Set();
            }
        })
        {
            IsBackground = true,
            Name = "HUD_Overlay_UI_Thread"
        };

        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();

        readyEvent.WaitOne(3000);
        Console.WriteLine($"[HUD] TopMost Glassmorphism HUD Overlay Service initialized ({_monitors.Count} monitor(s) detected).");
    }

    private void ApplyTargetMonitorToAllWindows()
    {
        if (_monitors.Count == 0) return;

        if (_targetMonitorIndex < 0 || _targetMonitorIndex >= _monitors.Count)
        {
            _targetMonitorIndex = 0;
        }

        var targetMon = _monitors[_targetMonitorIndex];

        _hudWindow?.SetTargetMonitor(targetMon);
        _assignmentWindow?.SetTargetMonitor(targetMon);
        _clearWindow?.SetTargetMonitor(targetMon);
        _presetSaveWindow?.SetTargetMonitor(targetMon);
        _muteToggleWindow?.SetTargetMonitor(targetMon);
    }

    public void SetTargetMonitorIndex(int index)
    {
        if (_monitors.Count == 0) return;

        _targetMonitorIndex = Math.Clamp(index, 0, _monitors.Count - 1);

        _dispatcher?.BeginInvoke(new Action(() =>
        {
            lock (_lock)
            {
                ApplyTargetMonitorToAllWindows();
            }
        }));
    }

    public int ShowOrCycleTargetMonitor()
    {
        if (_monitors.Count == 0) return 0;

        if (_isMonitorWindowShowing && _monitors.Count > 1)
        {
            _targetMonitorIndex = (_targetMonitorIndex + 1) % _monitors.Count;
        }

        _dispatcher?.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _isMonitorWindowShowing = true;
                    _monitorDismissTimer?.Dispose();
                    _dismissTimer?.Dispose();

                    _hudWindow?.HideHud();
                    _assignmentWindow?.HideWindow();
                    _clearWindow?.HideWindow();
                    _presetSaveWindow?.HideWindow();

                    ApplyTargetMonitorToAllWindows();

                    var mon = _monitors[_targetMonitorIndex];
                    _monitorWindow?.UpdateDisplay(mon, _monitors.Count);
                    _monitorWindow?.ShowWindow();

                    Console.WriteLine($"[HUD] Target Monitor info displayed for: Monitor {_targetMonitorIndex + 1} of {_monitors.Count} ({mon.Bounds.Width:F0}x{mon.Bounds.Height:F0} {mon.DeviceName})");

                    _monitorDismissTimer = new Timer(_ =>
                    {
                        _dispatcher?.BeginInvoke(new Action(() =>
                        {
                            lock (_lock)
                            {
                                _isMonitorWindowShowing = false;
                                _monitorWindow?.HideWindow();
                            }
                        }));
                    }, null, 2000, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Monitor Selection Error] {ex.Message}");
            }
        }));

        return _targetMonitorIndex;
    }

    public void ShowChannelOverview(
        int channelIndex,
        Channel channel,
        string activeControl = "",
        bool isFaderDirty = false,
        bool isFaderMoving = false,
        bool[]? isKnobDirty = null,
        bool[]? isKnobMoving = null,
        float[]? lastFaderVol = null,
        float[][]? lastKnobVol = null,
        int dismissDelayMs = 1000)
    {
        if (_dispatcher == null || _hudWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _dismissTimer?.Dispose();
                    _isMonitorWindowShowing = false;
                    _monitorWindow?.HideWindow();
                    _assignmentWindow?.HideWindow();
                    _clearWindow?.HideWindow();
                    _presetSaveWindow?.HideWindow();

                    _hudWindow.UpdateChannelOverview(
                        channelIndex,
                        channel,
                        activeControl,
                        isFaderDirty,
                        isFaderMoving,
                        isKnobDirty,
                        isKnobMoving,
                        lastFaderVol,
                        lastKnobVol
                    );
                    _hudWindow.ShowHud();

                    _dismissTimer = new Timer(_ =>
                    {
                        _dispatcher?.BeginInvoke(new Action(() =>
                        {
                            lock (_lock)
                            {
                                _hudWindow?.HideHud();
                            }
                        }));
                    }, null, dismissDelayMs, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Overview Error] Display update failed: {ex.Message}");
            }
        }));
    }

    public void ShowAssignmentWizard(StemAssignmentWizard wizard)
    {
        if (_dispatcher == null || _assignmentWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _dismissTimer?.Dispose();
                    _isMonitorWindowShowing = false;
                    _monitorWindow?.HideWindow();
                    _hudWindow?.HideHud();
                    _clearWindow?.HideWindow();
                    _presetSaveWindow?.HideWindow();

                    _assignmentWindow.UpdateWizard(wizard);
                    _assignmentWindow.ShowWindow();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Assignment Error] Show failed: {ex.Message}");
            }
        }));
    }

    public void UpdateAssignmentWizard(StemAssignmentWizard wizard)
    {
        if (_dispatcher == null || _assignmentWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _assignmentWindow.UpdateWizard(wizard);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Assignment Error] Update failed: {ex.Message}");
            }
        }));
    }

    public void CloseAssignmentWizard()
    {
        if (_dispatcher == null || _assignmentWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _assignmentWindow.HideWindow();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Assignment Error] Close failed: {ex.Message}");
            }
        }));
    }

    public void ShowPresetSaveWindow(IReadOnlyList<Channel> channels, bool[] selectedFlags)
    {
        if (_dispatcher == null || _presetSaveWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _dismissTimer?.Dispose();
                    _isMonitorWindowShowing = false;
                    _monitorWindow?.HideWindow();
                    _hudWindow?.HideHud();
                    _assignmentWindow?.HideWindow();
                    _clearWindow?.HideWindow();

                    _presetSaveWindow.UpdateChannelSelection(channels, selectedFlags);
                    _presetSaveWindow.ShowWindow();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Preset Save Error] Show failed: {ex.Message}");
            }
        }));
    }

    public void UpdatePresetSaveWindow(IReadOnlyList<Channel> channels, bool[] selectedFlags)
    {
        if (_dispatcher == null || _presetSaveWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _presetSaveWindow.UpdateChannelSelection(channels, selectedFlags);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Preset Save Error] Update failed: {ex.Message}");
            }
        }));
    }

    public void TransitionPresetSaveToNaming(string defaultName = "")
    {
        if (_dispatcher == null || _presetSaveWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _presetSaveWindow.TransitionToNamingStep(defaultName);
                    _presetSaveWindow.ShowWindow();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Preset Save Error] Transition failed: {ex.Message}");
            }
        }));
    }

    public void SubmitPresetSaveName()
    {
        if (_dispatcher == null || _presetSaveWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _presetSaveWindow.SubmitPresetName();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Preset Save Error] Submit failed: {ex.Message}");
            }
        }));
    }

    public void ClosePresetSaveWindow()
    {
        if (_dispatcher == null || _presetSaveWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _presetSaveWindow.HideWindow();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Preset Save Error] Close failed: {ex.Message}");
            }
        }));
    }

    public void ShowSingleChannelClear(int channelIndex, Stem stem)
    {
        if (_dispatcher == null || _clearWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _dismissTimer?.Dispose();
                    _isMonitorWindowShowing = false;
                    _monitorWindow?.HideWindow();
                    _hudWindow?.HideHud();
                    _assignmentWindow?.HideWindow();
                    _presetSaveWindow?.HideWindow();

                    _clearWindow.UpdateSingleChannelClear(channelIndex, stem);
                    _clearWindow.ShowWindow();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Single Clear Error] Show failed: {ex.Message}");
            }
        }));
    }

    public void ShowClearModeWindow(IReadOnlyList<Channel> channels, bool[] selectedFlags)
    {
        if (_dispatcher == null || _clearWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _dismissTimer?.Dispose();
                    _isMonitorWindowShowing = false;
                    _monitorWindow?.HideWindow();
                    _hudWindow?.HideHud();
                    _assignmentWindow?.HideWindow();
                    _presetSaveWindow?.HideWindow();

                    _clearWindow.UpdateClearSelection(channels, selectedFlags);
                    _clearWindow.ShowWindow();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Clear Mode Error] Show failed: {ex.Message}");
            }
        }));
    }

    public void UpdateClearModeWindow(IReadOnlyList<Channel> channels, bool[] selectedFlags)
    {
        if (_dispatcher == null || _clearWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _clearWindow.UpdateClearSelection(channels, selectedFlags);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Clear Mode Error] Update failed: {ex.Message}");
            }
        }));
    }

    public void CloseClearModeWindow()
    {
        if (_dispatcher == null || _clearWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _clearWindow.HideWindow();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Clear Mode Error] Close failed: {ex.Message}");
            }
        }));
    }

    public void ShowMuteToggleWindow(IReadOnlyList<Channel> channels, bool[] selectedFlags)
    {
        if (_dispatcher == null || _muteToggleWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _dismissTimer?.Dispose();
                    _isMonitorWindowShowing = false;
                    _monitorWindow?.HideWindow();
                    _hudWindow?.HideHud();
                    _assignmentWindow?.HideWindow();
                    _presetSaveWindow?.HideWindow();
                    _clearWindow?.HideWindow();

                    _muteToggleWindow.UpdateMuteToggleSelection(channels, selectedFlags);
                    _muteToggleWindow.ShowWindow();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Mute Toggle Error] Show failed: {ex.Message}");
            }
        }));
    }

    public void UpdateMuteToggleWindow(IReadOnlyList<Channel> channels, bool[] selectedFlags)
    {
        if (_dispatcher == null || _muteToggleWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _muteToggleWindow.UpdateMuteToggleSelection(channels, selectedFlags);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Mute Toggle Error] Update failed: {ex.Message}");
            }
        }));
    }

    public void CloseMuteToggleWindow()
    {
        if (_dispatcher == null || _muteToggleWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _muteToggleWindow.HideWindow();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Mute Toggle Error] Close failed: {ex.Message}");
            }
        }));
    }

    public void Dispose()
    {
        _dismissTimer?.Dispose();
        _monitorDismissTimer?.Dispose();

        if (_dispatcher != null)
        {
            _dispatcher.BeginInvoke(new Action(() =>
            {
                _hudWindow?.Close();
                _hudWindow = null;
                _assignmentWindow?.Close();
                _assignmentWindow = null;
                _clearWindow?.Close();
                _clearWindow = null;
                _monitorWindow?.Close();
                _monitorWindow = null;
                _presetSaveWindow?.Close();
                _presetSaveWindow = null;
                _muteToggleWindow?.Close();
                _muteToggleWindow = null;
                System.Windows.Application.Current?.Shutdown();
            }));
        }

        Console.WriteLine("[HUD] Disposed cleanly.");
    }
}
