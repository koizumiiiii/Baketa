# OCR前処理システム - A/Bテスト最適化と開発計画

*最終更新: 2025年4月24日*

## 1. A/Bテストによる最適化

### 1.1 A/Bテスト最適化の概要

A/Bテストによる最適化は、複数のパラメータセットを並行して評価し、最も効果的なものを採用するアプローチです。これにより、経験的なパラメータ調整を超えた体系的な最適化が可能になります。

主な利点：
- 複数のパラメータセットの客観的比較
- ゲーム固有の最適解の発見
- 継続的な改善と自己学習

### 1.2 A/Bテストバリエーションモデル

```csharp
namespace Baketa.Core.Models
{
    /// <summary>
    /// A/Bテストバリエーションモデル
    /// </summary>
    public class AbTestVariation
    {
        /// <summary>
        /// バリエーションID
        /// </summary>
        public Guid Id { get; set; } = Guid.NewGuid();
        
        /// <summary>
        /// バリエーション名
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// パラメータセット
        /// </summary>
        public ImageProcessingParameters Parameters { get; set; }
        
        /// <summary>
        /// 累積スコア
        /// </summary>
        public float CumulativeScore { get; set; } = 0f;
        
        /// <summary>
        /// 評価回数
        /// </summary>
        public int EvaluationCount { get; set; } = 0;
        
        /// <summary>
        /// 最終評価時刻
        /// </summary>
        public DateTime LastEvaluated { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// 平均スコアを計算
        /// </summary>
        public float AverageScore => EvaluationCount > 0 ? CumulativeScore / EvaluationCount : 0f;
    }
}
```

### 1.3 A/Bテスト最適化の実装

```csharp
namespace Baketa.Application.Services.Ocr
{
    /// <summary>
    /// A/Bテストによる最適化
    /// </summary>
    public class AbTestOptimizer : IOcrParameterOptimizer
    {
        private readonly ILogger<AbTestOptimizer> _logger;
        private readonly IGameProfileManager _profileManager;
        private readonly Random _random = new Random();
        
        // A/Bテストのバリエーション保存用ディクショナリ
        private readonly Dictionary<string, List<AbTestVariation>> _testVariations = 
            new Dictionary<string, List<AbTestVariation>>();
        
        // 最大バリエーション数
        private const int MaxVariations = 5;
        
        // 新バリエーション生成確率
        private const float NewVariationProbability = 0.2f;
        
        public AbTestOptimizer(
            ILogger<AbTestOptimizer> logger,
            IGameProfileManager profileManager)
        {
            _logger = logger;
            _profileManager = profileManager;
        }
        
        public async Task<ImageProcessingParameters> OptimizeParametersAsync(
            GameProfile gameProfile,
            List<OcrResult> ocrResults,
            ImageProcessingParameters currentParameters,
            OcrOptimizationParameters optimizationParameters)
        {
            if (gameProfile == null)
                throw new ArgumentNullException(nameof(gameProfile));
                
            if (currentParameters == null)
                throw new ArgumentNullException(nameof(currentParameters));
                
            if (ocrResults == null || ocrResults.Count == 0)
                return currentParameters;
                
            try
            {
                string gameId = gameProfile.GameId;
                
                // ゲーム用のバリエーションを初期化（必要に応じて）
                if (!_testVariations.ContainsKey(gameId))
                {
                    _testVariations[gameId] = new List<AbTestVariation>
                    {
                        new AbTestVariation
                        {
                            Name = "Base",
                            Parameters = currentParameters.Clone()
                        }
                    };
                }
                
                // 今回のOCR結果からスコアを計算
                float score = CalculateScore(ocrResults);
                
                // 現在のパラメータを使用しているバリエーションを特定
                var currentVariation = FindVariationWithParameters(gameId, currentParameters);
                
                // 現在のバリエーションを評価
                if (currentVariation != null)
                {
                    currentVariation.CumulativeScore += score;
                    currentVariation.EvaluationCount++;
                    currentVariation.LastEvaluated = DateTime.UtcNow;
                    
                    _logger.LogDebug(
                        "バリエーション評価: {Name}, スコア={Score}, 平均={Average}",
                        currentVariation.Name, score, currentVariation.AverageScore);
                }
                
                // 次のバリエーションを選択
                AbTestVariation nextVariation;
                
                // 一定確率で新しいバリエーションを生成
                if (_testVariations[gameId].Count < MaxVariations && 
                    _random.NextDouble() < NewVariationProbability)
                {
                    nextVariation = GenerateNewVariation(gameId, currentParameters, optimizationParameters);
                    _testVariations[gameId].Add(nextVariation);
                    
                    _logger.LogInformation(
                        "新しいバリエーションを生成: {Name}, ゲームID={GameId}",
                        nextVariation.Name, gameId);
                }
                else
                {
                    // 既存のバリエーションから選択
                    nextVariation = SelectNextVariation(gameId);
                }
                
                // 選択したバリエーションのパラメータを返す
                return nextVariation.Parameters;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A/Bテスト最適化中にエラーが発生しました");
                return currentParameters;
            }
        }
        
        private float CalculateScore(List<OcrResult> ocrResults)
        {
            // OCR結果からスコアを計算
            float confidenceScore = ocrResults.SelectMany(r => r.TextRegions)
                .Average(t => t.Confidence);
                
            float coverageScore = ocrResults.Average(r => r.TextRegions.Count) / 10.0f;
            
            // 信頼度と検出範囲のバランスを考慮したスコア
            return confidenceScore * 0.7f + Math.Min(coverageScore, 1.0f) * 0.3f;
        }
        
        private AbTestVariation FindVariationWithParameters(string gameId, ImageProcessingParameters parameters)
        {
            return _testVariations[gameId].FirstOrDefault(v =>
                AreParametersEqual(v.Parameters, parameters));
        }
        
        private bool AreParametersEqual(ImageProcessingParameters p1, ImageProcessingParameters p2)
        {
            // パラメータの等価性をチェック（簡易実装）
            return Math.Abs(p1.Contrast - p2.Contrast) < 0.01f &&
                Math.Abs(p1.Brightness - p2.Brightness) < 0.01f &&
                Math.Abs(p1.NoiseReduction - p2.NoiseReduction) < 0.01f &&
                p1.UseAdaptiveThreshold == p2.UseAdaptiveThreshold &&
                p1.ApplyMorphology == p2.ApplyMorphology;
        }
        
        private AbTestVariation GenerateNewVariation(
            string gameId, 
            ImageProcessingParameters baseParameters,
            OcrOptimizationParameters optimizationParameters)
        {
            var newParameters = baseParameters.Clone();
            
            // ランダムな変異を加える
            if (optimizationParameters.TargetParameters.Contains("Contrast"))
            {
                newParameters.Contrast += (float)(_random.NextDouble() * 0.4 - 0.2);
            }
            
            if (optimizationParameters.TargetParameters.Contains("Brightness"))
            {
                newParameters.Brightness += (float)(_random.NextDouble() * 20 - 10);
            }
            
            if (optimizationParameters.TargetParameters.Contains("NoiseReduction"))
            {
                newParameters.NoiseReduction += (float)(_random.NextDouble() * 0.4 - 0.2);
            }
            
            if (optimizationParameters.TargetParameters.Contains("UseAdaptiveThreshold"))
            {
                newParameters.UseAdaptiveThreshold = _random.NextDouble() > 0.5;
            }
            
            // パラメータ値を有効範囲内に修正
            ValidateParameters(newParameters);
            
            return new AbTestVariation
            {
                Name = $"Variation{_testVariations[gameId].Count + 1}",
                Parameters = newParameters
            };
        }
        
        private AbTestVariation SelectNextVariation(string gameId)
        {
            var variations = _testVariations[gameId];
            
            // Epsilon-greedy戦略（80%は最良のバリエーション、20%はランダム）
            if (_random.NextDouble() < 0.8)
            {
                // 評価回数が最小閾値に達したバリエーションの中から最良のものを選択
                var evaluatedVariations = variations
                    .Where(v => v.EvaluationCount >= 3)
                    .ToList();
                    
                if (evaluatedVariations.Count > 0)
                {
                    return evaluatedVariations
                        .OrderByDescending(v => v.AverageScore)
                        .First();
                }
            }
            
            // ランダム選択または十分に評価されたバリエーションがない場合
            return variations[_random.Next(variations.Count)];
        }
        
        private void ValidateParameters(ImageProcessingParameters parameters)
        {
            parameters.Contrast = Math.Clamp(parameters.Contrast, 0.5f, 2.0f);
            parameters.Brightness = Math.Clamp(parameters.Brightness, -50f, 50f);
            parameters.NoiseReduction = Math.Clamp(parameters.NoiseReduction, 0f, 1.0f);
            parameters.MorphologyKernelSize = Math.Clamp(parameters.MorphologyKernelSize, 1, 7);
        }
        
        public async Task CleanupOldVariationsAsync()
        {
            // 古いバリエーションのクリーンアップ処理
            foreach (var gameId in _testVariations.Keys.ToList())
            {
                var variations = _testVariations[gameId];
                
                // 1週間以上評価されていないバリエーションを削除
                var outdatedVariations = variations
                    .Where(v => (DateTime.UtcNow - v.LastEvaluated).TotalDays > 7 &&
                          v.Name != "Base")
                    .ToList();
                    
                foreach (var outdated in outdatedVariations)
                {
                    variations.Remove(outdated);
                    _logger.LogInformation(
                        "古いバリエーションを削除: {Name}, ゲームID={GameId}",
                        outdated.Name, gameId);
                }
                
                // パフォーマンスの悪いバリエーションを削除
                if (variations.Count > 3)
                {
                    var bestScore = variations.Max(v => v.AverageScore);
                    var poorVariations = variations
                        .Where(v => v.EvaluationCount >= 5 &&
                                 v.AverageScore < bestScore * 0.7 &&
                                 v.Name != "Base")
                        .OrderBy(v => v.AverageScore)
                        .Take(Math.Max(0, variations.Count - 3))
                        .ToList();
                        
                    foreach (var poor in poorVariations)
                    {
                        variations.Remove(poor);
                        _logger.LogInformation(
                            "低パフォーマンスのバリエーションを削除: {Name}, スコア={Score}, ゲームID={GameId}",
                            poor.Name, poor.AverageScore, gameId);
                    }
                }
            }
            
            await Task.CompletedTask;
        }
    }
}
```

### 1.4 バリエーション永続化

```csharp
namespace Baketa.Infrastructure.Services
{
    /// <summary>
    /// A/Bテストバリエーション永続化サービス
    /// </summary>
    public class AbTestPersistenceService : IAbTestPersistenceService
    {
        private readonly IFileSystem _fileSystem;
        private readonly IJsonSerializer _serializer;
        private readonly ILogger<AbTestPersistenceService> _logger;
        
        public AbTestPersistenceService(
            IFileSystem fileSystem,
            IJsonSerializer serializer,
            ILogger<AbTestPersistenceService> logger)
        {
            _fileSystem = fileSystem;
            _serializer = serializer;
            _logger = logger;
        }
        
        public async Task SaveVariationsAsync(string gameId, List<AbTestVariation> variations)
        {
            try
            {
                string directoryPath = Path.Combine(
                    _fileSystem.GetAppDataDirectory(), 
                    "AbTest");
                    
                if (!_fileSystem.DirectoryExists(directoryPath))
                {
                    _fileSystem.CreateDirectory(directoryPath);
                }
                
                string filePath = Path.Combine(directoryPath, $"{gameId}.json");
                string json = _serializer.Serialize(variations);
                
                await _fileSystem.WriteAllTextAsync(filePath, json);
                
                _logger.LogDebug(
                    "A/Bテストバリエーションを保存しました: ゲームID={GameId}, バリエーション数={Count}",
                    gameId, variations.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A/Bテストバリエーションの保存中にエラーが発生しました: ゲームID={GameId}", gameId);
                throw;
            }
        }
        
        public async Task<List<AbTestVariation>> LoadVariationsAsync(string gameId)
        {
            try
            {
                string filePath = Path.Combine(
                    _fileSystem.GetAppDataDirectory(), 
                    "AbTest",
                    $"{gameId}.json");
                    
                if (!_fileSystem.FileExists(filePath))
                {
                    _logger.LogDebug("A/Bテストバリエーションが見つかりません: ゲームID={GameId}", gameId);
                    return new List<AbTestVariation>();
                }
                
                string json = await _fileSystem.ReadAllTextAsync(filePath);
                var variations = _serializer.Deserialize<List<AbTestVariation>>(json);
                
                _logger.LogDebug(
                    "A/Bテストバリエーションを読み込みました: ゲームID={GameId}, バリエーション数={Count}",
                    gameId, variations.Count);
                    
                return variations;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "A/Bテストバリエーションの読み込み中にエラーが発生しました: ゲームID={GameId}", gameId);
                return new List<AbTestVariation>();
            }
        }
    }
}
```

## 2. 開発計画と実装ロードマップ

### 2.1 開発フェーズ

OCR前処理システムの開発は、以下のフェーズで進行します：

#### 2.1.1 フェーズ1: 基盤構築 (2週間)

- **目標**: 基本的なOCR前処理のフレームワークとインフラストラクチャを構築
- **主要タスク**:
  - OpenCVラッパークラスの設計と実装
  - 画像処理抽象化層のインターフェース設計
  - 基本的な前処理フィルターの実装
  - パイプラインの基本フレームワーク構築
- **成果物**:
  - OpenCVラッパーの実装
  - 基本的な前処理フィルター一式
  - 単体テストの整備

#### 2.1.2 フェーズ2: テキスト検出システム (3週間)

- **目標**: 高精度なテキスト領域検出アルゴリズムを実装
- **主要タスク**:
  - MSERベースのテキスト検出実装
  - エッジベースのテキスト検出実装
  - 複合検出アルゴリズムの実装
  - 検出結果の最適化処理
- **成果物**:
  - テキスト検出アルゴリズム群
  - 検出パラメータ調整機能
  - テスト用ベンチマークデータセット

#### 2.1.3 フェーズ3: 差分検出システム (2週間)

- **目標**: 効率的な画面変化検出システムの実装
- **主要タスク**:
  - ヒストグラム比較による差分検出
  - サンプリングベースの高速差分検出
  - テキスト領域重点サンプリングの実装
  - パフォーマンス最適化
- **成果物**:
  - 差分検出コンポーネント
  - パフォーマンス測定機能
  - 検出精度・速度のベンチマーク

#### 2.1.4 フェーズ4: 最適化システム (3週間)

- **目標**: OCR結果に基づいた自己最適化システムの実装
- **主要タスク**:
  - フィードバックベースの最適化ロジック実装
  - A/Bテスト最適化フレームワーク構築
  - ゲームプロファイル連携機能
  - 永続化システム
- **成果物**:
  - パラメータ最適化エンジン
  - A/Bテスト管理システム
  - プロファイル管理コンポーネント

#### 2.1.5 フェーズ5: 統合とUI (2週間)

- **目標**: 構築したシステムの統合とユーザーインターフェース実装
- **主要タスク**:
  - アプリケーション層との統合
  - 設定UIの実装
  - デバッグ・診断機能の実装
  - パフォーマンス最終調整
- **成果物**:
  - 統合されたOCR前処理システム
  - ユーザー設定インターフェース
  - システム診断ツール

### 2.2 開発優先度マトリックス

| 機能 | 優先度 | 工数見積 | 技術的難易度 | 影響度 |
|------|--------|----------|--------------|--------|
| OpenCVラッパー | 高 | 中 | 中 | 高 |
| 基本フィルター | 高 | 小 | 低 | 高 |
| パイプライン管理 | 高 | 中 | 中 | 高 |
| MSERテキスト検出 | 高 | 大 | 高 | 高 |
| エッジベース検出 | 中 | 中 | 高 | 中 |
| ヒストグラム差分検出 | 高 | 小 | 低 | 高 |
| サンプリング差分検出 | 中 | 中 | 中 | 高 |
| フィードバック最適化 | 中 | 中 | 中 | 高 |
| A/Bテスト最適化 | 低 | 大 | 高 | 中 |
| ゲームプロファイル連携 | 高 | 中 | 低 | 高 |
| 設定UI | 中 | 小 | 低 | 中 |

### 2.3 依存関係と実装順序

```
+-------------------+
| OpenCVラッパー     |
+--------+----------+
         |
         v
+--------+----------+     +-------------------+
| 基本フィルター群    +---->+ パイプライン管理    |
+--------+----------+     +--------+----------+
                                   |
                                   v
+-----------------+      +---------+---------+     +------------------+
| MSERテキスト検出 +----->+ テキスト検出管理    +---->+ エッジベース検出   |
+-----------------+      +---------+---------+     +------------------+
                                   |
                                   v
+------------------+     +---------+---------+     +------------------+
| ヒストグラム差分   +---->+ 差分検出システム    +---->+ サンプリング差分   |
+------------------+     +---------+---------+     +------------------+
                                   |
                                   v
+---------------------+   +---------+---------+    +------------------+
| フィードバック最適化  +-->+ 最適化システム      +--->+ A/Bテスト最適化   |
+---------------------+   +---------+---------+    +------------------+
                                   |
                                   v
+----------------------+  +---------+---------+
| ゲームプロファイル連携 +->+ システム統合        |
+----------------------+  +---------+---------+
                                   |
                                   v
                           +---------+---------+
                           | UI実装             |
                           +-------------------+
```

### 2.4 実装上の注意点

1. **メモリ管理**: 大きな画像データを扱うため、効率的なメモリ管理が不可欠
   - `using`ステートメントの徹底
   - 不要なメモリコピーの削減
   - 大きなオブジェクトの適切な解放

2. **並列処理**: 複数のCPUコアを活用するための並列処理実装
   - 画像処理の並列化
   - スレッドセーフな設計
   - 並列処理のオーバーヘッド考慮

3. **テスト戦略**: 効果的なテスト環境の構築
   - モックによるOpenCV依存の分離
   - 画像処理結果の自動検証
   - パフォーマンスベンチマークの自動化

4. **拡張性**: 将来の拡張を考慮した設計
   - 新しいアルゴリズムの追加容易性
   - カスタムフィルターの追加機構
   - 設定パラメータの拡張性

## 3. パフォーマンス目標と評価指標

### 3.1 パフォーマンス目標

- **CPU使用率**: アイドル時10%未満、処理時30%未満
- **メモリ使用量**: 最大100MB
- **処理時間**: 1フレームあたり50ms以内（20fps相当）
- **テキスト検出精度**: 75%以上（様々なゲームシナリオで）
- **差分検出効率**: 95%の精度で50%の処理スキップ

### 3.2 評価指標

- **OCR精度**: 正しく認識されたテキストの割合
- **検出カバレッジ**: 画面内のテキスト領域の検出率
- **処理時間**: 各処理ステップの実行時間
- **リソース使用量**: CPU、メモリ、GPUなどの使用状況
- **バッテリー影響**: ノートPC使用時のバッテリー消費率

### 3.3 ベンチマーク方法

1. **標準テストセット**: 様々なゲームシーンの静止画像コレクション
2. **リアルタイムキャプチャ**: 実際のゲームプレイ中のキャプチャ
3. **自動化テスト**: 定期的な回帰テストと性能測定
4. **比較分析**: 異なるアルゴリズムとパラメータセットの比較

## 4. 今後の拡張ポイント

### 4.1 アルゴリズム強化

- **機械学習ベースのテキスト検出**: 現在のルールベースから機械学習モデルへの拡張
- **言語固有の最適化**: 言語特性に基づく前処理パラメータの最適化
- **フォント認識**: ゲーム固有のフォントスタイル認識と対応

### 4.2 システム拡張

- **GPU加速**: OpenCL/CUDAによる処理の高速化
- **リアルタイム適応**: ゲームシーン変化に応じた動的パラメータ調整
- **分散処理**: 複雑な処理の分散化とバックグラウンド処理

### 4.3 ユーザー体験向上

- **視覚的デバッグ**: 処理パイプラインの視覚化
- **パラメータ推奨**: ユーザー特性に基づくパラメータ推奨
- **自動プロファイル共有**: コミュニティベースのプロファイル共有と評価