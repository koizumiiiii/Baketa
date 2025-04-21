# Issue 8-4: パイプラインパラメータ最適化機能の実装

## 概要
OCR前処理パイプラインのパラメータを自動的・半自動的に最適化する機能を実装します。これにより、様々なゲームや画面状況に対して最適なOCR前処理設定を簡単に見つけられるようになります。

## 目的・理由
OCR前処理パイプラインには多数のパラメータがあり、手動での最適化は以下の問題があります：
1. 膨大な組み合わせがあり、試行錯誤に時間がかかる
2. ユーザーに専門知識が必要となる
3. ゲームタイトルごとに最適な設定が異なる

自動・半自動の最適化機能により、これらの問題を解決し、OCR精度向上とユーザビリティ改善を実現します。

## 詳細
- パラメータ最適化アルゴリズムの設計と実装
- 最適化プロセスのUI表示とユーザー操作機能
- 最適化結果の保存と管理機能
- ゲームプロファイルとの連携機能

## タスク分解
- [ ] パラメータ最適化フレームワークの設計
  - [ ] 最適化対象パラメータの定義構造
  - [ ] 評価関数インターフェースの設計
  - [ ] 最適化アルゴリズムの選定と実装
- [ ] OCR精度評価指標の実装
  - [ ] 文字認識率推定関数
  - [ ] テキスト検出カバレッジ評価関数
  - [ ] ノイズ除去効果評価関数
- [ ] 最適化アルゴリズムの実装
  - [ ] グリッド探索法の実装
  - [ ] ヒル・クライミング法の実装
  - [ ] 遺伝的アルゴリズムの実装
- [ ] ユーザーガイド付き最適化機能
  - [ ] 段階的パラメータ調整UI
  - [ ] 視覚的フィードバック機能
  - [ ] A/Bテスト比較機能
- [ ] 最適化結果の管理機能
  - [ ] 最適化履歴の記録
  - [ ] 設定プロファイルの保存
  - [ ] 設定のエクスポート/インポート機能
- [ ] ゲームプロファイル連携機能
  - [ ] ゲーム別最適化設定の自動適用
  - [ ] 画面状況に応じた設定切替機能
- [ ] 単体テストの実装

## インターフェース設計案
```csharp
namespace Baketa.OCR.Abstractions.Optimization
{
    /// <summary>
    /// パイプラインパラメータ最適化を行うインターフェース
    /// </summary>
    public interface IPipelineOptimizer
    {
        /// <summary>
        /// 最適化プロセスを開始します
        /// </summary>
        /// <param name="pipeline">最適化対象のパイプライン</param>
        /// <param name="sampleImages">最適化に使用するサンプル画像</param>
        /// <param name="options">最適化オプション</param>
        /// <param name="progressCallback">進捗コールバック</param>
        /// <returns>最適化結果</returns>
        Task<OptimizationResult> OptimizeAsync(
            IImagePipeline pipeline,
            IReadOnlyList<IAdvancedImage> sampleImages,
            OptimizationOptions options,
            IProgress<OptimizationProgress>? progressCallback = null);
            
        /// <summary>
        /// 最適化をキャンセルします
        /// </summary>
        void CancelOptimization();
        
        /// <summary>
        /// 最適化設定をプロファイルとして保存します
        /// </summary>
        /// <param name="profileName">プロファイル名</param>
        /// <param name="result">保存する最適化結果</param>
        Task SaveProfileAsync(string profileName, OptimizationResult result);
        
        /// <summary>
        /// プロファイルから最適化設定を読み込みます
        /// </summary>
        /// <param name="profileName">プロファイル名</param>
        /// <returns>読み込まれた最適化結果</returns>
        Task<OptimizationResult> LoadProfileAsync(string profileName);
    }
    
    /// <summary>
    /// 最適化オプションを表すクラス
    /// </summary>
    public class OptimizationOptions
    {
        /// <summary>
        /// 最適化アルゴリズムの種類
        /// </summary>
        public OptimizationAlgorithm Algorithm { get; set; } = OptimizationAlgorithm.GridSearch;
        
        /// <summary>
        /// 最適化の最大反復回数
        /// </summary>
        public int MaxIterations { get; set; } = 100;
        
        /// <summary>
        /// 最適化対象のパラメータ定義リスト
        /// </summary>
        public List<ParameterOptimizationDefinition> Parameters { get; set; } = new();
        
        /// <summary>
        /// 評価関数の重み付け
        /// </summary>
        public Dictionary<string, float> EvaluationWeights { get; set; } = new();
        
        /// <summary>
        /// ユーザーガイド付き最適化を使用するかどうか
        /// </summary>
        public bool UseGuidedOptimization { get; set; } = false;
        
        /// <summary>
        /// 並列処理で使用するスレッド数（0=自動）
        /// </summary>
        public int ParallelThreads { get; set; } = 0;
    }
    
    /// <summary>
    /// パラメータ最適化定義を表すクラス
    /// </summary>
    public class ParameterOptimizationDefinition
    {
        /// <summary>
        /// パイプラインステップの名前
        /// </summary>
        public required string StepName { get; set; }
        
        /// <summary>
        /// パラメータの名前
        /// </summary>
        public required string ParameterName { get; set; }
        
        /// <summary>
        /// 最小値
        /// </summary>
        public object? MinValue { get; set; }
        
        /// <summary>
        /// 最大値
        /// </summary>
        public object? MaxValue { get; set; }
        
        /// <summary>
        /// 離散的な値のステップ幅
        /// </summary>
        public object? StepSize { get; set; }
        
        /// <summary>
        /// 離散的な選択肢リスト
        /// </summary>
        public List<object>? DiscreteValues { get; set; }
        
        /// <summary>
        /// 最適化の優先度（0～1、1が最高）
        /// </summary>
        public float Priority { get; set; } = 0.5f;
    }
    
    /// <summary>
    /// 最適化結果を表すクラス
    /// </summary>
    public class OptimizationResult
    {
        /// <summary>
        /// 最適化されたパイプライン
        /// </summary>
        public required IImagePipeline OptimizedPipeline { get; set; }
        
        /// <summary>
        /// 最適化スコア（0～1、1が最高）
        /// </summary>
        public float Score { get; set; }
        
        /// <summary>
        /// 詳細な評価結果
        /// </summary>
        public Dictionary<string, float> DetailedScores { get; set; } = new();
        
        /// <summary>
        /// 最適化プロセスの履歴
        /// </summary>
        public List<OptimizationIteration> History { get; set; } = new();
        
        /// <summary>
        /// 最適化に要した時間（ミリ秒）
        /// </summary>
        public long OptimizationTimeMs { get; set; }
    }
    
    /// <summary>
    /// 最適化アルゴリズムの種類を表す列挙型
    /// </summary>
    public enum OptimizationAlgorithm
    {
        GridSearch,
        HillClimbing,
        GeneticAlgorithm,
        BayesianOptimization,
        UserGuided
    }
    
    /// <summary>
    /// 最適化の進捗状況を表すクラス
    /// </summary>
    public class OptimizationProgress
    {
        /// <summary>
        /// 進捗率（0～1）
        /// </summary>
        public float Progress { get; set; }
        
        /// <summary>
        /// 現在の反復回数
        /// </summary>
        public int CurrentIteration { get; set; }
        
        /// <summary>
        /// 最大反復回数
        /// </summary>
        public int MaxIterations { get; set; }
        
        /// <summary>
        /// 現在の最高スコア
        /// </summary>
        public float CurrentBestScore { get; set; }
        
        /// <summary>
        /// 現在テスト中のパラメータ
        /// </summary>
        public Dictionary<string, object> CurrentParameters { get; set; } = new();
        
        /// <summary>
        /// 残り時間の推定（秒）
        /// </summary>
        public double EstimatedRemainingSeconds { get; set; }
    }
}
```

## 実装上の注意点
- 最適化プロセスは時間がかかる可能性があるため、バックグラウンド実行とキャンセル機能を実装する
- 中間結果を保存し、最適化が中断されても再開できるようにする
- ユーザー操作と自動最適化のバランスを考慮し、インタラクティブな要素を組み込む
- パラメータによって最適化アルゴリズムの効果が異なることを考慮し、適切な戦略を選択できるようにする
- メモリ使用量に注意し、大量のサンプル画像を効率的に処理できるようにする

## 関連Issue/参考
- 親Issue: #8 OpenCVベースのOCR前処理最適化
- 依存Issue: #8-2 画像前処理パイプラインの設計と実装
- 関連Issue: #13 OCR設定UIとプロファイル管理
- 参照: E:\dev\Baketa\docs\3-architecture\ocr\parameter-optimization.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (3.4 キャンセレーション対応)

## マイルストーン
マイルストーン2: キャプチャとOCR基盤

## ラベル
- `type: feature`
- `priority: medium`
- `component: ocr`
