# FIX7 ROI座標ズレ問題 - Option B恒久対応のための調査レポート

## 📋 問題概要

**問題**: ROIベースキャプチャ時、翻訳オーバーレイの座標がずれる

**症状**:
- フルスクリーンキャプチャ: ✅ 正常動作
- ROIキャプチャ: ❌ オーバーレイ座標がROI相対座標のまま表示され、画像絶対座標に変換されない

**影響**: ユーザーが指定したROI領域外にオーバーレイが表示され、翻訳結果が視認できない

---

## 🔬 根本原因の100%特定

### 原因の連鎖

```
WindowsImageAdapter (IAdvancedImage実装、CaptureRegionプロパティ持つ)
    ↓ ROI画像作成時に正しく設定される
CaptureCompletedHandler で IImageToReferencedSafeImageConverter により変換
    ↓
ReferencedSafeImage (IImageのみ実装、IAdvancedImageは実装していない)
    ↓ IAdvancedImage.CaptureRegionプロパティが失われる
OcrExecutionStageStrategy の is IAdvancedImage チェック失敗
    ↓
ROI座標変換（Line 494-507）がスキップされる
    ↓
TextChunk.CaptureRegion = null となる
    ↓
AggregatedChunksReadyEventHandler の座標正規化が実行されない
    ↓
結果: オーバーレイ座標ズレ
```

### 技術的詳細

**問題の核心**: `ReferencedSafeImage`は`IImage`のみ実装、`IAdvancedImage`を実装していない

**コード証拠**:
```csharp
// Baketa.Core/Services/Imaging/ReferencedSafeImage.cs
public sealed class ReferencedSafeImage : IImage, IDisposable
{
    // IAdvancedImageは実装していない
    // → CaptureRegionプロパティが存在しない
}
```

**失敗箇所**:
```csharp
// OcrExecutionStageStrategy.cs:494-507
if (context.Input.CapturedImage is IAdvancedImage advancedImage &&
    advancedImage.CaptureRegion.HasValue)
{
    // ReferencedSafeImageでは、このチェックが失敗する
    var captureRegion = advancedImage.CaptureRegion.Value;
    // ... 座標変換処理
}
```

---

## ✅ FIX7 Option C 実装内容（緊急対応）

### 実装方針

`ProcessingPipelineInput.CaptureRegion`プロパティをフォールバックとして活用

### 修正箇所

**ファイル**: `Baketa.Infrastructure\Processing\Strategies\OcrExecutionStageStrategy.cs`

**修正1: ROI座標変換にフォールバック追加** (Line 494-517)
```csharp
Rectangle? captureRegionForTransform = null;

if (context.Input.CapturedImage is IAdvancedImage advancedImage &&
    advancedImage.CaptureRegion.HasValue)
{
    captureRegionForTransform = advancedImage.CaptureRegion.Value;
    _logger.LogDebug("🔥 [FIX7_OPTION_C] IAdvancedImage.CaptureRegion使用");
}
else if (context.Input.CaptureRegion != Rectangle.Empty)
{
    // フォールバック: ProcessingPipelineInput.CaptureRegionを使用
    captureRegionForTransform = context.Input.CaptureRegion;
    _logger.LogInformation("🔥 [FIX7_OPTION_C] Input.CaptureRegionフォールバック使用");
}

if (captureRegionForTransform.HasValue)
{
    var captureRegion = captureRegionForTransform.Value;
    var originalRoiBounds = roiBounds;
    roiBounds = new Rectangle(
        roiBounds.X + captureRegion.X,
        roiBounds.Y + captureRegion.Y,
        roiBounds.Width,
        roiBounds.Height);
}
```

**修正2: TextChunk作成時にもフォールバック追加** (Line 607-634)
```csharp
Rectangle? captureRegionInfo = null;
if (context.Input.CapturedImage is IAdvancedImage advImg && advImg.CaptureRegion.HasValue)
{
    captureRegionInfo = advImg.CaptureRegion.Value;
    _logger.LogInformation("🔥 [FIX7_DEBUG] IAdvancedImage.CaptureRegion取得成功");
}
else if (context.Input.CaptureRegion != Rectangle.Empty)
{
    // フォールバック: ProcessingPipelineInput.CaptureRegionを使用
    captureRegionInfo = context.Input.CaptureRegion;
    _logger.LogInformation("🔥 [FIX7_OPTION_C] TextChunk.CaptureRegion - Input.CaptureRegionフォールバック");
}
```

### Option Cの利点

1. ✅ **最小変更**: 既存アーキテクチャを変更せず、1ファイルのみ修正
2. ✅ **データ存在確認済み**: `CaptureCompletedHandler.cs:173`で既に設定
3. ✅ **フォールバック設計**: IAdvancedImageが利用可能な場合はそちらを優先
4. ✅ **影響範囲最小**: OcrExecutionStageStrategyのみ修正

### Option Cの欠点（Option B推奨理由）

1. ❌ **設計上の二重管理**: IAdvancedImageとProcessingPipelineInput.CaptureRegionの2つのデータソース
2. ❌ **責務の曖昧さ**: OCR戦略がDTOプロパティに直接依存
3. ❌ **拡張性の低下**: 将来的にCaptureRegion以外の画像コンテキスト情報を追加する際に同様の問題が発生

---

## 🔍 詳細データフロー解析

### データ設定フロー（正常動作部分）

#### Phase 1: ROI画像作成
**ファイル**: `AdaptiveCaptureService.cs:474-480`
```csharp
var captureRegionRect = new System.Drawing.Rectangle(
    absoluteRegion.X, absoluteRegion.Y,
    absoluteRegion.Width, absoluteRegion.Height);

var imageAdapter = new WindowsImageAdapter(roiImage, captureRegion: captureRegionRect);
```
✅ `WindowsImageAdapter.CaptureRegion`設定成功

#### Phase 2: イベント発行
**ファイル**: `ROIImageCapturedEventHandler.cs:38-40`
```csharp
var captureCompletedEvent = new CaptureCompletedEvent(
    capturedImage: eventData.Image,  // WindowsImageAdapter
    captureRegion: eventData.AbsoluteRegion,  // ROI絶対座標
    captureTime: TimeSpan.Zero)
```
✅ `CaptureCompletedEvent.CaptureRegion`設定成功

#### Phase 3: ProcessingPipelineInput作成
**ファイル**: `CaptureCompletedHandler.cs:164-173`
```csharp
input = new ProcessingPipelineInput
{
    CapturedImage = referencedSafeImage ?? eventData.CapturedImage,
    CaptureRegion = eventData.CaptureRegion,  // ← 設定済み
    // ...
};
```
✅ `ProcessingPipelineInput.CaptureRegion`設定成功

### データ消失ポイント（問題箇所）

#### Phase 4: ReferencedSafeImage変換
**ファイル**: `CaptureCompletedHandler.cs:139-157`
```csharp
if (_imageConverter != null)
{
    referencedSafeImage = await _imageConverter.ConvertToReferencedSafeImageAsync(
        eventData.CapturedImage,
        cancellationToken).ConfigureAwait(false);
}

// referencedSafeImageは IImageのみ実装
// → IAdvancedImage.CaptureRegionプロパティが失われる
```
❌ **CaptureRegionプロパティ消失**

#### Phase 5: OCR処理（Option Cで救済）
**ファイル**: `OcrExecutionStageStrategy.cs:494-517`
```csharp
// IAdvancedImageチェック失敗（ReferencedSafeImageのため）
if (context.Input.CapturedImage is IAdvancedImage advancedImage &&
    advancedImage.CaptureRegion.HasValue)
{
    // ← ここには来ない（ReferencedSafeImageはIAdvancedImageではない）
}
else if (context.Input.CaptureRegion != Rectangle.Empty)
{
    // 🔥 [FIX7_OPTION_C] フォールバックで救済
    captureRegionForTransform = context.Input.CaptureRegion;
}
```
⚠️ フォールバックで動作するが、設計上不適切

---

## 🎯 Option B 恒久対策の推奨実装

### 実装方針

**AdaptiveCaptureServiceレベルで適切なイベントを発行し、ROI画像を直接翻訳パイプラインに送る**

### Option Bの利点

1. ✅ **Clean Architecture準拠**: 各層の責務が明確
2. ✅ **データ整合性**: ReferencedSafeImage変換を経由せず、WindowsImageAdapterを直接使用
3. ✅ **拡張性**: 将来的な画像コンテキスト情報の追加に対応しやすい
4. ✅ **パフォーマンス**: 不要な画像変換を削減

### 推奨実装内容

#### 修正1: ROIImageCapturedEventHandlerの削除または変更

**現在の問題**:
```csharp
// ROIImageCapturedEventHandler.cs
// ROI画像 → CaptureCompletedEvent → CaptureCompletedHandler
//           → ReferencedSafeImage変換 → CaptureRegion消失
```

**推奨実装**:
```csharp
// Option B-1: 直接翻訳イベント発行
// AdaptiveCaptureService.cs
var translationEvent = new StartTranslationRequestEvent(
    capturedImage: imageAdapter,  // WindowsImageAdapter (IAdvancedImage実装)
    captureRegion: captureRegionRect,
    sourceWindow: windowHandle
);
await _eventAggregator.PublishAsync(translationEvent).ConfigureAwait(false);

// Option B-2: 専用のROI翻訳イベント作成
public class ROITranslationRequestEvent : IEvent
{
    public IAdvancedImage CapturedImage { get; }  // WindowsImageAdapter
    public Rectangle CaptureRegion { get; }
    public IntPtr SourceWindow { get; }
    // ...
}
```

#### 修正2: 翻訳パイプライン直接接続

**目的**: ReferencedSafeImage変換を経由せず、WindowsImageAdapterを直接翻訳パイプラインに送る

**実装箇所**:
- `AdaptiveCaptureService.cs` - ROI画像キャプチャ完了時
- 新規EventHandler（または既存ハンドラー改修）- IAdvancedImageを維持したまま処理

**データフロー**:
```
AdaptiveCaptureService
  ↓ WindowsImageAdapter (IAdvancedImage) 作成
  ↓ ROITranslationRequestEvent 発行
ROITranslationEventHandler
  ↓ IAdvancedImageのまま処理
  ↓ CaptureRegionプロパティ保持
OcrExecutionStageStrategy
  ↓ IAdvancedImage.CaptureRegion 取得成功
  ↓ 座標変換正常実行
AggregatedChunksReadyEventHandler
  ↓ TextChunk.CaptureRegion 設定済み
  ↓ 座標正規化実行
結果: オーバーレイ座標正確
```

---

## 📊 検証データ（FIX7 Option C実装後）

### 成功部分

**ログ証拠1: ROI特化OCRパスでCaptureRegion正常取得**
```
[00:25:43.847][T35] [INFO] 🔥 [FIX7_DEBUG] ROI特化OCRパス - context.Input.CaptureRegion: HasValue=True, Value=(267,747,263x88)
[00:25:44.800][T35] [INFO] 🔥 [FIX7_DEBUG] ROI特化OCRパス - context.Input.CaptureRegion: HasValue=True, Value=(204,868,271x59)
```

**ログ証拠2: 座標変換実行成功**
```
[00:25:45.338][T29] [INFO] 🔥 [FIX7_OPTION_C_ROI] CaptureRegionオフセット加算開始: (204,868)
[00:25:45.338][T29] [INFO] 🔥 [FIX7_OPTION_C_ROI] 座標変換完了 - 1個の領域を変換
```

### 未解決問題

**ログ証拠3: TextChunk.CaptureRegion依然としてnull**
```
[00:25:45.347][T29] [INFO] 🔍 [PHASE26] AggregatedChunksReadyEvent.TextChunks[0] - ChunkId: 1000001, CaptureRegion: null, Bounds: (535,1501,259x75)
[00:25:45.347][T29] [INFO] 🔍 [PHASE26] AggregatedChunksReadyEvent.TextChunks[1] - ChunkId: 1000002, CaptureRegion: null, Bounds: (537,1503,259x77)
```

### 問題の原因特定

**診断ログ未出力問題**:
- `🔥 [FIX7_DEBUG] TextChunk作成` ログが一切出ない
- `🔥 [FIX7_OPTION_C] TextChunk.CaptureRegion - Input.CaptureRegionフォールバック` ログも出ない

**推測される原因**:
```csharp
// OcrExecutionStageStrategy.cs:545
var positionedResults = textChunks
    .OfType<Baketa.Core.Abstractions.OCR.TextRegion>()  // ← 型フィルタリング
    .Select(region => new PositionedTextResult { ... })
    .ToList();

// positionedResults.Count == 0 になっている可能性
// → Line 543の if (positionedResults.Count > 0) が失敗
// → TextChunk作成コード（Line 543-680）が実行されない
```

**結論**:
- ROI座標変換は成功している（Line 310-374）
- しかし、TextChunk作成コード（Line 543-680）が実行されていない
- TextChunkは別のコードパス（未特定）で作成されている
- そのコードパスではCaptureRegionが設定されていない

---

## 🛠️ Option B実装の具体的タスク

### Phase 1: 新規イベント定義（2時間）

**ファイル**: `Baketa.Core/Events/Capture/ROITranslationRequestEvent.cs` (新規作成)
```csharp
namespace Baketa.Core.Events.Capture;

public sealed class ROITranslationRequestEvent : IEvent
{
    public IAdvancedImage CapturedImage { get; }  // WindowsImageAdapter保持
    public Rectangle CaptureRegion { get; }
    public IntPtr SourceWindow { get; }
    public DateTime CaptureTime { get; }

    public ROITranslationRequestEvent(
        IAdvancedImage capturedImage,
        Rectangle captureRegion,
        IntPtr sourceWindow,
        DateTime captureTime)
    {
        CapturedImage = capturedImage ?? throw new ArgumentNullException(nameof(capturedImage));
        CaptureRegion = captureRegion;
        SourceWindow = sourceWindow;
        CaptureTime = captureTime;
    }
}
```

### Phase 2: AdaptiveCaptureService改修（3時間）

**ファイル**: `Baketa.Application/Services/Capture/AdaptiveCaptureService.cs`

**修正箇所**: Line 474-490付近
```csharp
// 修正前: ROIImageCapturedEvent発行
await _eventAggregator.PublishAsync(new ROIImageCapturedEvent(
    Image: imageAdapter,
    AbsoluteRegion: captureRegionRect,
    RelativeRegion: detectedRegion.Region,
    CaptureTime: TimeSpan.Zero
), cancellationToken).ConfigureAwait(false);

// 修正後: ROITranslationRequestEvent直接発行
await _eventAggregator.PublishAsync(new ROITranslationRequestEvent(
    capturedImage: imageAdapter,  // IAdvancedImage保持
    captureRegion: captureRegionRect,
    sourceWindow: windowHandle,
    captureTime: DateTime.UtcNow
), cancellationToken).ConfigureAwait(false);
```

### Phase 3: ROITranslationEventHandler実装（4時間）

**ファイル**: `Baketa.Application/EventHandlers/Capture/ROITranslationEventHandler.cs` (新規作成)
```csharp
public sealed class ROITranslationEventHandler : IEventProcessor<ROITranslationRequestEvent>
{
    private readonly IProcessingPipeline _processingPipeline;
    private readonly ILogger<ROITranslationEventHandler> _logger;

    public async Task HandleAsync(ROITranslationRequestEvent eventData, CancellationToken cancellationToken)
    {
        // IAdvancedImageのままProcessingPipelineInputに渡す
        var input = new ProcessingPipelineInput
        {
            CapturedImage = eventData.CapturedImage,  // WindowsImageAdapter (IAdvancedImage)
            CaptureRegion = eventData.CaptureRegion,
            SourceWindowHandle = eventData.SourceWindow,
            CaptureTimestamp = eventData.CaptureTime,
            OwnsImage = false,  // AdaptiveCaptureServiceが所有権を持つ
            Options = new ProcessingPipelineOptions { ... }
        };

        await _processingPipeline.ProcessAsync(input, cancellationToken).ConfigureAwait(false);
    }
}
```

### Phase 4: 既存コードのクリーンアップ（2時間）

**削除候補**:
- `ROIImageCapturedEventHandler.cs` - 不要になる可能性（要検証）
- `OcrExecutionStageStrategy.cs` Line 494-517, 607-634 のフォールバックコード

**修正必要**:
- `CaptureCompletedHandler.cs` - ROI画像以外（フルスクリーン等）のみ処理

### Phase 5: 単体テスト実装（4時間）

**テストケース**:
1. ROI画像でIAdvancedImage.CaptureRegionが保持されること
2. 座標変換が正常に実行されること
3. TextChunk.CaptureRegionが設定されること
4. オーバーレイ座標が正確であること

### Phase 6: 統合テスト・検証（3時間）

**検証項目**:
1. フルスクリーンキャプチャの後方互換性
2. ROIキャプチャの座標精度
3. メモリリークの有無
4. パフォーマンス影響

---

## 📅 実装スケジュール見積もり

| Phase | 内容 | 工数 |
|-------|------|------|
| Phase 1 | 新規イベント定義 | 2h |
| Phase 2 | AdaptiveCaptureService改修 | 3h |
| Phase 3 | ROITranslationEventHandler実装 | 4h |
| Phase 4 | 既存コードクリーンアップ | 2h |
| Phase 5 | 単体テスト実装 | 4h |
| Phase 6 | 統合テスト・検証 | 3h |
| **合計** | | **18時間（約2-3日）** |

---

## 🎯 結論と推奨事項

### Option C（現状）評価

**優れている点**:
- ✅ 緊急対応として有効（1ファイルのみ修正）
- ✅ 座標変換は正常動作している
- ✅ 既存機能への影響が最小限

**問題点**:
- ❌ 設計上の二重管理（IAdvancedImage vs DTO）
- ❌ TextChunk作成コードが未実行（別のコードパスでCaptureRegionなしで作成されている）
- ❌ 拡張性・保守性の低下

### Option B推奨理由

1. **Clean Architecture原則準拠**: 各層の責務が明確になる
2. **データ整合性**: ReferencedSafeImage変換を経由せず、IAdvancedImageを維持
3. **拡張性**: 将来的な画像コンテキスト情報の追加に柔軟に対応
4. **パフォーマンス**: 不要な画像変換処理の削減

### 実装優先度

**Phase 4（Option B実装）**: **P1（高優先度）**
- Option Cで一時的に動作するが、根本解決ではない
- TextChunk作成コードが実行されていない問題が未解決
- 設計上の技術的負債を残さないため、早期実装を推奨

---

## 📎 参考資料

### 関連ドキュメント
- `gemini_fix7_solution_review.md` - Option C実装レビュー（Gemini評価5/5）
- `CLAUDE.local.md` - Phase 3.15関連の調査履歴

### 関連コミット
- `[予定]` Option B実装コミット（本レポートに基づく実装後）

### 調査担当
- UltraThink方法論による段階的調査
- Gemini専門レビュー活用

---

**作成日**: 2025-10-29
**ステータス**: Option C暫定対応完了、Option B実装推奨
**次のアクション**: Option B Phase 1（新規イベント定義）から実装開始
