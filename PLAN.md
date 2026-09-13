# Apple Music Windows Widget — PLAN

> **改訂 v2（2026-09-12）: 配置先をデスクトップからタスクバーへ変更。**
> 変更点は §0 に集約する。§2 / §3.3 / §9 / §11 の記述のうち §0 と矛盾する箇所は §0 を優先する。
> §1 の要件（起動連動・低負荷・除外UI）、§4〜§8 のライフサイクル / GSMTC 設計、Phase 0〜2 の成果はそのまま有効。

## 0. 改訂 v2 — タスクバー配置

### 0.1 配置

- Windows 11 のタスクバー上、**通知領域（トレイ）の左隣**に常時表示する。
- Windows 11 は DeskBand（タスクバーツールバー）を廃止しているため、タスクバーへの正式な埋め込み手段はない。
  **最前面のツールウィンドウをタスクバー帯に重ねるオーバーレイ方式**を採用する。
- 実測（メイン 2560px / 48px 高）: Start 808–853、アプリ領域 853–1443、トレイ 2307–2560。
  ウィジェット右端 = トレイ左端 − 余白。アプリ領域は中央から対称に広がるため、衝突時はコンパクト表示へ縮める。

### 0.2 見た目 — Windows デザインシステム

- WPF Fluent テーマ（.NET 10 同梱 `PresentationFramework.Fluent`）を `ThemeMode="System"` で使用し、ライト / ダーク / アクセント色に追従する。
- グリフは `Segoe Fluent Icons`（再生 E768 / 一時停止 E769 / 前 E892 / 次 E893 / … E712 / リスト E90B）。
- 背景は透過し、タスクバー自体を透かす。ホバー時のみタスクバーボタンと同じ淡い角丸ハイライト。
- テキストは Fluent の `TextFillColorPrimary` / `TextFillColorSecondary` 相当を使用し、テーマ切替に自動追従する。

### 0.3 レイアウト（48px 帯 / 幅 約 340px）

```text
┌────────────────────────────────────────────────┐
│ [Art] 曲名                        ⏮  ⏯  ⏭      │
│ 32px  アーティスト — アルバム                    │
│       ▔▔▔▔▔▔▔▔▔▔▔▔ 2px 進捗線（アクセント色）    │
└────────────────────────────────────────────────┘
```

- 経過 / 残り時間、`…`、再生待ちリストは帯に収まらないため、**クリックで上方向にフライアウト**（音量フライアウトと同形式）を開き、
  §3.3 の 350×150 カードをそこに表示する。フライアウトはフォーカス喪失で閉じる。
- 帯の高さはタスクバー高さに追従する（48px 既定。将来のサイズ変更にも対応）。

### 0.4 ウィンドウ仕様（§9 を置換）

- `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE`、`ShowInTaskbar=false`、`Topmost=true`。クリックしてもフォーカスを奪わない。
- ドラッグ移動・位置保存・最前面切替は廃止。代わりに `taskbarOffsetX`（トレイ左端からの余白）を設定に持つ。
- 追従は `SetWinEventHook`（explorer.exe の `EVENT_OBJECT_LOCATIONCHANGE` / `EVENT_OBJECT_DESTROY`）でイベント駆動。
  トレイ幅の変化、タスクバー移動、解像度 / DPI 変更、Explorer 再起動に対応する。ポーリングは行わない。
- **非表示条件**: Apple Music 非起動（従来どおり）／タスクバー自動非表示で隠れている／同モニターで全画面アプリが前面／アプリ領域と衝突して最小幅も確保できない。
- 初期版はメインタスクバー（`Shell_TrayWnd`）のみ。セカンダリ（`Shell_SecondaryTrayWnd`）は設定で将来対応。

### 0.5 アーキテクチャ追加（§11 に追記）

```text
Services/TaskbarService.cs        トレイ矩形の取得・追従イベント・全画面/自動非表示判定
UI/TaskbarStrip.xaml(.cs)         48px 帯（PlayerWidget を置換）
UI/PlayerFlyout.xaml(.cs)         クリックで開くカード（旧 PlayerWidget のレイアウトを移植）
```

### 0.6 進捗（2026-09-12 時点）

- Phase 0〜2.5 完了、Phase 3 完了（結果は `docs/APPLE_MUSIC_CAPABILITY_MATRIX.md` の Phase 3 measurements）。
  Private WS 40.7MB（再生中）、待機時 WS 34MB、再生中 CPU 0.04%。ハンドルリーク修正、WinForms 依存削除、ICU 除外。
- Phase 3 で未実施: 1 時間連続再生（20 分まで確認）、起動/終了 50 回（20 回まで確認）→ Phase 7 の品質確認で実施。
- トレイアイコンの実マウス右クリックでメニューが表示されることを確認済み（2026-09-13）。

### 0.6.1 既知の制約（Windows 11 シェル仕様由来、回避不可）

- **スタートメニュー/検索を開いている間、帯はタスクバーの後ろに隠れる。** フライアウト表示中、Explorer は `Shell_TrayWnd` を通常の TOPMOST より上の z-band に昇格させる。外部プロセスがこの帯を越える手段は `CreateWindowInBand`（uiAccess＋コード署名必須）か explorer.exe への DLL インジェクションのみで、本アプリでは採用しない。フライアウトを閉じると遮蔽ウォッチドッグ（`TaskbarStrip.IsOccluded` + 1 秒間隔の再主張）が自動復帰する。
- `Shell_TrayWnd` への子ウィンドウ埋め込み（TrafficMonitor 方式）は Win11 25H2 で描画が抑止されることを spike で確認済み（`tools/ChildSpike`）。以後の Windows 更新でこの挙動が変わる可能性はある。

### 0.7 フェーズへの影響

- Phase 2.5（新設）: TaskbarService + TaskbarStrip + テーマ追従。フライアウトは骨組みのみ。
- Phase 4（UI再現）: Fluent 仕上げ、ホバー / 押下、フライアウトのカード完成。
- Phase 6（Windows統合）: 位置保存 → offset 設定、セカンダリタスクバー、衝突時コンパクト表示。
- Phase 7（品質確認）に追加: Explorer 再起動、タスクバー自動非表示 ON/OFF、全画面アプリ、ライト⇄ダーク切替、トレイアイコン増減。

---

## 1. 目的

Windows版 Apple Music の再生情報を、独立した小型ウィジェットとしてタスクバー上に表示する（改訂 v2。旧: デスクトップ上）。

参考画像のデザインを基準にしつつ、赤線で指定された不要要素は表示しない。Apple Music 本体は再生エンジンとして使用し、本アプリは「表示」「基本的な再生操作」「Apple Musicへの導線」に専念する。

このプロジェクトでは、見た目の再現だけでなく、常駐アプリとしての軽量性を重要要件とする。

### 最重要要件

1. Apple Music が閉じている間はウィジェットを表示しない。
2. Apple Music を起動したらウィジェットを自動表示する。
3. Apple Music を終了したらウィジェットを自動非表示にする。
4. Apple Music が閉じている間は監視処理以外を休止し、CPU・メモリ負荷を最小化する。
5. Apple Music が一時停止中・未再生でも、アプリ自体が起動していればウィジェットは表示する。
6. Apple Music 以外のメディアセッションには反応しない。
7. 赤線で除外されたUIは Visual Tree 上にも原則生成しない。

---

## 2. 想定環境

- OS: Windows 11
- Apple Music: Microsoft Store版
- 実装言語: C#
- ランタイム: .NET 10
- UI: WPF
- 常駐形式: 独立したボーダーレス小型ウィンドウ
- 配置: デスクトップ上に自由配置

Windows標準の `Win + W` Widget Board には統合せず、常時表示可能な独立ウィンドウとして実装する。

理由:

- 表示位置を自由にできる
- Apple Music の起動状態と連動しやすい
- UI再現の自由度が高い
- Widget Board の制約を受けにくい
- 今回の用途ではデスクトップ常駐型の方が自然

---

## 3. UI要件

### 3.1 表示する要素

- アルバムアートワーク
- 曲名
- アーティスト名
- アルバム名
- 再生位置バー
- 経過時間
- 残り時間
- 前の曲
- 再生 / 一時停止
- 次の曲
- `…` メニュー
- 再生待ちリストボタン

### 3.2 表示しない要素

- 音量関連UI
- Lossless / Hi-Res Lossless / Dolby Atmos 等の音質表示
- 歌詞ボタン / 歌詞表示
- Windows標準タイトルバー
- 最小化ボタン
- 最大化ボタン
- 閉じるボタン
- 参考画像で赤線を引いたその他の要素

### 3.3 基本レイアウト

```text
┌──────────────────────────────────────┐
│ ┌────────┐  曲名                     │
│ │ Artwork│  アーティスト — アルバム │
│ │        │                           │
│ └────────┘  0:09 ─────────── -2:32  │
│                                      │
│        …    ◀    ▶/Ⅱ    ▶    Queue │
└──────────────────────────────────────┘
```

目安:

- 初期サイズ: 約 350 × 150 px
- アルバムアート: 約 58〜64 px
- ウィンドウ角丸: 10〜14 px 程度
- 曲名: 1行、省略表示
- アーティスト / アルバム: 1行、省略表示
- 再生バー: 細め
- 操作ボタン: 中央寄せ

### 3.4 長い文字列

長い曲名・アーティスト名・アルバム名は、レイアウトを崩さず省略表示する。

例:

```text
This Is A Very Long Song Tit…
Artist Name — Very Long Alb…
```

必要ならホバー時に Tooltip で全文を表示する。

### 3.5 背景

- アルバムアートから代表色を抽出
- 代表色をベースに暗めの背景を生成
- 単色または弱いグラデーションを使用
- 文字のコントラストを自動調整
- 曲変更時のみ色抽出を実行

色抽出処理をフレーム単位で実行しない。

---

## 4. 表示ライフサイクル

ウィジェットの表示条件は「再生状態」ではなく「Apple Music の起動状態」とする。

### 4.1 Apple Music が閉じている

状態:

```text
WaitingForAppleMusic
```

動作:

- ウィジェット非表示
- GSMTCセッション切断
- 再生位置タイマー停止
- Artwork処理停止
- 色抽出停止
- UI Animation停止
- UI Automation停止
- Apple Music の起動検知のみ継続

この状態では、アプリを実質的な休眠状態に近づける。

### 4.2 Apple Music を起動した

Apple Music プロセスを検出したら:

1. ウィジェットを表示
2. GSMTCセッションを探索
3. Apple Music のセッションへ接続
4. 再生状態を取得
5. 曲情報を取得
6. アルバムアートを取得
7. UIへ反映

Apple Music 起動直後に GSMTC セッションがまだ生成されていない場合は、短時間だけ再試行する。

### 4.3 Apple Music が未再生

Apple Music が起動している限りウィジェットは表示する。

曲情報がまだ存在しない場合:

```text
Apple Music
再生する曲を選択してください
```

または最小限の待機UIを表示する。

### 4.4 一時停止中

- ウィジェット表示を維持
- 曲情報を維持
- Artworkを維持
- 再生バー更新を停止
- 再生ボタンを Play 状態へ変更

### 4.5 再生中

- 再生バー更新
- 経過時間更新
- 再生ボタンを Pause 状態へ変更

### 4.6 Apple Music を終了した

即座に:

- ウィジェット非表示
- GSMTCセッション解放
- Artwork参照解放
- UI Automation参照解放
- Timeline Timer停止
- 曲情報キャッシュを必要最小限に整理
- Apple Music の起動監視状態へ戻る

アプリ本体は終了しない。

---

## 5. 低負荷設計

### 5.1 基本方針

高頻度ポーリングではなく、イベント駆動を優先する。

避けるもの:

- 100msごとのプロセス全走査
- 60fpsでの常時タイマー更新
- 毎フレームのGSMTC問い合わせ
- 毎フレームのArtwork再処理
- 常時UI Automation監視
- 無制限の画像キャッシュ

優先するもの:

- プロセス開始 / 終了イベント
- GSMTCイベント
- 必要時だけUI Automation
- 曲変更時だけArtwork更新
- ローカル時間補間

### 5.2 Apple Music 非起動時

理想状態:

```text
Widget Hidden
GSMTC OFF
Artwork OFF
Timeline OFF
UI Automation OFF
Process Watcher ON
```

CPU使用率はほぼ0%を目標とする。

### 5.3 Apple Music 起動検知

第一候補:

- Windowsのプロセス開始 / 終了通知
- WMI / Event-based process watcher
- その他イベントベースの監視手段

フォールバック:

- 2〜5秒程度の低頻度ポーリング

Apple Music が起動した後は、プロセス状態よりも GSMTC イベントを中心に動作する。

### 5.4 再生位置更新

毎回Apple Musicへ現在位置を問い合わせない。

GSMTCから取得した:

```text
基準再生位置
基準時刻
再生状態
```

を使用し、再生中はローカルで補間する。

例:

```text
CurrentPosition = BasePosition + (Now - BaseTimestamp)
```

UI更新頻度は 250〜500ms 程度を目安とする。

### 5.5 再同期条件

以下のイベントで再同期する。

- 曲変更
- 再生開始
- 一時停止
- シーク
- GSMTC timeline update
- Apple Music セッション再接続
- Windows スリープ復帰

---

## 6. メモリ設計

### 6.1 目標

- 上限目標: 100MB以下
- 実用目標: 50〜80MB程度
- Apple Music 非起動時: 可能な限り低く維持

数値だけでなく「時間とともに増え続けない」ことを重視する。

### 6.2 Artwork

Artworkはメモリリークの主要候補になるため、明示的に管理する。

方針:

- 原寸画像を長期間保持しない
- ウィジェット表示サイズに縮小する
- 前曲のBitmapを解放する
- 不要なStreamを即座にDispose
- 同一曲では再デコードしない
- 大量のArtworkをキャッシュしない

保持数の目安:

```text
CurrentArtwork
PreviousArtwork（必要な場合のみ）
```

最大1〜2枚程度に限定する。

### 6.3 キャッシュ

長期キャッシュを作らない。

保持可能:

- 現在曲のTrackInfo
- 現在Artwork
- 前回Artwork（フェード用、短時間のみ）
- 現在背景色

保持しない:

- 過去数十曲分の画像
- 過去曲のメタデータ履歴
- Apple Music ライブラリ全体

### 6.4 UI Automation

UI Automationオブジェクトを常駐させない。

必要な操作時のみ:

1. Apple Music ウィンドウ取得
2. 対象Element探索
3. 操作実行
4. 参照解放

---

## 7. Apple Music 連携方式

### 7.1 第一候補: GSMTC

Windows の Global System Media Transport Controls Session を利用する。

取得候補:

- 曲名
- アーティスト名
- アルバム名
- アルバムアート
- 再生状態
- 再生位置
- 曲の総時間
- 再生 / 一時停止可否
- 前曲可否
- 次曲可否
- シーク可否

操作候補:

- Play
- Pause
- Previous
- Next
- Seek

### 7.2 Apple Music セッションの限定

Windows上で複数セッションが存在しても、Apple Music だけを対象にする。

対象外:

- Chrome / Edge のYouTube
- Spotify
- Discord
- VLC
- その他メディアアプリ

Apple Music の識別方法は Phase 0 で検証する。

候補:

- SourceAppUserModelId
- Session Source
- Process IDとの関連付け
- Apple Music 固有識別子

### 7.3 GSMTCセッション消失時

Apple Music 自体が起動している場合:

- ウィジェットは閉じない
- 再接続待ち状態にする
- 短時間だけ再試行

Apple Music が終了した場合のみウィジェットを非表示にする。

---

## 8. UI Automation

GSMTCで取得・操作できないApple Music固有機能だけに限定して使用する。

対象候補:

- `…` メニュー
- 再生待ちリスト
- GSMTCで不足する一部の操作

### 8.1 `…` メニュー

第一目標:

Apple Music 本体の現在曲に対する `…` メニューを開く。

候補:

1. UI AutomationでApple Musicの対応ボタンを呼び出す
2. ウィジェット側に利用可能な操作だけ独自メニューとして再構成する

Apple Music固有機能を再現できない場合、意味の異なる操作を割り当てない。

### 8.2 再生待ちリスト

第一目標:

Apple Music 本体の「次に再生」画面を開く。

初期版ではキュー内容をウィジェット内へ再実装しない。

将来拡張:

- キュー一覧
- 曲削除
- 並び替え

---

## 9. ウィンドウ仕様

### 9.1 ボーダーレス

Windows標準タイトルバーは使用しない。

### 9.2 移動

背景の空き領域をドラッグして移動可能にする。

操作ボタンや再生バーをドラッグした場合はウィンドウ移動を発生させない。

### 9.3 位置保存

保存項目:

- X
- Y
- 使用モニター
- DPI

次回表示時に復元する。

モニターが存在しなくなった場合はメインディスプレイ内へ自動補正する。

### 9.4 最前面表示

設定で切替可能にする。

```text
Always on Top: ON / OFF
```

デフォルトは OFF を推奨。

### 9.5 位置固定

設定ON時は誤ドラッグを防止する。

### 9.6 タスクトレイ

タスクトレイアイコンを用意する。

最低限:

- ウィジェットを表示
- ウィジェットを非表示
- 最前面表示 ON / OFF
- 位置固定 ON / OFF
- Windows起動時に自動起動
- 設定
- 終了

Apple Music を閉じていてもタスクトレイからアプリを終了できる。

---

## 10. 状態管理

アプリ内で状態を明確に分離する。

```text
WaitingForAppleMusic
AppleMusicStarting
AppleMusicIdle
Playing
Paused
AppleMusicClosing
Recovering
```

### WaitingForAppleMusic

```text
Widget: Hidden
GSMTC: Disconnected
Timeline: Off
Artwork: Off
UI Automation: Off
Process Watcher: On
```

### AppleMusicStarting

```text
Widget: Visible
GSMTC: Connecting
Timeline: Off
```

### AppleMusicIdle

```text
Widget: Visible
GSMTC: Connected / Waiting
Timeline: Off
```

### Playing

```text
Widget: Visible
Timeline: On
Controls: Enabled where supported
```

### Paused

```text
Widget: Visible
Timeline: Off
Controls: Enabled where supported
```

### AppleMusicClosing

- タイマー停止
- GSMTC切断
- Artwork解放
- ウィジェット非表示

### Recovering

GSMTCが一時的に切れた場合などに使用する。

---

## 11. 内部アーキテクチャ

```text
src/
├─ App/
│  ├─ App.xaml
│  └─ App.xaml.cs
│
├─ UI/
│  ├─ PlayerWidget.xaml
│  ├─ PlayerWidget.xaml.cs
│  ├─ Controls/
│  │  ├─ PlaybackControls.xaml
│  │  ├─ ProgressBar.xaml
│  │  └─ TrackInfoView.xaml
│  └─ Themes/
│
├─ Lifecycle/
│  ├─ AppleMusicProcessMonitor.cs
│  ├─ WidgetLifecycleController.cs
│  └─ AppState.cs
│
├─ Media/
│  ├─ IMediaSessionProvider.cs
│  ├─ GsmtcSessionProvider.cs
│  ├─ AppleMusicSessionMatcher.cs
│  ├─ PlaybackState.cs
│  └─ TimelineSynchronizer.cs
│
├─ AppleMusic/
│  ├─ AppleMusicAutomation.cs
│  └─ AppleMusicWindowLocator.cs
│
├─ Artwork/
│  ├─ ArtworkProvider.cs
│  ├─ ArtworkCache.cs
│  └─ DominantColorExtractor.cs
│
├─ Services/
│  ├─ TrayService.cs
│  ├─ SettingsService.cs
│  ├─ StartupService.cs
│  └─ MonitorService.cs
│
└─ Models/
   ├─ TrackInfo.cs
   └─ WidgetSettings.cs
```

---

## 12. 責務分離

### AppleMusicProcessMonitor

担当:

- Apple Music 起動検知
- Apple Music 終了検知

UIには触れない。

### WidgetLifecycleController

担当:

- アプリ状態遷移
- Widget表示 / 非表示
- GSMTC接続 / 切断
- リソース解放

アプリ全体の中心。

### GsmtcSessionProvider

担当:

- GSMTC セッション取得
- 曲情報取得
- 再生状態イベント
- 再生操作

### AppleMusicSessionMatcher

担当:

- Apple Music セッションだけを選択

### TimelineSynchronizer

担当:

- 再生位置基準管理
- ローカル補間
- シーク後の再同期

### ArtworkProvider

担当:

- Artwork取得
- デコード
- 縮小
- 解放

### DominantColorExtractor

担当:

- Artworkから背景色抽出

曲変更時だけ動作。

### AppleMusicAutomation

担当:

- `…` メニュー
- 再生待ちリスト

必要な操作時だけ利用。

---

## 13. 設定ファイル

例:

```json
{
  "alwaysOnTop": false,
  "lockPosition": false,
  "launchAtStartup": true,
  "hideWhenAppleMusicClosed": true,
  "showWhenAppleMusicLaunches": true,
  "position": {
    "x": 1200,
    "y": 800,
    "monitor": "DISPLAY1"
  }
}
```

初期要件として以下は `true` を前提とする。

```json
"hideWhenAppleMusicClosed": true,
"showWhenAppleMusicLaunches": true
```

---

## 14. 開発フェーズ

## Phase 0 — 実機調査

最初にUIを作り込まず、Windows版Apple Musicの実際の挙動を検証する。

### 検証項目

- Apple Music プロセス名
- 起動検知方法
- 終了検知方法
- GSMTCでApple Musicを識別できるか
- 曲名取得
- アーティスト取得
- アルバム名取得
- Artwork取得
- 総再生時間取得
- 現在位置取得
- Play / Pause
- Previous / Next
- Seek
- Apple Music最小化中の取得
- Apple Musicが別仮想デスクトップにある場合
- Apple Musicが起動中だが未再生の場合
- `…` メニュー外部操作
- 再生待ちリスト外部操作

### 成果物

```text
docs/
└─ APPLE_MUSIC_CAPABILITY_MATRIX.md
```

形式例:

```text
Feature                   Supported   Method
------------------------------------------------
Track title               Yes         GSMTC
Artist                    Yes         GSMTC
Artwork                   Yes/No      GSMTC
Timeline                  Yes/No      GSMTC
Seek                      Yes/No      GSMTC
More menu                 Yes/No      UI Automation
Queue                     Yes/No      UI Automation
```

---

## Phase 1 — ライフサイクル最小実装

まず「Apple Musicと一緒に出現・消失する」動作を完成させる。

実装:

- Apple Music起動検知
- Apple Music終了検知
- ウィジェット自動表示
- ウィジェット自動非表示
- WaitingForAppleMusic状態
- AppleMusicStarting状態
- 終了時リソース解放

この段階では曲情報を表示できなくてもよい。

完成条件:

```text
Apple Music OFF → Widget Hidden
Apple Music ON  → Widget Visible
Apple Music OFF → Widget Hidden
```

---

## Phase 2 — 基本メディア連携

実装:

- Apple Music セッション識別
- 曲名
- アーティスト
- アルバム
- Artwork
- Play / Pause
- Previous
- Next
- 再生位置
- 総時間
- Seek

デザインは仮UIでよい。

---

## Phase 3 — 低負荷化

UI完成前にパフォーマンスを固める。

### 実装

- イベント駆動化
- 高頻度ポーリング削除
- Apple Music非起動時の休眠化
- Timeline Timerの停止制御
- Bitmap / Stream解放
- Artworkキャッシュ制限
- UI Automation遅延初期化
- セッション再接続最適化

### 計測

- Apple Music OFF時 CPU
- Apple Music OFF時 メモリ
- Apple Music ON / 未再生時 CPU
- 再生中 CPU
- 再生中 メモリ
- 1時間連続再生後 メモリ
- 100曲連続切替後 メモリ
- Apple Music 起動 / 終了を50回繰り返した後のメモリ

### 合格条件

- メモリ使用量が継続的に増えない
- Apple Music非起動時CPUほぼ0%
- 再生中CPU 1%未満を目標
- メモリ 100MB以下を目標

---

## Phase 4 — UI再現

参考画像に合わせてUIを完成させる。

調整:

- Artworkサイズ
- 曲名位置
- アーティスト / アルバム位置
- 再生バー
- 時間表示
- ボタンサイズ
- ボタン間隔
- 角丸
- 背景色
- ホバー状態
- 押下状態
- フェード

赤線で除外された要素は実装しない。

---

## Phase 5 — Apple Music固有操作

実装候補:

- `…` メニュー
- 再生待ちリスト

UI Automationは必要な場合のみ追加する。

### 完了メモ（2026-09-13）

- `…` メニュー: `AutomationId="ActionButton"` を `InvokePattern.Invoke()` で呼び出し、実機でメニュー表示を確認。
- 再生待ちリスト: `AutomationId="PlayQueueToggleButton"` を `TogglePattern.Toggle()` で呼び出し、`PlayQueueListView` の表示を確認。
- UIA はボタン押下時のみ使用し、常駐する UIA 監視は追加しない。

---

## Phase 6 — Windows統合

実装:

- タスクトレイ
- 最前面表示
- 位置固定
- 自動起動
- 位置保存
- マルチモニター対応
- DPI対応

### 完了メモ（2026-09-13）

§0（改訂 v2）が旧仕様を置換する前提での初期スコープは完了。

- 実装済み: タスクトレイ（表示/非表示・自動起動・終了）、最前面の固定オーバーレイ、`TaskbarGapPx` の設定永続化、メインタスクバー追従、DPI 追従、タスクバー自動非表示 / 全画面検出、Explorer 再起動復帰、アプリ領域衝突時のコンパクト表示（340→136 DIP）と最小幅未満での非表示、Windows 起動時自動起動（HKCU Run）。
- 対象外: セカンダリタスクバーは §0.4 のとおり初期版では扱わず、設定で将来対応。

---

## Phase 7 — 品質確認

### Apple Music

- 起動
- 終了
- 起動直後
- 未再生
- 一時停止
- 再生再開
- 曲変更
- 高速な曲送り
- 長時間再生
- 最小化
- 再起動

### Windows

- スリープ
- 復帰
- ロック
- ログイン
- モニター抜き差し
- 解像度変更
- DPI 100%
- DPI 125%
- DPI 150%
- DPI 200%

### 楽曲

- Artworkあり
- Artworkなし
- 長い曲名
- 長いアーティスト名
- 長いアルバム名
- 1分未満の曲
- 1時間以上の曲

---

## 15. エラー処理

以下の状態でクラッシュしないこと。

- Apple Music未起動
- Apple Music起動直後
- Apple Music起動中だが未再生
- GSMTCセッションがまだ存在しない
- GSMTCセッションが途中で消失
- Artwork取得失敗
- 曲情報の一部欠落
- UI Automation対象が見つからない
- Apple Music UI変更
- スリープ復帰
- モニター変更
- シーク失敗

### 表示方針

曲変更時に情報取得が遅れても、前曲情報を長時間残さない。

必要なら一時的に:

```text
Loading…
```

または空状態へ移行する。

---

## 16. パフォーマンス目標

### Apple Music 非起動時

- Widget: 非表示
- CPU: ほぼ 0%
- GSMTC: 停止
- Artwork処理: 停止
- UI Automation: 停止
- Timeline Timer: 停止

### Apple Music 起動・未再生時

- CPU: ほぼ 0%
- Widget: 表示
- GSMTC: 必要最小限
- Timeline Timer: 停止

### 再生中

- CPU: 1%未満を目標
- メモリ: 100MB以下を目標
- 曲変更反映: 1秒以内
- UI更新: 250〜500ms程度

---

## 17. 完成条件

### 17.1 表示制御

- Apple Music が閉じている → ウィジェット非表示
- Apple Music を起動 → 自動表示
- Apple Music 未再生 → 表示維持
- Apple Music 一時停止 → 表示維持
- Apple Music 再生中 → 正常表示
- Apple Music を終了 → 自動非表示

### 17.2 UI

- 赤線要素が存在しない
- Artworkが正しく表示される
- 曲名が正しく表示される
- アーティスト / アルバムが正しく表示される
- 再生位置が正しく表示される
- 基本操作が動作する
- 長い文字列で崩れない
- DPI変更で崩れない

### 17.3 Apple Music連携

- Apple Music以外のセッションへ切り替わらない
- Apple Music最小化中でも追従
- 曲変更を自動検出
- Play / Pause反映
- Previous / Next動作
- 対応環境ではSeek動作

### 17.4 パフォーマンス

- Apple Music 非起動時は実質休眠
- 高頻度ポーリングなし
- 不要なArtwork保持なし
- 不要なUI Automation常駐なし
- 長時間実行でメモリリークなし

---

## 18. 初期版で実装しないもの

- Apple Music本体の再実装
- Apple Musicライブラリ全体の閲覧
- プレイリスト編集
- 歌詞表示
- 音量操作
- Lossless表示
- Dolby Atmos表示
- Apple Musicアカウント操作
- Windows標準Widget Board統合
- ウィジェット内キュー完全再実装

---

## 19. 実装優先順位

```text
1. Apple Music 起動 / 終了検知
2. ウィジェット自動表示 / 非表示
3. GSMTCセッション接続
4. 曲情報 / Artwork取得
5. 基本再生操作
6. 再生位置 / Seek
7. 低負荷化
8. UI再現
9. タスクトレイ / Windows統合
10. `…` / 再生待ちリスト
11. 長時間安定性テスト
```

見た目を先に完成させず、ライフサイクルと低負荷設計を先に確定する。

---

## 20. 最初に行う作業

最初の実装では、以下の6点だけを確認する小さな検証アプリを作る。

1. Apple Music 起動を低負荷で検出できるか
2. Apple Music 終了を低負荷で検出できるか
3. Apple Music 起動後にGSMTCセッションを取得できるか
4. 曲名・Artwork・再生状態を取得できるか
5. Apple Musicを閉じた際にGSMTC / Artwork / Timerを完全に解放できるか
6. Apple Music非起動時にCPU使用率がほぼ0%まで下がるか

この検証が通った後に、UI再現へ進む。

---

## 21. 最終的な動作イメージ

```text
Windows起動
   │
   ▼
Widget App 起動
   │
   ▼
Apple Music 起動待ち
   │
   │  Apple Music 起動
   ▼
Widget 自動表示
   │
   ▼
GSMTC 接続
   │
   ├─ 未再生 → 待機表示
   ├─ 再生   → 曲情報 + Timeline
   └─ 一時停止 → 曲情報維持
   │
   │  Apple Music 終了
   ▼
GSMTC切断
Artwork解放
Timer停止
Widget非表示
   │
   ▼
低負荷の起動待ち状態へ戻る
```

このライフサイクルをプロジェクト全体の基本設計とする。
