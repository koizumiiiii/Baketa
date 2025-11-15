# NLLB-200翻訳システム同時接続制御最適化ロードマップ

## 🎯 概要

Geminiレビュー結果を反映し、NLLB-200翻訳システムの同時接続制御を段階的に最適化する詳細ロードマップ。

### 現在の実装状況
- **Python側**: ThreadPoolExecutor 8ワーカー（4から拡張済み）
- **C#側**: 最小8接続保証、プロセッサ数ベース動的スケーリング
- **アーキテクチャ**: OptimizedPythonTranslationEngine + FixedSizeConnectionPool

## 📋 3段階最適化戦略

### Phase 1: GPU使用時のバッチ処理最適化 🚀

#### 🔍 問題分析
**Gemini指摘**: "GPUはスループット指向。バッチサイズ最適化が鍵"

**現在の課題**:
- 個別リクエスト処理による GPU 使用効率の低下
- VRAM 使用量の非最適化
- バッチ処理機会の逸失

#### 🎯 目標
- **GPU推論スループット向上**: 30-50% のスループット改善
- **VRAM使用効率化**: メモリ使用量の最適化
- **レイテンシ制御**: バッチ処理とレスポンス時間のバランス

#### 📊 技術設計

##### 1.1 動的バッチ集約システム
```python
class DynamicBatchAggregator:
    def __init__(self, max_batch_size=32, max_wait_time_ms=30):  # 🔧 Gemini推奨: 100ms→30msに短縮
        self.max_batch_size = max_batch_size
        self.max_wait_time_ms = max_wait_time_ms
        self.pending_requests = asyncio.Queue()
        self.batch_processor = BatchProcessor()
    
    async def aggregate_requests(self):
        """GPU最適化バッチ集約"""
        batch = []
        start_time = time.time()
        
        while len(batch) < self.max_batch_size:
            try:
                timeout = self.max_wait_time_ms / 1000.0
                request = await asyncio.wait_for(
                    self.pending_requests.get(), 
                    timeout=timeout
                )
                batch.append(request)
            except asyncio.TimeoutError:
                break
        
        if batch:
            return await self.batch_processor.process_batch(batch)
```

##### 1.2 GPU リソース監視
```python
class GpuResourceMonitor:
    def __init__(self):
        self.vram_threshold = 0.85  # 85%使用率で制限
        
    async def get_optimal_batch_size(self) -> int:
        """VRAM使用量ベースの動的バッチサイズ計算"""
        if torch.cuda.is_available():
            vram_used = torch.cuda.memory_allocated() / torch.cuda.max_memory_allocated()
            
            if vram_used < 0.5:
                return 32  # 大バッチ
            elif vram_used < 0.7:
                return 16  # 中バッチ
            else:
                return 8   # 小バッチ
        return 8  # CPU fallback
```

##### 1.3 実装箇所
- **ファイル**: `scripts/nllb_translation_server.py`
- **クラス**: `NllbTranslationServer`
- **メソッド**: 
  - `handle_client()` - バッチ集約ロジック追加
  - `translate_batch()` - GPU最適化バッチ処理

#### 📈 期待効果
- **GPU使用効率**: 30-50%向上
- **メモリ使用量**: 最適化による安定性向上
- **スループット**: 高負荷時の処理能力向上

---

### Phase 2: C#側サーキットブレーカー実装 🛡️

#### 🔍 問題分析
**Gemini指摘**: "応答時間監視＋サーキットブレーカーで連鎖障害防止"

**現在の課題**:
- サーバー過負荷時の連鎖障害
- テールレイテンシ（極端に遅いリクエスト）への対処不足
- 障害時の自動回復機能なし

#### 🎯 目標
- **連鎖障害防止**: サーバー過負荷時の自動制御
- **テールレイテンシ制御**: 95パーセンタイル値監視
- **段階的回復**: Half-Open状態での徐々の回復

#### 📊 技術設計

##### 2.1 サーキットブレーカーコア（Pollyライブラリ活用推奨）
```csharp
// 🔧 Gemini推奨: Pollyライブラリの使用を強く推奨
// using Polly;
// using Polly.CircuitBreaker;

public class TranslationCircuitBreaker
{
    private readonly ILogger<TranslationCircuitBreaker> _logger;
    private readonly CircuitBreakerOptions _options;
    private readonly SlidingWindow<ResponseMetrics> _responseWindow;
    private volatile CircuitState _state = CircuitState.Closed;
    private DateTime _lastOpenTime;
    
    public class CircuitBreakerOptions
    {
        public int FailureThreshold { get; set; } = 5;
        public TimeSpan OpenTimeoutDuration { get; set; } = TimeSpan.FromSeconds(30);
        public double ErrorRateThreshold { get; set; } = 0.5; // 50%
        public TimeSpan ResponseTimeThreshold { get; set; } = TimeSpan.FromMilliseconds(2000);
        public int WindowSize { get; set; } = 100; // 直近100リクエスト
    }
    
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken = default)
    {
        if (_state == CircuitState.Open && !ShouldAttemptReset())
        {
            throw new CircuitBreakerOpenException("Circuit breaker is open");
        }
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var result = await operation();
            stopwatch.Stop();
            
            RecordSuccess(stopwatch.Elapsed);
            TransitionToClosedIfNeeded();
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            RecordFailure(stopwatch.Elapsed, ex);
            EvaluateCircuitState();
            throw;
        }
    }
}
```

##### 2.2 メトリクス収集とテールレイテンシ監視
```csharp
public class ResponseMetricsCollector
{
    private readonly List<TimeSpan> _responseTimes = new();
    private readonly object _lock = new();
    
    public void RecordResponse(TimeSpan responseTime, bool isSuccess)
    {
        lock (_lock)
        {
            _responseTimes.Add(responseTime);
            
            // 直近1000件を保持
            if (_responseTimes.Count > 1000)
            {
                _responseTimes.RemoveAt(0);
            }
        }
    }
    
    public ResponseMetrics GetCurrentMetrics()
    {
        lock (_lock)
        {
            if (_responseTimes.Count == 0) return new ResponseMetrics();
            
            var sorted = _responseTimes.OrderBy(t => t.TotalMilliseconds).ToList();
            
            return new ResponseMetrics
            {
                P50 = sorted[sorted.Count / 2],
                P95 = sorted[(int)(sorted.Count * 0.95)],
                P99 = sorted[(int)(sorted.Count * 0.99)],
                Average = TimeSpan.FromMilliseconds(sorted.Average(t => t.TotalMilliseconds))
            };
        }
    }
}
```

##### 2.3 実装箇所
- **新規ファイル**: `Baketa.Infrastructure/Translation/Resilience/TranslationCircuitBreaker.cs`
- **修正ファイル**: `OptimizedPythonTranslationEngine.cs`
- **統合箇所**: `TranslateAsync()`, `TranslateBatchAsync()` メソッド

##### 2.4 フォールバック戦略
```csharp
// 🔧 Gemini指摘: サーキットブレーカー作動時の必須対策
public class TranslationFallbackService
{
    public async Task<string> HandleCircuitBreakerOpen(string text)
    {
        // 1. ユーザー通知: "サーバー高負荷中"
        // 2. ローカル翻訳エンジンへの切り替え（将来実装）
        // 3. キャッシュされた翻訳結果の活用
        return "⚠️ 翻訳サーバー高負荷中 - しばらくお待ちください";
    }
}
```

#### 📈 期待効果
- **連鎖障害防止**: サーバー負荷時の自動制御
- **可用性向上**: 段階的回復による安定性
- **監視強化**: リアルタイムメトリクス可視化
- **🆕 ユーザー体験**: フォールバック機能による継続性確保

---

### Phase 3: 動的リソース監視機能追加 📊

#### 🔍 問題分析
**Gemini指摘**: "CPU使用率監視＋ヒステリシス制御でハンチング防止"

**現在の課題**:
- 固定的な接続数制御（CPU コア数ベース）
- リアルタイムリソース状況の未反映
- 頻繁な設定変更によるパフォーマンス影響

#### 🎯 目標
- **動的リソース監視**: CPU/GPU使用率ベース制御
- **ハンチング防止**: ヒステリシス制御実装
- **予測的制御**: 負荷傾向予測による事前調整

#### 📊 技術設計

##### 3.1 リソース監視システム
```csharp
public class SystemResourceMonitor : IHostedService
{
    private readonly ILogger<SystemResourceMonitor> _logger;
    private readonly Timer _monitoringTimer;
    private readonly ConcurrentQueue<ResourceSnapshot> _resourceHistory;
    
    public class ResourceSnapshot
    {
        public DateTime Timestamp { get; set; }
        public double CpuUsage { get; set; }
        public double GpuUsage { get; set; }
        public long MemoryUsed { get; set; }
        public long GpuMemoryUsed { get; set; }
    }
    
    private async Task MonitorResourcesAsync()
    {
        var cpuUsage = await GetCpuUsageAsync();
        var gpuUsage = await GetGpuUsageAsync();
        var memoryUsage = GC.GetTotalMemory(false);
        
        var snapshot = new ResourceSnapshot
        {
            Timestamp = DateTime.UtcNow,
            CpuUsage = cpuUsage,
            GpuUsage = gpuUsage,
            MemoryUsed = memoryUsage
        };
        
        _resourceHistory.Enqueue(snapshot);
        
        // 直近60秒のデータを保持
        while (_resourceHistory.TryPeek(out var oldest) && 
               (DateTime.UtcNow - oldest.Timestamp).TotalSeconds > 60)
        {
            _resourceHistory.TryDequeue(out _);
        }
    }
}
```

##### 3.2 ヒステリシス制御による動的調整
```csharp
public class DynamicConnectionController
{
    private readonly SystemResourceMonitor _resourceMonitor;
    private readonly HysteresisController _hysteresisController;
    
    public class HysteresisController
    {
        private int _currentConnections;
        private DateTime _lastChange = DateTime.MinValue;
        private readonly TimeSpan _minChangeInterval = TimeSpan.FromSeconds(10);
        
        public int CalculateOptimalConnections(ResourceSnapshot currentState)
        {
            // 変更間隔制限
            if (DateTime.UtcNow - _lastChange < _minChangeInterval)
            {
                return _currentConnections;
            }
            
            var availableCpuCapacity = Math.Max(0, 100 - currentState.CpuUsage) / 100.0;
            var baseConnections = Environment.ProcessorCount;
            
            var targetConnections = (int)Math.Max(4, baseConnections * availableCpuCapacity);
            
            // ヒステリシス適用（±2接続の遊びを設ける）
            var hysteresisZone = 2;
            if (Math.Abs(targetConnections - _currentConnections) > hysteresisZone)
            {
                _currentConnections = targetConnections;
                _lastChange = DateTime.UtcNow;
                return _currentConnections;
            }
            
            return _currentConnections; // 変更なし
        }
    }
}
```

##### 3.3 実装箇所
- **新規ファイル**: `Baketa.Infrastructure/Monitoring/SystemResourceMonitor.cs`
- **新規ファイル**: `Baketa.Infrastructure/Translation/Adaptive/DynamicConnectionController.cs`
- **修正ファイル**: `FixedSizeConnectionPool.cs` - 動的調整機能追加

#### 📈 期待効果
- **リソース効率化**: 40-60% のリソース使用効率向上
- **安定性向上**: ハンチング防止による安定動作
- **予測制御**: 負荷傾向に基づく事前調整

---

## 🚀 実装スケジュール

### Phase 1 (1-2週間)
1. **GPU バッチ集約システム実装** (3日)
2. **VRAM監視機能追加** (2日)
3. **統合テスト・調整** (2-3日)

### Phase 2 (1-2週間)  
1. **サーキットブレーカー実装** (4日)
2. **メトリクス収集システム** (2日)
3. **統合・テスト** (2-3日)

### Phase 3 (2週間)
1. **リソース監視システム実装** (5日)
2. **ヒステリシス制御実装** (3日)
3. **統合テスト・パフォーマンス調整** (4日)

## 📊 成功指標

### 定量的指標
- **GPU使用効率**: ベースライン比 30-50%向上
- **平均応答時間**: 現在の2秒 → 1秒以下
- **P95レイテンシ**: 5秒 → 2秒以下  
- **システム可用性**: 99.9%以上
- **リソース使用効率**: 40-60%向上

### 定性的指標
- 高負荷時の安定性確保
- 障害時の自動回復能力
- 運用監視の容易性

## ⚠️ リスクと対策

### 実装リスク
1. **複雑性増加**: 段階的実装による影響最小化
2. **パフォーマンス劣化**: 十分なテストとベンチマーク
3. **既存機能への影響**: 後方互換性の維持

### 対策
- 各Phaseでの詳細なコードレビュー
- パフォーマンステストの自動化
- フィーチャーフラグによる段階的ロールアウト