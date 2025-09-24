# ITextRegionDetector インターフェース競合問題 完全調査報告書

**報告日**: 2025-01-24
**調査方法**: UltraThink Phase 77.3 段階的解析
**問題**: ROI検出が全画面フォールバック (2560x1080) に陥る問題

## 🚨 問題の概要

Baketaアプリケーションにおいて、テキスト領域検出（ROI）が正常に動作せず、常に全画面（2560x1080）が単一領域として返される問題が発生。これにより翻訳精度とパフォーマンスが大幅に劣化。

## 🔍 調査結果サマリー

### 1. 根本原因の特定

**DI解決失敗による依存性注入問題**

| 要求側 | 期待インターフェース | 実際の登録状況 | 結果 |
|--------|-------------------|---------------|------|
| `OcrExecutionStageStrategy` | `Capture.ITextRegionDetector` | **❌ 未登録** | `null` 取得 |
| `OcrProcessingModule` | `OCR.TextDetection.ITextRegionDetector` | ✅ 正常登録 | `AdaptiveTextRegionDetector` |

### 2. インターフェース重複の詳細

#### A) Capture名前空間版
```csharp
// Baketa.Core.Abstractions.Capture.ITextRegionDetector
namespace Baketa.Core.Abstractions.Capture;

public interface ITextRegionDetector
{
    Task<IList<Rectangle>> DetectTextRegionsAsync(IWindowsImage image);
}
```

**実装**: `FastTextRegionDetector` (コメントアウト状態)
**使用箇所**: `OcrExecutionStageStrategy.cs:59`, `ROIBasedCaptureStrategy.cs`

#### B) OCR.TextDetection名前空間版
```csharp
// Baketa.Core.Abstractions.OCR.TextDetection.ITextRegionDetector
namespace Baketa.Core.Abstractions.OCR.TextDetection;

public interface ITextRegionDetector
{
    Task<IReadOnlyList<OCRTextRegion>> DetectRegionsAsync(
        IAdvancedImage image,
        CancellationToken cancellationToken = default);
}
```

**実装**: `AdaptiveTextRegionDetector` (正常動作)
**DI登録**: `OcrProcessingModule.cs:169` で正常登録済み

### 3. 実装クラス解析

#### FastTextRegionDetector
- **場所**: `Baketa.Infrastructure\OCR\PaddleOCR\TextDetection\FastTextRegionDetector.cs`
- **実装インターフェース**: `Capture.ITextRegionDetector`
- **状態**: AdaptiveCaptureModule.cs:117でコメントアウト
- **機能**: PaddleOCRベース、軽量グリッド検出フォールバック

#### AdaptiveTextRegionDetector
- **場所**: `Baketa.Infrastructure\OCR\TextDetection\AdaptiveTextRegionDetector.cs`
- **実装インターフェース**: `OCR.TextDetection.ITextRegionDetector`
- **状態**: OcrProcessingModule.cs:169で正常登録
- **機能**: Sobel edge detection + LBP texture analysis による高精度検出

## 🎯 解決戦略

### 推奨解決策: アダプターパターン実装

**Option B: 専用アダプタークラス作成** (Clean Architecture準拠)

#### 利点
- ✅ 単一責任原則維持
- ✅ 既存コードへの影響最小化
- ✅ 型変換を適切に処理
- ✅ テスト容易性確保
- ✅ AdaptiveTextRegionDetectorの高性能を活用

#### 実装方針

1. **TextRegionDetectorAdapter 作成**
```csharp
namespace Baketa.Infrastructure.Platform.Adapters;

public sealed class TextRegionDetectorAdapter : Capture.ITextRegionDetector
{
    private readonly OCR.TextDetection.ITextRegionDetector _adaptiveDetector;
    private readonly IImageConverter _imageConverter;

    public async Task<IList<Rectangle>> DetectTextRegionsAsync(IWindowsImage image)
    {
        // 1. IWindowsImage → IAdvancedImage 変換
        var advancedImage = await _imageConverter.ConvertAsync(image);

        // 2. AdaptiveTextRegionDetector で高精度検出
        var ocrRegions = await _adaptiveDetector.DetectRegionsAsync(advancedImage);

        // 3. OCRTextRegion → Rectangle 変換
        return ocrRegions.Select(region => region.BoundingBox).ToList();
    }
}
```

2. **DI登録修正**
```csharp
// AdaptiveCaptureModule.cs
services.AddSingleton<Capture.ITextRegionDetector, TextRegionDetectorAdapter>();
```

## 📊 期待効果

### Before (現状)
- ❌ ROI検出: 全画面 (2560x1080) 単一領域
- ❌ 翻訳精度: 低下 (全画面OCRによるノイズ)
- ❌ パフォーマンス: 大幅劣化 (全画面処理)

### After (修正後)
- ✅ ROI検出: 個別テキスト領域の正確検出
- ✅ 翻訳精度: AdaptiveTextRegionDetector による高精度Sobel+LBP検出
- ✅ パフォーマンス: 必要領域のみ処理による高速化
- ✅ 拡張性: Clean Architecture原則を維持

## 🔄 実装スケジュール

### Phase 1: アダプター実装 (即座実施)
1. `TextRegionDetectorAdapter` クラス作成
2. 型変換ロジック実装
3. DI登録修正

### Phase 2: 統合テスト (1時間以内)
1. ROI検出動作確認
2. 翻訳パイプライン正常動作確認
3. パフォーマンス検証

### Phase 3: 品質保証 (追加30分)
1. エラーハンドリング検証
2. メモリリーク確認
3. 例外ケース対応

## 📋 関連ファイル

### 修正対象ファイル
- `Baketa.Infrastructure.Platform\Adapters\TextRegionDetectorAdapter.cs` (新規作成)
- `Baketa.Infrastructure.Platform\DI\Modules\AdaptiveCaptureModule.cs` (DI登録修正)

### 依存ファイル
- `Baketa.Infrastructure\Processing\Strategies\OcrExecutionStageStrategy.cs` (DI要求側)
- `Baketa.Infrastructure\OCR\TextDetection\AdaptiveTextRegionDetector.cs` (委譲先)
- `Baketa.Infrastructure\DI\OcrProcessingModule.cs` (既存DI登録)

### テスト対象
- ROI検出機能の統合テスト
- 翻訳パイプライン end-to-end テスト
- パフォーマンステスト

## 🎉 成功指標

1. **機能復旧**: ROI検出が個別テキスト領域を正常に返す
2. **翻訳品質**: 全画面フォールバックが解消され、正確な領域翻訳を実現
3. **パフォーマンス**: 処理時間の大幅短縮
4. **安定性**: DI解決エラーの完全解消

---

**UltraThink調査完了**: 根本原因特定、解決策確定、実装準備完了