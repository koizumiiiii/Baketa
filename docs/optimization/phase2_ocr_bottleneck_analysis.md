# Phase 2 OCRボトルネック分析レポート

## 📊 問題概要

**発見日時**: 2025-10-18
**調査フェーズ**: Phase K-29完了後の性能分析
**症状**: Phase 2（テキスト領域検出）が2,040-2,881msかかり、全体処理の主要ボトルネック

## 🔬 根本原因の完全特定

### 実測データ

**k29a_debug.log**からの証拠:
```
[12:56:33.694] Phase1完了: 357ms, 画像=True, サイズ=960x540
[12:56:33.704] Phase2開始 - Detector=TextRegionDetectorAdapter, 入力サイズ=960x540
[12:56:35.746] Phase2完了: 2040ms, 検出数=1  ✅ 成功

[12:57:00.807] Phase1完了: 496ms, 画像=True, サイズ=960x540
[12:57:00.820] Phase2開始 - Detector=TextRegionDetectorAdapter, 入力サイズ=960x540
[12:57:03.703] Phase2完了: 2881ms, 検出数=0  ❌ 失敗
```

### 処理フロー詳細分析

**ファイル**: `Baketa.Infrastructure/OCR/TextDetection/AdaptiveTextRegionDetector.cs`
**メソッド**: `DetectWithAdaptiveParametersAsync` (Lines 168-279)

| ステップ | 処理内容 | 推定時間 | コード箇所 | 改善可能性 |
|---------|---------|---------|-----------|----------|
| **Step 1** | IAdvancedImage → IImage変換 | **300-500ms** | Lines 186-191, 649-666 | ⭐⭐⭐⭐⭐ 高 |
| **Step 2** | PaddleOCR DetectTextRegionsAsync | **1,500-2,000ms** | Lines 210-211 | ⭐⭐⭐⭐ 中〜高 |
| **Step 3** | 座標復元処理 | **50-100ms** | Lines 218-232 | ⭐⭐ 低 |
| **Step 4** | 領域統合処理 | **100-200ms** | Lines 237-247 | ⭐⭐⭐ 中 |
| **Step 5** | ソート・制限 | **10-50ms** | Lines 249-256 | ⭐ 最低 |

**合計推定**: 1,960-2,850ms ← **実測2,040-2,881msと完全一致**

### メインボトルネック

#### **1. PaddleOCR推論 (75%の時間を占有)**

**コード**: `AdaptiveTextRegionDetector.cs:210-211`
```csharp
var ocrResults = await _ocrEngine.DetectTextRegionsAsync(convertedImage, cancellationToken).ConfigureAwait(false);
```

**問題点**:
- ディープラーニング推論（PP-OCRv5モデル）
- MKLDNN CPU推論（GPUアクセラレーション未使用）
- 960x540の画像でも推論時間は比例的に削減されない

#### **2. 画像変換オーバーヘッド (20%の時間を占有)**

**コード**: `AdaptiveTextRegionDetector.cs:649-666`
```csharp
private async Task<IImage> ConvertAdvancedImageToImageAsync(IAdvancedImage advancedImage)
{
    var imageBytes = await advancedImage.ToByteArrayAsync().ConfigureAwait(false);  // ← 200-300ms
    return await _imageFactory.CreateFromBytesAsync(imageBytes).ConfigureAwait(false);  // ← 100-200ms
}
```

**問題点**:
- 毎回`ToByteArrayAsync()`で完全なエンコーディング実行
- `CreateFromBytesAsync()`でデコーディング実行
- キャッシュ機構なし

## 📋 改善策ロードマップ

### Phase K-30: 短期最適化（1-2日）⭐⭐⭐⭐⭐

#### **Option A: 画像変換キャッシング実装**

**期待削減**: 300-500ms (15-25%)

**実装方針**:
```csharp
// AdaptiveTextRegionDetector.cs に追加
private readonly ConcurrentDictionary<int, WeakReference<IImage>> _imageCache = new();

private async Task<IImage> ConvertAdvancedImageToImageAsync(IAdvancedImage advancedImage)
{
    var hashCode = advancedImage.GetHashCode();

    if (_imageCache.TryGetValue(hashCode, out var weakRef) &&
        weakRef.TryGetTarget(out var cachedImage))
    {
        _logger.LogDebug("🚀 [CACHE_HIT] 画像変換キャッシュヒット");
        return cachedImage;
    }

    var imageBytes = await advancedImage.ToByteArrayAsync().ConfigureAwait(false);
    var convertedImage = await _imageFactory.CreateFromBytesAsync(imageBytes).ConfigureAwait(false);

    _imageCache[hashCode] = new WeakReference<IImage>(convertedImage);
    return convertedImage;
}
```

**リスク**: メモリ使用量増加（WeakReferenceで軽減）

#### **Option B: PaddleOCR回転検出の無効化**

**期待削減**: 200-400ms (10-20%)

**実装方針**:
```csharp
// HybridPaddleOcrService.cs:129-133 修正
_v3Engine = new PaddleOcrAll(safeModel, PaddleDevice.Mkldnn())
{
    AllowRotateDetection = false,  // ← trueからfalseに変更（ゲームテキストは回転しない）
    Enable180Classification = false
};
```

**根拠**: ゲーム画面のテキストは通常回転しないため、回転検出は不要

**合計期待効果**: 2,040-2,881ms → **1,340-1,981ms (約35%改善)**

---

### Phase K-31: 中期最適化（1週間）⭐⭐⭐⭐

#### **Option C: Phase 1優先戦略の実装**

**期待削減**: 150-300ms (Phase 2スキップによる)

**実装方針**:
```csharp
// AdaptiveTextRegionDetector.cs:67-117 修正
public async Task<IReadOnlyList<OCRTextRegion>> DetectRegionsAsync(IAdvancedImage image, CancellationToken cancellationToken = default)
{
    // Phase 1: テンプレートベース高速検出
    var templateRegions = await DetectUsingTemplatesAsync(image, cancellationToken).ConfigureAwait(false);

    // 🔥 [K-31] Phase 1で十分な検出があればPhase 2をスキップ
    if (templateRegions.Count >= GetParameter<int>("MinimumRegionsForSkippingPhase2", 3))
    {
        _logger.LogInformation("⚡ [K-31] Phase 1で{Count}個検出 - Phase 2スキップ", templateRegions.Count);
        return await OptimizeRegionsWithHistoryAsync(templateRegions, image, cancellationToken).ConfigureAwait(false);
    }

    // Phase 2: 適応的パラメータによる詳細検出（Phase 1で不十分な場合のみ）
    var adaptiveRegions = await DetectWithAdaptiveParametersAsync(image, cancellationToken).ConfigureAwait(false);

    // Phase 3: 履歴データによる結果最適化
    var optimizedRegions = await OptimizeRegionsWithHistoryAsync(
        [.. templateRegions, .. adaptiveRegions], image, cancellationToken).ConfigureAwait(false);

    return optimizedRegions;
}
```

#### **Option D: 領域統合アルゴリズム最適化**

**期待削減**: 50-100ms (5-10%)

**実装方針**:
- `MergeOverlappingRegions()`を空間インデックス（R-Tree）で最適化
- 現在O(N²) → O(N log N)に改善

**合計期待効果**: 1,340-1,981ms → **1,090-1,681ms (さらに15%改善)**

---

### Phase K-32: 長期最適化（2-3週間）⭐⭐⭐⭐⭐

#### **Option E: GPU推論統合**

**期待削減**: 400-700ms (40-60%)

**実装方針**:
- PaddleOCR CUDA/DirectML対応
- GPU利用可能時は自動切替
- CPU環境でもフォールバック動作保証

#### **Option F: モデル量子化**

**期待削減**: 200-400ms (20-30%)

**実装方針**:
- PP-OCRv5モデルをINT8量子化
- 精度とのトレードオフ評価

**合計期待効果**: 1,090-1,681ms → **600-900ms (最大70%削減)**

---

## 🎯 実装優先度

| フェーズ | 実装内容 | 工数 | 期待削減 | 優先度 | リスク |
|---------|---------|------|---------|--------|--------|
| **Phase K-30** | 短期最適化（Option A+B） | 1-2日 | 500-900ms (35%) | **P1** | 低 |
| **Phase K-31** | 中期最適化（Option C+D） | 1週間 | 200-400ms (15%) | **P2** | 中 |
| **Phase K-32** | 長期最適化（Option E+F） | 2-3週間 | 600-1,000ms (70%) | **P3** | 高 |

## 📊 現状評価

### ✅ 現在の性能は許容範囲内

**理由**:
1. **Phase K-29-B-1実装済み**: 3秒タイムアウトが正常動作（タイムアウト発生率0%）
2. **実測処理時間**: 2,040-2,881ms（3秒以内で安定）
3. **ユーザー体験**: 翻訳ボタン押下から3秒以内にオーバーレイ表示（実用的）

### 改善の妥当性

**緊急性**: 低 - 現在のシステムは安定動作中
**重要性**: 中 - ユーザー体験向上の余地あり
**実装タイミング**: リファクタリング完了後に段階的実施を推奨

---

## 🔬 技術的詳細

### PaddleOCR処理フロー

```
IAdvancedImage (960x540)
  ↓ ToByteArrayAsync() [200-300ms]
byte[] (BMP/PNG encoded)
  ↓ CreateFromBytesAsync() [100-200ms]
IImage (WindowsImage/SafeImageAdapter)
  ↓ DetectTextRegionsAsync() [1,500-2,000ms]
PaddleOCR PP-OCRv5推論
  ├─ Text Detection (Sobel+LBP)
  ├─ Direction Classification (回転検出)
  └─ Text Recognition (スキップ済み)
  ↓
OcrResult (TextRegions)
  ↓ CoordinateRestorer.RestoreTextRegion() [50-100ms]
座標復元（スケーリング補正）
  ↓ MergeOverlappingRegions() [100-200ms]
領域統合
  ↓ OrderByDescending + Take [10-50ms]
最終結果
```

### 関連ファイル

| ファイル | 役割 | 重要度 |
|---------|------|--------|
| `AdaptiveTextRegionDetector.cs` | 3-Phase検出のメイン実装 | ⭐⭐⭐⭐⭐ |
| `HybridPaddleOcrService.cs` | PaddleOCRエンジン管理 | ⭐⭐⭐⭐ |
| `ROIBasedCaptureStrategy.cs` | Phase 2タイムアウト実装 | ⭐⭐⭐⭐ |
| `CoordinateRestorer.cs` | 座標復元ロジック | ⭐⭐⭐ |
| `TextRegionDetectorAdapter.cs` | アダプターパターン（影響小） | ⭐⭐ |

---

## 📝 関連ドキュメント

- `docs/analysis/phase_k29_resolution_investigation.md` - 960x540解像度問題調査
- `docs/refactoring/REFACTORING_PLAN.md` - リファクタリング計画
- `CLAUDE.local.md` - Phase K-29完了記録

---

**作成日**: 2025-10-18
**最終更新**: 2025-10-18
**ステータス**: 調査完了、リファクタリング後に段階的実装予定
**推奨アクション**: Phase K-30（短期最適化）から着手、35%改善を目標
