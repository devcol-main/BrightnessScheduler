// Copyright 2026 DevCol
// SPDX-License-Identifier: Apache-2.0

using System.Windows;
using System.Windows.Controls.Primitives;
using BrightnessScheduler.Localization;
using BrightnessScheduler.ViewModels;

namespace BrightnessScheduler.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        FillEnforceOptions();
        Loc.LanguageChanged += FillEnforceOptions;
        Closed += (_, _) => Loc.LanguageChanged -= FillEnforceOptions;
    }

    private void FillEnforceOptions()
    {
        int index = (DataContext as MainViewModel)?.EnforceIndex ?? EnforceCombo.SelectedIndex;
        EnforceCombo.Items.Clear();
        EnforceCombo.Items.Add(Loc.T("Off"));
        foreach (var m in new[] { 5, 15, 30, 60 })
            EnforceCombo.Items.Add(Loc.F("EveryMinFmt", m));
        EnforceCombo.SelectedIndex = index < 0 ? 0 : index;
    }

    private System.Windows.Threading.DispatcherTimer? _nightLightKeyTimer;

    /// <summary>
    /// Keyboard: apply 1 second after the last key press, so tapping the arrow keys
    /// several times opens Settings only once.
    /// </summary>
    private void NightLightSlider_KeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_nightLightKeyTimer == null)
        {
            _nightLightKeyTimer = new System.Windows.Threading.DispatcherTimer { Interval = System.TimeSpan.FromSeconds(1) };
            _nightLightKeyTimer.Tick += (_, _) =>
            {
                _nightLightKeyTimer.Stop();
                (DataContext as MainViewModel)?.CommitNightLightStrength();
            };
        }
        _nightLightKeyTimer.Stop();
        _nightLightKeyTimer.Start();
    }

    /// <summary>Mouse: apply once when the slider is released (or loses focus).</summary>
    private void NightLightSlider_Release(object sender, RoutedEventArgs e)
    {
        _nightLightKeyTimer?.Stop();
        // Let the slider finish updating its value first.
        Dispatcher.BeginInvoke(() => (DataContext as MainViewModel)?.CommitNightLightStrength(),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        PauseMenu.DataContext = DataContext;
        PauseMenu.PlacementTarget = PauseButton;
        PauseMenu.Placement = PlacementMode.Bottom;
        PauseMenu.IsOpen = true;
    }
}
