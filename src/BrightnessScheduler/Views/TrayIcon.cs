// Copyright 2026 DevCol
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Drawing;
using System.Linq;
using System.Windows;
using BrightnessScheduler.Localization;
using BrightnessScheduler.Services;
using BrightnessScheduler.ViewModels;
using WinForms = System.Windows.Forms;

namespace BrightnessScheduler.Views;

/// <summary>System-tray icon with quick actions.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly SchedulerService _scheduler;
    private readonly MainViewModel _vm;
    private readonly WinForms.NotifyIcon _icon;
    private readonly WinForms.ContextMenuStrip _menu;
    private readonly WinForms.ToolStripMenuItem _status, _open, _apply, _modes, _back, _nightLight, _pause1h, _pauseNext, _pauseForever, _resume, _exit;
    private readonly Icon _dayIcon, _nightIcon, _pausedIcon;

    public TrayIcon(SchedulerService scheduler, MainViewModel vm, Action showWindow, Action exit)
    {
        _scheduler = scheduler;
        _vm = vm;
        _dayIcon = LoadIcon("tray_day.ico");
        _nightIcon = LoadIcon("tray_night.ico");
        _pausedIcon = LoadIcon("tray_paused.ico");

        _menu = new WinForms.ContextMenuStrip { ShowImageMargin = false };
        _status = new WinForms.ToolStripMenuItem { Enabled = false };
        _open = new WinForms.ToolStripMenuItem("", null, (_, _) => showWindow());
        _open.Font = new Font(_open.Font, System.Drawing.FontStyle.Bold);
        _apply = new WinForms.ToolStripMenuItem("", null, (_, _) => _scheduler.ApplyNow());
        _modes = new WinForms.ToolStripMenuItem("");
        ((WinForms.ToolStripDropDownMenu)_modes.DropDown).ShowCheckMargin = true;
        _back = new WinForms.ToolStripMenuItem("", null, (_, _) => _scheduler.ClearOverride());
        _nightLight = new WinForms.ToolStripMenuItem("", null, (_, _) => { _vm.NightLightEnabled = !_vm.NightLightEnabled; Refresh(); });
        _pause1h = new WinForms.ToolStripMenuItem("", null, (_, _) => _scheduler.Pause(TimeSpan.FromHours(1)));
        _pauseNext = new WinForms.ToolStripMenuItem("", null, (_, _) => _scheduler.PauseUntilNext());
        _pauseForever = new WinForms.ToolStripMenuItem("", null, (_, _) => _scheduler.Pause(null));
        _resume = new WinForms.ToolStripMenuItem("", null, (_, _) => _scheduler.Resume());
        _exit = new WinForms.ToolStripMenuItem("", null, (_, _) => exit());
        _menu.Items.AddRange(new WinForms.ToolStripItem[]
        {
            _status, new WinForms.ToolStripSeparator(),
            _open, _apply, _modes, _back, _nightLight, new WinForms.ToolStripSeparator(),
            _pause1h, _pauseNext, _pauseForever, _resume, new WinForms.ToolStripSeparator(),
            _exit,
        });

        _icon = new WinForms.NotifyIcon { Icon = _dayIcon, ContextMenuStrip = _menu, Visible = true };
        _icon.MouseClick += (_, e) => { if (e.Button == WinForms.MouseButtons.Left) showWindow(); };

        _scheduler.StateChanged += (_, _) => Refresh();
        Loc.LanguageChanged += Refresh;
        Refresh();
    }

    private static Icon LoadIcon(string name)
    {
        var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"));
        using var s = info!.Stream;
        return new Icon(s, WinForms.SystemInformation.SmallIconSize);
    }

    private void Refresh()
    {
        bool paused = _scheduler.IsPaused;
        string title = _vm.ActiveTitle;
        string sub = paused ? _vm.ActiveSubtitle : _vm.NextText;

        _status.Text = $"{title}  ·  {sub}";
        _open.Text = Loc.T("Tray_Open");
        _apply.Text = Loc.T("ApplyNow");
        _pause1h.Text = Loc.T("Pause1h");
        _pauseNext.Text = Loc.T("PauseNext");
        _pauseForever.Text = Loc.T("PauseForever");
        _resume.Text = Loc.T("Resume");
        _exit.Text = Loc.T("Tray_Exit");

        _modes.Text = Loc.T("SwitchMode");
        _back.Text = Loc.T("BackToSchedule");
        _back.Visible = _scheduler.IsOverridden;
        _modes.DropDownItems.Clear();
        foreach (var entry in _vm.Entries.Where(en => en.Enabled))
        {
            var e = entry;
            var item = new WinForms.ToolStripMenuItem($"{e.DisplayName}  ({e.TimeText})", null, (_, _) => _scheduler.SetOverride(e.Model))
            {
                Checked = e.IsActive,
            };
            _modes.DropDownItems.Add(item);
        }
        _modes.Visible = _vm.ShowModeSwitcher;
        _nightLight.Text = (_vm.NightLightEnabled ? "\u2713  " : "      ") + Loc.T("NightLight");
        _nightLight.Visible = _vm.NightLightSupported;

        _pause1h.Visible = _pauseNext.Visible = _pauseForever.Visible = !paused;
        _resume.Visible = paused;
        _apply.Enabled = !paused;

        _icon.Icon = paused ? _pausedIcon : _vm.ActiveLevel < 0.6 ? _nightIcon : _dayIcon;
        var tip = $"{Loc.T("AppName")}\n{title}\n{sub}";
        _icon.Text = tip.Length > 127 ? tip[..127] : tip;
    }

    public void Notify(string title, string text)
    {
        _icon.ShowBalloonTip(3000, title, text, WinForms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        Loc.LanguageChanged -= Refresh;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }
}
