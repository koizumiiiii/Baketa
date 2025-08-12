# Tier 1: 即効性改善戦略（2-3週間）

## 概要

Baketa翻訳アプリケーションの処理速度を劇的に向上させるための即効性改善施策。実装完了後、翻訳全体の処理時間を**60-80%削減**することを目標とします。

## 実装対象項目

### 1. OCR GPU推論 🚀
**目標**: RTX4070で8-12倍高速化

#### 技術詳細
- **現在**: CPU PaddleOCR処理（2,000-20,000ms）
- **改善後**: GPU推論利用（200-2,000ms）
- **対象GPU**: RTX4070, GTX1660以上
- **フォールバック**: 統合GPU非対応時はCPU継続

#### 実装計画
```csharp
// GPU検出・初期化
public class GpuOcrAccelerator 
{
    public bool IsGpuAvailable { get; }
    public GpuType DetectedGpuType { get; } // RTX, GTX, Integrated
    public async Task<OcrResult> ProcessWithGpuAsync(Mat image)
}
```

#### 期待効果
- **処理時間**: 2,000-20,000ms → 200-2,000ms
- **改善率**: 80-90%削減
- **対象ユーザー**: RTX/GTX搭載PC（約70%）

---

### 2. 画像ハッシュキャッシュ 💾
**目標**: 重複処理90%削減

#### 技術詳細
- **現在**: 同一画像も毎回OCR実行
- **改善後**: PerceptualHash使用のスマートキャッシュ
- **アルゴリズム**: pHash（類似画像検出）
- **キャッシュ容量**: 最大1,000エントリ（約50MB）

#### 実装計画
```csharp
public class ImageHashCache
{
    private readonly Dictionary<ulong, OcrResult> _cache = new();
    
    public bool TryGetCachedResult(Mat image, out OcrResult result)
    {
        var hash = ComputePerceptualHash(image);
        return _cache.TryGetValue(hash, out result);
    }
    
    private ulong ComputePerceptualHash(Mat image)
    {
        // pHash実装: 64bitハッシュ生成
        // 類似画像（95%以上）を同一視
    }
}
```

#### 期待効果
- **重複処理削減**: 90%（ゲーム画面の反復性を活用）
- **メモリ使用量**: +50MB（キャッシュ分）
- **体感速度**: 大幅向上（リアルタイム翻訳実現）

---

### 3. ROI検出 🎯
**目標**: 処理範囲50-70%限定

#### 技術詳細
- **現在**: 画面全体（2560x1080）をOCR処理
- **改善後**: テキスト存在領域のみ処理
- **検出方法**: エッジ検出 + 連結成分分析
- **ROI最適化**: 動的領域サイズ調整

#### 実装計画
```csharp
public class TextRegionDetector
{
    public List<Rect> DetectTextRegions(Mat image)
    {
        // 1. グレースケール変換
        // 2. Cannyエッジ検出
        // 3. モルフォロジー演算（膨張・収縮）
        // 4. 輪郭検出・矩形化
        // 5. テキスト領域フィルタリング
        return textRegions;
    }
    
    public float CalculateProcessingReduction()
    {
        // 処理範囲削減率を計算
        return reductionPercentage; // 50-70%目標
    }
}
```

#### 期待効果
- **処理範囲**: 全画面 → 30-50%（テキスト領域のみ）
- **処理時間削減**: 50-70%
- **精度向上**: ノイズ削減により文字認識率向上

---

## 統合実装アーキテクチャ

```csharp
public class OptimizedOcrPipeline
{
    private readonly GpuOcrAccelerator _gpuAccelerator;
    private readonly ImageHashCache _hashCache;
    private readonly TextRegionDetector _roiDetector;
    
    public async Task<OcrResult> ProcessImageAsync(Mat image)
    {
        // 1. キャッシュチェック（最優先）
        if (_hashCache.TryGetCachedResult(image, out var cachedResult))
        {
            return cachedResult; // 即座に返却
        }
        
        // 2. ROI検出
        var textRegions = _roiDetector.DetectTextRegions(image);
        
        // 3. GPU推論実行（各ROIに対して）
        var results = new List<OcrResult>();
        foreach (var region in textRegions)
        {
            var roi = new Mat(image, region);
            var result = await _gpuAccelerator.ProcessWithGpuAsync(roi);
            results.Add(result);
        }
        
        // 4. 結果統合・キャッシュ保存
        var finalResult = MergeResults(results);
        _hashCache.CacheResult(image, finalResult);
        
        return finalResult;
    }
}
```

## 実装順序・スケジュール

### Week 1: GPU推論実装
- [ ] GPU検出・初期化システム
- [ ] PaddleOCR GPU版統合
- [ ] パフォーマンステスト実装

### Week 2: キャッシュシステム実装  
- [ ] PerceptualHash実装
- [ ] キャッシュ管理システム
- [ ] メモリ使用量最適化

### Week 3: ROI検出・統合
- [ ] テキスト領域検出アルゴリズム
- [ ] パイプライン統合
- [ ] 総合パフォーマンステスト

## 成功指標

### パフォーマンス指標
- **総合処理時間**: 9,339ms → 1,500-3,000ms（50-80%削減）
- **OCR処理時間**: 2,000-20,000ms → 200-2,000ms
- **キャッシュヒット率**: 80%以上
- **ROI検出精度**: 95%以上（テキスト見逃し5%以下）

### ユーザー体験指標
- **リアルタイム翻訳**: 3秒以内の応答時間実現
- **システム負荷**: CPU使用率30%以下維持
- **メモリ使用量**: +100MB以内（キャッシュ含む）

## リスク・制約事項

### 技術リスク
- **GPU互換性**: 古いGPU（GTX900番台以前）では効果限定
- **メモリ制約**: 4GB以下のRAMでは制限あり
- **ドライバ依存**: GPU推論がドライババージョンに依存

### 軽減策
- **フォールバックシステム**: GPU非対応時は従来処理継続
- **メモリ監視**: 動的キャッシュサイズ調整
- **設定可能**: ユーザーがGPU使用をON/OFF可能

## 次ステップ（Tier 2への準備）

Tier 1完了後、以下の情報を取得：
- 実際のパフォーマンス改善値
- ボトルネック箇所の特定
- ユーザーフィードバック収集

これらの結果を基に、Tier 2（中期改善）の優先順位を調整します。