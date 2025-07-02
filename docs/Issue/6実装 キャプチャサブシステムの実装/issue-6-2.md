# 実装: 差分検出サブシステム

## 概要
連続したキャプチャ画像間の差分を効率的に検出し、テキスト部分の変化を識別するサブシステムを実装します。

## 目的・理由
画面全体を常に処理することはリソースの無駄使いになります。テキストが変化した場合にのみOCR処理を実行するよう、効率的な差分検出機能が必要です。特にゲーム画面では、テキスト部分のみの変化を正確に検出することで、システム負荷を大幅に軽減できます。

## 詳細
- `IDifferenceDetector`インターフェースの設計と実装
- 差分検出アルゴリズムの実装（ピクセルベース、ブロックベース、特徴ベース）
- テキスト領域変化に特化した検出最適化
- ノイズや小さな変化を除外する閾値設定機能

## タスク分解
- [ ] `IDifferenceDetector`インターフェースの設計
  - [ ] 画像間の差分検出メソッドの定義
  - [ ] 閾値設定機能の定義
  - [ ] 差分領域取得機能の定義
- [ ] 基本差分検出アルゴリズムの実装
  - [ ] ピクセルベース差分検出の実装
  - [ ] ブロックベース差分検出の実装（パフォーマンス向上）
  - [ ] ヒストグラムベース差分検出の実装（照明変化に強い）
- [ ] テキスト領域特化の最適化
  - [ ] エッジベース差分検出の実装
  - [ ] テキスト特徴に基づく重み付け
  - [ ] OCR前処理と連携した差分検出
- [ ] 差分検出の調整機能
  - [ ] 感度調整パラメータの実装
  - [ ] ノイズ除去パラメータの実装
  - [ ] 変化の種類（テキスト/非テキスト）の分類
- [ ] 差分検出結果の可視化（デバッグ用）
- [ ] パフォーマンステストと最適化
- [ ] 単体テストの作成

## インターフェース設計案
```csharp
namespace Baketa.Core.Abstractions.Capture
{
    /// <summary>
    /// 画像間の差分を検出するインターフェース
    /// </summary>
    public interface IDifferenceDetector
    {
        /// <summary>
        /// 二つの画像間に有意な差分があるかを検出します
        /// </summary>
        /// <param name="previousImage">前回の画像</param>
        /// <param name="currentImage">現在の画像</param>
        /// <returns>有意な差分がある場合はtrue</returns>
        Task<bool> HasSignificantChangeAsync(IImage previousImage, IImage currentImage);
        
        /// <summary>
        /// 二つの画像間の差分領域を検出します
        /// </summary>
        /// <param name="previousImage">前回の画像</param>
        /// <param name="currentImage">現在の画像</param>
        /// <returns>差分が検出された領域のリスト</returns>
        Task<IReadOnlyList<Rectangle>> DetectChangedRegionsAsync(IImage previousImage, IImage currentImage);
        
        /// <summary>
        /// 差分検出の閾値を設定します
        /// </summary>
        /// <param name="threshold">閾値（0.0～1.0）</param>
        void SetThreshold(double threshold);
        
        /// <summary>
        /// 現在の差分検出設定を取得します
        /// </summary>
        /// <returns>設定情報</returns>
        DifferenceDetectionSettings GetSettings();
        
        /// <summary>
        /// 差分検出設定を適用します
        /// </summary>
        /// <param name="settings">設定情報</param>
        void ApplySettings(DifferenceDetectionSettings settings);
    }
    
    /// <summary>
    /// 差分検出設定を表すクラス
    /// </summary>
    public class DifferenceDetectionSettings
    {
        /// <summary>
        /// 差分検出の閾値（0.0～1.0）
        /// </summary>
        public double Threshold { get; set; } = 0.05;
        
        /// <summary>
        /// ブロックサイズ（ブロックベース検出用）
        /// </summary>
        public int BlockSize { get; set; } = 16;
        
        /// <summary>
        /// 最小変化領域サイズ（小さな変化を無視）
        /// </summary>
        public int MinimumChangedArea { get; set; } = 100;
        
        /// <summary>
        /// テキスト領域に重点を置く（true）か全体の変化を検出（false）か
        /// </summary>
        public bool FocusOnTextRegions { get; set; } = true;
        
        /// <summary>
        /// エッジ変化の重み（テキスト領域検出で使用）
        /// </summary>
        public double EdgeChangeWeight { get; set; } = 2.0;
        
        /// <summary>
        /// 照明変化を無視するか
        /// </summary>
        public bool IgnoreLightingChanges { get; set; } = true;
    }
}
```

## 実装例
```csharp
namespace Baketa.Core.Capture
{
    /// <summary>
    /// 拡張差分検出アルゴリズム
    /// </summary>
    public class EnhancedDifferenceDetector : IDifferenceDetector
    {
        private readonly ILogger<EnhancedDifferenceDetector>? _logger;
        private DifferenceDetectionSettings _settings = new();
        
        public EnhancedDifferenceDetector(ILogger<EnhancedDifferenceDetector>? logger = null)
        {
            _logger = logger;
        }
        
        public async Task<bool> HasSignificantChangeAsync(IImage previousImage, IImage currentImage)
        {
            if (previousImage == null || currentImage == null)
                throw new ArgumentNullException(previousImage == null ? nameof(previousImage) : nameof(currentImage));
                
            // サイズチェック
            if (previousImage.Width != currentImage.Width || previousImage.Height != currentImage.Height)
            {
                _logger?.LogDebug("画像サイズが異なるため、有意な変化があると判断: {PrevSize} vs {CurrSize}",
                    $"{previousImage.Width}x{previousImage.Height}",
                    $"{currentImage.Width}x{currentImage.Height}");
                    
                return true;
            }
            
            // 高速パスとして、まずはブロックベースの差分検出を実行
            return await DetectChangesBlockBased(previousImage, currentImage);
        }
        
        public async Task<IReadOnlyList<Rectangle>> DetectChangedRegionsAsync(IImage previousImage, IImage currentImage)
        {
            if (previousImage == null || currentImage == null)
                throw new ArgumentNullException(previousImage == null ? nameof(previousImage) : nameof(currentImage));
                
            // サイズチェック
            if (previousImage.Width != currentImage.Width || previousImage.Height != currentImage.Height)
            {
                // サイズが異なる場合は画面全体を変化領域とする
                return new[] { new Rectangle(0, 0, currentImage.Width, currentImage.Height) };
            }
            
            // 詳細な差分領域検出を実行
            var changedBlocks = await DetectChangedBlocksAsync(previousImage, currentImage);
            
            // 隣接するブロックをまとめて領域化
            var regions = MergeAdjacentBlocks(changedBlocks, currentImage.Width, currentImage.Height);
            
            // 小さすぎる領域を除外
            regions = regions.Where(r => r.Width * r.Height >= _settings.MinimumChangedArea).ToList();
            
            _logger?.LogDebug("変化領域を {Count} 個検出: {Regions}", 
                regions.Count,
                string.Join(", ", regions.Select(r => $"({r.X},{r.Y},{r.Width},{r.Height})")));
                
            return regions;
        }
        
        private async Task<bool> DetectChangesBlockBased(IImage previousImage, IImage currentImage)
        {
            // ブロックベースの差分検出
            // 画像をブロックに分割して効率的に差分検出
            
            int blockSize = _settings.BlockSize;
            int widthInBlocks = previousImage.Width / blockSize;
            int heightInBlocks = previousImage.Height / blockSize;
            
            // Advanced Imageに変換（高度な画像処理が必要な場合）
            IAdvancedImage? prevAdvanced = previousImage as IAdvancedImage;
            IAdvancedImage? currAdvanced = currentImage as IAdvancedImage;
            
            if (_settings.FocusOnTextRegions && prevAdvanced != null && currAdvanced != null)
            {
                // テキスト領域に焦点を当てた検出（エッジベース）
                return await DetectTextChangesAsync(prevAdvanced, currAdvanced);
            }
            
            // 標準的なブロックベース検出
            // IImageの直接比較で実装（詳細実装は省略）
            
            // 変化したブロックの割合を計算
            double changedRatio = 0.1; // 実際の実装では計算で求める
            
            bool hasSignificantChange = changedRatio > _settings.Threshold;
            
            _logger?.LogTrace("ブロックベース差分検出: 変化率 {ChangeRatio:P}, 閾値 {Threshold:P}, 有意な変化: {HasChange}",
                changedRatio, _settings.Threshold, hasSignificantChange);
                
            return hasSignificantChange;
        }
        
        private async Task<bool> DetectTextChangesAsync(IAdvancedImage previousImage, IAdvancedImage currentImage)
        {
            // テキスト検出に特化した差分検出
            // エッジ検出やテキスト特徴に基づく検出を実装
            
            // 1. 両画像をグレースケールに変換
            var prevGray = await previousImage.ToGrayscaleAsync();
            var currGray = await currentImage.ToGrayscaleAsync();
            
            // 2. エッジ検出フィルタを適用
            // （実際の実装では適切なIImageFilterを使用）
            
            // 3. エッジの差分を重点的に評価
            
            // 変化率を返す（実装の詳細は省略）
            double edgeChangedRatio = 0.08; // 実際の実装では計算で求める
            
            bool hasSignificantChange = edgeChangedRatio > (_settings.Threshold / _settings.EdgeChangeWeight);
            
            _logger?.LogTrace("テキスト領域差分検出: エッジ変化率 {ChangeRatio:P}, 調整閾値 {AdjustedThreshold:P}, 有意な変化: {HasChange}",
                edgeChangedRatio, _settings.Threshold / _settings.EdgeChangeWeight, hasSignificantChange);
                
            return hasSignificantChange;
        }
        
        private async Task<List<Rectangle>> DetectChangedBlocksAsync(IImage previousImage, IImage currentImage)
        {
            // 変化のあったブロックの詳細な検出
            // （実際の実装では画像の詳細比較を行う）
            
            var changedBlocks = new List<Rectangle>();
            int blockSize = _settings.BlockSize;
            
            // （実装の詳細は省略）
            
            return changedBlocks;
        }
        
        private List<Rectangle> MergeAdjacentBlocks(List<Rectangle> blocks, int width, int height)
        {
            // 隣接するブロックを結合して、より大きな矩形領域に
            // （実装の詳細は省略 - 連結成分ラベリングなどのアルゴリズムを使用）
            
            var mergedRegions = new List<Rectangle>();
            
            // （実装の詳細は省略）
            
            return mergedRegions;
        }
        
        public void SetThreshold(double threshold)
        {
            if (threshold < 0.0 || threshold > 1.0)
                throw new ArgumentOutOfRangeException(nameof(threshold), "閾値は0.0から1.0の間で設定してください");
                
            _settings.Threshold = threshold;
            _logger?.LogDebug("差分検出閾値を {Threshold:P} に設定", threshold);
        }
        
        public DifferenceDetectionSettings GetSettings()
        {
            return new DifferenceDetectionSettings
            {
                Threshold = _settings.Threshold,
                BlockSize = _settings.BlockSize,
                MinimumChangedArea = _settings.MinimumChangedArea,
                FocusOnTextRegions = _settings.FocusOnTextRegions,
                EdgeChangeWeight = _settings.EdgeChangeWeight,
                IgnoreLightingChanges = _settings.IgnoreLightingChanges
            };
        }
        
        public void ApplySettings(DifferenceDetectionSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
                
            _settings = new DifferenceDetectionSettings
            {
                Threshold = settings.Threshold,
                BlockSize = settings.BlockSize,
                MinimumChangedArea = settings.MinimumChangedArea,
                FocusOnTextRegions = settings.FocusOnTextRegions,
                EdgeChangeWeight = settings.EdgeChangeWeight,
                IgnoreLightingChanges = settings.IgnoreLightingChanges
            };
            
            _logger?.LogDebug("差分検出設定を更新: 閾値={Threshold:P}, ブロックサイズ={BlockSize}, テキスト重視={FocusText}",
                _settings.Threshold, _settings.BlockSize, _settings.FocusOnTextRegions);
        }
    }
}
```

## 改良版差分アルゴリズム案
1. **テキスト特性を考慮した重み付け**
   - エッジと形状に基づくテキスト検出
   - テキスト領域での変化に高い重みを設定

2. **マルチスケール分析**
   - 複数解像度での差分検出
   - 大きな変化と細かな変化の両方を検出

3. **時間的安定性**
   - 複数フレームにわたる変化の分析
   - 一時的なノイズを除外

4. **機械学習ベースの最適化**
   - ゲームごとのプロファイルに基づく自動調整
   - 使用パターンに基づく差分検出パラメータの最適化

## 関連Issue/参考
- 親Issue: #6 実装: キャプチャサブシステムの実装
- 関連: #5 実装: 画像処理抽象化レイヤーの拡張
- 参照: E:\dev\Baketa\docs\3-architecture\ocr-system\ocr-opencv-approach.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (3. 非同期プログラミング)

## マイルストーン
マイルストーン2: キャプチャとOCR基盤

## ラベル
- `type: feature`
- `priority: high`
- `component: core`
