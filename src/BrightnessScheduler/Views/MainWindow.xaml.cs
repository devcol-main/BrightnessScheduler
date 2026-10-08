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

    /// <summary>Night Light strength is applied only when the slider is released (mouse up / key up).</summary>
    private void NightLightSlider_Release(object sender, RoutedEventArgs e)
    {
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
