# OCR精度向上実装計画

## 🎯 目的
BaketaプロジェクトのOCR認識精度を向上させ、ゲーム画面からのテキスト抽出をより正確に行う

## 📊 現状分析

### 使用中の技術
- PaddleOCR PP-OCRv5（実際にはV4にフォールバック中）
- 基本的な前処理実装済み（グレースケール、二値化、ノイズ除去、コントラスト強調）
- 画像2倍拡大実装済み

### 既存設定の確認結果
```csharp
// 現在有効な設定
EnableImagePreprocessing = true
ConvertToGrayscale = true
EnableBinarization = true（自動閾値）
EnableNoiseReduction = true
EnhanceContrast = true
ImageScaleFactor = 2.0
```

## 🚀 実装可能な改善策

### 1. エッジ強調の有効化（即効性：高）
```csharp
// OcrSettings.cs
EnhanceEdges = true  // 現在false
```
**理由**: ゲーム画面のテキストは背景との境界が曖昧な場合が多く、エッジ強調により文字の輪郭が明確になる

### 2. 画像拡大率の最適化（即効性：高）
```csharp
ImageScaleFactor = 3.0  // 現在2.0 → 3.0へ
```
**理由**: 調査結果では5倍拡大が推奨されているが、処理速度とのバランスを考慮し3倍から試行

### 3. 適応的二値化の実装（効果：高、実装工数：中）
```csharp
// 新規実装が必要
public async Task<Mat> ApplyAdaptiveThreshold(Mat input)
{
    var result = new Mat();
    Cv2.AdaptiveThreshold(
        input, result,
        maxValue: 255,
        adaptiveMethod: AdaptiveThresholdTypes.GaussianC,
        thresholdType: ThresholdTypes.Binary,
        blockSize: 11,  // 局所領域のサイズ
        c: 2            // 定数
    );
    return result;
}
```
**理由**: ゲーム画面は局所的な明暗差が大きいため、適応的二値化が効果的

### 4. CLAHE（Contrast Limited Adaptive Histogram Equalization）実装（効果：高、実装工数：中）
```csharp
public async Task<Mat> ApplyCLAHE(Mat input)
{
    var clahe = Cv2.CreateCLAHE(
        clipLimit: 2.0,
        tileGridSize: new Size(8, 8)
    );
    
    var result = new Mat();
    clahe.Apply(input, result);
    return result;
}
```
**理由**: 局所的なコントラスト強調により、暗い部分や明るい部分のテキストも認識しやすくなる

### 5. モルフォロジー演算の追加（効果：中、実装工数：小）
```csharp
public async Task<Mat> ApplyMorphology(Mat input)
{
    var kernel = Cv2.GetStructuringElement(
        MorphShapes.Rect,
        new Size(2, 2)
    );
    
    var result = new Mat();
    // クロージング処理（文字の断片をつなげる）
    Cv2.MorphologyEx(input, result, MorphTypes.Close, kernel);
    return result;
}
```
**理由**: 文字の断片化を防ぎ、認識率を向上

### 6. 処理済み画像のキャッシュ実装（効果：中、実装工数：小）
```csharp
private readonly MemoryCache _preprocessedImageCache = new(new MemoryCacheOptions
{
    SizeLimit = 100
});

public async Task<Mat> GetPreprocessedImage(string imageHash, Func<Task<Mat>> preprocessFunc)
{
    if (_preprocessedImageCache.TryGetValue(imageHash, out Mat cachedImage))
    {
        return cachedImage;
    }
    
    var processed = await preprocessFunc();
    _preprocessedImageCache.Set(imageHash, processed, new MemoryCacheEntryOptions
    {
        Size = 1,
        SlidingExpiration = TimeSpan.FromMinutes(5)
    });
    
    return processed;
}
```
**理由**: 同じ画面領域の再処理を避け、パフォーマンスを向上

### 7. PaddleOCRパラメータの最適化（効果：高、実装工数：小）
```csharp
// PaddleOcrEngine.csに以下を追加
det_db_thresh: 0.3      // 文字検出の閾値を下げる（デフォルト0.5）
det_db_box_thresh: 0.5  // ボックス検出の閾値
det_db_unclip_ratio: 1.8 // 検出ボックスの拡張率
```
**理由**: ゲーム画面の多様なフォントに対応

## 📋 実装優先順位

1. **即座に実装可能（設定変更のみ）**
   - エッジ強調の有効化
   - 画像拡大率を3.0に変更
   - PaddleOCRパラメータ調整

2. **短期実装（1-2日）**
   - モルフォロジー演算の追加
   - 処理済み画像のキャッシュ

3. **中期実装（3-5日）**
   - 適応的二値化
   - CLAHE実装

## 🧪 効果測定方法

1. **ベンチマークテストの作成**
   - 様々なゲーム画面のサンプル画像セット準備
   - 認識精度の定量的測定

2. **A/Bテスト**
   - 設定変更前後での認識率比較
   - 処理時間の計測

3. **ユーザーフィードバック**
   - 実際のゲーム画面での体感精度

## ⚠️ 注意事項

1. **処理速度とのトレードオフ**
   - 画像拡大率を上げすぎると処理時間が増加
   - リアルタイム性を損なわない範囲で調整

2. **メモリ使用量**
   - 高解像度処理はメモリ消費が増加
   - 適切なガベージコレクション実装

3. **互換性**
   - 既存の翻訳パイプラインとの整合性確保
   - エラーハンドリングの強化

## 🎯 期待される成果

- OCR認識精度: 10-20%向上
- 特にゲーム画面特有の問題（半透明背景、特殊フォント）への対応力向上
- 処理速度は現状維持または5%以内の低下に抑制

---
**作成日**: 2025-07-26
**ステータス**: 実装準備完了