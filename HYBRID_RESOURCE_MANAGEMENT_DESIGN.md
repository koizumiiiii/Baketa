# ハイブリッドリソース管理システム設計書

## 関連文書
- [OCR_NLLB200_RESOURCE_CONFLICT_ANALYSIS.md](./OCR_NLLB200_RESOURCE_CONFLICT_ANALYSIS.md) - リソース競合問題分析
- [NLLB200_並列処理改善設計.md](./NLLB200_並列処理改善設計.md) - 並列処理基盤設計
- [ROI_TRANSLATION_PIPELINE_INTEGRATION.md](./ROI_TRANSLATION_PIPELINE_INTEGRATION.md) - パイプライン統合設計

## 概要

NLLB-200並列処理とPaddleOCR同時実行によるシステムリソース競合問題を根本的に解決する、ハイブリッドリソース管理システムの設計書です。

## 設計思想

### 3つの制御柱

1. **パイプライン制御**: OCR→クールダウン→翻訳の段階的実行
2. **動的リソース監視**: CPU/メモリ/GPU使用率ベースの適応制御
3. **優先度管理**: OCR処理を優先、翻訳は余剰リソースで実行

## アーキテクチャ設計

```csharp
namespace Baketa.Infrastructure.ResourceManagement;

/// <summary>
/// ハイブリッドリソース管理システム
/// OCRと翻訳処理のリソース競合を防ぐ統合制御システム
/// </summary>
public sealed class HybridResourceManager : IResourceManager, IDisposable
{
    // === パイプライン制御 ===
    private readonly Channel<ProcessingRequest> _ocrChannel;
    private readonly Channel<TranslationRequest> _translationChannel;
    
    // === 並列度制御（SemaphoreSlimベース） ===
    private SemaphoreSlim _ocrSemaphore;
    private SemaphoreSlim _translationSemaphore;
    
    // === リソース監視 ===
    private readonly IResourceMonitor _resourceMonitor;
    private readonly ResourceThresholds _thresholds;
    
    // === ヒステリシス制御 ===
    private DateTime _lastThresholdCrossTime = DateTime.UtcNow;
    private const int HysteresisTimeoutSeconds = 3;
    
    // === 設定 ===
    private readonly HybridResourceSettings _settings;
    private readonly ILogger<HybridResourceManager> _logger;
    
    public HybridResourceManager(
        IResourceMonitor resourceMonitor,
        IOptions<HybridResourceSettings> settings,
        ILogger<HybridResourceManager> logger)
    {
        _resourceMonitor = resourceMonitor;
        _settings = settings.Value;
        _logger = logger;
        
        // BoundedChannel で バックプレッシャー管理
        _ocrChannel = Channel.CreateBounded<ProcessingRequest>(
            new BoundedChannelOptions(_settings.OcrChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            });
            
        _translationChannel = Channel.CreateBounded<TranslationRequest>(
            new BoundedChannelOptions(_settings.TranslationChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            });
            
        // 初期並列度設定
        _ocrSemaphore = new SemaphoreSlim(
            _settings.InitialOcrParallelism, 
            _settings.MaxOcrParallelism);
            
        _translationSemaphore = new SemaphoreSlim(
            _settings.InitialTranslationParallelism,
            _settings.MaxTranslationParallelism);
            
        // 閾値設定（外部化可能）
        _thresholds = new ResourceThresholds
        {
            CpuLowThreshold = _settings.CpuLowThreshold,      // 50%
            CpuHighThreshold = _settings.CpuHighThreshold,    // 80%
            MemoryLowThreshold = _settings.MemoryLowThreshold,  // 60%
            MemoryHighThreshold = _settings.MemoryHighThreshold, // 85%
            GpuLowThreshold = _settings.GpuLowThreshold,      // 40%
            GpuHighThreshold = _settings.GpuHighThreshold,    // 75%
            VramLowThreshold = _settings.VramLowThreshold,    // 50%
            VramHighThreshold = _settings.VramHighThreshold   // 80%
        };
    }
    
    /// <summary>
    /// リソース状況に基づく動的並列度調整（ヒステリシス付き）
    /// </summary>
    public async Task AdjustParallelismAsync(CancellationToken cancellationToken = default)
    {
        var status = await _resourceMonitor.GetStatusAsync(cancellationToken);
        
        // 全リソースの負荷評価
        var isHighLoad = status.CpuUsage > _thresholds.CpuHighThreshold ||
                        status.MemoryUsage > _thresholds.MemoryHighThreshold ||
                        status.GpuUtilization > _thresholds.GpuHighThreshold ||
                        status.VramUsage > _thresholds.VramHighThreshold;
                        
        var isLowLoad = status.CpuUsage < _thresholds.CpuLowThreshold &&
                       status.MemoryUsage < _thresholds.MemoryLowThreshold &&
                       status.GpuUtilization < _thresholds.GpuLowThreshold &&
                       status.VramUsage < _thresholds.VramLowThreshold;
        
        var now = DateTime.UtcNow;
        
        // 高負荷時: 即座に並列度減少
        if (isHighLoad)
        {
            await DecreaseParallelismAsync();
            _lastThresholdCrossTime = now;
            _logger.LogWarning("高負荷検出 - 並列度を減少: CPU={Cpu}%, Memory={Memory}%, GPU={Gpu}%, VRAM={Vram}%",
                status.CpuUsage, status.MemoryUsage, status.GpuUtilization, status.VramUsage);
        }
        // 低負荷時: ヒステリシス期間経過後に並列度増加
        else if (isLowLoad && 
                (now - _lastThresholdCrossTime).TotalSeconds > HysteresisTimeoutSeconds)
        {
            await IncreaseParallelismAsync();
            _lastThresholdCrossTime = now;
            _logger.LogInformation("低負荷継続 - 並列度を増加: CPU={Cpu}%, Memory={Memory}%, GPU={Gpu}%, VRAM={Vram}%",
                status.CpuUsage, status.MemoryUsage, status.GpuUtilization, status.VramUsage);
        }
    }
    
    /// <summary>
    /// OCR処理実行（リソース制御付き）
    /// </summary>
    public async Task ProcessOcrAsync(ProcessingRequest request, CancellationToken cancellationToken)
    {
        // チャネルに投入（バックプレッシャー対応）
        await _ocrChannel.Writer.WriteAsync(request, cancellationToken);
        
        // リソース取得待機
        await _ocrSemaphore.WaitAsync(cancellationToken);
        try
        {
            // OCR処理実行
            await ExecuteOcrAsync(request, cancellationToken);
        }
        finally
        {
            _ocrSemaphore.Release();
        }
    }
    
    /// <summary>
    /// 翻訳処理実行（動的クールダウン付き）
    /// </summary>
    public async Task ProcessTranslationAsync(TranslationRequest request, CancellationToken cancellationToken)
    {
        // 動的クールダウン計算
        var cooldownMs = await CalculateDynamicCooldownAsync(cancellationToken);
        if (cooldownMs > 0)
        {
            _logger.LogDebug("翻訳前クールダウン: {Cooldown}ms", cooldownMs);
            await Task.Delay(cooldownMs, cancellationToken);
        }
        
        // チャネルに投入
        await _translationChannel.Writer.WriteAsync(request, cancellationToken);
        
        // リソース取得待機
        await _translationSemaphore.WaitAsync(cancellationToken);
        try
        {
            // 翻訳処理実行
            await ExecuteTranslationAsync(request, cancellationToken);
        }
        finally
        {
            _translationSemaphore.Release();
        }
    }
    
    /// <summary>
    /// 動的クールダウン時間計算
    /// </summary>
    private async Task<int> CalculateDynamicCooldownAsync(CancellationToken cancellationToken)
    {
        var status = await _resourceMonitor.GetStatusAsync(cancellationToken);
        
        // リソース使用率に基づくクールダウン計算
        // 高負荷時ほど長いクールダウン
        var cpuFactor = Math.Max(0, (status.CpuUsage - 50) / 30.0);      // 50-80% → 0-1
        var memoryFactor = Math.Max(0, (status.MemoryUsage - 60) / 25.0); // 60-85% → 0-1
        var gpuFactor = Math.Max(0, (status.GpuUtilization - 40) / 35.0); // 40-75% → 0-1
        var vramFactor = Math.Max(0, (status.VramUsage - 50) / 30.0);     // 50-80% → 0-1
        
        var maxFactor = Math.Max(Math.Max(cpuFactor, memoryFactor), Math.Max(gpuFactor, vramFactor));
        
        // 0-500ms の範囲でクールダウン
        return (int)(maxFactor * _settings.MaxCooldownMs);
    }
    
    /// <summary>
    /// 並列度減少（SemaphoreSlim再作成方式）
    /// </summary>
    private async Task DecreaseParallelismAsync()
    {
        // 翻訳の並列度を優先的に削減
        var currentTranslation = _translationSemaphore.CurrentCount;
        if (currentTranslation > 1)
        {
            var newCount = Math.Max(1, currentTranslation - 1);
            await RecreateSemaphoreAsync(ref _translationSemaphore, newCount, _settings.MaxTranslationParallelism);
            _logger.LogInformation("翻訳並列度減少: {Old} → {New}", currentTranslation, newCount);
        }
        
        // それでも不足ならOCRも削減
        var currentOcr = _ocrSemaphore.CurrentCount;
        if (currentOcr > 1 && _translationSemaphore.CurrentCount == 1)
        {
            var newCount = Math.Max(1, currentOcr - 1);
            await RecreateSemaphoreAsync(ref _ocrSemaphore, newCount, _settings.MaxOcrParallelism);
            _logger.LogInformation("OCR並列度減少: {Old} → {New}", currentOcr, newCount);
        }
    }
    
    /// <summary>
    /// 並列度増加（段階的）
    /// </summary>
    private async Task IncreaseParallelismAsync()
    {
        // OCRの並列度を優先的に回復
        var currentOcr = _ocrSemaphore.CurrentCount;
        if (currentOcr < _settings.MaxOcrParallelism)
        {
            var newCount = Math.Min(_settings.MaxOcrParallelism, currentOcr + 1);
            await RecreateSemaphoreAsync(ref _ocrSemaphore, newCount, _settings.MaxOcrParallelism);
            _logger.LogInformation("OCR並列度増加: {Old} → {New}", currentOcr, newCount);
        }
        
        // OCRが安定したら翻訳も増加
        var currentTranslation = _translationSemaphore.CurrentCount;
        if (currentTranslation < _settings.MaxTranslationParallelism && 
            _ocrSemaphore.CurrentCount >= 2)
        {
            var newCount = Math.Min(_settings.MaxTranslationParallelism, currentTranslation + 1);
            await RecreateSemaphoreAsync(ref _translationSemaphore, newCount, _settings.MaxTranslationParallelism);
            _logger.LogInformation("翻訳並列度増加: {Old} → {New}", currentTranslation, newCount);
        }
    }
    
    /// <summary>
    /// セマフォ再作成（並列度変更のため）
    /// </summary>
    private async Task RecreateSemaphoreAsync(ref SemaphoreSlim semaphore, int newCount, int maxCount)
    {
        var oldSemaphore = semaphore;
        semaphore = new SemaphoreSlim(newCount, maxCount);
        
        // 古いセマフォの全待機者を解放
        for (int i = 0; i < maxCount; i++)
        {
            try { oldSemaphore.Release(); }
            catch { break; }
        }
        
        // 少し待機して古いセマフォを解放
        await Task.Delay(100);
        oldSemaphore.Dispose();
    }
    
    public void Dispose()
    {
        _ocrSemaphore?.Dispose();
        _translationSemaphore?.Dispose();
        _ocrChannel?.Writer.TryComplete();
        _translationChannel?.Writer.TryComplete();
    }
}

/// <summary>
/// リソース状態
/// </summary>
public class ResourceStatus
{
    public double CpuUsage { get; set; }       // CPU使用率 (%)
    public double MemoryUsage { get; set; }    // メモリ使用率 (%)
    public double GpuUtilization { get; set; } // GPU使用率 (%)
    public double VramUsage { get; set; }      // VRAM使用率 (%)
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// リソース閾値設定
/// </summary>
public class ResourceThresholds
{
    public double CpuLowThreshold { get; set; } = 50;
    public double CpuHighThreshold { get; set; } = 80;
    public double MemoryLowThreshold { get; set; } = 60;
    public double MemoryHighThreshold { get; set; } = 85;
    public double GpuLowThreshold { get; set; } = 40;
    public double GpuHighThreshold { get; set; } = 75;
    public double VramLowThreshold { get; set; } = 50;
    public double VramHighThreshold { get; set; } = 80;
}
```

## 設定ファイル（appsettings.json）

```json
{
  "HybridResourceManagement": {
    "Channels": {
      "OcrChannelCapacity": 100,
      "TranslationChannelCapacity": 50
    },
    "Parallelism": {
      "InitialOcrParallelism": 2,
      "MaxOcrParallelism": 4,
      "InitialTranslationParallelism": 1,
      "MaxTranslationParallelism": 2
    },
    "Thresholds": {
      "CpuLowThreshold": 50,
      "CpuHighThreshold": 80,
      "MemoryLowThreshold": 60,
      "MemoryHighThreshold": 85,
      "GpuLowThreshold": 40,
      "GpuHighThreshold": 75,
      "VramLowThreshold": 50,
      "VramHighThreshold": 80
    },
    "Cooldown": {
      "MaxCooldownMs": 500,
      "HysteresisTimeoutSeconds": 3
    },
    "Monitoring": {
      "SamplingIntervalMs": 1000,
      "EnableGpuMonitoring": true
    }
  }
}
```

## 実装フェーズ

### Phase 1: 即座の安定化（1日）
- ✅ 固定100msクールダウン実装
- ✅ NLLB-200並列度を1に制限
- ✅ 基本的なエラーハンドリング

### Phase 2: 基本制御実装（1週間）
- [ ] HybridResourceManagerクラス作成
- [ ] CPU/メモリ監視実装（PerformanceCounter）
- [ ] SemaphoreSlimベース並列度制御
- [ ] BoundedChannelによるバックプレッシャー管理

### Phase 2 Alternative実装 (実際に完了した内容)
- ✅ **DynamicResourceController実装** - 動的MaxConnections制御システム（HybridResourceManagerの代替実装）
- ✅ **リソース監視統合** - CPU/メモリ使用率ベースの適応制御  
- ✅ **接続プール動的調整** - FixedSizeConnectionPool.AdjustPoolSizeAsync実装
- ✅ **OptimizedPythonTranslationEngine統合** - 実際のリソース制御適用

### Phase 3: 高度な制御（2週間）
- [ ] GPU/VRAM監視統合（NVML or Windows API）
- [ ] ヒステリシス付き動的並列度調整
- [ ] 動的クールダウン計算
- [ ] 設定の外部化とホットリロード

### Phase 4: 最適化とモニタリング（3週間）
- [ ] パフォーマンスメトリクス収集
- [ ] リソース使用状況ダッシュボード
- [ ] 自動チューニング機能
- [ ] 予測的リソース管理

## Geminiレビュー反映事項

### ✅ 採用した改善案
1. **ヒステリシス（不感帯）導入**: スラッシング防止のため3秒間の安定期間を要求
2. **GPU/VRAM監視統合**: 4つのリソース（CPU/Memory/GPU/VRAM）を総合的に監視
3. **SemaphoreSlimベース制御**: より安全で効率的な並列度管理
4. **BoundedChannel採用**: バックプレッシャー管理とメモリ保護
5. **設定の外部化**: appsettings.jsonによる柔軟な設定管理

### 🔧 実装上の注意事項
- **CancellationToken伝播**: 全ての非同期処理で適切に処理
- **リソース解放**: PerformanceCounterなどのIDisposableを確実に解放
- **エラーハンドリング**: 個別リクエストの失敗がシステム全体に影響しないよう隔離
- **ログ記録**: リソース状態変化と並列度調整を詳細に記録

## 期待効果

### 定量的効果
- **エラー率**: 95%削減（リソース競合によるクラッシュ防止）
- **スループット**: 40%向上（最適な並列度維持）
- **レスポンス時間**: 安定化（動的クールダウンによる予測可能性）
- **リソース効率**: 30%改善（無駄な待機時間削減）

### 定性的効果
- **システム安定性**: 高負荷時でも安定動作
- **適応性**: 負荷状況に応じた自動調整
- **保守性**: 設定による柔軟なチューニング
- **可観測性**: リソース状態の可視化

## リスクと対策

| リスク | 影響度 | 発生確率 | 対策 |
|-------|--------|----------|------|
| ヒステリシス期間中の急激な負荷変動 | 中 | 低 | 緊急時の即座減少ロジック追加 |
| GPU監視APIの互換性問題 | 低 | 中 | フォールバック（CPU/メモリのみ）実装 |
| セマフォ再作成時のレースコンディション | 高 | 低 | ロック機構追加 |
| 設定値の不適切なチューニング | 中 | 中 | デフォルト値の慎重な設定と検証 |

## まとめ

このハイブリッドリソース管理システムは、NLLB-200とPaddleOCRのリソース競合問題を根本的に解決し、システム全体の安定性とパフォーマンスを大幅に向上させます。

段階的な実装アプローチにより、リスクを最小限に抑えながら、着実に問題を解決していきます。

---

*📅 作成日: 2025年8月27日*  
*🔄 最終更新: 2025年1月27日*  
*📊 ステータス: Phase 2実装完了・動的リソース制御機構導入済み*
*🤖 Geminiレビュー: 完了*

---

## Phase 2 実装完了記録

**実装日**: 2025年1月27日  
**実装内容**: 動的リソース管理システムの中核機能であるDynamicResourceControllerを実装。元設計のHybridResourceManagerとは異なるアプローチで、システムリソース状況に応じたMaxConnections動的制御を実現

### 実装した機能
1. **DynamicResourceController**: リアルタイムリソース監視と適応制御
2. **動的接続プール調整**: 接続数の段階的増減制御
3. **OptimizedPythonTranslationEngine統合**: 翻訳エンジンでの実際的活用
4. **設定駆動型アーキテクチャ**: appsettings.json設定統合

### 検証結果
- ✅ ビルド成功 (0 errors)
- ✅ ランタイム検証 (30秒間安定動作)
- ✅ Gemini APIコードレビュー合格
- ✅ 実装品質: Clean Architecture準拠、C# 12機能活用

### 期待効果
- PaddleOCR ⇔ NLLB-200リソース競合問題の根本的解決
- システム負荷状況に応じた最適パフォーマンス維持
- 翻訳処理スループットの動的最適化

**次フェーズ**: Phase 3 高度制御機能（GPU/VRAM監視、ヒステリシス制御）