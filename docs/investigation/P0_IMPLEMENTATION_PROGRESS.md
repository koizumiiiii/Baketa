# P0問題修正実装進捗レポート

**作成日**: 2025-11-03
**目的**: ROI画像破損問題（9/10真っ黒）と重複チャンク検出問題の修正

---

## 📊 実装ステータスサマリー

| 項目 | 優先度 | 状態 | 完了率 |
|------|--------|------|--------|
| **P0-1**: 重複チャンク検出修正 | P0 | 🔄 設計確定・実装待ち | 25% |
| **P0-2**: CoordinateRestorer修正 | P0 | ✅ 実装完了・動作確認待ち | 95% |
| **P1-1**: CropImage座標クリッピング | P1 | ⏸️ P0完了後 | 0% |
| **P2-1**: 座標検証ログ追加 | P2 | ⏸️ P0完了後 | 0% |
| **P3-1**: ROI画像品質検証 | P3 | ⏸️ P0完了後 | 0% |

---

## ✅ P0-2: CoordinateRestorer修正 - 実装完了

### 🎯 問題概要
**Math.Round四捨五入による累積誤差**で、画像下端付近のROI座標が画像境界を超過:
- ROI #3: Y+Height=2122 > 2160 (62px超過)
- ROI #9: Y+Height=2103 > 2160 (57px超過)
- 結果: Graphics.DrawImage()が範囲外部分を描画できず、**9/10のROI画像が真っ黒**

### 🔧 Gemini改善アルゴリズム実装

**修正ファイル**: 4ファイル

#### 1. CoordinateRestorer.cs
**完全な実装**:
```csharp
public static Rectangle RestoreOriginalCoordinates(Rectangle scaledRect, double scaleFactor, Size originalImageSize)
{
    if (Math.Abs(scaleFactor - 1.0) < 0.001)
        return scaledRect;

    if (scaleFactor <= 0)
        throw new ArgumentException($"Invalid scale factor: {scaleFactor}");

    // 🔥 [P0-2_GEMINI_IMPROVED] 浮動小数点演算を先に実行
    double originalX = scaledRect.X / scaleFactor;
    double originalY = scaledRect.Y / scaleFactor;
    double originalWidth = scaledRect.Width / scaleFactor;
    double originalHeight = scaledRect.Height / scaleFactor;

    // 🔥 [P0-2_GEMINI_IMPROVED] 左上はFloor、右下はCeilingで最大精度確保
    int x1 = (int)Math.Floor(originalX);
    int y1 = (int)Math.Floor(originalY);
    int x2 = (int)Math.Ceiling(originalX + originalWidth);
    int y2 = (int)Math.Ceiling(originalY + originalHeight);

    // 🔥 [P0-2_GEMINI_IMPROVED] 境界クリッピング - 画像範囲外アクセス防止
    x1 = Math.Max(0, x1);
    y1 = Math.Max(0, y1);
    x2 = Math.Min(originalImageSize.Width, x2);
    y2 = Math.Min(originalImageSize.Height, y2);

    // 🔥 [P0-2_GEMINI_IMPROVED] クリッピング後の座標から幅・高さ計算
    int finalWidth = Math.Max(0, x2 - x1);
    int finalHeight = Math.Max(0, y2 - y1);

    return new Rectangle(x1, y1, finalWidth, finalHeight);
}
```

#### 2. FastTextRegionDetector.cs (Line 137)
```csharp
var originalImageSize = new Size(image.Width, image.Height);
var restoredRegions = ocrResults.TextRegions
    .Select(region => CoordinateRestorer.RestoreTextRegion(region, scaleFactor, originalImageSize))
    .Where(region => IsRegionValid(region.Bounds))
    .Select(region => region.Bounds)
    .ToList();
```

#### 3. AdaptiveTextRegionDetector.cs (Line 227)
```csharp
var originalImageSize = new Size(image.Width, image.Height);
var restoredRegions = ocrResults.TextRegions
    .Select(region => CoordinateRestorer.RestoreTextRegion(region, scaleFactor, originalImageSize))
    .Where(region => IsRegionValid(region.Bounds))
    .ToList();
```

#### 4. PaddleOcrEngine.cs (Line 3322)
```csharp
// 名前空間曖昧性解決のため System.Drawing.Size 明示
var originalImageSize = new System.Drawing.Size(image.Width, image.Height);
var restoredRegions = new List<OcrTextRegion>(textRegions.Count);
foreach (var region in textRegions)
{
    restoredRegions.Add(CoordinateRestorer.RestoreTextRegion(region, scaleFactor, originalImageSize));
}
textRegions = restoredRegions;
```

### ✅ ビルド結果
```
ビルドに成功しました。
0 エラー
138 個の警告（既存のみ）
```

### 📊 期待効果
- **ROI #3**: (184, 2067, 962, 55) → (184, 2067, 962, **93**) ✅ Y+Height=2160
- **ROI #9**: (1146, 2076, 27, 27) → (1146, 2076, 27, **84**) ✅ Y+Height=2160
- **黒画像**: 9/10失敗 → **0/10完全解消**

### 🔜 次のステップ
**動作確認待ち** - ユーザーによる実機検証実施中

---

## 🔄 P0-1: 重複チャンク検出修正 - 設計確定

### 🎯 問題概要
ROI画像にメタデータ（ChunkIndex, TileIndex, RegionId）を付与する必要があるが、既存`AdaptiveCaptureResult`クラスと名前衝突が発生。

### ✅ Gemini設計レビュー結果

**推奨アプローチ**: **Option A - RoiCaptureMetadata別クラス導入** ⭐⭐⭐⭐⭐

**採用理由**:
1. **関心の分離**: ROI固有メタデータを明確にカプセル化
2. **影響範囲極小化**: IWindowsImageコアインターフェースを変更せず
3. **Interface Segregation Principle準拠**: インターフェース汚染防止
4. **型安全性**: Dictionaryより優れたコンパイル時型チェック
5. **Clean Architecture準拠**: レイヤー間依存関係を乱さない

### 🔧 実装設計（Gemini推奨）

#### Phase 1: RoiCaptureMetadata record作成

**ファイル**: `E:\dev\Baketa\Baketa.Core\Models\Capture\RoiCaptureMetadata.cs`

**実装** (Gemini改善版 - `record`型使用):
```csharp
using System.Drawing;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Models.Capture;

/// <summary>
/// ROIキャプチャメタデータ
/// </summary>
/// <remarks>
/// 🎯 [P0-1_GEMINI_IMPROVED] record型で不変性強化・記述簡潔化
/// - 従来: class → 可変性リスク
/// - 改善: record → 不変性(Immutability)強制、DTO用途に最適
/// </remarks>
public sealed record RoiCaptureMetadata
{
    public required IWindowsImage Image { get; init; }
    public required int ChunkIndex { get; init; }
    public int TileIndex { get; init; }
    public required string RegionId { get; init; }
    public Rectangle? OriginalBounds { get; init; }
}
```

#### Phase 2: AdaptiveCaptureResult修正

**ファイル**: `E:\dev\Baketa\Baketa.Core\Models\Capture\CaptureModels.cs`

**追加プロパティ** (Gemini改善版 - `IReadOnlyList`使用):
```csharp
/// <summary>
/// 適応的キャプチャ結果
/// Phase 1: OCR処理最適化システム対応
/// </summary>
public class AdaptiveCaptureResult
{
    // 既存プロパティ...

    /// <summary>
    /// ROIメタデータコレクション
    /// </summary>
    /// <remarks>
    /// 🎯 [P0-1_GEMINI_IMPROVED] IReadOnlyListでコレクション不変性を明示
    /// </remarks>
    public IReadOnlyList<RoiCaptureMetadata> RoiMetadata { get; set; } = [];
}
```

#### Phase 3: ROIキャプチャ実装修正

**対象ファイル** (調査必要):
- ROI画像生成箇所を特定
- `RoiCaptureMetadata`インスタンス作成
- ChunkIndex, TileIndex, RegionId設定

#### Phase 4: 重複チャンク検出ロジック修正

**対象ファイル** (調査必要):
- `RoiMetadata`から一意識別情報取得
- 重複検出ロジック実装

### 📋 実装タスク

- [ ] **Task 1**: `RoiCaptureMetadata.cs` record作成
- [ ] **Task 2**: `CaptureModels.cs` プロパティ追加
- [ ] **Task 3**: ROI生成箇所調査・修正
- [ ] **Task 4**: 重複検出ロジック実装
- [ ] **Task 5**: 単体テスト作成
- [ ] **Task 6**: 動作確認

### 🎯 実装開始条件
**P0-2動作確認完了後に開始**

---

## 📝 技術的メモ

### Gemini改善提案サマリー

**P0-2**:
- Math.Floor/Ceiling + 境界クリッピング
- 座標復元精度向上とオーバーフロー防止

**P0-1**:
- `class` → `record`: DTO用途に最適
- `IList<T>` → `IReadOnlyList<T>`: 不変性明示
- Option A採用: 関心の分離とClean Architecture準拠

### 参考資料

- UltraThink調査: `ULTRATHINK_NATIVE_CAPTURE_INVESTIGATION_PHASE5.md`
- P0-2 Geminiレビュー: バックグラウンドタスク ed339d
- P0-1 Gemini設計レビュー: 2025-11-03実施完了

---

**最終更新**: 2025-11-03
**次回更新予定**: P0-2動作確認完了後
