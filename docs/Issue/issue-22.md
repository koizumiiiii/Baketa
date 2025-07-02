# Issue 22: OCR-翻訳処理の最適化とフロー制御

## 概要
Baketaにおける OCR処理と翻訳処理のフローを最適化し、システムリソースの効率的な使用とユーザー体験の向上を実現します。画面変化の効率的な検出に基づくOCR実行トリガーの段階的実装と、テキスト変化の検出による不要な翻訳実行の回避を行います。

## 目的・理由
1. **システムリソースの効率的利用**: 必要な時のみOCR/翻訳処理を実行し、CPUとメモリ使用量を最適化する
2. **ゲームパフォーマンスへの影響最小化**: 翻訳オーバーレイとしての主目的を損なわないよう、ゲーム実行への影響を最小限に抑える
3. **API呼び出しの削減**: 不要な翻訳API呼び出しを避けることでコストを削減し、API制限への対応を強化する
4. **応答性の向上**: 処理の最適化により、テキスト表示から翻訳表示までの遅延を短縮する

## 詳細
- OCR実行トリガーアプローチの段階的実装（定期OCR → 差分検出）
- テキスト内容の変化検出によるトリガー制御
- 処理フローの最適化とパフォーマンスモニタリング

## タスク分解
- [ ] 定期OCRアプローチの実装（フェーズ1）
  - [ ] OCR実行インターバル設定の実装（デフォルト500ms）
  - [ ] テキスト内容比較ロジックの実装
  - [ ] 同一テキスト継続検出機能の実装
  - [ ] OCR結果キャッシュの実装
  - [ ] パフォーマンス測定と最適化
- [ ] 差分検出アプローチの実装（フェーズ2）
  - [ ] 画面変更検出アルゴリズムの実装（サンプリングベース）
  - [ ] 画面領域追跡と変化検出の実装
  - [ ] 閾値設定と調整機能の実装
  - [ ] 検出精度と処理負荷のバランス調整
  - [ ] パフォーマンス比較と評価
- [ ] 処理フロー制御
  - [ ] OCR→翻訳パイプラインの構築
  - [ ] テキスト変化検出に基づく翻訳実行条件の実装
  - [ ] 処理優先度設定と調整機能
  - [ ] 並列処理の最適化
- [ ] イベント通知システム
  - [ ] 画面変更検出イベントの実装
  - [ ] テキスト変更検出イベントの実装
  - [ ] 処理開始/完了イベントの実装
  - [ ] パフォーマンス監視イベントの実装
- [ ] 設定とUI
  - [ ] OCR実行モード設定UI（定期/差分検出）
  - [ ] 実行インターバル設定UI
  - [ ] 変化検出感度設定UI
  - [ ] パフォーマンスモニタリングUI
- [ ] テストと評価
  - [ ] シミュレーション環境でのパフォーマンステスト
  - [ ] 実際のゲーム環境でのテスト
  - [ ] CPU/メモリ使用率の測定
  - [ ] 応答時間の測定と評価

## クラスとインターフェース設計案

```csharp
namespace Baketa.Core.Processing
{
    /// <summary>
    /// OCR-翻訳処理コントローラインターフェース
    /// </summary>
    public interface IOcrTranslationController
    {
        /// <summary>
        /// 処理モード
        /// </summary>
        ProcessingMode Mode { get; }
        
        /// <summary>
        /// 処理を開始します
        /// </summary>
        /// <returns>開始が成功したかどうか</returns>
        Task<bool> StartAsync();
        
        /// <summary>
        /// 処理を停止します
        /// </summary>
        /// <returns>停止が成功したかどうか</returns>
        Task<bool> StopAsync();
        
        /// <summary>
        /// 処理モードを設定します
        /// </summary>
        /// <param name="mode">処理モード</param>
        /// <returns>設定が成功したかどうか</returns>
        Task<bool> SetModeAsync(ProcessingMode mode);
        
        /// <summary>
        /// 処理設定を更新します
        /// </summary>
        /// <param name="settings">処理設定</param>
        /// <returns>更新が成功したかどうか</returns>
        Task<bool> UpdateSettingsAsync(ProcessingSettings settings);
        
        /// <summary>
        /// 処理統計を取得します
        /// </summary>
        /// <returns>処理統計</returns>
        Task<ProcessingStatistics> GetStatisticsAsync();
        
        /// <summary>
        /// 処理状態変更イベント
        /// </summary>
        event EventHandler<ProcessingStateChangedEventArgs> StateChanged;
    }
    
    /// <summary>
    /// 差分検出サービスインターフェース
    /// </summary>
    public interface IDifferenceDetectionService
    {
        /// <summary>
        /// 差分検出を実行します
        /// </summary>
        /// <param name="previousImage">前回の画像</param>
        /// <param name="currentImage">現在の画像</param>
        /// <param name="settings">検出設定</param>
        /// <returns>差分検出結果</returns>
        Task<DifferenceDetectionResult> DetectDifferencesAsync(
            IImage previousImage, 
            IImage currentImage, 
            DifferenceDetectionSettings settings);
            
        /// <summary>
        /// テキスト差分を検出します
        /// </summary>
        /// <param name="previousText">前回のテキスト</param>
        /// <param name="currentText">現在のテキスト</param>
        /// <returns>差分検出結果</returns>
        TextDifferenceResult DetectTextDifferences(string previousText, string currentText);
    }
    
    /// <summary>
    /// 処理モード列挙型
    /// </summary>
    public enum ProcessingMode
    {
        /// <summary>
        /// 定期的なOCR処理
        /// </summary>
        PeriodicOcr,
        
        /// <summary>
        /// 差分検出によるOCR処理
        /// </summary>
        DifferenceBasedOcr,
        
        /// <summary>
        /// 手動トリガーのみ
        /// </summary>
        ManualOnly
    }
    
    /// <summary>
    /// 処理設定クラス
    /// </summary>
    public class ProcessingSettings
    {
        /// <summary>
        /// 処理間隔（ミリ秒）
        /// </summary>
        public int IntervalMs { get; set; } = 500;
        
        /// <summary>
        /// 差分検出感度（0.0～1.0）
        /// </summary>
        public double DifferenceSensitivity { get; set; } = 0.2;
        
        /// <summary>
        /// サンプリングレート
        /// </summary>
        public int SamplingRate { get; set; } = 10;
        
        /// <summary>
        /// 最小テキスト変化率（0.0～1.0）
        /// </summary>
        public double MinimumTextChangeFactor { get; set; } = 0.1;
        
        /// <summary>
        /// 処理優先度
        /// </summary>
        public ProcessingPriority Priority { get; set; } = ProcessingPriority.Normal;
        
        /// <summary>
        /// 並列処理の有効化
        /// </summary>
        public bool EnableParallelProcessing { get; set; } = true;
    }
    
    /// <summary>
    /// 差分検出結果クラス
    /// </summary>
    public class DifferenceDetectionResult
    {
        /// <summary>
        /// 変化が検出されたかどうか
        /// </summary>
        public bool HasChanged { get; set; }
        
        /// <summary>
        /// 変化の度合い（0.0～1.0）
        /// </summary>
        public double ChangeFactor { get; set; }
        
        /// <summary>
        /// 変化が検出された領域
        /// </summary>
        public IReadOnlyList<Rectangle> ChangedRegions { get; set; } = new List<Rectangle>();
        
        /// <summary>
        /// 検出にかかった時間（ミリ秒）
        /// </summary>
        public double DetectionTimeMs { get; set; }
    }
    
    /// <summary>
    /// テキスト差分結果クラス
    /// </summary>
    public class TextDifferenceResult
    {
        /// <summary>
        /// 変化が検出されたかどうか
        /// </summary>
        public bool HasChanged { get; set; }
        
        /// <summary>
        /// 変化の度合い（0.0～1.0）
        /// </summary>
        public double ChangeFactor { get; set; }
        
        /// <summary>
        /// 追加されたテキスト
        /// </summary>
        public string AddedText { get; set; } = string.Empty;
        
        /// <summary>
        /// 削除されたテキスト
        /// </summary>
        public string RemovedText { get; set; } = string.Empty;
    }
    
    /// <summary>
    /// 処理統計クラス
    /// </summary>
    public class ProcessingStatistics
    {
        /// <summary>
        /// OCR処理実行回数
        /// </summary>
        public int OcrExecutionCount { get; set; }
        
        /// <summary>
        /// 翻訳処理実行回数
        /// </summary>
        public int TranslationExecutionCount { get; set; }
        
        /// <summary>
        /// 画面変化検出回数
        /// </summary>
        public int ScreenChangeDetectionCount { get; set; }
        
        /// <summary>
        /// テキスト変化検出回数
        /// </summary>
        public int TextChangeDetectionCount { get; set; }
        
        /// <summary>
        /// 平均OCR処理時間（ミリ秒）
        /// </summary>
        public double AverageOcrTimeMs { get; set; }
        
        /// <summary>
        /// 平均翻訳処理時間（ミリ秒）
        /// </summary>
        public double AverageTranslationTimeMs { get; set; }
        
        /// <summary>
        /// 平均画面変化検出時間（ミリ秒）
        /// </summary>
        public double AverageChangeDetectionTimeMs { get; set; }
        
        /// <summary>
        /// CPU使用率（%）
        /// </summary>
        public double CpuUsagePercentage { get; set; }
        
        /// <summary>
        /// メモリ使用量（MB）
        /// </summary>
        public double MemoryUsageMB { get; set; }
    }
    
    /// <summary>
    /// 処理優先度列挙型
    /// </summary>
    public enum ProcessingPriority
    {
        /// <summary>
        /// 高い（ゲームパフォーマンスに影響する可能性あり）
        /// </summary>
        High,
        
        /// <summary>
        /// 普通（バランス）
        /// </summary>
        Normal,
        
        /// <summary>
        /// 低い（ゲームパフォーマンス優先）
        /// </summary>
        Low,
        
        /// <summary>
        /// 最小（アイドル時のみ処理）
        /// </summary>
        Minimal
    }
}
```

## 実装上の注意点
- OCR実行の適切なインターバル設定はパフォーマンスと翻訳レスポンス時間のバランスを考慮して決定する
- 画面差分検出は効率的なアルゴリズムを使用し、CPU負荷を最小限に抑える
- テキスト内容の比較は単純な文字列比較だけでなく、意味的な変化も考慮する（例：数字の変化のみの場合）
- フェーズ1（定期OCR）からフェーズ2（差分検出）への移行はパフォーマンス評価に基づいて実施する
- システムリソースの使用状況に応じた動的な処理調整を実装する
- ユーザー環境（PC性能）に応じた初期設定の最適化を検討する
- ライセンスの有無（有料/無料プラン）に応じた処理の最適化を実装する

## 関連Issue/参考
- 関連: #9 翻訳システム基盤の構築
- 関連: #16 パフォーマンス最適化
- 関連: #19 クラウドAI翻訳連携（有料プラン向け）
- 関連: #20 ローカル翻訳モデル最適化（無料プラン向け）
- 参照: E:\dev\Baketa\docs\baketa-ai-translation-requirements.md (3.2.1 OCR実行トリガーアプローチ)
- 参照: E:\dev\Baketa\docs\baketa-ai-translation-requirements.md (3.2.2 基本処理フロー)
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\performance.md (6. 差分検出アルゴリズムの最適化)

## マイルストーン
マイルストーン4: 機能拡張と最適化

## ラベル
- `type: feature`
- `priority: high`
- `component: processing`
- `performance: optimization`
