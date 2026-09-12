# AGENTS.md

Apple Music (Windows 11 Store 版) の再生情報をタスクバー上に表示する常駐ウィジェット。設計は `PLAN.md`（§0 改訂 v2 が最優先）、実機で確認した Apple Music / GSMTC の挙動は `docs/APPLE_MUSIC_CAPABILITY_MATRIX.md` を参照。

## 構成

- `src/AppleMusicWidget/` — 本体（WPF, `net10.0-windows10.0.19041.0`）
  - `Lifecycle/` 起動・終了検知と状態機械、`Media/` GSMTC 接続と再生位置補間、`Artwork/` サムネイル復号、`Services/` タスクバー追従・トレイ・設定、`UI/` タスクバー帯とフライアウト
- `src/AppleMusicWidget.Probe/` — Phase 0 の検証コンソール（GSMTC / プロセス検知 / CPU 計測）
- `tools/GsmtcCtl/` — Apple Music の GSMTC セッションを CLI で操作・観察（`status|play|pause|toggle|next|prev|seek|watchpos`）
- `tools/*.ps1` — 検証用スクリプト（帯のスクリーンショット、UIA 読み取り、CPU 計測、タスクバー計測）。出力は `tools/out/`（git 管理外）

## ビルドと実行

```powershell
dotnet build src\AppleMusicWidget\AppleMusicWidget.csproj          # 0 warnings / 0 errors を維持
dotnet build src\AppleMusicWidget\AppleMusicWidget.csproj -c Release
Stop-Process -Name AppleMusicWidget -ErrorAction SilentlyContinue   # 旧ビルドが動いていると出力がロックされる
& src\AppleMusicWidget\bin\Debug\net10.0-windows10.0.19041.0\AppleMusicWidget.exe
```

- Debug ビルドは `%TEMP%\amw-debug.log` に `Debug.WriteLine` を書き出す（`App.OnStartup`、`#if DEBUG`）。
- `UseWindowsForms` が有効なため `Application` / `Timer` / `Color` などは `System.Windows.*` を完全修飾する。
- PowerShell で `$` を含むワンライナーをネスト実行すると展開されて壊れる。検証スクリプトは `tools/*.ps1` にファイルとして置き、`powershell.exe -NoProfile -ExecutionPolicy Bypass -File` で実行する。

## 実機検証の定石

- Apple Music 起動: `Start-Process "shell:AppsFolder\AppleInc.AppleMusicWin_nzyj5cx40ttqa!App"`、終了: `(Get-Process AppleMusic).CloseMainWindow()`
- 帯のスクリーンショット: `tools/cap-widget.ps1 -ProcessId <pid> -OutPath tools/out/x.png`（PerMonitorV2 で矩形を取る）
- 帯の文字列を UIA で読む: `tools/read-widget.ps1`
- CPU: `tools/measure-cpu.ps1`（`TotalProcessorTime` の 10 秒差分）
- UIA の `Invoke()` はフォーカスを奪うため NOACTIVATE の検証には実クリック（`SetCursorPos` + `mouse_event`）を使う

## Git 運用

- `main` には直接コミットしない。作業は `feature/` `fix/` `chore/` `docs/` ブランチで行い、`--ff-only` で `main` に取り込む。
- コミットは巻戻し単位で分け、メッセージは日本語の固定構造（`<種別>(<対象>): <要約>` + 理由 / 内容 / 確認）。
- `bin/ obj/ tools/out/ *.log` は管理外。検証スクリーンショットはコミットせず、報告に添える。
