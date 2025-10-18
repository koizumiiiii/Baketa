# Phase 13.2.31K-25: TextRegionDetector フルスクリーン領域返却問題

## 問題概要

PaddlePredictor(Detector) run failedエラーの真の原因を調査した結果、K-24のパディング除去処理ではなく、**TextRegionDetectorがフルスクリーン領域を返す**ことが根本原因であると判明した。

## 調査タイムライン

### Phase K-24: パディング除去処理実装 ✅
- **実装内容**: Stride mismatch時のパディング除去処理
- **実装箇所**: `PaddleOcrEngine.cs:1047-1141`
- **結果**: 正常動作確認
  ```
  [K-24] Stride mismatch detected - パディング除去処理開始
  Expected stride: 5655, Actual stride: 5656, Padding: 1 bytes
  [K-24] Mat.FromPixelData成功（パディング除去後） - Mat.Cols=1885, Mat.Rows=1060, Channels=3
  ```

### Phase K-25: 真の問題発見 ❌
- **発見**: K-24成功後も依然としてPaddlePredictor(Detector)エラー発生
- **調査結果**:
  ```
  [ROI_OCR] 領域OCR開始 - 座標=(0,0), サイズ=(3840x2160)
  [ROI_IMAGE_SAVE] 画像サイズ: 3840x2160 ← フルスクリーン！
  ```

## 根本原因の特定

### 問題の連鎖構造

```
1. TextRegionDetector.DetectTextRegionsAsync()
   ↓ 返却値: Rectangle(0, 0, 3840, 2160) ← フルスクリーン！

2. OcrExecutionStageStrategy.ExecuteAsync()
   ↓ detectedRegions = フルスクリーン領域
   ↓ foreach (var region in detectedRegions)

3. _ocrEngine.RecognizeAsync(ocrImage, region, ...)
   ↓ ocrImage = 3840x2160のフルスクリーン画像
   ↓ region = Rectangle(0, 0, 3840, 2160)

4. PaddleOCR Detector
   ↓ 巨大画像処理でエラー
   → PaddlePredictor(Detector) run failed ❌
```

### コード証拠

**OcrExecutionStageStrategy.cs:210-215**:
```csharp
try
{
    // TextRegionDetectorAdapter による高精度 ROI 検出実行
    detectedRegions = await _textRegionDetector.DetectTextRegionsAsync(windowsImage).ConfigureAwait(false);
    _logger.LogInformation("🎯 UltraThink: ROI検出完了 - 検出領域数: {RegionCount}", detectedRegions.Count);
}
```

**OcrExecutionStageStrategy.cs:288-301**:
```csharp
// 各検出領域に対してOCR実行
foreach (var region in detectedRegions)
{
    try
    {
        _logger.LogDebug("🎯 UltraThink: 領域指定OCR実行 - ({X},{Y},{Width},{Height})",
            region.X, region.Y, region.Width, region.Height);

        DebugLogUtility.WriteLog($"🔍 [ROI_OCR] 領域OCR開始 - 座標=({region.X},{region.Y}), サイズ=({region.Width}x{region.Height})");

        var regionOcrResults = await _ocrEngine.RecognizeAsync(
            ocrImage, // フルスクリーン画像を渡している
            region,   // (0,0, 3840x2160)を渡している
            cancellationToken: cancellationToken).ConfigureAwait(false);
```

## 成功ケースと失敗ケースの比較

### 成功ケース (00:18:38-00:18:39)
```
[K-24] Mat.FromPixelData成功 - Mat.Cols=1885, Mat.Rows=1060, Channels=3
[ROI_OCR] 領域OCR成功 - テキスト='GPU14％1CPU93％...', チャンク数=17
```

### 失敗ケース (00:18:45-00:18:47)
```
[K-24] Mat.FromPixelData成功 - Mat.Cols=1885, Mat.Rows=1060, Channels=3 （同じ条件！）
[ROI_OCR] 領域OCRエラー - 座標=(0,0), エラー=PaddlePredictor(Detector) run failed.
```

**重要な発見**: 同じ画像サイズ・チャンネル数で、成功するケースと失敗するケースがある。

## 疑問点

1. **なぜTextRegionDetectorがフルスクリーン領域を返すのか？**
   - 実装の意図は何か？
   - テキスト領域検出の失敗時のフォールバック処理？
   - 設定やパラメータの問題？

2. **成功ケースと失敗ケースの違いは何か？**
   - 画像の内容（ピクセルパターン）に依存？
   - 処理タイミングやメモリ状態に依存？
   - TextRegionDetectorの内部状態？

3. **K-24で生成された1885x1060のMatはどこへ行った？**
   - Clone()されたMatが破棄されている？
   - 別のコードパスで上書きされている？

4. **PaddleOCR Detectorがフルスクリーン画像を処理できない理由**
   - メモリ不足？
   - タイムアウト？
   - 内部バッファサイズの制限？

## 次のステップ

### 1. TextRegionDetectorの実装確認 (Priority: P0)
- `TextRegionDetectorAdapter.DetectTextRegionsAsync()`の実装を確認
- なぜフルスクリーン領域を返すのか特定
- フォールバック処理の条件を確認

### 2. Gemini専門レビュー依頼
- 収集したデータを基にGeminiに根本原因分析を依頼
- TextRegionDetectorの設計意図の推測
- 修正方針の提案

### 3. 修正実装
- Geminiの分析結果に基づいて修正実装
- テスト・検証

## 技術的考察

### なぜK-24のパディング除去は無意味だったのか？

K-24では1885x1060のROI画像（パディングあり）を正常に処理していますが、その後の処理で3840x2160のフルスクリーン画像がPaddleOCRに渡されているため、K-24の処理結果が使用されていません。

つまり、**2つの画像処理パスが存在**している可能性があります：

1. **ROI切り取りパス**: 1885x1060の画像を生成（K-24で処理） → 未使用
2. **フルスクリーンパス**: 3840x2160の画像を使用 → PaddleOCRエラー

### PaddleOCR Detector失敗の予想される理由

- **3840x2160の巨大画像**: 約8.3メガピクセル
- **メモリ消費**: RGB24で約24MB、処理バッファを含めると50-100MB
- **Detector処理時間**: 大規模画像で数秒～十数秒
- **タイムアウトまたはメモリ不足**: エラー発生

成功ケースでは、偶然テキストが少ない領域だったため処理が完了した可能性があります。

## ログ証拠

### 失敗時のログ (00:18:45-00:18:47)
```
[2025-10-18 00:18:45.005] Actual stride (PixelLock): 5656
[2025-10-18 00:18:45.010] ✅ [K-24] Mat.FromPixelData成功（パディング除去後） - Mat.Cols=1885, Mat.Rows=1060, Channels=3
[00:18:47.808][T08] 🔍 [ROI_OCR] 領域OCRエラー - 座標=(0,0), エラー=OCR処理中にエラーが発生しました: PaddlePredictor(Detector) run failed.
[00:18:48.846][T06] 🖼️ [ROI_IMAGE_SAVE] 画像サイズ: 3840x2160
```

### 成功時のログ (00:18:38-00:18:39)
```
[2025-10-18 00:18:38.608] ✅ [K-24] Mat.FromPixelData成功（パディング除去後） - Mat.Cols=1885, Mat.Rows=1060, Channels=3
[00:18:39.550][T20] 🔍 [ROI_OCR] 領域OCR成功 - テキスト='GPU14％1CPU93％...', チャンク数=17
```

## 結論

Phase K-24のパディング除去処理は正しく実装され、正常に動作しています。しかし、真の問題は**TextRegionDetectorがフルスクリーン領域を返し、それがPaddleOCR Detectorに渡されている**ことです。

次のステップとして、Geminiに専門レビューを依頼し、TextRegionDetectorの動作分析と修正方針を策定します。
