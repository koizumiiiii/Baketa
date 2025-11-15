# PaddleOCR安定性改善戦略 - 恒久的解決アプローチ

## 概要

本文書は、PP-OCRv5統一移行後に発生したPaddlePredictor run failed問題に対する恒久的解決戦略を定義します。場当たり的な対処ではなく、アーキテクチャレベルでの根本的改善を通じて、OCR処理の安定性を抜本的に向上させることを目的とします。

## 問題の本質分析

### 根本原因の特定

**表層問題**: PaddlePredictor run failed（継続実行時）
**真の原因**: 
1. **アンマネージドメモリ管理**: OpenCV Matオブジェクトの不適切な解放
2. **ステートフル共有**: プール化されたOCRエンジンの状態汚染

### 症状パターンの分析

- **「初回成功・継続失敗」**: エンジン内部状態の累積的破壊
- **「V5統一後に顕在化」**: 単一モデルへの負荷集中で問題増幅
- **「フォールバック依存」**: 根本解決未達による症状対処

## 恒久的解決アーキテクチャ

### コア戦略: Immutable OCR Service Pattern

**基本原理**: 
- 各OCR処理を完全に独立した状態で実行
- リソース競合の原理的排除
- メモリリークの自動防止

**アーキテクチャ概念図**:
```
[Application Layer]
       ↓
[IOcrServiceFactory] ← シングルトン登録
       ↓
[CreateService()] ← 毎回新規インスタンス生成
       ↓
[IOcrService] ← using文で確実なDispose
       ↓
[PaddleOcrEngine] ← 処理完了後に確実破棄
```

### インターフェース設計

```csharp
namespace Baketa.Core.Abstractions.OCR;

/// <summary>
/// OCRサービスファクトリ - 恒久的安定性確保のための新アーキテクチャ
/// </summary>
public interface IOcrServiceFactory
{
    /// <summary>
    /// 新しいOCRサービスインスタンスを生成（同期版）
    /// </summary>
    /// <returns>破棄可能なOCRサービスインスタンス</returns>
    IOcrService CreateService();
    
    /// <summary>
    /// 新しいOCRサービスインスタンスを生成（非同期版）
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>破棄可能なOCRサービスインスタンス</returns>
    Task<IOcrService> CreateServiceAsync(CancellationToken cancellationToken = default);
}
```

## 段階的実装計画

### Phase 1: 緊急対処（1-2日）

**目的**: メモリリークの即座改善  
**対象**: 全Matオブジェクト生成箇所の修正

```csharp
// 修正前（危険）
var mat = new Mat();
var processedMat = PreprocessImage(mat);
// ... Dispose未確保

// 修正後（安全）
using var mat = new Mat();
using var processedMat = PreprocessImage(mat);
// ... using文により自動Dispose確保
```

**実装対象ファイル**:
- `PaddleOcrEngine.cs`: ~30箇所のMat生成
- `ImageFilterBase.cs`: フィルター処理のMat
- `BatchOcrProcessor.cs`: バッチ処理のMat管理

### Phase 2: アーキテクチャ改善（3-5日）

**目的**: ファクトリパターン導入による状態汚染排除

#### 2.1 ファクトリ実装

```csharp
namespace Baketa.Infrastructure.OCR.PaddleOCR.Factory;

/// <summary>
/// PaddleOCRサービスファクトリ実装 - 毎回クリーンなインスタンス生成
/// </summary>
public class PaddleOcrServiceFactory : IOcrServiceFactory
{
    private readonly ILogger<PaddleOcrServiceFactory> _logger;
    private readonly IOptionsMonitor<OcrSettings> _ocrSettings;
    
    // モデルは静的キャッシュで保持（ロード時間削減）
    private static readonly Lazy<FullOcrModel> _cachedModel = 
        new(() => LocalFullModels.ChineseV5);
    
    public PaddleOcrServiceFactory(
        ILogger<PaddleOcrServiceFactory> logger,
        IOptionsMonitor<OcrSettings> ocrSettings)
    {
        _logger = logger;
        _ocrSettings = ocrSettings;
    }
    
    public IOcrService CreateService()
    {
        // 新しいエンジンインスタンスを毎回生成
        var instanceId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogDebug("OCRサービス新規作成: {InstanceId}", instanceId);
        
        var engine = new PaddleOcrEngine(_cachedModel.Value, _ocrSettings.CurrentValue, instanceId);
        return new PaddleOcrService(engine, _logger);
    }
    
    public async Task<IOcrService> CreateServiceAsync(CancellationToken cancellationToken = default)
    {
        // モデル初期化の非同期処理が必要な場合の実装予約
        await Task.Yield(); // 現在は同期処理のため形式的
        return CreateService();
    }
}
```

#### 2.2 DI設定変更

```csharp
// Baketa.Infrastructure/DI/Modules/InfrastructureModule.cs

// 旧: プール化戦略（削除）
// services.AddSingleton<IOcrService, PooledOcrService>();

// 新: ファクトリ戦略（追加）
services.AddSingleton<IOcrServiceFactory, PaddleOcrServiceFactory>();
```

### Phase 3: 利用側移行（5-7日）

**目的**: 全利用箇所のファクトリパターン移行

#### 3.1 主要サービス修正

```csharp
namespace Baketa.Application.Services.Translation;

public class TranslationOrchestrationService : IDisposable
{
    private readonly IOcrServiceFactory _ocrServiceFactory;
    private readonly ILogger<TranslationOrchestrationService> _logger;
    
    public TranslationOrchestrationService(
        IOcrServiceFactory ocrServiceFactory,
        ILogger<TranslationOrchestrationService> logger)
    {
        _ocrServiceFactory = ocrServiceFactory;
        _logger = logger;
    }
    
    public async Task<TranslationResult> ExecuteTranslationAsync(
        IAdvancedImage image, 
        CancellationToken cancellationToken = default)
    {
        // OCRフェーズ - 独立したインスタンスで実行
        OcrResult ocrResult;
        using (var ocrService = _ocrServiceFactory.CreateService())
        {
            _logger.LogDebug("OCR処理開始 - 専用インスタンス使用");
            ocrResult = await ocrService.PerformOcrAsync(image, cancellationToken);
        } // ここで確実にリソース解放
        
        _logger.LogInformation("OCR完了: {TextRegionCount}個のテキスト領域検出", ocrResult.TextRegions.Count);
        
        // 翻訳フェーズ
        return await TranslateOcrResult(ocrResult, cancellationToken);
    }
}
```

#### 3.2 バッチ処理修正

```csharp
namespace Baketa.Infrastructure.OCR.BatchProcessing;

public class BatchOcrProcessor
{
    private readonly IOcrServiceFactory _ocrServiceFactory;
    
    public async Task<BatchOcrResult> ProcessBatchAsync(
        IReadOnlyList<OcrRegion> regions,
        CancellationToken cancellationToken = default)
    {
        var results = new List<OcrResult>();
        
        // 各領域を独立したOCRインスタンスで処理
        foreach (var region in regions)
        {
            using var ocrService = _ocrServiceFactory.CreateService();
            var result = await ocrService.ProcessRegionAsync(region, cancellationToken);
            results.Add(result);
        }
        
        return new BatchOcrResult(results);
    }
}
```

## 検証と監視戦略

### メトリクス定義

```csharp
namespace Baketa.Infrastructure.OCR.Monitoring;

/// <summary>
/// OCRサービスの運用メトリクス
/// </summary>
public class OcrServiceMetrics
{
    /// <summary>総インスタンス生成数</summary>
    public long TotalInstancesCreated { get; set; }
    
    /// <summary>現在アクティブなインスタンス数</summary>
    public long CurrentActiveInstances { get; set; }
    
    /// <summary>平均処理時間（ミリ秒）</summary>
    public long AverageProcessingTimeMs { get; set; }
    
    /// <summary>メモリ使用量（MB）</summary>
    public long MemoryUsageMB { get; set; }
    
    /// <summary>失敗カウント</summary>
    public long FailureCount { get; set; }
    
    /// <summary>成功率（%）</summary>
    public double SuccessRate => 
        TotalInstancesCreated > 0 ? 
        ((double)(TotalInstancesCreated - FailureCount) / TotalInstancesCreated) * 100 : 
        0;
}
```

### 診断ログ強化

```csharp
// ファクトリでのインスタンス生成時
_logger.LogInformation(
    "OCRインスタンス作成: {InstanceId}, メモリ: {MemoryMB}MB, 総作成数: {TotalCreated}",
    instanceId, 
    GC.GetTotalMemory(false) / 1_048_576,
    Interlocked.Increment(ref _totalInstancesCreated));

// using文でのDispose時
_logger.LogDebug(
    "OCRインスタンス破棄: {InstanceId}, 処理時間: {ProcessingTimeMs}ms",
    instanceId,
    (DateTime.UtcNow - createdAt).TotalMilliseconds);
```

## リスク管理

### 潜在的リスク

| リスク | 影響 | 確率 | 緩和策 |
|--------|------|------|--------|
| パフォーマンス劣化 | 中 | 中 | モデルの静的キャッシュ、インスタンス生成最適化 |
| メモリ使用量増加 | 中 | 低 | SemaphoreSlimで同時実行数制限 |
| 初期実装不具合 | 高 | 低 | 段階的移行、十分なテスト |

### 代替アプローチ

1. **プロセス分離**: OCRを別プロセスで実行（最終手段）
2. **代替OCRエンジン**: Tesseract.NET、Azure Computer Vision API
3. **ハイブリッド戦略**: 軽量処理は使い捨て、重量処理はプール

## 成功判定基準

### 定量的指標

| メトリクス | 現状 | 目標 | 測定方法 |
|------------|------|------|----------|
| PaddlePredictor失敗率 | ~30% | <5% | エラーログ監視 |
| 継続実行安定性 | 30分で不安定 | 24時間安定 | 長時間稼働テスト |
| メモリ使用量 | 増加傾向 | 一定範囲内 | パフォーマンスカウンター |
| テキスト検出成功率 | 変動大 | >95% | OCR結果統計 |

### 定性的指標

- **エラーログの大幅削減**
- **フォールバック処理の発生頻度低下**  
- **開発者体験の向上**（デバッグ容易性）
- **ユーザー体験の向上**（応答性・安定性）

## 実装スケジュール

| フェーズ | 内容 | 工数 | 開始条件 | 完了条件 |
|---------|------|------|----------|----------|
| Phase 1 | Mat管理改善 | 1-2日 | 即座開始可能 | 全Mat箇所修正完了 |
| Phase 2 | ファクトリ導入 | 3-5日 | Phase 1完了 | ファクトリ実装・テスト完了 |
| Phase 3 | 全面移行 | 5-7日 | Phase 2完了 | 全利用箇所移行・検証完了 |

### 推奨実装順序

1. **即座実施**: Phase 1（Mat管理）- 低リスク・高効果
2. **週内実施**: Phase 2（ファクトリ）- アーキテクチャ基盤
3. **段階移行**: Phase 3（利用側）- 慎重にテストしながら

## 結論

本戦略により、以下の恒久的改善を実現します：

1. **根本原因解決**: メモリ管理とステート管理の抜本的改善
2. **アーキテクチャ向上**: クリーンアーキテクチャ原則に沿った設計
3. **保守性向上**: 問題の再発防止とデバッグ容易性の確保
4. **拡張性確保**: 将来的なOCRエンジン変更への対応力

この戦略は場当たり的な対処ではなく、**システム全体の品質とレジリエンス**を向上させる包括的アプローチです。

---

**Document Version**: 1.0  
**Last Updated**: 2025-08-24  
**Author**: Claude Code Assistant  
**Review Status**: Ready for Implementation