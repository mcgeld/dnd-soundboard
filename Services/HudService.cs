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
    private TrackVolumeOverlayWindow? _volumeWindow;
    private ChannelClearWindow? _clearWindow;
    private MonitorSelectionWindow? _monitorWindow;

    private readonly List<DisplayMonitorInfo> _monitors;
    private int _targetMonitorIndex = 0;

    private Timer? _dismissTimer;
    private Timer? _volumeDismissTimer;
    private Timer? _monitorDismissTimer;
    private readonly object _lock = new();

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
                _volumeWindow = new TrackVolumeOverlayWindow();
                _clearWindow = new ChannelClearWindow();
                _monitorWindow = new MonitorSelectionWindow();

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
        _volumeWindow?.SetTargetMonitor(targetMon);
        _clearWindow?.SetTargetMonitor(targetMon);
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

    public int CycleTargetMonitor()
    {
        if (_monitors.Count <= 1) return 0;

        _targetMonitorIndex = (_targetMonitorIndex + 1) % _monitors.Count;

        _dispatcher?.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _monitorDismissTimer?.Dispose();
                    _dismissTimer?.Dispose();
                    _volumeDismissTimer?.Dispose();

                    _hudWindow?.HideHud();
                    _assignmentWindow?.HideWindow();
                    _volumeWindow?.HideOverlay();
                    _clearWindow?.HideWindow();

                    ApplyTargetMonitorToAllWindows();

                    var mon = _monitors[_targetMonitorIndex];
                    _monitorWindow?.UpdateDisplay(mon, _monitors.Count);
                    _monitorWindow?.ShowWindow();

                    Console.WriteLine($"[HUD] Target Monitor cycled to: Monitor {_targetMonitorIndex + 1} of {_monitors.Count} ({mon.Bounds.Width:F0}x{mon.Bounds.Height:F0} {mon.DeviceName})");

                    _monitorDismissTimer = new Timer(_ =>
                    {
                        _dispatcher?.BeginInvoke(new Action(() =>
                        {
                            lock (_lock)
                            {
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

    public void ShowChannelInfo(int channelIndex, Stem? stem)
    {
        if (_dispatcher == null || _hudWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _dismissTimer?.Dispose();
                    _monitorWindow?.HideWindow();
                    _assignmentWindow?.HideWindow();
                    _volumeWindow?.HideOverlay();
                    _clearWindow?.HideWindow();

                    _hudWindow.UpdateDisplay(channelIndex, stem);
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
                    }, null, 3000, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Error] Display update failed: {ex.Message}");
            }
        }));
    }

    public void ShowTrackVolumeInfo(int channelIndex, string stemName, string trackTitle, string knobLabel, float hardwareVolumePercent, float? audioVolumePercent = null)
    {
        if (_dispatcher == null || _volumeWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _volumeDismissTimer?.Dispose();
                    _monitorWindow?.HideWindow();
                    _hudWindow?.HideHud();
                    _assignmentWindow?.HideWindow();
                    _clearWindow?.HideWindow();

                    string tagText = $"CHANNEL {channelIndex + 1}  ●  {stemName}";

                    _volumeWindow.UpdateVolumeDisplay(tagText, trackTitle, knobLabel, hardwareVolumePercent, isMasterVolume: false, audioVolumePercent: audioVolumePercent);
                    _volumeWindow.ShowOverlay();

                    _volumeDismissTimer = new Timer(_ =>
                    {
                        _dispatcher?.BeginInvoke(new Action(() =>
                        {
                            lock (_lock)
                            {
                                _volumeWindow?.HideOverlay();
                            }
                        }));
                    }, null, 1000, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Track Volume Error] Show failed: {ex.Message}");
            }
        }));
    }

    public void ShowMasterVolumeInfo(int channelIndex, string stemName, float hardwareVolumePercent, float? audioVolumePercent = null)
    {
        if (_dispatcher == null || _volumeWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _volumeDismissTimer?.Dispose();
                    _monitorWindow?.HideWindow();
                    _hudWindow?.HideHud();
                    _assignmentWindow?.HideWindow();
                    _clearWindow?.HideWindow();

                    string stemTitle = string.IsNullOrWhiteSpace(stemName) ? "Unassigned" : stemName;
                    string tagText = $"CHANNEL {channelIndex + 1}  ●  {stemTitle}";
                    string titleText = "Master Volume";
                    string labelText = $"Fader {channelIndex + 1}";

                    _volumeWindow.UpdateVolumeDisplay(tagText, titleText, labelText, hardwareVolumePercent, isMasterVolume: true, audioVolumePercent: audioVolumePercent);
                    _volumeWindow.ShowOverlay();

                    _volumeDismissTimer = new Timer(_ =>
                    {
                        _dispatcher?.BeginInvoke(new Action(() =>
                        {
                            lock (_lock)
                            {
                                _volumeWindow?.HideOverlay();
                            }
                        }));
                    }, null, 1000, Timeout.Infinite);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Master Volume Error] Show failed: {ex.Message}");
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
                    _volumeDismissTimer?.Dispose();
                    _monitorWindow?.HideWindow();
                    _hudWindow?.HideHud();
                    _volumeWindow?.HideOverlay();
                    _clearWindow?.HideWindow();

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

    public void ShowClearConfirmation(int channelIndex, Stem? stem)
    {
        if (_dispatcher == null || _clearWindow == null) return;

        _dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                lock (_lock)
                {
                    _dismissTimer?.Dispose();
                    _volumeDismissTimer?.Dispose();
                    _monitorWindow?.HideWindow();
                    _hudWindow?.HideHud();
                    _volumeWindow?.HideOverlay();
                    _assignmentWindow?.HideWindow();

                    _clearWindow.UpdateDisplay(channelIndex, stem);
                    _clearWindow.ShowWindow();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HUD Clear Error] Show failed: {ex.Message}");
            }
        }));
    }

    public void CloseClearConfirmation()
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
                Console.WriteLine($"[HUD Clear Error] Close failed: {ex.Message}");
            }
        }));
    }

    public void Dispose()
    {
        _dismissTimer?.Dispose();
        _volumeDismissTimer?.Dispose();
        _monitorDismissTimer?.Dispose();

        if (_dispatcher != null)
        {
            _dispatcher.BeginInvoke(new Action(() =>
            {
                _hudWindow?.Close();
                _hudWindow = null;
                _assignmentWindow?.Close();
                _assignmentWindow = null;
                _volumeWindow?.Close();
                _volumeWindow = null;
                _clearWindow?.Close();
                _clearWindow = null;
                _monitorWindow?.Close();
                _monitorWindow = null;
                System.Windows.Application.Current?.Shutdown();
            }));
        }

        Console.WriteLine("[HUD] Disposed cleanly.");
    }
}
