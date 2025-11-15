# ハイブリッドモード再設計 - 詳細実装計画

**作成日**: 2025-01-11
**ステータス**: 設計確定済み、実装準備完了
**優先度**: P0（OCR座標ずれとテキスト未検出の根本修正）

---

## 📋 目次

1. [問題概要](#問題概要)
2. [根本原因分析](#根本原因分析)
3. [Geminiレビュー結果](#geminiレビュー結果)
4. [UltraThink最終方針](#ultrathink最終方針)
5. [実装計画](#実装計画)
6. [技術要件](#技術要件)
7. [リスク管理](#リスク管理)
8. [期待効果](#期待効果)

---

## 問題概要

### 発生した2つの問題

1. **オーバーレイ座標のずれ**
   - 翻訳テキストが正しい位置に表示されない
   - ROI相対座標がスクリーン座標に変換されていない

2. **OCR検出不全**
   - セリフ1行目 "この周波数帯はKバンドと呼ばれ、通信衛星や観測衛星の" が検出されない
   - Y座標640-690の領域が完全に未検出

### ユーザー報告

```
[17:32:48.006][T11] [DEBUG] Baketa.Infrastructure.OCR.PaddleOCR.Services.PaddleOcrResultConverter:
⚡ RotatedRect座標抽出成功: Center=(1035.3, 925.5), Size=(43.3x1321.3), Angle=90.0°, Bounds={X=374,Y=903,Width=1322,Height=44}
```

ユーザー仮説: "想定と違う諸ルートで処理されている？"

---

## 根本原因分析

### UltraThink調査結果（100%特定完了）

#### 根本原因: EnableHybridMode設定による検出専用モード実行

**証拠連鎖**:

1. **appsettings.json:345** - EnableHybridMode: true設定
```json
{
  "OCR": {
    "PaddleOCR": {
      "EnableHybridMode": true  // ← 根本原因
    }
  }
}
```

2. **PaddleOcrEngine.cs:545-565** - ハイブリッドモード分岐
```csharp
if (_isHybridMode && _hybridService != null)
{
    __logger?.LogDebug("🔄 ハイブリッドモードでOCR実行（予防処理済み）");
    var processingMode = DetermineProcessingMode();
    textRegions = await _hybridService.ExecuteHybridOcrAsync(processedMat, processingMode, cancellationToken).ConfigureAwait(false);
}
```

3. **PaddleOcrEngine.cs:1846** - 検出専用モード呼び出し
```csharp
var paddleResult = await _executor.ExecuteDetectionOnlyAsync(processedMat, cancellationToken);
return _resultConverter.ConvertDetectionOnlyResult(new[] { paddleResult });
```

4. **PaddleOcrResultConverter.cs:131-173** - 検出専用結果変換
```csharp
public IReadOnlyList<OcrTextRegion> ConvertDetectionOnlyResult(PaddleOcrResult[] paddleResults)
{
    // ExtractBoundsFromRegion() 呼び出し
    // ❌ ApplyScalingAndRoi() 呼び出しなし
}
```

5. **PaddleOcrResultConverter.cs:638-641** - 空テキスト設定
```csharp
return new OcrTextRegion(
    text: "",  // 🔥 検出専用なのでテキストは空
    bounds: boundingBox,
    confidence: 0.8
);
```

### 処理ルート比較

| 項目 | 通常モード | 検出専用モード（問題あり） |
|------|----------|-------------------------|
| **エントリメソッド** | ConvertToTextRegions() | ConvertDetectionOnlyResult() |
| **座標変換** | ProcessPaddleRegion() | ExtractBoundsFromRegion() |
| **テキスト処理** | ✅ 完全なテキスト認識 | ❌ 空文字列設定 |
| **ROI調整** | ✅ ApplyScalingAndRoi() | ❌ なし（座標ずれの原因） |
| **ログ出力箇所** | - | ⚡ RotatedRect座標抽出成功（Line 580） |

**問題の本質**: 検出専用モードは「検出のみ」を目的としており、テキスト認識もROI座標変換もスキップしている

---

## Geminiレビュー結果

### 全体評価: ⭐⭐⭐⭐⭐ 非常に的確かつ妥当

#### 高評価ポイント

1. **IHybridOcrStrategy抽象化**
   - Clean Architecture原則に完全準拠
   - Strategy Patternの適切な適用
   - 将来の拡張性確保

2. **段階的リファクタリング方針**
   - リスクを最小化する実装順序
   - 各フェーズでの検証可能性

3. **パフォーマンス考慮**
   - 前処理最適化による速度低下緩和
   - ベンチマーク測定の必須化

#### 追加考慮事項（Geminiフィードバック）

1. **DI登録変更が必要**
   - ファイル: `Baketa.Infrastructure/DI/DependencyInjection.cs`
   - IHybridOcrStrategy実装クラスの登録
   - Factory Patternによる戦略選択

2. **HybridPaddleOcrService.cs既存実装のリファクタリング**
   - 既存コードを調査してから方針決定
   - Option A: 再利用してIHybridOcrStrategy実装に変換
   - Option B: 新規実装して段階的置き換え

3. **結果キャッシュの検討**
   - パフォーマンス最適化の追加手段
   - 画像ハッシュをキーにOCR結果をキャッシュ

4. **EnableHybridMode設定の再利用**
   - 削除せずに意味を変更
   - 新しい意味: ハイブリッド戦略システムの有効化

5. **ベンチマークテスト必須**
   - 通常モード vs ハイブリッドモード（修正後）の処理時間比較
   - OCR精度、メモリ使用量の測定

6. **リグレッションテスト必須**
   - 座標ずれ問題の再発防止
   - テキスト検出問題の再発防止

---

## UltraThink最終方針

### Phase 1: Geminiフィードバックの統合分析

**検証結果の要点**:
- ✅ 全体設計は「非常に的確かつ妥当」
- ✅ IHybridOcrStrategy抽象化は優れている
- ✅ Strategy Pattern適用は理想的、過剰設計ではない
- ⚠️ 追加考慮事項（6点）を設計に統合

### Phase 2: 設計方針の最終決定

#### 決定事項1: IHybridOcrStrategy実装戦略

**採用方針**: Strategy Patternで3つの戦略を実装

```csharp
// Baketa.Core/Abstractions/OCR/IHybridOcrStrategy.cs
public interface IHybridOcrStrategy
{
    Task<IReadOnlyList<OcrTextRegion>> ExecuteAsync(
        Mat processedImage,
        double scaleFactor,
        Rectangle? roi,
        CancellationToken cancellationToken);

    string StrategyName { get; }
}
```

**実装クラス**:
1. `FullRecognitionStrategy` - 通常モード（完全OCR）
2. `FastPreprocessingStrategy` - 軽量前処理 + 完全OCR
3. `AdaptiveStrategy` - 画像品質に応じて動的切替

**DI登録方法**（Geminiフィードバック反映）:
```csharp
// Baketa.Infrastructure/DI/Modules/InfrastructureModule.cs
services.AddSingleton<FullRecognitionStrategy>();
services.AddSingleton<FastPreprocessingStrategy>();
services.AddSingleton<AdaptiveStrategy>();
services.AddSingleton<IHybridOcrStrategyFactory, HybridOcrStrategyFactory>();
```

#### 決定事項2: HybridPaddleOcrService.csリファクタリング方針

**調査後に決定**: Phase 1で既存実装の詳細分析を実施してから判断

**候補**:
- Option A: 既存コードを再利用してIHybridOcrStrategy実装に変換
- Option B: 新規実装して既存コードを段階的に置き換え

#### 決定事項3: パフォーマンス最適化戦略

**結果キャッシュ実装**（Geminiフィードバック採用）:
```csharp
public class CachedHybridOcrStrategy : IHybridOcrStrategy
{
    private readonly ConcurrentDictionary<string, OcrResult> _resultCache;
    private readonly IHybridOcrStrategy _innerStrategy;

    public async Task<IReadOnlyList<OcrTextRegion>> ExecuteAsync(
        Mat processedImage,
        double scaleFactor,
        Rectangle? roi,
        CancellationToken cancellationToken)
    {
        var imageHash = ComputeImageHash(processedImage);

        if (_resultCache.TryGetValue(imageHash, out var cachedResult))
        {
            return cachedResult.Regions;
        }

        var result = await _innerStrategy.ExecuteAsync(processedImage, scaleFactor, roi, cancellationToken);
        _resultCache.TryAdd(imageHash, new OcrResult { Regions = result });

        return result;
    }
}
```

**前処理最適化アプローチ**:
- 軽量な画像品質評価（ヒストグラム分析、エッジ検出）
- 高品質画像 → 前処理スキップ
- 低品質画像 → 最適化された前処理適用

#### 決定事項4: EnableHybridMode設定の再利用

**新しい意味定義**（Geminiフィードバック採用）:
```json
{
  "EnableHybridMode": true,  // ハイブリッド戦略システムの有効化
  "HybridStrategy": "Adaptive",  // 使用する戦略: Full/FastPreprocessing/Adaptive
  "AdaptiveThreshold": 0.6  // Adaptive戦略の画像品質閾値
}
```

**マイグレーション**:
- 既存設定は自動的にFullRecognitionStrategyにマッピング
- 後方互換性を維持

#### 決定事項5: 実装フェーズの優先順位

| Phase | タスク | 期間 | 優先度 |
|-------|-------|------|--------|
| **Phase 1** | アーキテクチャ設計 | 1-2日 | **P0** |
| **Phase 2** | 検出専用モード廃止 | 2-3日 | **P0** |
| **Phase 3** | 高速化代替実装 | 3-4日 | **P1** |
| **Phase 4** | テストと検証 | 1-2日 | **P0** |

**合計期間**: 6-10日

### Phase 3: リスク評価と緩和策

| リスク | 影響度 | 緩和策 |
|-------|-------|--------|
| **既存HybridPaddleOcrService.csとの互換性** | 中 | Phase 1で完全分析、feature flagで切替可能に |
| **パフォーマンス低下の可能性** | 中 | ベンチマークテスト必須化、基準: 検出専用モード比+20%以内 |
| **DI登録変更による影響** | 低 | Singleton登録で既存依存関係維持、単体テスト追加 |

### Phase 4: 最終方針確定

**採用アプローチ**: **段階的リファクタリング + Strategy Pattern**

**理由**:
1. ✅ Gemini評価で設計妥当性確認済み
2. ✅ Clean Architecture原則に準拠
3. ✅ 既存コードとの共存可能（リスク最小化）
4. ✅ 将来の拡張性確保（新戦略追加が容易）

**実装開始条件**:
- [ ] HybridPaddleOcrService.cs既存実装の完全理解
- [ ] DI登録方法の詳細設計完了
- [ ] ベンチマーク測定環境の準備

---

## 実装計画

### Phase 1: 現状分析とアーキテクチャ設計（1-2日）

#### 1.1 既存ハイブリッドモード実装の完全分析

**調査対象ファイル**:
- `PaddleOcrEngine.cs:545-565` - ハイブリッドモード分岐ロジック
- `HybridPaddleOcrService.cs` - ハイブリッドOCR実装（Serena MCP検索）
- `DetermineProcessingMode()` メソッド - 処理モード決定ロジック

**調査タスク**:
```bash
# Serena MCP使用
find_symbol "HybridPaddleOcrService" --include_body true
find_symbol "DetermineProcessingMode" --include_body true
search_for_pattern "ExecuteHybridOcrAsync" --restrict_search_to_code_files true
```

**調査事項**:
1. ExecuteHybridOcrAsync()の内部実装詳細
2. ProcessingMode enum値とその使用箇所
3. なぜ検出専用モードが必要とされたか？（設計意図）
4. 高速化の実現メカニズム（GPU並列処理？テキスト認識スキップ？）

#### 1.2 Clean Architecture準拠設計

**新規ファイル作成**:

1. **Baketa.Core/Abstractions/OCR/IHybridOcrStrategy.cs**
```csharp
namespace Baketa.Core.Abstractions.OCR;

/// <summary>
/// ハイブリッドOCR戦略の抽象化
/// Strategy Patternにより、異なるOCR処理モードを切り替え可能
/// </summary>
public interface IHybridOcrStrategy
{
    /// <summary>
    /// OCR処理を実行
    /// </summary>
    /// <param name="processedImage">前処理済み画像</param>
    /// <param name="scaleFactor">スケール係数</param>
    /// <param name="roi">ROI領域（オプション）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>OCRテキスト領域リスト</returns>
    Task<IReadOnlyList<OcrTextRegion>> ExecuteAsync(
        Mat processedImage,
        double scaleFactor,
        Rectangle? roi,
        CancellationToken cancellationToken);

    /// <summary>
    /// 戦略名（ログ出力、デバッグ用）
    /// </summary>
    string StrategyName { get; }
}
```

2. **Baketa.Core/Abstractions/OCR/IHybridOcrStrategyFactory.cs**
```csharp
namespace Baketa.Core.Abstractions.OCR;

/// <summary>
/// ハイブリッドOCR戦略のファクトリ
/// </summary>
public interface IHybridOcrStrategyFactory
{
    /// <summary>
    /// 設定に基づいて適切な戦略を取得
    /// </summary>
    IHybridOcrStrategy GetStrategy(string strategyName);
}
```

3. **Baketa.Infrastructure/OCR/PaddleOCR/Strategies/FullRecognitionStrategy.cs**
```csharp
namespace Baketa.Infrastructure.OCR.PaddleOCR.Strategies;

/// <summary>
/// 完全OCR戦略（従来の通常モード）
/// </summary>
public class FullRecognitionStrategy : IHybridOcrStrategy
{
    private readonly IOcrExecutor _executor;
    private readonly IPaddleOcrResultConverter _converter;

    public string StrategyName => "FullRecognition";

    public async Task<IReadOnlyList<OcrTextRegion>> ExecuteAsync(
        Mat processedImage,
        double scaleFactor,
        Rectangle? roi,
        CancellationToken cancellationToken)
    {
        // 完全OCR実行
        var paddleResult = await _executor.ExecuteOcrAsync(processedImage, null, cancellationToken);

        // 座標変換あり
        return _converter.ConvertToTextRegions(new[] { paddleResult }, scaleFactor, roi);
    }
}
```

#### 1.3 DI登録方法の詳細設計

**ファイル**: `Baketa.Infrastructure/DI/Modules/InfrastructureModule.cs`

**追加登録**:
```csharp
private void RegisterHybridOcrStrategies(IServiceCollection services)
{
    // 各戦略をSingletonで登録
    services.AddSingleton<FullRecognitionStrategy>();
    services.AddSingleton<FastPreprocessingStrategy>();
    services.AddSingleton<AdaptiveStrategy>();

    // ファクトリーを登録
    services.AddSingleton<IHybridOcrStrategyFactory, HybridOcrStrategyFactory>();

    _logger?.LogDebug("ハイブリッドOCR戦略登録完了: 3戦略");
}
```

**呼び出し**: `RegisterServices()` メソッド内で呼び出し

---

### Phase 2: 検出専用モード廃止と統合（2-3日）

#### 2.1 ConvertDetectionOnlyResult()の段階的廃止

**Step 1: 呼び出し箇所の特定**

```bash
# Serena MCP使用
find_referencing_symbols "ConvertDetectionOnlyResult"
```

**期待結果**: PaddleOcrEngine.cs:1846のみ

**Step 2: ExecuteDetectionOnlyAsync()の修正**

**ファイル**: `Baketa.Infrastructure/OCR/PaddleOCR/Engine/PaddleOcrEngine.cs`

**修正箇所**: Line 1846付近

**修正前**:
```csharp
var paddleResult = await _executor.ExecuteDetectionOnlyAsync(processedMat, cancellationToken);
return _resultConverter.ConvertDetectionOnlyResult(new[] { paddleResult });
```

**修正後**:
```csharp
// 🔥 [HYBRID_REDESIGN] 検出専用モード廃止 - 完全OCRに変更
var paddleResult = await _executor.ExecuteOcrAsync(processedMat, null, cancellationToken);

// 座標変換あり（ROI調整実施）
return _resultConverter.ConvertToTextRegions(
    new[] { paddleResult },
    scaleFactor,
    regionOfInterest
);
```

**Step 3: ConvertDetectionOnlyResult()メソッド削除**

**ファイル**: `Baketa.Infrastructure/OCR/PaddleOCR/Services/PaddleOcrResultConverter.cs`

**削除行**: 131-173（約42行）

**影響確認**:
```bash
# Serena MCP検索で使用箇所が0件であることを確認
find_referencing_symbols "ConvertDetectionOnlyResult"
```

**Step 4: インターフェース宣言削除**

**ファイル**: `Baketa.Infrastructure/OCR/PaddleOCR/Abstractions/IPaddleOcrResultConverter.cs`

**削除メソッド宣言**:
```csharp
IReadOnlyList<OcrTextRegion> ConvertDetectionOnlyResult(PaddleOcrResult[] paddleResults);
```

#### 2.2 ExecuteHybridOcrAsync()の修正

**ファイル**: `Baketa.Infrastructure/OCR/PaddleOCR/Engine/PaddleOcrEngine.cs`

**修正箇所**: Line 545-565

**修正前**:
```csharp
if (_isHybridMode && _hybridService != null)
{
    __logger?.LogDebug("🔄 ハイブリッドモードでOCR実行（予防処理済み）");
    var processingMode = DetermineProcessingMode();
    textRegions = await _hybridService.ExecuteHybridOcrAsync(processedMat, processingMode, cancellationToken).ConfigureAwait(false);
    __logger?.LogDebug($"🔄 ハイブリッドOCR完了: {textRegions.Count}領域検出 ({processingMode}モード)");
}
```

**修正後**:
```csharp
if (_isHybridMode && _strategyFactory != null)
{
    __logger?.LogDebug("🔄 ハイブリッドモードでOCR実行（戦略パターン）");

    // 設定から戦略名を取得
    var strategyName = _settingsService.GetValue("OCR:PaddleOCR:HybridStrategy", "FullRecognition");
    var strategy = _strategyFactory.GetStrategy(strategyName);

    // 戦略実行（完全OCR + 座標変換）
    textRegions = await strategy.ExecuteAsync(processedMat, scaleFactor, regionOfInterest, cancellationToken).ConfigureAwait(false);

    __logger?.LogDebug($"🔄 ハイブリッドOCR完了: {textRegions.Count}領域検出 ({strategy.StrategyName}戦略)");
}
```

#### 2.3 HybridPaddleOcrService.cs既存実装の調査と方針決定

**調査タスク**:
```bash
# Serena MCP使用
find_symbol "HybridPaddleOcrService" --include_body true --depth 2
```

**調査事項**:
1. 既存実装の詳細構造
2. ExecuteHybridOcrAsync()の内部ロジック
3. 再利用可能なコード部分の特定

**方針決定**:
- **Option A採用の場合**: HybridPaddleOcrService.csをFullRecognitionStrategyに統合
- **Option B採用の場合**: 新規ファイル作成、既存コードは段階的にdeprecated

---

### Phase 3: 高速化の代替実装（3-4日）

#### 3.1 前処理最適化アプローチ

**新規ファイル**: `Baketa.Infrastructure/OCR/PaddleOCR/Strategies/FastPreprocessingStrategy.cs`

**実装**:
```csharp
namespace Baketa.Infrastructure.OCR.PaddleOCR.Strategies;

/// <summary>
/// 高速前処理戦略
/// 軽量な前処理で処理速度を向上させつつ、完全なテキスト認識を実行
/// </summary>
public class FastPreprocessingStrategy : IHybridOcrStrategy
{
    private readonly IOcrExecutor _executor;
    private readonly IPaddleOcrResultConverter _converter;
    private readonly ILogger<FastPreprocessingStrategy> _logger;

    public string StrategyName => "FastPreprocessing";

    public async Task<IReadOnlyList<OcrTextRegion>> ExecuteAsync(
        Mat processedImage,
        double scaleFactor,
        Rectangle? roi,
        CancellationToken cancellationToken)
    {
        // 軽量前処理適用
        var optimizedMat = ApplyFastPreprocessing(processedImage);

        try
        {
            // 完全OCR実行
            var paddleResult = await _executor.ExecuteOcrAsync(optimizedMat, null, cancellationToken);

            // 座標変換あり
            return _converter.ConvertToTextRegions(new[] { paddleResult }, scaleFactor, roi);
        }
        finally
        {
            optimizedMat?.Dispose();
        }
    }

    private Mat ApplyFastPreprocessing(Mat image)
    {
        // 通常モードより軽量な前処理
        // - Gaussian Blurのカーネルサイズ削減（5x5 → 3x3）
        // - Morphology処理のスキップ
        // - Adaptive Threshold簡素化

        var blurred = new Mat();
        Cv2.GaussianBlur(image, blurred, new Size(3, 3), 0);

        // 軽量二値化
        var binary = new Mat();
        Cv2.Threshold(blurred, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        blurred.Dispose();
        return binary;
    }
}
```

#### 3.2 適応的戦略の実装

**新規ファイル**: `Baketa.Infrastructure/OCR/PaddleOCR/Strategies/AdaptiveStrategy.cs`

**実装**:
```csharp
namespace Baketa.Infrastructure.OCR.PaddleOCR.Strategies;

/// <summary>
/// 適応的戦略
/// 画像品質に応じて最適な前処理を自動選択
/// </summary>
public class AdaptiveStrategy : IHybridOcrStrategy
{
    private readonly IFullRecognitionStrategy _fullStrategy;
    private readonly IFastPreprocessingStrategy _fastStrategy;
    private readonly double _qualityThreshold;

    public string StrategyName => "Adaptive";

    public async Task<IReadOnlyList<OcrTextRegion>> ExecuteAsync(
        Mat processedImage,
        double scaleFactor,
        Rectangle? roi,
        CancellationToken cancellationToken)
    {
        // 画像品質評価
        var quality = EvaluateImageQuality(processedImage);

        // 閾値以上なら高速戦略、未満なら完全戦略
        var selectedStrategy = quality >= _qualityThreshold ? _fastStrategy : _fullStrategy;

        _logger?.LogDebug($"画像品質: {quality:F2}, 選択戦略: {selectedStrategy.StrategyName}");

        return await selectedStrategy.ExecuteAsync(processedImage, scaleFactor, roi, cancellationToken);
    }

    private double EvaluateImageQuality(Mat image)
    {
        // ヒストグラム分析による品質評価
        // - コントラスト
        // - 明度分布
        // - エッジ強度

        // 簡易実装: 画像の標準偏差を品質指標とする
        Cv2.MeanStdDev(image, out var mean, out var stddev);

        // 標準偏差が高い = コントラストが高い = 高品質
        return stddev.Val0 / 128.0; // 0.0-1.0に正規化
    }
}
```

#### 3.3 結果キャッシュの実装（オプション）

**新規ファイル**: `Baketa.Infrastructure/OCR/PaddleOCR/Strategies/CachedHybridOcrStrategy.cs`

**実装**:
```csharp
namespace Baketa.Infrastructure.OCR.PaddleOCR.Strategies;

/// <summary>
/// キャッシュ付きハイブリッドOCR戦略
/// Decorator Patternによりキャッシング機能を追加
/// </summary>
public class CachedHybridOcrStrategy : IHybridOcrStrategy
{
    private readonly IHybridOcrStrategy _innerStrategy;
    private readonly ConcurrentDictionary<string, OcrCacheEntry> _resultCache;
    private readonly int _maxCacheSize;

    public string StrategyName => $"Cached{_innerStrategy.StrategyName}";

    public async Task<IReadOnlyList<OcrTextRegion>> ExecuteAsync(
        Mat processedImage,
        double scaleFactor,
        Rectangle? roi,
        CancellationToken cancellationToken)
    {
        // 画像ハッシュ計算
        var imageHash = ComputeImageHash(processedImage);

        // キャッシュヒット確認
        if (_resultCache.TryGetValue(imageHash, out var cachedEntry))
        {
            _logger?.LogDebug($"OCR結果キャッシュヒット: {imageHash}");
            return cachedEntry.Regions;
        }

        // キャッシュミス: OCR実行
        var result = await _innerStrategy.ExecuteAsync(processedImage, scaleFactor, roi, cancellationToken);

        // キャッシュ追加（LRU eviction）
        if (_resultCache.Count >= _maxCacheSize)
        {
            EvictOldestEntry();
        }

        _resultCache.TryAdd(imageHash, new OcrCacheEntry
        {
            Regions = result,
            Timestamp = DateTime.UtcNow
        });

        return result;
    }

    private string ComputeImageHash(Mat image)
    {
        // pHash (Perceptual Hash) 実装
        // または MD5ハッシュ（簡易版）
        using var resized = new Mat();
        Cv2.Resize(image, resized, new Size(8, 8));

        var hash = MD5.HashData(resized.Data);
        return Convert.ToBase64String(hash);
    }
}
```

#### 3.4 StrategyFactoryの実装

**新規ファイル**: `Baketa.Infrastructure/OCR/PaddleOCR/Factories/HybridOcrStrategyFactory.cs`

**実装**:
```csharp
namespace Baketa.Infrastructure.OCR.PaddleOCR.Factories;

/// <summary>
/// ハイブリッドOCR戦略のファクトリ実装
/// </summary>
public class HybridOcrStrategyFactory : IHybridOcrStrategyFactory
{
    private readonly IServiceProvider _serviceProvider;

    public IHybridOcrStrategy GetStrategy(string strategyName)
    {
        return strategyName switch
        {
            "FullRecognition" => _serviceProvider.GetRequiredService<FullRecognitionStrategy>(),
            "FastPreprocessing" => _serviceProvider.GetRequiredService<FastPreprocessingStrategy>(),
            "Adaptive" => _serviceProvider.GetRequiredService<AdaptiveStrategy>(),
            _ => throw new ArgumentException($"Unknown strategy: {strategyName}")
        };
    }
}
```

#### 3.5 appsettings.json設定追加

**ファイル**: `Baketa.UI/appsettings.json`

**追加設定**:
```json
{
  "OCR": {
    "PaddleOCR": {
      "EnableHybridMode": true,
      "HybridStrategy": "Adaptive",
      "AdaptiveThreshold": 0.6,
      "CacheEnabled": true,
      "MaxCacheSize": 100
    }
  }
}
```

#### 3.6 ベンチマーク測定

**ベンチマーク項目**:

1. **処理時間比較**
   - 通常モード（EnableHybridMode: false）
   - FullRecognitionStrategy
   - FastPreprocessingStrategy
   - AdaptiveStrategy

2. **メモリ使用量比較**
   - ピークメモリ使用量
   - 平均メモリ使用量
   - キャッシュ有効時の影響

3. **OCR精度比較**
   - テキスト認識率（文字単位、単語単位）
   - 座標精度（ピクセル単位の誤差）

4. **画質別性能評価**
   - 高品質画像（コントラスト高）
   - 低品質画像（コントラスト低、ノイズあり）

**測定ツール**: `BenchmarkDotNet` 使用

**新規ファイル**: `tests/Baketa.Infrastructure.Tests/OCR/PaddleOCR/HybridOcrStrategyBenchmarks.cs`

```csharp
[MemoryDiagnoser]
public class HybridOcrStrategyBenchmarks
{
    private Mat _testImage;

    [GlobalSetup]
    public void Setup()
    {
        _testImage = Cv2.ImRead("TestData/sample_dialogue.png");
    }

    [Benchmark(Baseline = true)]
    public async Task<IReadOnlyList<OcrTextRegion>> FullRecognitionStrategy_Execute()
    {
        var strategy = new FullRecognitionStrategy(_executor, _converter);
        return await strategy.ExecuteAsync(_testImage, 1.0, null, CancellationToken.None);
    }

    [Benchmark]
    public async Task<IReadOnlyList<OcrTextRegion>> FastPreprocessingStrategy_Execute()
    {
        var strategy = new FastPreprocessingStrategy(_executor, _converter);
        return await strategy.ExecuteAsync(_testImage, 1.0, null, CancellationToken.None);
    }

    [Benchmark]
    public async Task<IReadOnlyList<OcrTextRegion>> AdaptiveStrategy_Execute()
    {
        var strategy = new AdaptiveStrategy(_fullStrategy, _fastStrategy, 0.6);
        return await strategy.ExecuteAsync(_testImage, 1.0, null, CancellationToken.None);
    }
}
```

---

### Phase 4: テストと検証（1-2日）

#### 4.1 単体テスト追加

**新規ファイル**: `tests/Baketa.Infrastructure.Tests/OCR/PaddleOCR/Strategies/FullRecognitionStrategyTests.cs`

**テストケース**:

1. **完全テキスト認識テスト**
```csharp
[Fact]
public async Task ExecuteAsync_ShouldRecognizeFullText()
{
    // Arrange
    var mockExecutor = new Mock<IOcrExecutor>();
    mockExecutor
        .Setup(x => x.ExecuteOcrAsync(It.IsAny<Mat>(), null, It.IsAny<CancellationToken>()))
        .ReturnsAsync(new PaddleOcrResult
        {
            Regions = new[]
            {
                new PaddleOcrRegion
                {
                    Text = "この周波数帯はKバンドと呼ばれ、通信衛星や観測衛星の",
                    Confidence = 0.95,
                    Box = new[] { new Point(100, 640), new Point(800, 640), new Point(800, 690), new Point(100, 690) }
                }
            }
        });

    var mockConverter = new Mock<IPaddleOcrResultConverter>();
    mockConverter
        .Setup(x => x.ConvertToTextRegions(It.IsAny<PaddleOcrResult[]>(), 1.0, null))
        .Returns(new[]
        {
            new OcrTextRegion("この周波数帯はKバンドと呼ばれ、通信衛星や観測衛星の", new Rectangle(100, 640, 700, 50), 0.95)
        });

    var strategy = new FullRecognitionStrategy(mockExecutor.Object, mockConverter.Object);
    var testImage = new Mat(100, 100, MatType.CV_8UC1);

    // Act
    var result = await strategy.ExecuteAsync(testImage, 1.0, null, CancellationToken.None);

    // Assert
    result.Should().NotBeEmpty();
    result[0].Text.Should().Contain("この周波数帯はKバンドと呼ばれ");
    mockConverter.Verify(x => x.ConvertToTextRegions(It.IsAny<PaddleOcrResult[]>(), 1.0, null), Times.Once);
}
```

2. **座標変換テスト**
```csharp
[Fact]
public async Task ExecuteAsync_ShouldApplyCorrectCoordinates()
{
    // Arrange
    var roi = new Rectangle(100, 100, 800, 600);
    var scaleFactor = 1.5;

    // ... (Mockセットアップ)

    // Act
    var result = await strategy.ExecuteAsync(testImage, scaleFactor, roi, CancellationToken.None);

    // Assert
    mockConverter.Verify(x => x.ConvertToTextRegions(
        It.IsAny<PaddleOcrResult[]>(),
        scaleFactor,  // スケール係数が渡されることを確認
        roi),  // ROIが渡されることを確認
        Times.Once);
}
```

#### 4.2 統合テスト

**新規ファイル**: `tests/Baketa.Infrastructure.Tests/OCR/PaddleOCR/HybridModeIntegrationTests.cs`

**テストケース**:

1. **実画像でのOCR精度確認**
```csharp
[Fact]
public async Task HybridMode_RealImage_ShouldDetectAllDialogueLines()
{
    // Arrange
    var engine = CreatePaddleOcrEngineWithHybridMode(enabled: true, strategy: "FullRecognition");
    var testImage = Cv2.ImRead("TestData/chrono_trigger_dialogue.png");

    // Act
    var result = await engine.RecognizeTextAsync(testImage);

    // Assert
    result.Should().HaveCountGreaterThanOrEqualTo(2);  // 最低2行のセリフ検出
    result.Should().Contain(r => r.Text.Contains("この周波数帯はKバンドと呼ばれ"));
    result.Should().Contain(r => r.Bounds.Y >= 640 && r.Bounds.Y <= 690);  // Y座標範囲確認
}
```

2. **オーバーレイ座標の正確性確認**
```csharp
[Fact]
public async Task HybridMode_RealImage_ShouldProduceCorrectOverlayCoordinates()
{
    // Arrange
    var engine = CreatePaddleOcrEngineWithHybridMode(enabled: true, strategy: "FullRecognition");
    var testImage = Cv2.ImRead("TestData/chrono_trigger_dialogue.png");
    var roi = new Rectangle(0, 600, 1920, 480);  // 下部領域

    // Act
    var result = await engine.RecognizeTextAsync(testImage, roi);

    // Assert
    foreach (var region in result)
    {
        // スクリーン座標に変換されていることを確認
        region.Bounds.X.Should().BeGreaterThanOrEqualTo(0);
        region.Bounds.Y.Should().BeGreaterThanOrEqualTo(600);  // ROI Y offset適用確認
        region.Bounds.Y.Should().BeLessThan(1080);  // 画面範囲内
    }
}
```

3. **戦略切替テスト**
```csharp
[Theory]
[InlineData("FullRecognition")]
[InlineData("FastPreprocessing")]
[InlineData("Adaptive")]
public async Task HybridMode_ShouldSwitchStrategiesCorrectly(string strategyName)
{
    // Arrange
    var engine = CreatePaddleOcrEngineWithHybridMode(enabled: true, strategy: strategyName);
    var testImage = Cv2.ImRead("TestData/sample_dialogue.png");

    // Act
    var result = await engine.RecognizeTextAsync(testImage);

    // Assert
    result.Should().NotBeEmpty();
    result.All(r => !string.IsNullOrWhiteSpace(r.Text)).Should().BeTrue();
}
```

#### 4.3 リグレッションテスト

**目的**: 2つの問題が再発しないことを確認

**テストケース**:

1. **座標ずれ問題の再発防止**
```csharp
[Fact]
public async Task RegressionTest_CoordinateMisalignment_ShouldNotOccur()
{
    // Arrange
    var engine = CreatePaddleOcrEngineWithHybridMode(enabled: true, strategy: "FullRecognition");
    var testImage = LoadTestImageWithKnownCoordinates();
    var expectedBounds = new Rectangle(374, 903, 1322, 44);

    // Act
    var result = await engine.RecognizeTextAsync(testImage);

    // Assert
    result.Should().ContainSingle();
    var actualBounds = result[0].Bounds;

    // 許容誤差5ピクセル以内
    Math.Abs(actualBounds.X - expectedBounds.X).Should().BeLessThan(5);
    Math.Abs(actualBounds.Y - expectedBounds.Y).Should().BeLessThan(5);
    Math.Abs(actualBounds.Width - expectedBounds.Width).Should().BeLessThan(5);
    Math.Abs(actualBounds.Height - expectedBounds.Height).Should().BeLessThan(5);
}
```

2. **テキスト未検出問題の再発防止**
```csharp
[Fact]
public async Task RegressionTest_MissingDialogueLine_ShouldNotOccur()
{
    // Arrange
    var engine = CreatePaddleOcrEngineWithHybridMode(enabled: true, strategy: "FullRecognition");
    var testImage = Cv2.ImRead("TestData/chrono_trigger_dialogue.png");

    // Act
    var result = await engine.RecognizeTextAsync(testImage);

    // Assert
    // Y座標640-690の領域が検出されることを確認
    result.Should().Contain(r => r.Bounds.Y >= 640 && r.Bounds.Y <= 690);

    // テキストが空文字列でないことを確認
    result.All(r => !string.IsNullOrWhiteSpace(r.Text)).Should().BeTrue();
}
```

#### 4.4 パフォーマンステスト

**測定項目**:
- 処理時間: 通常モード vs FullRecognitionStrategy vs FastPreprocessingStrategy
- メモリ使用量: ピークメモリ、平均メモリ
- OCR精度: テキスト認識率、座標精度

**基準値**:
- 処理時間増加: 検出専用モード比で+20%以内
- メモリ使用量増加: +10%以内
- OCR精度: 95%以上維持

---

## 技術要件

### 新規ファイル一覧

| ファイルパス | 種類 | 目的 |
|------------|------|------|
| `Baketa.Core/Abstractions/OCR/IHybridOcrStrategy.cs` | インターフェース | Strategy Pattern抽象化 |
| `Baketa.Core/Abstractions/OCR/IHybridOcrStrategyFactory.cs` | インターフェース | Factory Pattern抽象化 |
| `Baketa.Infrastructure/OCR/PaddleOCR/Strategies/FullRecognitionStrategy.cs` | 実装 | 完全OCR戦略 |
| `Baketa.Infrastructure/OCR/PaddleOCR/Strategies/FastPreprocessingStrategy.cs` | 実装 | 高速前処理戦略 |
| `Baketa.Infrastructure/OCR/PaddleOCR/Strategies/AdaptiveStrategy.cs` | 実装 | 適応的戦略 |
| `Baketa.Infrastructure/OCR/PaddleOCR/Strategies/CachedHybridOcrStrategy.cs` | 実装 | キャッシュDecorator |
| `Baketa.Infrastructure/OCR/PaddleOCR/Factories/HybridOcrStrategyFactory.cs` | Factory | 戦略ファクトリ実装 |
| `tests/Baketa.Infrastructure.Tests/OCR/PaddleOCR/Strategies/FullRecognitionStrategyTests.cs` | 単体テスト | FullRecognitionStrategy検証 |
| `tests/Baketa.Infrastructure.Tests/OCR/PaddleOCR/HybridModeIntegrationTests.cs` | 統合テスト | 実画像検証 |
| `tests/Baketa.Infrastructure.Tests/OCR/PaddleOCR/HybridOcrStrategyBenchmarks.cs` | ベンチマーク | 性能測定 |

### 修正ファイル一覧

| ファイルパス | 修正内容 | 行数 |
|------------|----------|------|
| `Baketa.Infrastructure/DI/Modules/InfrastructureModule.cs` | DI登録追加 | +15行 |
| `Baketa.Infrastructure/OCR/PaddleOCR/Engine/PaddleOcrEngine.cs` | ハイブリッドモード分岐修正 | ±20行 |
| `Baketa.Infrastructure/OCR/PaddleOCR/Services/PaddleOcrResultConverter.cs` | ConvertDetectionOnlyResult削除 | -42行 |
| `Baketa.Infrastructure/OCR/PaddleOCR/Abstractions/IPaddleOcrResultConverter.cs` | メソッド宣言削除 | -1行 |
| `Baketa.UI/appsettings.json` | ハイブリッド設定追加 | +5行 |

### 依存パッケージ

- ✅ 既存パッケージのみ使用、追加不要
- BenchmarkDotNet（テストプロジェクトに既存）

### DI登録変更

**ファイル**: `Baketa.Infrastructure/DI/Modules/InfrastructureModule.cs`

**追加メソッド**:
```csharp
private void RegisterHybridOcrStrategies(IServiceCollection services)
{
    // 各戦略をSingletonで登録
    services.AddSingleton<FullRecognitionStrategy>();
    services.AddSingleton<FastPreprocessingStrategy>();
    services.AddSingleton<AdaptiveStrategy>();

    // ファクトリーを登録
    services.AddSingleton<IHybridOcrStrategyFactory, HybridOcrStrategyFactory>();

    Console.WriteLine("🚀 ハイブリッドOCR戦略登録完了: FullRecognition, FastPreprocessing, Adaptive");
}
```

**呼び出し**: `RegisterServices()` メソッド内で `RegisterPaddleOcrServices()` の後に呼び出し

---

## リスク管理

### リスク評価

| リスク | 影響度 | 発生確率 | 緩和策 |
|-------|-------|---------|--------|
| **パフォーマンス低下** | 中 | 中 | ベンチマークテスト必須化、基準値超過時はPhase 3実装 |
| **既存コード破壊** | 高 | 低 | 段階的実装、各フェーズでビルド・テスト必須 |
| **DI解決エラー** | 中 | 低 | 単体テストで事前検証、適切なエラーハンドリング |
| **座標変換ロジック破損** | 高 | 低 | リグレッションテスト必須、統合テストで実画像検証 |
| **OCR精度低下** | 中 | 低 | ベンチマーク測定、95%以上の精度維持 |

### ロールバック戦略

**Git Commit粒度**:
- Phase 1完了: `feat: IHybridOcrStrategy抽象化とDI登録`
- Phase 2.1完了: `refactor: ConvertDetectionOnlyResult削除`
- Phase 2.2完了: `refactor: ExecuteHybridOcrAsync修正`
- Phase 3.1完了: `feat: FastPreprocessingStrategy実装`
- Phase 3.2完了: `feat: AdaptiveStrategy実装`
- Phase 4完了: `test: ハイブリッドモード統合テスト追加`

**ロールバック手順**:
1. 問題発生フェーズを特定
2. `git revert <commit-hash>` で該当コミットを取り消し
3. appsettings.jsonで `EnableHybridMode: false` に設定
4. 従来の通常モードに戻す

**Feature Flag制御**:
```json
{
  "OCR": {
    "PaddleOCR": {
      "EnableHybridMode": false  // ← 緊急時にfalseに変更
    }
  }
}
```

---

## 期待効果

### 修正前後の比較

| 項目 | 現状（検出専用モード） | Phase 2完了後 | Phase 3完了後 |
|------|---------------------|-------------|-------------|
| **テキスト認識** | ❌ 空文字列 | ✅ 完全認識 | ✅ 完全認識 |
| **座標精度** | ❌ ROI相対座標 | ✅ スクリーン座標 | ✅ スクリーン座標 |
| **処理時間** | 基準値 | +30%程度（推定） | +20%以内（目標） |
| **OCR精度** | 不明（テキストなし） | ✅ 95%以上 | ✅ 95%以上 |
| **保守性** | 低（2ルート） | ✅ 高（1ルート） | ✅ 高（Strategy Pattern） |
| **拡張性** | 低 | 中 | ✅ 高（戦略追加容易） |

### ユーザー体験の改善

1. **オーバーレイ座標の正確性**
   - 修正前: 翻訳テキストが正しい位置に表示されない
   - 修正後: ✅ ゲームテキストの正確な位置にオーバーレイ表示

2. **テキスト検出の完全性**
   - 修正前: セリフ1行目が未検出
   - 修正後: ✅ すべてのセリフを検出

3. **処理速度**
   - Phase 2: 若干遅くなる可能性（+30%）
   - Phase 3: 最適化により+20%以内に抑制

### Clean Architecture準拠度

- ✅ Strategy Patternによる関心の分離
- ✅ Interface抽象化（IHybridOcrStrategy）
- ✅ DI Containerによる依存性注入
- ✅ Factory Patternによるオブジェクト生成
- ✅ Decorator Patternによる機能拡張（キャッシュ）

---

## 実装開始チェックリスト

### 事前準備

- [ ] HybridPaddleOcrService.cs既存実装の完全理解（Serena MCP検索）
- [ ] DetermineProcessingMode()メソッドの動作確認
- [ ] 検出専用モードが必要とされた理由の特定
- [ ] ベンチマーク測定環境の準備（BenchmarkDotNet）
- [ ] テスト画像の準備（chrono_trigger_dialogue.png等）

### Phase 1実装準備

- [ ] IHybridOcrStrategy.cs作成
- [ ] IHybridOcrStrategyFactory.cs作成
- [ ] FullRecognitionStrategy.cs作成
- [ ] HybridOcrStrategyFactory.cs作成
- [ ] InfrastructureModule.cs修正（DI登録）
- [ ] ビルド成功確認
- [ ] 単体テスト追加（FullRecognitionStrategyTests.cs）

### Phase 2実装準備

- [ ] Serena MCP検索でConvertDetectionOnlyResult使用箇所確認
- [ ] PaddleOcrEngine.cs修正準備
- [ ] PaddleOcrResultConverter.cs修正準備
- [ ] リグレッションテスト追加

### Phase 3実装準備

- [ ] FastPreprocessingStrategy.cs設計
- [ ] AdaptiveStrategy.cs設計
- [ ] CachedHybridOcrStrategy.cs設計（オプション）
- [ ] appsettings.json設定追加
- [ ] ベンチマーク測定計画

### Phase 4実装準備

- [ ] 統合テスト計画
- [ ] リグレッションテスト計画
- [ ] パフォーマンステスト計画
- [ ] テスト画像準備

---

## 参考資料

### UltraThink調査レポート

- [E:\dev\Baketa\docs\investigations\ULTRATHINK_HYBRID_MODE_INVESTIGATION.md](E:\dev\Baketa\docs\investigations\ULTRATHINK_HYBRID_MODE_INVESTIGATION.md)

### Geminiレビュー結果

- 全体評価: ⭐⭐⭐⭐⭐ 非常に的確かつ妥当
- IHybridOcrStrategy抽象化: 優れている
- Strategy Pattern適用: 理想的
- 追加考慮事項: 6点（すべて設計に統合済み）

### 関連Issue

- OCR座標ずれ問題
- セリフ1行目未検出問題

---

## 変更履歴

| 日付 | バージョン | 変更内容 | 担当者 |
|------|-----------|---------|--------|
| 2025-01-11 | 1.0 | 初版作成（UltraThink + Geminiレビュー統合） | Claude Code |

---

## 承認

| 役割 | 承認日 | 署名 |
|------|--------|------|
| **設計レビュー** | 2025-01-11 | Gemini ⭐⭐⭐⭐⭐ |
| **技術方針確定** | 2025-01-11 | UltraThink Phase 4完了 |
| **実装開始承認** | - | 待機中 |

---

**次のアクション**: Phase 1実装開始（HybridPaddleOcrService.cs既存実装の調査）
