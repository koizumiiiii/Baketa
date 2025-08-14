# 翻訳エンジン最適化技術仕様書

## 📋 概要

Issue #144「Python翻訳エンジン最適化」で発見された接続ロック競合問題の解決策と、Phase 1-3の詳細実装仕様を定義。

**文書バージョン**: 1.1  
**作成日**: 2025-08-14  
**最終更新**: 2025-08-14  
**更新内容**: 外部フィードバック反映（Phase 1/2統合修正、ハイブリッド戦略簡素化、優先度調整）  

## 🎯 技術目標

### パフォーマンス目標
- **接続ロック待機時間**: 2.7-8.5秒 → <100ms（97%削減）
- **20テキストバッチ処理**: 100秒 → <5秒（95%削減）
- **総合性能**: 15-25倍向上（Phase 3完了時）

### 品質目標
- **翻訳精度**: 100%維持
- **システム安定性**: 99.9%可用性
- **エラー率**: <1%増加
- **Clean Architecture**: 原則遵守

## 🏗️ アーキテクチャ設計

### Phase 1: 動的接続プール実装

#### **1.1 接続プール数の決定ロジック**

**決定要因と優先順位:**
```csharp
public class ConnectionPoolCalculator
{
    public int CalculateOptimalConnections(int chunkCount, SystemResourceInfo resources)
    {
        // 1. CPU制約（最重要）
        var cpuLimit = Math.Max(1, Environment.ProcessorCount / 2);
        
        // 2. メモリ制約（重要）
        var memoryLimitGB = resources.AvailableMemoryGB;
        var memoryLimit = Math.Max(1, memoryLimitGB / 2); // 1プロセス≈2GB
        
        // 3. チャンク数要求（中重要）
        var chunkBasedNeed = Math.Max(1, chunkCount / OptimalChunksPerConnection);
        
        // 4. 設定上限（制約）
        var configuredMax = _settings.MaxConnectionsOverride ?? int.MaxValue;
        
        // 最終決定
        return Math.Min(
            Math.Min(cpuLimit, memoryLimit),
            Math.Min(chunkBasedNeed, configuredMax)
        );
    }
}
```

**計算式の根拠:**
- **CPU制約**: OPUS-MT翻訳はCPUバウンド、コア数/2で最適バランス
- **メモリ制約**: 1プロセス1-2GB、システムメモリの50%までが安全
- **チャンク分散**: 1接続あたり4チャンクが効率的（経験則）

#### **1.2 動的スケーラビリティ戦略**

**チャンク数別処理方針:**

| チャンク数 | 推奨接続数 | 処理時間見積もり | 制約要因 |
|------------|------------|------------------|----------|
| 1-5 | 1-2 | 1-2秒 | 最小構成 |
| 5-20 | 2-5 | 1-3秒 | バランス型 |
| 20-50 | 5-8 | 2-4秒 | CPU最適化 |
| 50-100 | 8-10 | 3-5秒 | CPU+メモリ制約 |
| 100+ | バッチ処理推奨 | 2-5秒 | Phase 2移行 |

**動的調整アルゴリズム（Phase 1.5で実装推奨）:**
```csharp
// ★ 優先度調整: Phase 1では固定サイズプール実装を優先
// 動的スケーリングはシステム安定後の追加機能として実装
public class AdaptivePoolManager
{
    private readonly SemaphoreSlim _poolSemaphore;
    private readonly Channel<PersistentConnection> _connectionChannel;
    private readonly ConnectionPoolMetrics _metrics;
    
    public async Task<bool> ShouldScaleUp()
    {
        return _metrics.ConnectionUtilization > 0.8 && 
               _metrics.QueuedRequests > 0 && 
               _poolSemaphore.CurrentCount < _maxConnections;
    }
    
    public async Task<bool> ShouldScaleDown()
    {
        return _metrics.ConnectionUtilization < 0.3 && 
               _poolSemaphore.CurrentCount > _minConnections;
    }
    
    public async Task ScaleUpAsync()
    {
        if (await ShouldScaleUp())
        {
            var newConnection = await CreateConnectionAsync();
            await _connectionChannel.Writer.WriteAsync(newConnection);
            _metrics.ActiveConnections++;
        }
    }
}
```

#### **1.3 設定管理とオプション**

**appsettings.json 構成:**
```json
{
  "TranslationEngine": {
    "ConnectionPool": {
      "MaxConnections": null,
      "MinConnections": 1,
      "MaxConnectionsOverride": 10,
      "OptimalChunksPerConnection": 4,
      "ScalingStrategy": "Adaptive",
      "ScaleUpThreshold": 0.8,
      "ScaleDownThreshold": 0.3,
      "ConnectionTimeout": 30000,
      "HealthCheckInterval": 30000
    },
    "Performance": {
      "EnableMetrics": true,
      "MetricsCollectionInterval": 5000,
      "PerformanceAlertThreshold": 500
    }
  }
}
```

**設定クラス:**
```csharp
public class TranslationEngineSettings
{
    public ConnectionPoolSettings ConnectionPool { get; set; } = new();
    public PerformanceSettings Performance { get; set; } = new();
}

public class ConnectionPoolSettings
{
    public int? MaxConnections { get; set; }
    public int MinConnections { get; set; } = 1;
    public int? MaxConnectionsOverride { get; set; }
    public int OptimalChunksPerConnection { get; set; } = 4;
    public string ScalingStrategy { get; set; } = "Adaptive";
    public double ScaleUpThreshold { get; set; } = 0.8;
    public double ScaleDownThreshold { get; set; } = 0.3;
    public int ConnectionTimeout { get; set; } = 30000;
    public int HealthCheckInterval { get; set; } = 30000;
}
```

### Phase 2: 真のバッチ処理実装

#### **2.1 Python側バッチエンドポイント**

**新しいバッチ処理エンドポイント:**
```python
# optimized_translation_server.py - バッチ処理拡張
class BatchTranslationRequest:
    texts: List[str]
    source_lang: str
    target_lang: str
    batch_mode: bool = True
    max_batch_size: int = 50

class BatchTranslationResponse:
    success: bool
    translations: List[str]
    confidence_scores: List[float]
    processing_time: float
    batch_size: int
    errors: Optional[List[str]] = None

async def translate_batch(self, request: BatchTranslationRequest) -> BatchTranslationResponse:
    """
    複数テキストを1回のリクエストで効率的に処理
    """
    start_time = time.time()
    
    try:
        # バッチサイズ制限
        if len(request.texts) > request.max_batch_size:
            raise ValueError(f"Batch size {len(request.texts)} exceeds limit {request.max_batch_size}")
        
        # モデル取得
        model_key = self._get_model_key(request.source_lang, request.target_lang)
        model, tokenizer = self.models[model_key]
        
        # バッチトークナイズ（効率化）
        inputs = tokenizer(
            request.texts, 
            return_tensors="pt", 
            padding=True, 
            truncation=True, 
            max_length=512
        )
        inputs = {k: v.to(self.device) for k, v in inputs.items()}
        
        # バッチ推論（GPU最適化）
        with torch.no_grad():
            if self.device.type == "cuda":
                with torch.cuda.amp.autocast():
                    outputs = model.generate(
                        **inputs, 
                        max_length=512, 
                        num_beams=1, 
                        early_stopping=True
                    )
            else:
                outputs = model.generate(
                    **inputs, 
                    max_length=512, 
                    num_beams=1, 
                    early_stopping=True
                )
        
        # バッチデコード
        translations = []
        confidence_scores = []
        
        for i, output in enumerate(outputs):
            translation = tokenizer.decode(output, skip_special_tokens=True)
            translations.append(translation)
            
            # ★ 信頼度スコア改善: モデル生成確率から実際の信頼度を計算
            # 将来実装候補：outputs.scoresまたはlogitsから生成確率を計算
            # confidence = torch.softmax(outputs.scores[i], dim=-1).max().item()
            # パフォーマンストレードオフを考慮し、現段階では固定値を使用
            confidence_scores.append(0.95)  # TODO: 実装フェーズでlogits活用検討
        
        processing_time = (time.time() - start_time) * 1000
        
        return BatchTranslationResponse(
            success=True,
            translations=translations,
            confidence_scores=confidence_scores,
            processing_time=processing_time,
            batch_size=len(request.texts)
        )
        
    except Exception as e:
        processing_time = (time.time() - start_time) * 1000
        logger.error(f"Batch translation error: {e}")
        
        return BatchTranslationResponse(
            success=False,
            translations=[],
            confidence_scores=[],
            processing_time=processing_time,
            batch_size=len(request.texts),
            errors=[str(e)]
        )
```

#### **2.2 C#側バッチ処理実装**

**新しいバッチ処理メソッド:**
```csharp
public async Task<IReadOnlyList<TranslationResponse>> TranslateBatchOptimizedAsync(
    IReadOnlyList<TranslationRequest> requests,
    CancellationToken cancellationToken = default)
{
    if (requests.Count == 0)
        return [];
        
    // バッチサイズ制限確認
    const int maxBatchSize = 50;
    if (requests.Count > maxBatchSize)
    {
        // 大きなバッチを分割処理
        return await ProcessLargeBatchAsync(requests, maxBatchSize, cancellationToken);
    }
    
    var batchStopwatch = Stopwatch.StartNew();
    PersistentConnection? connection = null;
    
    try
    {
        // ★ Phase 1統合修正: 接続プールから接続を取得（接続ロックではなく）
        connection = await _connectionPool.AcquireConnectionAsync(cancellationToken);
        
        // バッチリクエスト構築
        var batchRequest = new
        {
            texts = requests.Select(r => r.SourceText).ToList(),
            source_lang = requests[0].SourceLanguage.Code,
            target_lang = requests[0].TargetLanguage.Code,
            batch_mode = true,
            max_batch_size = maxBatchSize
        };
        
        // JSON送信（接続プールの接続を使用）
        var jsonRequest = JsonSerializer.Serialize(batchRequest);
        await connection.Writer.WriteLineAsync(jsonRequest);
        
        // レスポンス受信（接続プールの接続を使用）
        var jsonResponse = await connection.Reader.ReadLineAsync();
        var batchResponse = JsonSerializer.Deserialize<PythonBatchResponse>(jsonResponse);
        
        batchStopwatch.Stop();
        
        // レスポンスマッピング
        return MapBatchResponse(batchResponse, requests, batchStopwatch.ElapsedMilliseconds);
    }
    finally
    {
        // ★ Phase 1統合修正: 接続をプールに返却
        if (connection != null)
            await _connectionPool.ReleaseConnectionAsync(connection);
    }
}

private async Task<IReadOnlyList<TranslationResponse>> ProcessLargeBatchAsync(
    IReadOnlyList<TranslationRequest> requests,
    int maxBatchSize,
    CancellationToken cancellationToken)
{
    var results = new List<TranslationResponse>();
    
    // バッチを分割して並列処理
    var batches = requests
        .Select((request, index) => new { request, index })
        .GroupBy(x => x.index / maxBatchSize)
        .Select(g => g.Select(x => x.request).ToList())
        .ToList();
    
    // 並列バッチ処理
    var tasks = batches.Select(batch => TranslateBatchOptimizedAsync(batch, cancellationToken));
    var batchResults = await Task.WhenAll(tasks);
    
    // 結果をフラット化
    foreach (var batchResult in batchResults)
    {
        results.AddRange(batchResult);
    }
    
    return results;
}
```

### Phase 3: ハイブリッドアプローチ実装

#### **3.1 インテリジェント処理選択**

**自動最適化ロジック:**
```csharp
public class HybridTranslationStrategy
{
    private readonly IConnectionPoolManager _poolManager;
    private readonly IBatchProcessor _batchProcessor;
    private readonly TranslationEngineSettings _settings;
    
    public async Task<IReadOnlyList<TranslationResponse>> TranslateAsync(
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var strategy = DetermineOptimalStrategy(requests);
        
        // ★ ハイブリッド戦略簡素化: 3つの処理方式に統合
        return strategy switch
        {
            ProcessingStrategy.Single => 
                await ProcessSingleAsync(requests[0], cancellationToken),
                
            ProcessingStrategy.Parallel => 
                await ProcessWithConnectionPoolAsync(requests, cancellationToken),
                
            ProcessingStrategy.Batch => 
                await ProcessWithBatchModeAsync(requests, cancellationToken),
                
            _ => throw new ArgumentOutOfRangeException()
        };
    }
    
    private ProcessingStrategy DetermineOptimalStrategy(IReadOnlyList<TranslationRequest> requests)
    {
        var count = requests.Count;
        var batchThreshold = _settings.ConnectionPool.OptimalChunksPerConnection * 2;
        
        // ★ ハイブリッド戦略簡素化: 3段階の自動判定
        return count switch
        {
            1 => ProcessingStrategy.Single,                    // 1個: 通常処理
            <= batchThreshold => ProcessingStrategy.Parallel, // 少数: 接続プール並列
            _ => ProcessingStrategy.Batch                      // 多数: バッチ処理（自動分割含む）
        };
    }
}

public enum ProcessingStrategy
{
    Single,        // 1個: 通常処理
    Parallel,      // 少数: 接続プール並列処理
    Batch          // 多数: バッチ処理（大容量は自動分割＋並列）
}
```

#### **3.2 フォールバック機能**

**エラー復旧とフォールバック:**
```csharp
public class ResilientTranslationProcessor
{
    public async Task<IReadOnlyList<TranslationResponse>> ProcessWithFallbackAsync(
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken)
    {
        var strategies = new[]
        {
            ProcessingStrategy.Batch,
            ProcessingStrategy.Parallel,
            ProcessingStrategy.Single
        };
        
        Exception? lastException = null;
        
        foreach (var strategy in strategies)
        {
            try
            {
                _logger.LogInformation("翻訳処理を試行: {Strategy}", strategy);
                
                var result = await ExecuteStrategyAsync(strategy, requests, cancellationToken);
                
                _logger.LogInformation("翻訳処理成功: {Strategy}, 結果数: {Count}", 
                    strategy, result.Count);
                    
                return result;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "翻訳処理失敗: {Strategy}, 次の戦略にフォールバック", strategy);
                
                // 接続エラーの場合は再接続を試行
                if (IsConnectionError(ex))
                {
                    await _connectionManager.RecoverConnectionAsync();
                }
            }
        }
        
        // 全戦略が失敗した場合
        _logger.LogError(lastException, "すべての翻訳戦略が失敗しました");
        throw new TranslationProcessingException("翻訳処理に失敗しました", lastException);
    }
}
```

## 🔍 監視とメトリクス

### **監視指標の定義**

```csharp
public class TranslationPerformanceMetrics
{
    // 基本メトリクス
    public long TotalRequests { get; set; }
    public long SuccessfulRequests { get; set; }
    public long FailedRequests { get; set; }
    
    // パフォーマンスメトリクス
    public double AverageProcessingTimeMs { get; set; }
    public double P95ProcessingTimeMs { get; set; }
    public double P99ProcessingTimeMs { get; set; }
    
    // 接続プールメトリクス
    public int ActiveConnections { get; set; }
    public int IdleConnections { get; set; }
    public double ConnectionUtilization { get; set; }
    public int QueuedRequests { get; set; }
    
    // リソースメトリクス
    public long MemoryUsageMB { get; set; }
    public double CpuUtilization { get; set; }
    
    // 品質メトリクス
    public double AverageConfidenceScore { get; set; }
    public int CacheHitRate { get; set; }
    
    // アラート判定
    public bool IsPerformanceHealthy =>
        AverageProcessingTimeMs < 500 &&
        ConnectionUtilization < 0.9 &&
        SuccessRate > 0.99;
        
    public double SuccessRate => 
        TotalRequests > 0 ? (double)SuccessfulRequests / TotalRequests : 1.0;
}
```

### **リアルタイム監視実装**

```csharp
public class TranslationMetricsCollector : IHostedService
{
    private readonly ITranslationEngine _engine;
    private readonly ILogger<TranslationMetricsCollector> _logger;
    private readonly Timer _metricsTimer;
    
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _metricsTimer = new Timer(CollectMetrics, null, TimeSpan.Zero, TimeSpan.FromSeconds(30));
    }
    
    private async void CollectMetrics(object? state)
    {
        try
        {
            var metrics = await _engine.GetPerformanceMetricsAsync();
            
            // パフォーマンス警告
            if (!metrics.IsPerformanceHealthy)
            {
                _logger.LogWarning("パフォーマンス劣化検出: {Metrics}", JsonSerializer.Serialize(metrics));
                
                // 自動スケーリング判定
                if (metrics.ConnectionUtilization > 0.8)
                {
                    await _engine.ScaleUpConnectionsAsync();
                }
            }
            
            // メトリクス出力
            _logger.LogInformation("翻訳エンジンメトリクス: " +
                "平均処理時間={AvgTime}ms, 成功率={SuccessRate:P2}, " +
                "接続使用率={ConnectionUtil:P1}, アクティブ接続={ActiveConn}",
                metrics.AverageProcessingTimeMs,
                metrics.SuccessRate,
                metrics.ConnectionUtilization,
                metrics.ActiveConnections);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "メトリクス収集エラー");
        }
    }
}
```

## 🧪 テスト戦略

### **パフォーマンステスト**

```csharp
[TestClass]
public class TranslationEnginePerformanceTests
{
    [TestMethod]
    [TestCategory("Performance")]
    public async Task ConnectionPool_ScalabilityTest()
    {
        var testScenarios = new[]
        {
            new { ChunkCount = 5, ExpectedMaxTimeMs = 2000, Strategy = "Parallel" },
            new { ChunkCount = 20, ExpectedMaxTimeMs = 4000, Strategy = "Parallel" },
            new { ChunkCount = 50, ExpectedMaxTimeMs = 7000, Strategy = "Batch" },
            new { ChunkCount = 100, ExpectedMaxTimeMs = 5000, Strategy = "Batch" }
        };
        
        foreach (var scenario in testScenarios)
        {
            var texts = GenerateTestTexts(scenario.ChunkCount);
            var stopwatch = Stopwatch.StartNew();
            
            var results = await _translationEngine.TranslateBatchAsync(texts);
            
            stopwatch.Stop();
            
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < scenario.ExpectedMaxTimeMs,
                $"Scenario {scenario.ChunkCount} chunks exceeded {scenario.ExpectedMaxTimeMs}ms: " +
                $"actual {stopwatch.ElapsedMilliseconds}ms");
                
            Assert.AreEqual(scenario.ChunkCount, results.Count);
            Assert.IsTrue(results.All(r => r.IsSuccess));
        }
    }
    
    [TestMethod]
    [TestCategory("LoadTest")]
    public async Task ConnectionPool_ConcurrentLoadTest()
    {
        const int concurrentRequests = 10;
        const int textsPerRequest = 20;
        
        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(async i =>
            {
                var texts = GenerateTestTexts(textsPerRequest);
                var stopwatch = Stopwatch.StartNew();
                var results = await _translationEngine.TranslateBatchAsync(texts);
                stopwatch.Stop();
                
                return new { Index = i, ElapsedMs = stopwatch.ElapsedMilliseconds, Results = results };
            });
        
        var results = await Task.WhenAll(tasks);
        
        // 全リクエストが成功
        Assert.IsTrue(results.All(r => r.Results.All(tr => tr.IsSuccess)));
        
        // 平均処理時間が目標以下
        var averageTime = results.Average(r => r.ElapsedMs);
        Assert.IsTrue(averageTime < 6000, $"Average time {averageTime}ms exceeded threshold");
        
        // P95処理時間が許容範囲内
        var p95Time = results.OrderBy(r => r.ElapsedMs).Skip((int)(results.Length * 0.95)).First().ElapsedMs;
        Assert.IsTrue(p95Time < 10000, $"P95 time {p95Time}ms exceeded threshold");
    }
}
```

## 📝 実装チェックリスト

### **Phase 1: 固定サイズ接続プール（最優先）**
- [ ] TranslationEngineSettings設定クラス実装
- [ ] FixedSizeConnectionPool実装（固定サイズ優先）
- [ ] Channel<T>ベース接続管理
- [ ] appsettings.json設定定義（MaxConnections固定値）
- [ ] DI登録とライフサイクル管理
- [ ] 単体テスト作成
- [ ] パフォーマンステスト実装
- [ ] 負荷テストによる最適化

### **Phase 1.5: 動的スケーリング（Phase 1安定後）**
- [ ] AdaptiveConnectionPool実装
- [ ] 動的スケーリングロジック
- [ ] 接続プールメトリクス収集
- [ ] 自動スケーリング判定ロジック

### **Phase 2: バッチ処理（接続プール統合）**
- [ ] Python側バッチエンドポイント実装
- [ ] BatchTranslationRequest/Responseモデル
- [ ] C#側TranslateBatchOptimizedAsync実装（接続プール使用）
- [ ] 大容量バッチ分割処理（並列接続プール活用）
- [ ] Python信頼度スコア改善（logits活用検討）
- [ ] エラーハンドリングとタイムアウト
- [ ] パフォーマンス測定と最適化
- [ ] 統合テスト実装

### **Phase 3: ハイブリッド統合（3段階戦略）**
- [ ] HybridTranslationStrategy実装（Single/Parallel/Batch）
- [ ] インテリジェント処理選択ロジック（簡素化版）
- [ ] フォールバック機能実装
- [ ] TranslationMetricsCollector実装
- [ ] リアルタイム監視とアラート
- [ ] 包括的テストスイート
- [ ] パフォーマンス検証とチューニング

---
**作成者**: Claude Code Assistant  
**バージョン**: 1.1  
**関連Issue**: #144 Python翻訳エンジン最適化  
**依存技術**: .NET 8, Python 3.12, OPUS-MT, Clean Architecture  
**フィードバック統合**: Phase 1/2統合問題修正、戦略簡素化、優先度最適化