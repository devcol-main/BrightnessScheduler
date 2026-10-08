# Changelog

All notable changes to Brightness Scheduler are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).
한국어 설명은 각 항목 아래에 함께 적습니다. 처음 기획은 [docs/PLANNING.md](docs/PLANNING.md)를 참고하세요.

## [Unreleased]

### Added
- `CHANGELOG.md` and `docs/PLANNING.md` to keep the original plan and per-version changes in the repository.
  처음 기획과 버전별 변경 내역을 저장소에 기록합니다.
- Release workflow now uses the matching CHANGELOG section as the GitHub release notes.
  릴리스 시 CHANGELOG의 해당 버전 내용이 GitHub 릴리스 본문으로 들어갑니다.

## [1.2.0] - 2026-10-08

### Added
- **Mode switcher**: apply any schedule entry right now from the dashboard or the tray menu (e.g. Night mode during the day). The schedule takes over again at the next scheduled change; *Back to Schedule* returns immediately.
  **모드 전환**: 대시보드·트레이에서 원하는 일정을 즉시 적용하고, 다음 일정 시각에 자동으로 스케줄로 복귀합니다. "스케줄로 돌아가기"로 바로 복귀할 수도 있습니다.
- Dashboard status shows "Temporary · back to schedule at HH:mm" while an override is active.
  임시 적용 중에는 대시보드에 복귀 시각을 표시합니다.
- `NOTICE` file; `LICENSE` and `NOTICE` are attached to every release.
  `NOTICE` 파일을 추가하고 릴리스에 `LICENSE`, `NOTICE`를 함께 첨부합니다.

### Changed
- **License changed from MIT to Apache License 2.0.** SPDX headers added to all source files.
  **라이선스를 MIT에서 Apache 2.0으로 변경**하고 모든 소스 파일에 SPDX 헤더를 추가했습니다.
- English UI titles use Title Case (e.g. "Current Schedule", "Today's Timeline").
  영어 UI 제목을 Title Case로 통일했습니다.
- Updated README (EN/KO), screenshots, and CODE_SIGNING.md for the new license and feature.
  README(영/한), 스크린샷, 코드 서명 문서를 갱신했습니다.

### Internal
- `SchedulerService`: `SetOverride` / `ClearOverride`, `OverrideEntry`, `OverrideUntil`, `EffectiveEntry`. An override ends automatically at the next change, or when its entry is disabled or deleted. Pausing still takes priority.
- Mode switcher is shown only when two or more entries are enabled.

## [1.1.0] - 2026-10-07

First public release. 첫 공개 릴리스.

### Added
- Time-based schedule with unlimited entries; each entry lasts until the next one and wraps past midnight.
  시간 기반 스케줄(개수 제한 없음, 자정을 넘겨 이어짐).
- Per-display, per-value brightness/contrast; unchecked values are left untouched.
  디스플레이별·항목별 밝기/대비 설정.
- Day-of-week repeat.
  요일 반복.
- Laptop panel via **WMI**, external monitors via **DDC/CI** (VCP `0x10` brightness, `0x12` contrast).
  노트북은 WMI, 외부 모니터는 DDC/CI로 제어.
- Live manual sliders, smooth transitions (up to 10 minutes).
  실시간 슬라이더, 부드러운 전환.
- Re-apply after sleep/unlock/display change, optional periodic re-apply.
  절전·잠금 해제·모니터 연결 시 재적용, 주기적 재적용 옵션.
- Pause for 1 hour / until next entry / until resumed.
  일시정지(1시간 / 다음 일정까지 / 다시 켤 때까지).
- Tray app with day/night/paused icons, run at Windows startup, notifications.
  트레이 상주, 시작 시 자동 실행, 알림.
- Today's timeline, light/dark theme, Korean/English UI.
  오늘의 타임라인, 라이트/다크 테마, 한국어/영어 UI.
- JSON settings with portable mode; `--tray` and `--uninstall` command-line options.
  JSON 설정, 포터블 모드, 명령줄 옵션.
- WPF on **.NET 10**; self-contained single-file exe and small framework-dependent exe.
  .NET 10 WPF, 단일 파일 exe와 소형 exe 두 가지 배포.
- GitHub Actions release workflow with SHA256SUMS and SignPath code-signing hook.
  GitHub Actions 릴리스 자동화, SHA256 체크섬, SignPath 코드 서명 연동.

[Unreleased]: https://github.com/devcol-main/BrightnessScheduler/compare/v1.2.0...HEAD
[1.2.0]: https://github.com/devcol-main/BrightnessScheduler/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/devcol-main/BrightnessScheduler/releases/tag/v1.1.0
