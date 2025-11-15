# 2重翻訳問題解消設計書

## 📊 問題概要

**発見日時**: 2025-10-18
**重大度**: P0（ユーザー体験に直接影響）
**症状**: 翻訳処理が2回実行され、オーバーレイが重複表示される

## 🔬 問題の詳細分析

### 現在の誤った処理フロー

```
1. OCR実行 → 18個のTextChunk取得
   ↓
2. ❌ 全画面一括翻訳を実行（CoordinateBasedTranslationService.cs:580-598）
   - StreamingService.TranslateBatchWithStreamingAsync()呼び出し
   - 全チャンクのテキストを結合して1回の翻訳実行
   - 結果: "FPS17 1GPU 25% CPU 15% delay 0 milliseconds game time stop..."
   ↓
3. ❌ TranslationWithBoundsCompletedEvent発行（全画面翻訳結果）
   - TranslationWithBoundsCompletedHandler起動
   - InPlaceTranslationOverlayManager.ShowInPlaceOverlayAsync()実行
   - オーバーレイ表示（1回目） ← 全画面一括翻訳の結果
   ↓
4. ❌ 同じ18個のチャンクをTimedChunkAggregatorに追加
   - TranslationWithBoundsCompletedHandler内でTryAddTextChunkDirectlyAsync × 18回
   - TimedChunkAggregatorにバッファリング
   ↓
5. ❌ 150ms後にTimedChunkAggregatorがグルーピング完了
   - ProximityGroupingで14グループに集約
   - AggregatedChunksReadyEvent発行
   ↓
6. ❌ AggregatedChunksReadyEventHandler起動
   - 14グループを個別に翻訳（StreamingService.TranslateBatchWithStreamingAsync）
   - 各グループのオーバーレイ表示（2回目） ← 個別翻訳の結果
```

### 設計意図の確認

**ユーザー要求**:
- ✅ **個別翻訳が本来の動作**: 各テキスト位置の上に対応する翻訳結果を表示
- ✅ **全画面一括翻訳はフォールバック**: 個別翻訳が失敗した場合のみ実行

## 🎯 個別翻訳失敗ケースの分析

### 失敗する可能性のあるケース

#### **1. gRPC接続エラー（実際に発生実績あり）**
```csharp
// GrpcTranslationClient.cs:168-185
catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
{
    return TranslationResponse.CreateErrorFromException(...);
}
```
- **発生条件**: Pythonサーバーダウン、ポート競合、ネットワーク問題
- **頻度**: 中〜高（サーバー起動時、ネットワーク不安定時）
- **ユーザー影響**: ❌ 翻訳が一切表示されない
- **フォールバック必要性**: ✅ **必須**

#### **2. gRPCタイムアウト**
```csharp
// GrpcTranslationClient.cs:149-166
catch (RpcException ex) when (ex.StatusCode == StatusCode.DeadlineExceeded)
{
    return TranslationResponse.CreateErrorFromException(..., "TIMEOUT", ...);
}
```
- **発生条件**: サーバー過負荷、長文翻訳、ネットワーク遅延
- **頻度**: 低〜中（サーバー性能による）
- **ユーザー影響**: ❌ 一部チャンクが翻訳されない
- **フォールバック必要性**: ✅ **推奨**

#### **3. AggregatedChunksReadyEventHandler内の例外**
```csharp
// AggregatedChunksReadyEventHandler.cs:191-196
catch (Exception ex)
{
    _logger.LogError(ex, "❌ [PHASE12.2] 集約チャンクイベント処理エラー");
    throw; // ← 例外を再スロー（処理中断）
}
```
- **発生条件**: メモリ不足、並列処理の競合、予期せぬ例外
- **頻度**: 極低（バグまたはシステム異常時のみ）
- **ユーザー影響**: ❌❌ 翻訳処理が完全停止、オーバーレイ非表示
- **フォールバック必要性**: ✅ **必須**

### フォールバック必要性の結論

**✅ 全画面一括翻訳フォールバックは必須**

**理由**:
1. ✅ gRPC UNAVAILABLEエラーが実際に発生実績あり
2. ✅ 個別翻訳失敗時のフォールバックがない（throw;で処理中断）
3. ✅ 全画面一括翻訳は成功する可能性が高い（単一リクエストのため、並列処理の複雑性がない）

## 💡 修正方針: 個別翻訳優先 + 全画面フォールバック

### あるべき正しい処理フロー

```
1. OCR実行 → 18個のTextChunk取得
   ↓
2. ✅ 各チャンクをTimedChunkAggregatorに追加（即座）
   - CoordinateBasedTranslationService内で全チャンクを追加
   - 全画面一括翻訳は実行しない
   ↓
3. ✅ 150ms後にTimedChunkAggregatorがグルーピング完了
   - ProximityGroupingで14グループに集約
   - AggregatedChunksReadyEvent発行
   ↓
4. ✅ AggregatedChunksReadyEventHandler起動（個別翻訳）
   - 14グループを個別に翻訳
   - 各グループのオーバーレイ表示
   ↓
5. ✅ 個別翻訳成功 → 処理完了
   ↓
6. ❌ 個別翻訳失敗（例外発生）
   ↓
7. ✅ AggregatedChunksFailedEvent発行（フォールバック開始）
   ↓
8. ✅ CoordinateBasedTranslationServiceでイベント受信
   ↓
9. ✅ 全画面一括翻訳を実行（フォールバック）
   - 全チャンクのテキストを結合して翻訳
   - TranslationWithBoundsCompletedEvent発行
   ↓
10. ✅ 全画面オーバーレイ表示（ユーザーには何かが表示される）
```

## 📋 実装計画

### Phase 1: CoordinateBasedTranslationServiceの修正

**ファイル**: `Baketa.Application/Services/Translation/CoordinateBasedTranslationService.cs`

#### 修正1: 全画面一括翻訳の削除

**削除箇所**: Lines 580-598（TranslateBatchWithStreamingAsync呼び出し）

**理由**: 個別翻訳が本来の動作のため、初回実行時は全画面一括翻訳を実行しない

#### 修正2: フォールバック用イベントハンドラー追加

```csharp
// 新規追加: AggregatedChunksFailedEventハンドラー
private async Task HandleAggregatedChunksFailedAsync(AggregatedChunksFailedEvent eventData)
{
    _logger.LogWarning("🔄 [FALLBACK] 個別翻訳失敗 - 全画面一括翻訳にフォールバック");

    try
    {
        // 失敗したチャンクを全て結合
        var combinedText = string.Join(" ", eventData.FailedChunks.Select(c => c.CombinedText));

        // 全画面一括翻訳実行
        var translationResult = await _streamingTranslationService.TranslateBatchWithStreamingAsync(
            [combinedText],
            eventData.SourceLanguage,
            eventData.TargetLanguage,
            null,
            CancellationToken.None).ConfigureAwait(false);

        if (translationResult != null && translationResult.Count > 0)
        {
            var translatedText = translationResult[0];

            // 全画面翻訳結果をオーバーレイ表示
            var bounds = CalculateCombinedBounds(eventData.FailedChunks);
            await PublishTranslationWithBoundsCompletedEventAsync(
                combinedText,
                translatedText,
                bounds,
                eventData.SourceLanguage,
                eventData.TargetLanguage).ConfigureAwait(false);

            _logger.LogInformation("✅ [FALLBACK] 全画面一括翻訳成功 - Text: '{Text}'",
                translatedText.Substring(0, Math.Min(50, translatedText.Length)));
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "❌ [FALLBACK] 全画面一括翻訳失敗 - 翻訳を表示できません");
    }
}
```

### Phase 2: AggregatedChunksReadyEventHandlerの修正

**ファイル**: `Baketa.Application/EventHandlers/Translation/AggregatedChunksReadyEventHandler.cs`

#### 修正1: 例外処理の改善（throw削除）

**修正箇所**: Lines 191-196

```csharp
// 修正前
catch (Exception ex)
{
    _logger.LogError(ex, "❌ [PHASE12.2] 集約チャンクイベント処理エラー");
    throw; // ← これを削除
}

// 修正後
catch (Exception ex)
{
    _logger.LogError(ex, "❌ [PHASE12.2] 集約チャンクイベント処理エラー - フォールバックイベント発行");

    // 🔥 [FALLBACK] 個別翻訳失敗時にフォールバックイベントを発行
    var failedEvent = new AggregatedChunksFailedEvent
    {
        SessionId = eventData.SessionId,
        FailedChunks = eventData.Chunks.ToList(),
        SourceLanguage = "ja", // 設定から取得
        TargetLanguage = "en", // 設定から取得
        ErrorMessage = ex.Message,
        ErrorException = ex
    };

    await _eventAggregator.PublishAsync(failedEvent).ConfigureAwait(false);
    _logger.LogInformation("✅ [FALLBACK] AggregatedChunksFailedEvent発行完了");

    // 例外を再スローせずに正常終了（フォールバック処理に委ねる）
}
```

### Phase 3: TranslationWithBoundsCompletedHandlerの修正

**ファイル**: `Baketa.Application/EventHandlers/TranslationWithBoundsCompletedHandler.cs`

#### 修正1: TimedChunkAggregator追加処理の削除

**削除箇所**: Lines 523-762相当のコード（TryAddTextChunkDirectlyAsync呼び出し）

**理由**: 個別翻訳はOCR直後にTimedChunkAggregatorに追加されるため、全画面翻訳結果から再度追加する必要はない

#### 修正2: 個別翻訳オーバーレイのクリア処理追加

```csharp
public async Task HandleAsync(TranslationWithBoundsCompletedEvent eventData)
{
    ArgumentNullException.ThrowIfNull(eventData);

    // 🔥 [FALLBACK] 全画面一括翻訳が実行された場合、個別翻訳のオーバーレイを削除
    if (eventData.IsFallbackTranslation)
    {
        _logger.LogInformation("🧹 [FALLBACK] 個別翻訳オーバーレイを削除 - 全画面翻訳のみ表示");

        // 既存のオーバーレイを全て削除
        if (_overlayManager != null)
        {
            await _overlayManager.ClearAllOverlaysAsync().ConfigureAwait(false);
        }
    }

    // 既存のオーバーレイ表示処理
    if (_overlayManager != null)
    {
        // ... 省略 ...
    }
}
```

### Phase 4: イベント定義追加

**ファイル**: `Baketa.Core/Events/Translation/AggregatedChunksFailedEvent.cs`（新規作成）

```csharp
using System;
using System.Collections.Generic;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;

namespace Baketa.Core.Events.Translation;

/// <summary>
/// 集約チャンク翻訳失敗イベント
/// 個別翻訳が失敗した場合に発行され、全画面一括翻訳フォールバックを起動
/// </summary>
public sealed class AggregatedChunksFailedEvent : IEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    /// <summary>
    /// 翻訳セッションID（トレース用）
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// 翻訳に失敗したチャンクのリスト
    /// </summary>
    public required List<TextChunk> FailedChunks { get; init; }

    /// <summary>
    /// ソース言語
    /// </summary>
    public required string SourceLanguage { get; init; }

    /// <summary>
    /// ターゲット言語
    /// </summary>
    public required string TargetLanguage { get; init; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public required string ErrorMessage { get; init; }

    /// <summary>
    /// エラー例外（デバッグ用）
    /// </summary>
    public Exception? ErrorException { get; init; }
}
```

### Phase 5: TranslationWithBoundsCompletedEventの拡張

**ファイル**: `Baketa.Core/Events/EventTypes/TranslationWithBoundsCompletedEvent.cs`

```csharp
/// <summary>
/// フォールバック翻訳かどうかを示すフラグ
/// true: 全画面一括翻訳（フォールバック）
/// false: 通常の個別翻訳
/// </summary>
public bool IsFallbackTranslation { get; init; } = false;
```

## 🎯 期待効果

### ユーザー体験

| 項目 | 修正前 | 修正後 |
|------|--------|--------|
| **通常時** | 2回翻訳実行、オーバーレイ重複 | **1回のみ個別翻訳、正確な位置表示** |
| **個別翻訳成功** | ✅ 正常表示（2回目） | ✅ **正常表示（1回のみ）** |
| **個別翻訳失敗** | ❌ 何も表示されない | ✅ **全画面翻訳にフォールバック** |
| **gRPC接続エラー** | ❌ 翻訳が停止 | ✅ **全画面翻訳で補完** |

### システム性能

| 項目 | 修正前 | 修正後 |
|------|--------|--------|
| **翻訳リクエスト数** | 15回（1回全画面 + 14回個別） | **14回（個別のみ）** |
| **オーバーレイ表示数** | 15個（重複） | **14個（個別）** |
| **処理時間** | 約12秒（2重処理） | **約5秒（1回のみ）** |
| **メモリ使用量** | 高（2重データ保持） | **最適化** |

### 耐障害性

| シナリオ | 修正前 | 修正後 |
|---------|--------|--------|
| **gRPC接続エラー** | ❌ 翻訳失敗 | ✅ **フォールバック成功** |
| **タイムアウト** | ❌ 一部翻訳失敗 | ✅ **フォールバック成功** |
| **予期せぬ例外** | ❌ 処理停止 | ✅ **フォールバック成功** |

## 📊 実装優先度

| フェーズ | 内容 | 優先度 | 工数 |
|---------|------|--------|------|
| **Phase 1** | CoordinateBasedTranslationService修正 | **P0** | 2-3時間 |
| **Phase 2** | AggregatedChunksReadyEventHandler修正 | **P0** | 1-2時間 |
| **Phase 3** | TranslationWithBoundsCompletedHandler修正 | **P0** | 1-2時間 |
| **Phase 4** | イベント定義追加 | **P0** | 0.5時間 |
| **Phase 5** | TranslationWithBoundsCompletedEvent拡張 | **P1** | 0.5時間 |

**合計工数**: 5-8時間

## 🔧 技術的懸念事項

### 1. フォールバック時の座標計算

**問題**: 全画面一括翻訳では、個別チャンクの座標情報が失われる

**解決策**: `CalculateCombinedBounds()`メソッドで、全チャンクのBoundingBoxを結合した矩形を計算

```csharp
private Rectangle CalculateCombinedBounds(List<TextChunk> chunks)
{
    if (chunks.Count == 0)
        return Rectangle.Empty;

    var minX = chunks.Min(c => c.CombinedBounds.X);
    var minY = chunks.Min(c => c.CombinedBounds.Y);
    var maxX = chunks.Max(c => c.CombinedBounds.Right);
    var maxY = chunks.Max(c => c.CombinedBounds.Bottom);

    return new Rectangle(minX, minY, maxX - minX, maxY - minY);
}
```

### 2. フォールバック判定のタイミング

**問題**: どのタイミングで個別翻訳失敗と判定するか

**解決策**: `AggregatedChunksReadyEventHandler`の`catch (Exception ex)`ブロックで即座に判定

### 3. 個別翻訳オーバーレイの削除

**問題**: フォールバック時に、既に表示された個別翻訳オーバーレイをどう削除するか

**解決策**: `IInPlaceTranslationOverlayManager.ClearAllOverlaysAsync()`メソッド実装

```csharp
public interface IInPlaceTranslationOverlayManager
{
    Task ShowInPlaceOverlayAsync(TextChunk chunk, CancellationToken cancellationToken = default);
    Task ClearAllOverlaysAsync(CancellationToken cancellationToken = default); // 新規追加
    // ... 省略 ...
}
```

## 🎓 設計原則

### 1. フェイルファスト vs フェイルセーフ

**採用**: **フェイルセーフ**

**理由**: ユーザー体験を最優先し、個別翻訳失敗時も全画面翻訳で補完

### 2. 単一責任の原則（SRP）

- `AggregatedChunksReadyEventHandler`: 個別翻訳のみに責任
- `CoordinateBasedTranslationService`: フォールバック処理に責任
- `TranslationWithBoundsCompletedHandler`: オーバーレイ表示に責任

### 3. 依存性逆転の原則（DIP）

イベント駆動アーキテクチャにより、`AggregatedChunksReadyEventHandler` ↔ `CoordinateBasedTranslationService` 間の直接依存を排除

---

## 🎯 Geminiレビュー結果 (2025-10-18)

### 総合評価

> **「この内容で実装に進むことを強く推奨します」**

### 高評価ポイント

1. **イベント駆動型フォールバック機構**
   - `AggregatedChunksFailedEvent`による疎結合設計が優れている
   - エラーハンドリングのロジックが`AggregatedChunksReadyEventHandler`にカプセル化
   - フォールバック実行ロジックが`CoordinateBasedTranslationService`に集約

2. **個別翻訳優先+全画面フォールバックの設計**
   - ユーザー体験を最優先したフェイルセーフな方針
   - 精度の高い個別翻訳を主軸とし、失敗時も何らかの翻訳結果を提供
   - アプリケーションの堅牢性を高める正しい判断

3. **Clean Architecture原則準拠**
   - イベントがCore層で定義され、依存関係の方向が正しい
   - Application層のハンドラとサービスが適切にイベントを利用
   - UI層はイベント結果を受けて表示更新のみ（関心の分離）

4. **エラーハンドリング**
   - `try-catch`で個別翻訳全体を囲み、失敗時に`AggregatedChunksFailedEvent`を発行
   - エラーを回復可能なドメインイベントとして扱う現代的な実践
   - アプリケーション全体のクラッシュを防ぎ、後続処理に制御を渡す

5. **タイミングとライフサイクル**
   - `IsFallbackTranslation`フラグによるオーバーレイクリアは最もシンプルで確実
   - フォールバック表示の直前にクリア処理が入り、ユーザー体験が最適化
   - 競合状態のリスクは最小限（一つでも例外発生で全チャンクを失敗と見なす設計）

6. **座標計算とオーバーレイ管理**
   - `TextChunk.CombinedBounds.Union`（Min/Max計算）は標準的で適切
   - `ClearAllOverlaysAsync()`の実装は必須（情報重複によるユーザー混乱を防止）

### 将来的な拡張提案

1. **部分的フォールバック（将来検討）**
   - 現状は「全か無か」だが、将来的には失敗したチャンクグループのみフォールバック
   - 成功したグループの個別翻訳と共存させる高度な実装も可能
   - ⚠️ 複雑性が増すため、まずは現在の設計で安定稼働させることが先決

2. **フォールバック失敗時のUI通知（将来検討）**
   - フォールバックの全画面翻訳自体が失敗する場合も考慮済み（`try-catch`で囲まれている）
   - 現状はログ出力のみで妥当だが、UIに「翻訳に失敗しました」メッセージ表示も検討可能

### 技術的懸念事項への回答

**Q: オーバーレイクリアのタイミングは適切か？**
- ✅ 設計通りで問題なし
- フォールバック表示の直前にクリア処理が入るため、古いオーバーレイが一瞬見えてから消える挙動を防げる

**Q: 競合状態のリスクは？**
- ✅ 低い
- 個別翻訳が「全体として成功」または「全体として失敗」のいずれかを取る設計
- 「一つでも例外が発生したら全チャンクを失敗と見なす」割り切りは堅牢な初期実装として妥当

**Q: パフォーマンスとユーザー体験は？**
- ✅ 妥当
- 個別翻訳のタイムアウト後、フォールバック処理が走り、表示が切り替わる
- 「何も表示されない」より格段に良い体験
- 画面のちらつき（クリア→再表示）は避けられないが、UI側の工夫（フェード効果等）で緩和可能

### 実装推奨事項

1. ✅ 現在の設計は非常に優れており、このまま実装に進むことを推奨
2. ✅ 実装計画も具体的で、問題なく実装可能
3. ✅ まずは現在の設計で安定稼働させ、部分的フォールバック等は将来検討

---

**作成日**: 2025-10-18
**最終更新**: 2025-10-18
**ステータス**: 設計完了、Geminiレビュー完了、実装準備完了
**レビュアー**: Gemini AI
**レビュー結果**: ✅ **実装推奨** - 「この内容で実装に進むことを強く推奨します」
**実装予定**: 即座に着手可能
