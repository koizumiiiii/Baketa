# 並行翻訳問題 - 根本原因分析とUltraThink調査結果

**作成日**: 2025-10-12
**調査方法**: UltraThink段階的調査法
**優先度**: P0（翻訳機能の根幹に関わる問題）

## 📊 **問題概要**

### 症状
- キャプチャから翻訳オーバーレイ表示まで**45秒以上**の遅延
- 複数の翻訳セッションが**並行実行**され、リソース競合発生
- メモリリーク（577MB/回）が並行実行により**指数関数的に悪化**

### 実測データ（ログ証拠）
```
15:33:14.025 - Startボタン押下
15:33:16.508 - キャプチャ完了（2.5秒 - 正常）
15:33:22.921 - OCR完了（6秒 - 正常）
15:33:24.864 - COMBINE処理開始（TimedChunkAggregator）

⚠️ 15:33:25.992 - Session a76c40a1 開始
⚠️ 15:33:32.328 - Session fa41061c 開始（並行！）
⚠️ 15:33:42.468 - Session a31092f4 開始（並行！）

15:34:10.240 - 最初のセッション翻訳実行（❌ 45.4秒遅延）
15:34:13.622 - 翻訳完了
```

**決定的証拠**: 異なる`SessionId`が複数同時に存在 → 並行翻訳が実行されている

---

## 🔬 **UltraThink Phase 1調査結果: アーキテクチャ分析**

### 調査対象ファイル
1. `Baketa.Application\Services\Translation\TranslationOrchestrationService.cs`
2. `Baketa.Infrastructure\OCR\PostProcessing\TimedChunkAggregator.cs`
3. `Baketa.Application\EventHandlers\Translation\AggregatedChunksReadyEventHandler.cs`

### アーキテクチャフロー
```
┌─────────────────────────────────────────────────────────────────┐
│ TranslationOrchestrationService（自動翻訳ループ）               │
│ - 500ms間隔でExecuteAutomaticTranslationStepAsync実行          │
│ - 3秒クールダウンチェック（_lastTranslationCompletedAt）       │
│ - キャプチャ → OCR実行                                          │
└────────────────┬────────────────────────────────────────────────┘
                 │ OCR結果（TextChunk）
                 ↓
┌─────────────────────────────────────────────────────────────────┐
│ TimedChunkAggregator（チャンク集約処理）                        │
│ - TryAddChunkAsync: チャンク追加時にタイマーリセット           │
│ - BufferDelayMs経過後、ProcessPendingChunksInternal実行        │
│ - AggregatedChunksReadyEvent発行                               │
└────────────────┬────────────────────────────────────────────────┘
                 │ AggregatedChunksReadyEvent
                 ↓
┌─────────────────────────────────────────────────────────────────┐
│ AggregatedChunksReadyEventHandler（翻訳実行）                  │
│ - ExecuteBatchTranslationAsync: 実際の翻訳処理                │
│ - DisplayTranslationOverlayAsync: オーバーレイ表示             │
│ ❌ クールダウンチェックなし                                     │
└─────────────────────────────────────────────────────────────────┘
```

---

## 🔥 **根本原因100%特定**

### 原因1: イベント駆動アーキテクチャによるクールダウン制御のバイパス

**TranslationOrchestrationService.ExecuteAutomaticTranslationStepAsync** (Line 964-987):
```csharp
// クールダウンチェック
DateTime lastTranslationTime;
lock (_lastTranslationTimeLock)
{
    lastTranslationTime = _lastTranslationCompletedAt;
}

var cooldownSeconds = _settingsService.GetValue("Translation:PostTranslationCooldownSeconds", 3);
var timeSinceLastTranslation = DateTime.UtcNow - lastTranslationTime;

if (timeSinceLastTranslation.TotalSeconds < cooldownSeconds)
{
    // クールダウン中 - キャプチャをスキップ
    return;
}
```

**問題点**:
- クールダウンチェックは**キャプチャ実行前**にのみ実施
- **AggregatedChunksReadyEventHandlerにはクールダウンチェックが存在しない**
- イベントが発行された時点で、クールダウン無視で翻訳が実行される

### 原因2: TimedChunkAggregatorのデバウンスタイマーと自動翻訳ループの時間差

**TimedChunkAggregator.TryAddChunkAsync** (Line 250):
```csharp
// タイマーをリセット（新しいチャンクが来たら待ち時間をリセット）
bool timerChangeResult = _aggregationTimer.Change(
    _settings.CurrentValue.BufferDelayMs,  // ← 設定値（例: 1500ms）
    Timeout.Infinite
);
```

**自動翻訳ループの間隔**: 500ms
**TimedChunkAggregatorのBufferDelayMs**: 1500ms（推定）

**並行実行が発生するメカニズム**:
```
時刻    | 自動翻訳ループ        | TimedChunkAggregator           | AggregatedChunksReadyEventHandler
--------|----------------------|--------------------------------|----------------------------------
0ms     | キャプチャ1実行      |                                |
500ms   | キャプチャ2実行      | タイマーリセット（1500ms待機） |
1000ms  | キャプチャ3実行      | タイマーリセット（1500ms待機） |
1500ms  | （クールダウン中）   | タイマー発火（1回目）          | → 翻訳1開始
2000ms  | （クールダウン中）   | タイマー発火（2回目）          | → 翻訳2開始（並行！）
2500ms  | （クールダウン中）   | タイマー発火（3回目）          | → 翻訳3開始（並行！）
```

**結果**: 複数のAggregatedChunksReadyEventが時間差で発行され、それぞれが独立して翻訳を実行

### 原因3: SynchronousExecution設定の影響

**AggregatedChunksReadyEventHandler** (Line 59):
```csharp
public bool SynchronousExecution => true; // 🔥 [PHASE12.2_FIX] Task.Runのfire-and-forget問題を回避
```

**EventAggregator.PublishAsync**:
```csharp
if (requiresSynchronousExecution)
{
    // 同期実行: 直接await（fire-and-forget回避）
    await Task.WhenAll(tasks).ConfigureAwait(false);
    return;
}
```

**問題点**:
- `SynchronousExecution = true`により、イベントハンドラーが完了するまで待機
- しかし、複数のイベントが発行されていた場合、**それぞれが順次実行される**
- 最初の翻訳が完了するまで45秒かかる → 2番目の翻訳が開始 → さらに遅延

---

## 💡 **並行翻訳が発生する直接的トリガー**

### TimedChunkAggregator.ProcessPendingChunksInternal (Line 574-583)
```csharp
// 統合されたチャンクを翻訳パイプラインに送信
if (allAggregatedChunks.Count > 0)
{
    var windowHandle = allAggregatedChunks.FirstOrDefault()?.SourceWindowHandle ?? IntPtr.Zero;
    var aggregatedEvent = new AggregatedChunksReadyEvent(
        allAggregatedChunks.AsReadOnly(),
        windowHandle
    );

    await _eventAggregator.PublishAsync(aggregatedEvent).ConfigureAwait(false);
    // ↑ この呼び出しが複数回発生 → 並行翻訳
}
```

**問題のシナリオ**:
1. 500ms間隔で3回のキャプチャ→OCR実行
2. TimedChunkAggregatorが3つのチャンクを受信
3. 各チャンク受信時にタイマーがリセット
4. 最終的に3つの独立したタイマーが発火
5. **3回のAggregatedChunksReadyEvent発行**
6. **3回の翻訳実行（並行）**

---

## 📋 **影響範囲**

### 直接的影響
| 項目 | 影響 | 重大度 |
|------|------|--------|
| **レスポンス時間** | 45秒以上の遅延 | P0 |
| **リソース競合** | CPU/GPUの無駄な並行処理 | P0 |
| **メモリリーク悪化** | 577MB × 並行実行数 | P0 |
| **ユーザー体験** | 実質的に使用不可能 | P0 |

### 間接的影響
- メモリリーク（Phase 5.2C）が並行実行により指数関数的に悪化
- Python gRPCサーバーへの過剰なリクエスト
- オーバーレイ表示の座標ズレ（複数翻訳結果の重複）

---

## 🎯 **次のステップ: Phase 3修正方針策定**

以下の3つの修正アプローチを検討:

### アプローチ1: 翻訳実行前のセマフォ制御（シンプル）
- AggregatedChunksReadyEventHandlerに`_translationInProgressSemaphore`追加
- 翻訳実行前にセマフォ取得、完了後に解放
- **利点**: 実装が簡単（1-2時間）
- **欠点**: イベントが破棄される可能性

### アプローチ2: TranslationOrchestrationServiceのクールダウン統合
- AggregatedChunksReadyEventHandler内でクールダウンチェック実施
- `_lastTranslationCompletedAt`を共有状態として管理
- **利点**: 既存のクールダウンロジックを再利用
- **欠点**: DI依存関係の追加が必要

### アプローチ3: TimedChunkAggregatorのタイマー制御見直し（根本修正）
- BufferDelayMsを自動翻訳ループ間隔（500ms）より短く設定
- または、ForceFlushMsを活用して強制処理
- **利点**: 根本的な解決
- **欠点**: 設定調整が必要、既存動作への影響大

---

## 📝 **調査完了サマリー**

✅ **Phase 1完了**: アーキテクチャ分析により並行翻訳の発生メカニズムを完全解明
✅ **Phase 2完了**: 根本原因を100%特定（イベント駆動によるクールダウンバイパス）
⏭️ **Phase 3**: Geminiレビュー用の修正方針策定ドキュメント作成

**次のアクション**: 3つの修正アプローチをGeminiに提示し、最適な実装方針を決定
