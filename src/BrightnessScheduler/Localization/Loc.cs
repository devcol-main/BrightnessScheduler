// Copyright 2026 DevCol
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace BrightnessScheduler.Localization;

/// <summary>Tiny runtime-switchable string table (Korean / English).</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    public static event Action? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public string Language { get; private set; } = "en";
    public CultureInfo Culture => Language == "ko" ? new CultureInfo("ko-KR") : new CultureInfo("en-US");

    public string this[string key] => Get(key);

    public static string T(string key) => Instance.Get(key);
    public static string F(string key, params object[] args) => string.Format(Instance.Culture, T(key), args);

    public void SetLanguage(string setting)
    {
        var lang = setting;
        if (lang is not ("ko" or "en"))
            lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko" ? "ko" : "en";
        Language = lang;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        LanguageChanged?.Invoke();
    }

    private string Get(string key)
        => Strings.TryGetValue(key, out var s) ? (Language == "ko" ? s.Ko : s.En) : key;

    public static string DayShort(int dayOfWeek) => T("Day" + dayOfWeek);

    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        int h = (int)span.TotalHours, m = span.Minutes;
        if (span.TotalMinutes < 1) return T("DurLessThanMinute");
        return h > 0 ? F("DurHM", h, m) : F("DurM", m);
    }

    private static readonly Dictionary<string, (string Ko, string En)> Strings = new()
    {
        ["AppName"] = ("Brightness Scheduler", "Brightness Scheduler"),
        ["AppTagline"] = ("화면 밝기 · 대비 자동 스케줄러", "Automatic brightness & contrast scheduler"),

        ["Nav_Dashboard"] = ("대시보드", "Dashboard"),
        ["Nav_Schedule"] = ("스케줄", "Schedule"),
        ["Nav_Settings"] = ("설정", "Settings"),
        ["Nav_About"] = ("정보", "About"),

        ["CurrentSchedule"] = ("현재 일정", "Current Schedule"),
        ["NoActive"] = ("활성 일정 없음", "No active entry"),
        ["SinceFmt"] = ("{0}부터 적용 중", "Active since {0}"),
        ["NextFmt"] = ("다음: {0} · {1} ({2} 후)", "Next: {0} at {1} (in {2})"),
        ["NoNext"] = ("예정된 변경 없음", "No upcoming change"),
        ["ApplyNow"] = ("지금 적용", "Apply now"),
        ["Pause"] = ("일시정지", "Pause"),
        ["Pause1h"] = ("1시간 동안 일시정지", "Pause for 1 hour"),
        ["PauseNext"] = ("다음 일정까지 일시정지", "Pause until next change"),
        ["PauseForever"] = ("다시 켤 때까지 일시정지", "Pause until resumed"),
        ["Resume"] = ("재개", "Resume"),
        ["PausedUntilFmt"] = ("일시정지됨 · {0}까지", "Paused until {0}"),
        ["PausedForever"] = ("일시정지됨", "Paused"),
        ["TodayTimeline"] = ("오늘의 타임라인", "Today's Timeline"),
        ["SwitchMode"] = ("모드 전환", "Switch Mode"),
        ["SwitchModeDesc"] = ("선택한 모드를 지금 바로 적용합니다. 다음 일정이 시작되면 자동으로 스케줄로 돌아가요.",
                              "Apply a mode right now. The schedule takes over again at the next change."),
        ["OverrideUntilFmt"] = ("임시 적용 중 · {0}에 스케줄로 복귀", "Temporary · back to schedule at {0}"),
        ["OverrideForever"] = ("임시 적용 중", "Temporary override"),
        ["BackToSchedule"] = ("스케줄로 돌아가기", "Back to Schedule"),

        ["Displays"] = ("디스플레이", "Displays"),
        ["DisplaysDesc"] = ("슬라이더로 바로 조절할 수 있어요. 다음 일정이 시작되면 스케줄 값으로 바뀝니다.",
                            "Adjust instantly with the sliders. The schedule takes over at the next change."),
        ["Refresh"] = ("새로고침", "Refresh"),
        ["Brightness"] = ("밝기", "Brightness"),
        ["Contrast"] = ("대비", "Contrast"),
        ["NotSupported"] = ("지원 안 함", "Not supported"),
        ["NoDisplays"] = ("조절 가능한 디스플레이를 찾지 못했어요. 외부 모니터라면 OSD 메뉴에서 DDC/CI를 켜 주세요.",
                          "No controllable displays found. For external monitors, enable DDC/CI in the monitor's OSD menu."),
        ["Detecting"] = ("디스플레이 검색 중…", "Detecting displays…"),
        ["BuiltInDisplay"] = ("내장 디스플레이", "Built-in display"),
        ["DisplayNumFmt"] = ("디스플레이 {0}", "Display {0}"),

        ["NightLight"] = ("야간 모드", "Night Light"),
        ["NightLightDesc"] = ("Windows 야간 모드 (블루라이트 감소)", "Windows Night Light (blue light reduction)"),
        ["Strength"] = ("강도", "Strength"),
        ["NightLightChange"] = ("변경", "Change"),
        ["NightLightTurnOn"] = ("켜기", "On"),
        ["NightLightWinSchedule"] = ("Windows 자체 야간 모드 일정이 켜져 있어 이 앱의 스케줄과 겹칠 수 있어요.",
                                     "Windows' own Night Light schedule is on and may conflict with this app's schedule."),
        ["NightLightDisableWinSchedule"] = ("Windows 일정 끄기", "Turn off Windows schedule"),
        ["NightLightHow"] = ("켜고 끌 때 Windows 설정 창이 1초 정도 열렸다 닫힙니다.", "The Windows Settings window opens for about a second when it changes."),
        ["NightLightUnknown"] = ("현재 상태 확인 전 (마지막으로 적용한 값 표시)", "Not checked yet (showing last applied value)"),
        ["NightLightCheck"] = ("상태 확인", "Check state"),
        ["ScheduleTitle"] = ("스케줄", "Schedule"),
        ["ScheduleDesc"] = ("각 일정은 시작 시각부터 다음 일정 전까지 유지됩니다. 체크한 항목만 변경돼요.",
                            "Each entry stays in effect from its start time until the next one. Only checked items are changed."),
        ["AddEntry"] = ("일정 추가", "Add entry"),
        ["Preview"] = ("미리 적용", "Preview"),
        ["Delete"] = ("삭제", "Delete"),
        ["StartsAt"] = ("시작 시각", "Starts at"),
        ["RepeatOn"] = ("반복 요일", "Repeat on"),
        ["NewEntry"] = ("새 일정", "New entry"),
        ["DefaultNight"] = ("야간", "Night"),
        ["DefaultDay"] = ("주간", "Day"),
        ["DeleteConfirmFmt"] = ("'{0}' 일정을 삭제할까요?", "Delete '{0}'?"),
        ["NoEntries"] = ("아직 일정이 없어요. '일정 추가'를 눌러 시작하세요.", "No entries yet. Click 'Add entry' to get started."),
        ["EntryEnabled"] = ("사용", "Enabled"),
        ["Unnamed"] = ("(이름 없음)", "(unnamed)"),
        ["ActiveBadge"] = ("적용 중", "Active"),

        ["Day0"] = ("일", "Sun"), ["Day1"] = ("월", "Mon"), ["Day2"] = ("화", "Tue"), ["Day3"] = ("수", "Wed"),
        ["Day4"] = ("목", "Thu"), ["Day5"] = ("금", "Fri"), ["Day6"] = ("토", "Sat"),

        ["SettingsTitle"] = ("설정", "Settings"),
        ["General"] = ("일반", "General"),
        ["Behavior"] = ("동작", "Behavior"),
        ["Language"] = ("언어", "Language"),
        ["LanguageDesc"] = ("앱 표시 언어", "Display language"),
        ["Lang_Auto"] = ("시스템 언어 따르기", "Use system language"),
        ["Theme"] = ("테마", "Theme"),
        ["ThemeDesc"] = ("앱 색상 모드", "App color mode"),
        ["Theme_System"] = ("시스템 설정 따르기", "Use system setting"),
        ["Theme_Light"] = ("라이트", "Light"),
        ["Theme_Dark"] = ("다크", "Dark"),
        ["RunAtStartup"] = ("Windows 시작 시 자동 실행", "Start with Windows"),
        ["RunAtStartupDesc"] = ("로그인하면 트레이에서 조용히 실행됩니다", "Runs quietly in the system tray after you sign in"),
        ["Transition"] = ("부드러운 전환", "Smooth transition"),
        ["TransitionDesc"] = ("일정이 바뀔 때 이 시간에 걸쳐 천천히 변경합니다", "Fade gradually over this time when the schedule changes"),
        ["Instant"] = ("즉시", "Instant"),
        ["SecondsFmt"] = ("{0}초", "{0} s"),
        ["MinSecFmt"] = ("{0}분 {1}초", "{0} min {1} s"),
        ["MinutesOnlyFmt"] = ("{0}분", "{0} min"),
        ["Reapply"] = ("자동 재적용", "Re-apply automatically"),
        ["ReapplyDesc"] = ("절전 해제 · 잠금 해제 · 모니터 연결 시 현재 일정을 다시 적용합니다",
                           "Re-apply the current entry after wake, unlock or display changes"),
        ["Enforce"] = ("주기적 재적용", "Periodic re-apply"),
        ["EnforceDesc"] = ("모니터가 값을 잃는 경우를 대비해 주기적으로 다시 적용합니다 (직접 조절한 값도 덮어씀)",
                           "Re-apply on an interval in case a monitor resets (overrides manual changes)"),
        ["Off"] = ("끄기", "Off"),
        ["EveryMinFmt"] = ("{0}분마다", "Every {0} min"),
        ["Notifications"] = ("알림", "Notifications"),
        ["NotificationsDesc"] = ("일정이 바뀌면 알림을 표시합니다", "Show a notification when the schedule changes"),
        ["DataFolder"] = ("데이터 폴더", "Data folder"),
        ["DataFolderDesc"] = ("설정(settings.json)과 로그가 저장되는 위치", "Where settings.json and the log are stored"),
        ["Open"] = ("열기", "Open"),

        ["AboutDesc"] = ("노트북 내장 화면의 밝기와 외부 모니터(DDC/CI)의 밝기·대비를 시간표에 따라 자동으로 조절합니다. 다른 프로그램 없이 단독으로 동작합니다.",
                         "Automatically adjusts your laptop panel brightness and external monitor (DDC/CI) brightness & contrast on a schedule. Runs on its own — no other tools required."),
        ["VersionFmt"] = ("버전 {0}", "Version {0}"),
        ["ViewOnGitHub"] = ("GitHub에서 보기", "View on GitHub"),
        ["OpenLog"] = ("로그 보기", "Open log"),
        ["Tips"] = ("도움말", "Tips"),
        ["Tip1"] = ("외부 모니터는 모니터 OSD 메뉴에서 DDC/CI가 켜져 있어야 조절됩니다.",
                    "External monitors must have DDC/CI enabled in their on-screen menu."),
        ["Tip2"] = ("노트북 밝기가 저절로 다시 바뀐다면 Windows 설정 › 디스플레이에서 '자동 밝기 / 콘텐츠 기반 밝기'를 꺼 주세요.",
                    "If laptop brightness keeps changing on its own, turn off auto / content-adaptive brightness in Windows Settings › Display."),
        ["Tip3"] = ("창을 닫아도 트레이에서 계속 실행됩니다. 완전히 끄려면 트레이 아이콘 › 종료를 선택하세요.",
                    "Closing the window keeps the app running in the tray. Use tray icon › Exit to quit."),
        ["License"] = ("Apache License 2.0 · © 2026 DevCol", "Apache License 2.0 · © 2026 DevCol"),

        ["UninstallConfirmFmt"] = ("자동 실행 등록과 설정·로그 폴더를 삭제할까요?\n\n{0}\n\n삭제 후 exe 파일만 지우면 완전히 제거됩니다.",
                                   "Remove the autostart entry and delete settings & log?\n\n{0}\n\nAfterwards just delete the exe to finish uninstalling."),
        ["UninstallDone"] = ("정리가 끝났습니다. 이제 exe 파일을 삭제하면 됩니다.", "Done. You can now delete the exe file."),
        ["Uninstall"] = ("제거", "Uninstall"),
        ["UninstallDesc"] = ("자동 실행 등록과 설정·로그를 삭제하고 종료합니다", "Remove autostart, settings and log, then exit"),
        ["Tray_Open"] = ("열기", "Open"),
        ["Tray_Exit"] = ("종료", "Exit"),
        ["Tray_StillRunning"] = ("트레이에서 계속 실행 중입니다.", "Still running in the system tray."),
        ["Notify_AppliedFmt"] = ("'{0}' 일정이 적용되었습니다.", "'{0}' is now active."),

        ["DurHM"] = ("{0}시간 {1}분", "{0} h {1} min"),
        ["DurM"] = ("{0}분", "{0} min"),
        ["DurLessThanMinute"] = ("1분 미만", "< 1 min"),
    };
}

/// <summary>XAML: Text="{l:T Key}"</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string key) { Key = key; }

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
