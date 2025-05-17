using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Events.Capture;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Capture.DifferenceDetection
{
    /// <summary>
    /// 拡張差分検出アルゴリズム
    /// </summary>
    public class EnhancedDifferenceDetector : IDifferenceDetector
    {
        private readonly ILogger<EnhancedDifferenceDetector>? _logger;
        private readonly IEventAggregator? _eventAggregator;
        private readonly Dictionary<DifferenceDetectionAlgorithm, IDetectionAlgorithm> _algorithms;
        private DifferenceDetectionSettings _settings;
        private IReadOnlyList<Rectangle> _previousTextRegions;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="algorithms">使用するアルゴリズムのリスト</param>
        /// <param name="eventAggregator">イベント集約オブジェクト</param>
        /// <param name="logger">ロガー</param>
        public EnhancedDifferenceDetector(
            IEnumerable<IDetectionAlgorithm> algorithms,
            IEventAggregator? eventAggregator = null,
            ILogger<EnhancedDifferenceDetector>? logger = null)
        {
            ArgumentNullException.ThrowIfNull(algorithms, nameof(algorithms));
                
            _algorithms = algorithms.ToDictionary(a => a.AlgorithmType, a => a);
            _eventAggregator = eventAggregator;
            _logger = logger;
            _settings = new DifferenceDetectionSettings();
            _previousTextRegions = new ReadOnlyCollection<Rectangle>(new List<Rectangle>());
        }
        
        /// <summary>
        /// 二つの画像間に有意な差分があるかを検出します
        /// </summary>
        public async Task<bool> HasSignificantChangeAsync(IImage previousImage, IImage currentImage, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(previousImage, nameof(previousImage));
ArgumentNullException.ThrowIfNull(currentImage, nameof(currentImage));
                
            // サイズチェック
            if (previousImage.Width != currentImage.Width || previousImage.Height != currentImage.Height)
            {
                _logger?.LogDebug("画像サイズが異なるため、有意な変化があると判断: {PrevSize} vs {CurrSize}",
                    $"{previousImage.Width}x{previousImage.Height}",
                    $"{currentImage.Width}x{currentImage.Height}");
                    
                return true;
            }
            
            try
            {
                // 選択されたアルゴリズムを取得
                IDetectionAlgorithm algorithm = GetSelectedAlgorithm(_settings.Algorithm);
                
                // 差分検出実行
                var result = await algorithm.DetectAsync(previousImage, currentImage, _settings, cancellationToken).ConfigureAwait(false);
                
                if (result.HasSignificantChange)
                {
                    _logger?.LogDebug("有意な変化を検出: 変化率 {ChangeRatio:P}", result.ChangeRatio);
                }
                else
                {
                    _logger?.LogTrace("有意な変化なし: 変化率 {ChangeRatio:P}", result.ChangeRatio);
                }
                
                // テキスト消失の確認と通知
                if (_previousTextRegions.Count > 0 && result.DisappearedTextRegions.Count > 0)
                {
                    await NotifyTextDisappearanceAsync(result.DisappearedTextRegions).ConfigureAwait(false);
                }
                
                return result.HasSignificantChange;
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "差分検出中に引数エラーが発生しました: {Message}", ex.Message);
                return true; // エラー時は安全のため変更ありと判断
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "差分検出中に操作エラーが発生しました: {Message}", ex.Message);
                return true;
            }
            catch (IOException ex)
            {
                _logger?.LogError(ex, "差分検出中にIO例外が発生しました: {Message}", ex.Message);
                return true;
            }
            catch (Exception ex) when (ex is not ApplicationException)
            {
                _logger?.LogError(ex, "差分検出中に予期しないエラーが発生しました: {Message}", ex.Message);
                return true;
            }
        }
        
        /// <summary>
        /// 二つの画像間の差分領域を検出します
        /// </summary>
        public async Task<IReadOnlyList<Rectangle>> DetectChangedRegionsAsync(IImage previousImage, IImage currentImage, CancellationToken cancellationToken = default)
        {
            if (previousImage == null || currentImage == null)
                throw new ArgumentNullException(previousImage == null ? nameof(previousImage) : nameof(currentImage));
                
            // サイズチェック
            if (previousImage.Width != currentImage.Width || previousImage.Height != currentImage.Height)
            {
                // サイズが異なる場合は画面全体を変化領域とする
                var entireRegion = new Rectangle(0, 0, currentImage.Width, currentImage.Height);
                return new ReadOnlyCollection<Rectangle>(new List<Rectangle> { entireRegion });
            }
            
            try
            {
                // 選択されたアルゴリズムを取得
                IDetectionAlgorithm algorithm = GetSelectedAlgorithm(_settings.Algorithm);
                
                // 差分検出実行
                var result = await algorithm.DetectAsync(previousImage, currentImage, _settings, cancellationToken).ConfigureAwait(false);
                
                // 変化領域を最小サイズでフィルタリング
                var regions = result.ChangedRegions
                    .Where(r => r.Width * r.Height >= _settings.MinimumChangedArea)
                    .ToList().AsReadOnly();
                
                _logger?.LogDebug("変化領域を {Count} 個検出: {Regions}",
                    regions.Count,
                    string.Join(", ", regions.Select(r => $"({r.X},{r.Y},{r.Width},{r.Height})")));
                
                // テキスト消失の確認と通知
                if (_previousTextRegions.Count > 0 && result.DisappearedTextRegions.Count > 0)
                {
                    await NotifyTextDisappearanceAsync(result.DisappearedTextRegions).ConfigureAwait(false);
                }
                
                return regions;
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "差分領域検出中に引数エラーが発生しました: {Message}", ex.Message);
                return CreateEntireRegionResult(currentImage);
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "差分領域検出中に操作エラーが発生しました: {Message}", ex.Message);
                return CreateEntireRegionResult(currentImage);
            }
            catch (IOException ex)
            {
                _logger?.LogError(ex, "差分領域検出中にIO例外が発生しました: {Message}", ex.Message);
                return CreateEntireRegionResult(currentImage);
            }
            catch (Exception ex) when (ex is not ApplicationException)
            {
                _logger?.LogError(ex, "差分領域検出中に予期しないエラーが発生しました: {Message}", ex.Message);
                return CreateEntireRegionResult(currentImage);
            }
        }
        
        /// <summary>
        /// テキスト消失を検出します
        /// </summary>
        public async Task<IReadOnlyList<Rectangle>> DetectTextDisappearanceAsync(IImage previousImage, IImage currentImage, CancellationToken cancellationToken = default)
        {
            if (previousImage == null || currentImage == null)
                throw new ArgumentNullException(previousImage == null ? nameof(previousImage) : nameof(currentImage));
                
            if (_previousTextRegions.Count == 0)
            {
                _logger?.LogDebug("前回のテキスト領域が設定されていないため、テキスト消失検出をスキップします");
                return new ReadOnlyCollection<Rectangle>(new List<Rectangle>());
            }
            
            try
            {
                // エッジベースのアルゴリズムを使用（テキスト検出に特化）
                IDetectionAlgorithm algorithm = GetSelectedAlgorithm(DifferenceDetectionAlgorithm.EdgeBased);
                
                // テキスト検出に最適化した設定を作成
                var textDetectionSettings = _settings.Clone();
                textDetectionSettings.FocusOnTextRegions = true;
                textDetectionSettings.EdgeChangeWeight = 3.0; // エッジ検出の重みを強化
                
                // 差分検出実行
                var result = await algorithm.DetectAsync(previousImage, currentImage, textDetectionSettings, cancellationToken).ConfigureAwait(false);
                
                // 消失テキスト領域をログ出力
                if (result.DisappearedTextRegions.Count > 0)
                {
                    _logger?.LogDebug("テキスト消失を {Count} 個検出: {Regions}",
                        result.DisappearedTextRegions.Count,
                        string.Join(", ", result.DisappearedTextRegions.Select(r => $"({r.X},{r.Y},{r.Width},{r.Height})")));
                        
                    // イベント通知
                    await NotifyTextDisappearanceAsync(result.DisappearedTextRegions).ConfigureAwait(false);
                }
                
                return result.DisappearedTextRegions;
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "テキスト消失検出中に引数エラーが発生しました: {Message}", ex.Message);
                return new ReadOnlyCollection<Rectangle>(new List<Rectangle>());
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "テキスト消失検出中に操作エラーが発生しました: {Message}", ex.Message);
                return new ReadOnlyCollection<Rectangle>(new List<Rectangle>());
            }
            catch (IOException ex)
            {
                _logger?.LogError(ex, "テキスト消失検出中にIO例外が発生しました: {Message}", ex.Message);
                return new ReadOnlyCollection<Rectangle>(new List<Rectangle>());
            }
            catch (Exception ex) when (ex is not ApplicationException)
            {
                _logger?.LogError(ex, "テキスト消失検出中に予期しないエラーが発生しました: {Message}", ex.Message);
                return new List<Rectangle>().AsReadOnly();
            }
        }
        
        /// <summary>
        /// 差分検出の閾値を設定します
        /// </summary>
        public void SetThreshold(double threshold)
        {
            if (threshold < 0.0 || threshold > 1.0)
                throw new ArgumentOutOfRangeException(nameof(threshold), "閾値は0.0から1.0の間で設定してください");
                
            _settings.Threshold = threshold;
            _logger?.LogDebug("差分検出閾値を {Threshold:P} に設定", threshold);
        }
        
        /// <summary>
        /// 現在の差分検出設定を取得します
        /// </summary>
        public DifferenceDetectionSettings GetSettings()
        {
            return _settings.Clone();
        }
        
        /// <summary>
        /// 差分検出設定を適用します
        /// </summary>
        public void ApplySettings(DifferenceDetectionSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings, nameof(settings));
                
            _settings = settings.Clone();
            
            _logger?.LogDebug("差分検出設定を更新: 閾値={Threshold:P}, アルゴリズム={Algorithm}, テキスト重視={FocusText}",
                _settings.Threshold, _settings.Algorithm, _settings.FocusOnTextRegions);
        }
        
        /// <summary>
        /// 前回検出されたテキスト領域を設定します
        /// </summary>
        public void SetPreviousTextRegions(IReadOnlyList<Rectangle> textRegions)
        {
            _previousTextRegions = textRegions ?? new ReadOnlyCollection<Rectangle>(new List<Rectangle>());
            _logger?.LogDebug("前回のテキスト領域を {Count} 個設定", _previousTextRegions.Count);
        }
        
        /// <summary>
        /// 選択されたアルゴリズムを取得します
        /// </summary>
        private IDetectionAlgorithm GetSelectedAlgorithm(DifferenceDetectionAlgorithm algorithmType)
        {
            // 指定されたアルゴリズムがない場合はフォールバック
            if (!_algorithms.TryGetValue(algorithmType, out var algorithm))
            {
                _logger?.LogWarning("指定されたアルゴリズム {Algorithm} が利用できないため、代替アルゴリズムを使用します", algorithmType);
                
                // 優先順位でフォールバック
                algorithm = _algorithms.Values.FirstOrDefault() ?? 
                    throw new InvalidOperationException("利用可能な差分検出アルゴリズムがありません");
            }
            
            return algorithm;
        }
        
        /// <summary>
        /// 画面全体を変化領域として返す結果を作成します
        /// </summary>
        private ReadOnlyCollection<Rectangle> CreateEntireRegionResult(IImage currentImage)
        {
            return new ReadOnlyCollection<Rectangle>(new List<Rectangle> { new Rectangle(0, 0, currentImage.Width, currentImage.Height) });
        }
        
        /// <summary>
        /// テキスト消失イベントを発行します
        /// </summary>
        /// <param name="disappearedRegions">消失したテキスト領域</param>
        private async Task NotifyTextDisappearanceAsync(IReadOnlyList<Rectangle> disappearedRegions)
        {
            if (_eventAggregator == null || disappearedRegions == null || disappearedRegions.Count == 0)
                return;
                
            try
            {
                // イベントオブジェクトの作成
                var textDisappearanceEvent = new DynamicTextDisappearanceEvent(disappearedRegions);
                
                // 動的イベントクラスを使用して発行
                await _eventAggregator.PublishAsync(textDisappearanceEvent).ConfigureAwait(false);
                
                _logger?.LogDebug("テキスト消失イベントを発行: {Count} 個の領域", disappearedRegions.Count);
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "テキスト消失イベント発行中に操作エラーが発生しました: {Message}", ex.Message);
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "テキスト消失イベント発行中に引数エラーが発生しました: {Message}", ex.Message);
            }
            catch (Exception ex) when (ex is not ApplicationException)
            {
                _logger?.LogError(ex, "テキスト消失イベント発行中に予期しないエラーが発生しました: {Message}", ex.Message);
            }
        }
        
        /// <summary>
        /// 動的テキスト消失イベントクラス
        /// </summary>
        private sealed class DynamicTextDisappearanceEvent : IEvent
        {
            public Guid Id { get; } = Guid.NewGuid();
            public string Name => "TextDisappearance";
            public string Category => "Capture";
            public DateTime Timestamp { get; }
            
            public IReadOnlyList<Rectangle> DisappearedRegions { get; }
            public IntPtr SourceWindowHandle { get; }
            
            public DynamicTextDisappearanceEvent(IReadOnlyList<Rectangle> regions, IntPtr sourceWindow = default)
            {
                ArgumentNullException.ThrowIfNull(regions, nameof(regions));
                
                DisappearedRegions = regions;
                SourceWindowHandle = sourceWindow;
                Timestamp = DateTime.Now;
            }
        }
    }
}