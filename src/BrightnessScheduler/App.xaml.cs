using System;
using System.Linq;
using System.Threading;
using System.Windows;
using BrightnessScheduler.Localization;
using BrightnessScheduler.Models;
using BrightnessScheduler.Services;
using BrightnessScheduler.ViewModels;
using BrightnessScheduler.Views;

namespace BrightnessScheduler;

public partial class App : Application
{
    private const string MutexName = @"Local\BrightnessScheduler.SingleInstance";
    private const string ShowEventName = @"Local\BrightnessScheduler.Show";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private AppSettings _settings = new();
    private SchedulerService? _scheduler;
    private MainViewModel? _vm;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private bool _exiting;
    private bool _toldAboutTray;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
        {
            Uninstall();
            return;
        }

        // ---- single instance: a second launch just brings the window up ----
        _mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { /* ignore */ }
            _mutex = null;
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        new Thread(() =>
        {
            while (_showEvent.WaitOne())
            {
                if (_exiting) break;
                Dispatcher.BeginInvoke(ShowMainWindow);
            }
        }) { IsBackground = true, Name = "ShowEventListener" }.Start();

        DispatcherUnhandledException += (_, ex) => { Logger.Error("Unhandled UI exception", ex.Exception); ex.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Logger.Error("Unhandled exception", ex.ExceptionObject as Exception);

        Logger.Info($"Starting {string.Join(' ', e.Args)}");
        _settings = SettingsService.Load();
        Loc.Instance.SetLanguage(_settings.Language);
        ApplyTheme(_settings.Theme);
        StartupService.Apply(_settings.RunAtStartup);

        _scheduler = new SchedulerService(_settings, Dispatcher);
        _vm = new MainViewModel(_settings, _scheduler);
        _tray = new TrayIcon(_scheduler, _vm, ShowMainWindow, ExitApp);
        _scheduler.EntryStarted += entry =>
        {
            if (_settings.ShowNotifications)
                _tray.Notify(Loc.T("AppName"), Loc.F("Notify_AppliedFmt", string.IsNullOrWhiteSpace(entry.Name) ? Loc.T("Unnamed") : entry.Name));
        };

        bool startHidden = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase)
                                        || a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        if (!startHidden) ShowMainWindow();

        await _vm.DetectDisplaysAsync();
        _scheduler.Start();
    }

    /// <summary>Removes everything the app put on the system (autostart entry, settings, log).</summary>
    private void Uninstall()
    {
        Loc.Instance.SetLanguage(SettingsService.Load().Language);
        var answer = MessageBox.Show(Loc.F("UninstallConfirmFmt", SettingsService.DataFolder), Loc.T("AppName"),
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.OK)
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("BrightnessScheduler"))
                if (p.Id != Environment.ProcessId) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
            StartupService.Apply(false);
            try
            {
                if (System.IO.Path.GetFullPath(SettingsService.DataFolder).TrimEnd('\\') != System.IO.Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'))
                    System.IO.Directory.Delete(SettingsService.DataFolder, true);
                else
                {
                    System.IO.File.Delete(SettingsService.SettingsPath);
                    System.IO.File.Delete(Logger.LogPath);
                }
            }
            catch { /* already gone */ }
            MessageBox.Show(Loc.T("UninstallDone"), Loc.T("AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        Shutdown();
    }

    public static void ApplyTheme(string theme)
    {
        Current.ThemeMode = theme switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => ThemeMode.System,
        };
    }

    private void ShowMainWindow()
    {
        if (_vm == null) return;
        if (_window == null)
        {
            _window = new MainWindow { DataContext = _vm };
            _window.Closing += (_, ev) =>
            {
                if (_exiting) return;
                ev.Cancel = true;
                _vm.Flush();
                _window.Hide();
                if (!_toldAboutTray)
                {
                    _toldAboutTray = true;
                    _tray?.Notify(Loc.T("AppName"), Loc.T("Tray_StillRunning"));
                }
            };
        }
        _vm.RefreshStatus();
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Topmost = true;   // reliably bring to front
        _window.Topmost = false;
    }

    private void ExitApp()
    {
        _exiting = true;
        _vm?.Flush();
        _scheduler?.Dispose();
        _tray?.Dispose();
        _showEvent?.Set();
        _window?.Close();
        Logger.Info("Exit");
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { /* ignore */ }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
