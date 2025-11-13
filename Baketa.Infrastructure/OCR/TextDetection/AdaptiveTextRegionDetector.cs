using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection; // 🔥 [ULTRATHINK_PHASE7] リフレクション経由でGetUnderlyingBitmap()呼び出し
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.OCR.TextDetection;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Infrastructure.OCR.Scaling;
using OCRTextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;
using TextDetectionMethod = Baketa.Core.Abstractions.OCR.TextDetection.TextDetectionMethod;
using IOcrEngine = Baketa.Core.Abstractions.OCR.IOcrEngine;
using IImageFactory = Baketa.Core.Abstractions.Factories.IImageFactory;
using IWindowsImageFactory = Baketa.Core.Abstractions.Factories.IWindowsImageFactory; // 🔥 [ULTRATHINK_PHASE7] Bitmap直接変換用
using Timer = System.Threading.Timer;

namespace Baketa.Infrastructure.OCR.TextDetection;

/// <summary>
/// 適応的テキスト領域検出器 - 履歴ベース最適化と動的パラメータ調整
/// 1-B2: テキスト領域検出高度化の実装
/// </summary>
public sealed class AdaptiveTextRegionDetector : ITextRegionDetector, IDisposable
{
    private readonly ILogger<AdaptiveTextRegionDetector> _logger;
    private readonly IOcrEngine? _ocrEngine;
    private readonly IImageFactory? _imageFactory;
    private readonly IWindowsImageFactory? _windowsImageFactory; // 🔥 [ULTRATHINK_PHASE7] PNG round-trip回避用
    private readonly Dictionary<string, object> _parameters = [];
    private readonly ConcurrentQueue<DetectionHistoryEntry> _detectionHistory = [];
    private readonly ConcurrentDictionary<string, RegionTemplate> _regionTemplates = [];
    private readonly Timer _adaptationTimer;
    
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// 🔥 [ULTRATHINK_PHASE9] Gemini推奨改善: 型名定数化でリネーム脆弱性解消
    /// SimpleAdvancedImageAdapter型の名前（リフレクションで使用）
    /// この定数により、リネーム時の影響箇所を明確化し、検索・メンテナンスを容易にする
    /// </summary>
    private const string SimpleAdvancedImageAdapterTypeName = "SimpleAdvancedImageAdapter";

    private bool _disposed;
    private int _detectionCount;
    private const int MaxHistorySize = 100;
    private const int AdaptationIntervalMs = 5000; // 5秒間隔で適応
    
    public string Name => "AdaptiveTextRegionDetector";
    public string Description => "適応的テキスト領域検出器 - 履歴ベース最適化と動的調整";
    public TextDetectionMethod Method => TextDetectionMethod.Adaptive;

    public AdaptiveTextRegionDetector(
        ILogger<AdaptiveTextRegionDetector> logger,
        IOcrEngine? ocrEngine = null,
        IImageFactory? imageFactory = null,
        IWindowsImageFactory? windowsImageFactory = null) // 🔥 [ULTRATHINK_PHASE7] PNG round-trip回避用
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ocrEngine = ocrEngine;
        _imageFactory = imageFactory;
        _windowsImageFactory = windowsImageFactory; // 🔥 [ULTRATHINK_PHASE7] Bitmap直接変換用

        InitializeDefaultParameters();

        // 定期的な適応処理を開始
        _adaptationTimer = new Timer(PerformAdaptation, null,
            TimeSpan.FromMilliseconds(AdaptationIntervalMs),
            TimeSpan.FromMilliseconds(AdaptationIntervalMs));

        _logger.LogInformation("適応的テキスト領域検出器を初期化 - PaddleOCR統合: {HasOcr}, ImageFactory: {HasFactory}, WindowsImageFactory: {HasWindowsFactory}",
            _ocrEngine != null, _imageFactory != null, _windowsImageFactory != null);
    }

    /// <summary>
    /// 適応的テキスト領域検出の実行
    /// 履歴データを活用した動的最適化
    /// </summary>
    public async Task<IReadOnlyList<OCRTextRegion>> DetectRegionsAsync(IAdvancedImage image, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var detectionId = Interlocked.Increment(ref _detectionCount);
        
        try
        {
            _logger.LogInformation("適応的テキスト領域検出開始: ID={DetectionId}, サイズ={Width}x{Height}", 
                detectionId, image.Width, image.Height);

            // Phase 1: テンプレートベース高速検出
            var templateRegions = await DetectUsingTemplatesAsync(image, cancellationToken).ConfigureAwait(false);
            
            // Phase 2: 適応的パラメータによる詳細検出
            var adaptiveRegions = await DetectWithAdaptiveParametersAsync(image, cancellationToken).ConfigureAwait(false);
            
            // Phase 3: 履歴データによる結果最適化
            var optimizedRegions = await OptimizeRegionsWithHistoryAsync(
                [.. templateRegions, .. adaptiveRegions], image, cancellationToken).ConfigureAwait(false);
            
            stopwatch.Stop();
            
            // 検出履歴に記録
            var historyEntry = new DetectionHistoryEntry
            {
                DetectionId = detectionId,
                Timestamp = DateTime.Now,
                ImageSize = new Size(image.Width, image.Height),
                DetectedRegions = [.. optimizedRegions],
                ProcessingTimeMs = stopwatch.Elapsed.TotalMilliseconds,
                TemplateMatchCount = templateRegions.Count,
                AdaptiveDetectionCount = adaptiveRegions.Count,
                FinalRegionCount = optimizedRegions.Count
            };
            
            AddToHistory(historyEntry);
            
            _logger.LogInformation("適応的テキスト領域検出完了: ID={DetectionId}, 領域数={RegionCount}, 処理時間={ProcessingMs}ms", 
                detectionId, optimizedRegions.Count, stopwatch.ElapsedMilliseconds);
                
            return optimizedRegions;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "適応的テキスト領域検出中にエラー: ID={DetectionId}", detectionId);
            return [];
        }
    }

    /// <summary>
    /// テンプレートベース高速検出
    /// 過去の成功パターンを利用した効率的検出
    /// </summary>
    private async Task<List<OCRTextRegion>> DetectUsingTemplatesAsync(IAdvancedImage image, CancellationToken cancellationToken)
    {
        var regions = new List<OCRTextRegion>();
        
        if (_regionTemplates.IsEmpty)
        {
            _logger.LogDebug("テンプレートが存在しないため、テンプレートベース検出をスキップ");
            return regions;
        }

        await Task.Run(() =>
        {
            var imageKey = GenerateImageKey(image);
            var matchingTemplates = _regionTemplates.Values
                .Where(t => t.IsCompatible(image.Width, image.Height))
                .OrderByDescending(t => t.SuccessRate)
                .Take(5); // 上位5個のテンプレートを使用

            foreach (var template in matchingTemplates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var templateRegions = template.GenerateRegions(image.Width, image.Height);
                foreach (var region in templateRegions)
                {
                    regions.Add(new OCRTextRegion
                    {
                        Bounds = region,
                        Confidence = template.SuccessRate,
                        RegionType = TextRegionType.Template,
                        DetectionMethod = "TemplateMatch"
                    });
                }
            }
            
            _logger.LogDebug("テンプレートベース検出完了: {RegionCount}個の候補領域", regions.Count);
        }, cancellationToken).ConfigureAwait(false);
        
        return regions;
    }

    /// <summary>
    /// 適応的パラメータによる詳細検出
    /// 動的に調整されたパラメータを使用した高精度検出
    /// </summary>
    private async Task<List<OCRTextRegion>> DetectWithAdaptiveParametersAsync(IAdvancedImage image, CancellationToken cancellationToken)
    {
        // PaddleOCRエンジンが利用できない場合はフォールバック
        if (_ocrEngine == null || _imageFactory == null)
        {
            _logger.LogWarning("⚠️ PaddleOCRエンジンまたはImageFactoryが注入されていません - 全画面フォールバック");
            return await CreateFullScreenFallbackAsync(image).ConfigureAwait(false);
        }

        try
        {
            _logger.LogDebug("🔍 [K-28_STEP1] PaddleOCRベーステキスト領域検出開始: サイズ={Width}x{Height}", image.Width, image.Height);

            // 🔥 [PHASE13.2.31K-28] 詳細ログ追加: どこで例外が発生するか100%特定
            _logger.LogDebug("🔍 [K-28_STEP2] IAdvancedImage → IImage変換開始");

            // IAdvancedImage → IImage変換
            var convertedImage = await ConvertAdvancedImageToImageAsync(image).ConfigureAwait(false);

            _logger.LogDebug("✅ [K-28_STEP2] IAdvancedImage → IImage変換成功: {Width}x{Height}",
                convertedImage.Width, convertedImage.Height);

            try
            {
                // 🔥 [ROI_FIX_FINAL] Resizing logic is the cause of detection failure.
                // Disable resizing to pass the full-resolution image to the detector.
                // The original crash issue with >960px images is likely resolved by other fixes (e.g., 4-channel conversion).
                const int det_limit_side_len = 960;
                var originalWidth = convertedImage.Width;
                var originalHeight = convertedImage.Height;
                double scale = 1.0;
                IImage imageForDetection = convertedImage;
                bool resizedImageNeedsDisposal = false;

                // if (originalWidth > det_limit_side_len || originalHeight > det_limit_side_len)
                // {
                //     scale = (originalWidth > originalHeight)
                //         ? (double)det_limit_side_len / originalWidth
                //         : (double)det_limit_side_len / originalHeight;
                //
                //     int newWidth = (int)(originalWidth * scale);
                //     int newHeight = (int)(originalHeight * scale);
                //
                //     _logger.LogInformation("🖼️ [ROI_DETECTION_FIX] 画像が大きすぎるため検出用にリサイズします: {OriginalW}x{OriginalH} -> {NewW}x{NewH} (スケール: {Scale:F3})",
                //         originalWidth, originalHeight, newWidth, newHeight, scale);
                //
                //     imageForDetection = await convertedImage.ResizeAsync(newWidth, newHeight);
                //     resizedImageNeedsDisposal = true;
                // }

                // PaddleOCRの検出専用機能を使用
                var ocrResults = await _ocrEngine.DetectTextRegionsAsync(imageForDetection, cancellationToken).ConfigureAwait(false);

                // リサイズした画像は不要になったら破棄
                if (resizedImageNeedsDisposal)
                {
                    (imageForDetection as IDisposable)?.Dispose();
                }

                if (ocrResults?.TextRegions == null || ocrResults.TextRegions.Count == 0)
                {
                    _logger.LogDebug("🔍 PaddleOCR検出結果が空 - 全画面フォールバック実行");
                    return await CreateFullScreenFallbackAsync(image).ConfigureAwait(false);
                }

                // 座標を元の画像サイズに復元
                var restorationScale = 1.0 / scale;
                var originalImageSize = new Size(originalWidth, originalHeight);
                var restoredRegions = ocrResults.TextRegions
                    .Select(region => CoordinateRestorer.RestoreTextRegion(region, restorationScale, originalImageSize))
                    .Where(region => IsRegionValid(region.Bounds))
                    .ToList();

                _logger.LogDebug("✅ [K-28_STEP4] 座標復元完了: 検出={DetectionCount}個, 復元後有効={RestoredCount}個",
                    ocrResults.TextRegions.Count, restoredRegions.Count);

                // 🔥 [K-28_STEP5] 領域統合前のログ
                _logger.LogDebug("🔍 [K-28_STEP5] 領域統合処理開始: {RegionCount}個", restoredRegions.Count);

                // 近接領域の統合（既存ロジックを活用）
                // OcrTextRegion → OCRTextRegion (TextDetection.TextRegion) 変換
                var convertedRegions = restoredRegions.Select(ocrRegion => new OCRTextRegion
                {
                    Bounds = ocrRegion.Bounds,
                    Confidence = ocrRegion.Confidence,
                    RegionType = TextRegionType.Unknown, // デフォルト値
                    DetectionMethod = "PaddleOCR"
                }).ToList();

                var mergedRegions = MergeOverlappingRegions(convertedRegions);

                _logger.LogDebug("✅ [K-28_STEP5] 領域統合完了: {MergedCount}個", mergedRegions.Count);

                // 適応的パラメータによる制限
                var maxRegions = GetParameter<int>("MaxRegionsPerImage");
                var finalRegions = mergedRegions
                    .OrderByDescending(r => r.Confidence)
                    .Take(maxRegions)
                    .ToList();

                _logger.LogInformation("✅ PaddleOCRベーステキスト領域検出完了: {OriginalCount}個 → 復元後{RestoredCount}個 → 統合後{MergedCount}個 → 最終{FinalCount}個",
                    ocrResults.TextRegions.Count, restoredRegions.Count, mergedRegions.Count, finalRegions.Count);

                return finalRegions;
            }
            finally
            {
                // 変換された画像のリソース解放
                convertedImage?.Dispose();
            }
        }
        catch (Exception ex)
        {
            // 🔥 [PHASE13.2.31K-27] Gemini推奨修正: フルスクリーンフォールバックの廃止
            // 問題: CreateFullScreenFallbackAsync()がフルスクリーン領域を返し、PaddlePredictor(Detector)が失敗
            // 解決策: K-26と同様に空リストを返してOCR処理をスキップ、システム安定化を優先
            _logger.LogError(ex, "❌ [K-27] PaddleOCR検出処理中にエラー: {ErrorMessage} - スタックトレース含む詳細ログ", ex.Message);

            // エラー時のフォールバック: 空のリストを返す（翻訳スキップ、システム安定化）
            _logger.LogWarning("🔄 [K-27] フォールバック: 空のリストを返します（PaddlePredictor過負荷回避、K-26統合）");
            return [];
        }
    }

    /// <summary>
    /// 履歴データによる結果最適化
    /// 過去の成功・失敗パターンを学習した結果改善
    /// </summary>
    private async Task<List<OCRTextRegion>> OptimizeRegionsWithHistoryAsync(List<OCRTextRegion> regions, IAdvancedImage image, CancellationToken cancellationToken)
    {
        if (regions.Count == 0) return regions;
        
        var optimizedRegions = new List<OCRTextRegion>();
        
        await Task.Run(() =>
        {
            var recentHistory = GetRecentHistory(10); // 直近10回の履歴
            if (recentHistory.Count == 0)
            {
                optimizedRegions.AddRange(regions);
                return;
            }
            
            // 履歴から成功パターンを分析
            var successPatterns = AnalyzeSuccessPatterns(recentHistory);
            
            foreach (var region in regions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // 履歴パターンとのマッチング度を計算
                var historyScore = CalculateHistoryMatchScore(region, successPatterns, image);
                
                // スコアに基づく信頼度調整
                var adjustedRegion = new OCRTextRegion(region.Bounds, (float)Math.Min(1.0, region.Confidence * historyScore))
                {
                    RegionType = region.RegionType,
                    DetectionMethod = $"{region.DetectionMethod}+History",
                    ProcessedImage = region.ProcessedImage
                };
                
                // 閾値以上の領域のみを採用
                var confidenceThreshold = GetParameter<double>("HistoryConfidenceThreshold");
                if (adjustedRegion.Confidence >= confidenceThreshold)
                {
                    optimizedRegions.Add(adjustedRegion);
                }
            }
            
            // 成功パターンから新しいテンプレートを生成
            UpdateRegionTemplates(optimizedRegions, image);
            
            _logger.LogDebug("履歴ベース最適化完了: {OriginalCount} → {OptimizedCount}個の領域", 
                regions.Count, optimizedRegions.Count);
        }, cancellationToken).ConfigureAwait(false);
        
        return optimizedRegions;
    }




    /// <summary>
    /// 重複する領域をマージ
    /// </summary>
    private List<OCRTextRegion> MergeOverlappingRegions(List<OCRTextRegion> regions)
    {
        if (regions.Count <= 1) return regions;

        var overlapThreshold = GetParameter<double>("OverlapThreshold");
        bool mergedInThisPass;

        do
        {
            mergedInThisPass = false;
            var merged = new List<OCRTextRegion>();
            var processed = new bool[regions.Count];

            for (int i = 0; i < regions.Count; i++)
            {
                if (processed[i]) continue;

                var currentRegion = regions[i];
                var mergedConfidence = currentRegion.Confidence;
                var mergeCount = 1;

                for (int j = i + 1; j < regions.Count; j++)
                {
                    if (processed[j]) continue;

                    var otherRegion = regions[j];
                    var overlap = CalculateOverlap(currentRegion.Bounds, otherRegion.Bounds);

                    if (overlap >= overlapThreshold)
                    {
                        currentRegion = new OCRTextRegion(Rectangle.Union(currentRegion.Bounds, otherRegion.Bounds), currentRegion.ConfidenceScore)
                        {
                            RegionType = currentRegion.RegionType,
                            DetectionMethod = $"{currentRegion.DetectionMethod}+{otherRegion.DetectionMethod}",
                            ProcessedImage = currentRegion.ProcessedImage
                        };
                        mergedConfidence += otherRegion.Confidence;
                        mergeCount++;
                        processed[j] = true;
                        mergedInThisPass = true; // A merge occurred
                    }
                }

                currentRegion = new OCRTextRegion(currentRegion.Bounds, (float)(mergedConfidence / mergeCount))
                {
                    RegionType = currentRegion.RegionType,
                    DetectionMethod = currentRegion.DetectionMethod,
                    ProcessedImage = currentRegion.ProcessedImage
                };
                merged.Add(currentRegion);
            }

            regions = merged; // Replace the list with the newly merged list

        } while (mergedInThisPass);

        return regions;
    }

    #region Parameter Management

    public void SetParameter(string parameterName, object value)
    {
        _parameters[parameterName] = value;
        _logger.LogDebug("パラメータ設定: {ParameterName} = {Value}", parameterName, value);
    }

    public object GetParameter(string parameterName)
    {
        return _parameters.TryGetValue(parameterName, out var value) ? value : GetDefaultParameter(parameterName);
    }

    public T GetParameter<T>(string parameterName)
    {
        var value = GetParameter(parameterName);
        return value is T typedValue ? typedValue : default!;
    }

    public IReadOnlyDictionary<string, object> GetParameters()
    {
        return _parameters;
    }

    private void InitializeDefaultParameters()
    {
        _parameters["AdaptiveSensitivity"] = 0.5;
        _parameters["AdaptiveMinArea"] = 50;
        _parameters["MaxRegionsPerImage"] = 80;
        _parameters["OverlapThreshold"] = 0.25;
        _parameters["HistoryConfidenceThreshold"] = 0.3;
        _parameters["TemplateUpdateThreshold"] = 0.6;
        _parameters["MinTemplateSuccessRate"] = 0.4;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "Method returns different value types based on parameter")]
    private object GetDefaultParameter(string parameterName) => parameterName switch
    {
        "AdaptiveSensitivity" => 0.5,
        "AdaptiveMinArea" => 50,
        "MaxRegionsPerImage" => 80,
        "OverlapThreshold" => 0.25,
        "HistoryConfidenceThreshold" => 0.3,
        "TemplateUpdateThreshold" => 0.6,
        "MinTemplateSuccessRate" => 0.4,
        _ => 0.0  // doubleリテラルに統一
    };

    #endregion

    #region Profile Management

    public async Task SaveProfileAsync(string profileName)
    {
        try
        {
            var profileData = new
            {
                ProfileName = profileName,
                CreatedAt = DateTime.Now,
                Parameters = _parameters,
                Templates = _regionTemplates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            };
            
            var profilePath = $"profiles/{profileName}_adaptive_detector.json";
            var json = System.Text.Json.JsonSerializer.Serialize(profileData, JsonOptions);
            
            await System.IO.File.WriteAllTextAsync(profilePath, json).ConfigureAwait(false);
            _logger.LogInformation("プロファイル保存完了: {ProfileName} → {ProfilePath}", profileName, profilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "プロファイル保存失敗: {ProfileName}", profileName);
            throw;
        }
    }

    public async Task LoadProfileAsync(string profileName)
    {
        try
        {
            var profilePath = $"profiles/{profileName}_adaptive_detector.json";
            if (!System.IO.File.Exists(profilePath))
            {
                _logger.LogWarning("プロファイルファイルが存在しません: {ProfilePath}", profilePath);
                return;
            }
            
            var json = await System.IO.File.ReadAllTextAsync(profilePath).ConfigureAwait(false);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;
            
            // パラメータの復元
            if (root.TryGetProperty("Parameters", out var parametersElement))
            {
                _parameters.Clear();
                foreach (var parameter in parametersElement.EnumerateObject())
                {
                    _parameters[parameter.Name] = parameter.Value.GetRawText();
                }
            }
            
            _logger.LogInformation("プロファイル読み込み完了: {ProfileName}", profileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "プロファイル読み込み失敗: {ProfileName}", profileName);
        }
    }

    #endregion

    #region History and Template Management

    private void AddToHistory(DetectionHistoryEntry entry)
    {
        _detectionHistory.Enqueue(entry);
        
        // 履歴サイズの制限
        while (_detectionHistory.Count > MaxHistorySize)
        {
            _detectionHistory.TryDequeue(out _);
        }
    }

    private List<DetectionHistoryEntry> GetRecentHistory(int count)
    {
        return [.. _detectionHistory.TakeLast(count)];
    }

    private List<RegionPattern> AnalyzeSuccessPatterns(List<DetectionHistoryEntry> history)
    {
        var patterns = new List<RegionPattern>();
        
        foreach (var entry in history.Where(h => h.FinalRegionCount > 0))
        {
            foreach (var region in entry.DetectedRegions)
            {
                patterns.Add(new RegionPattern
                {
                    RelativeX = (double)region.Bounds.X / entry.ImageSize.Width,
                    RelativeY = (double)region.Bounds.Y / entry.ImageSize.Height,
                    RelativeWidth = (double)region.Bounds.Width / entry.ImageSize.Width,
                    RelativeHeight = (double)region.Bounds.Height / entry.ImageSize.Height,
                    Confidence = region.Confidence,
                    DetectionMethod = region.DetectionMethod
                });
            }
        }
        
        return patterns;
    }

    private double CalculateHistoryMatchScore(OCRTextRegion region, List<RegionPattern> patterns, IAdvancedImage image)
    {
        if (patterns.Count == 0) return 1.0;
        
        var relativeX = (double)region.Bounds.X / image.Width;
        var relativeY = (double)region.Bounds.Y / image.Height;
        var relativeWidth = (double)region.Bounds.Width / image.Width;
        var relativeHeight = (double)region.Bounds.Height / image.Height;
        
        var bestMatch = patterns.Max(pattern =>
        {
            var positionSimilarity = 1.0 - Math.Abs(relativeX - pattern.RelativeX) - Math.Abs(relativeY - pattern.RelativeY);
            var sizeSimilarity = 1.0 - Math.Abs(relativeWidth - pattern.RelativeWidth) - Math.Abs(relativeHeight - pattern.RelativeHeight);
            return Math.Max(0, (positionSimilarity + sizeSimilarity) / 2.0);
        });
        
        return Math.Max(0.5, bestMatch); // 最低0.5の基準スコア
    }

    private void UpdateRegionTemplates(List<OCRTextRegion> regions, IAdvancedImage image)
    {
        var updateThreshold = GetParameter<double>("TemplateUpdateThreshold");
        var highConfidenceRegions = regions.Where(r => r.Confidence >= updateThreshold);
        
        foreach (var region in highConfidenceRegions)
        {
            var templateKey = GenerateTemplateKey(region, image);
            
            if (_regionTemplates.TryGetValue(templateKey, out var existingTemplate))
            {
                existingTemplate.UpdateSuccess();
            }
            else
            {
                var newTemplate = new RegionTemplate
                {
                    TemplateKey = templateKey,
                    RegionPattern = new RegionPattern
                    {
                        RelativeX = (double)region.Bounds.X / image.Width,
                        RelativeY = (double)region.Bounds.Y / image.Height,
                        RelativeWidth = (double)region.Bounds.Width / image.Width,
                        RelativeHeight = (double)region.Bounds.Height / image.Height,
                        Confidence = region.Confidence,
                        DetectionMethod = region.DetectionMethod
                    }
                };
                
                _regionTemplates.TryAdd(templateKey, newTemplate);
            }
        }
        
        // 成功率の低いテンプレートを削除
        var minSuccessRate = GetParameter<double>("MinTemplateSuccessRate");
        var templatesToRemove = _regionTemplates.Where(kvp => kvp.Value.SuccessRate < minSuccessRate).ToList();
        foreach (var template in templatesToRemove)
        {
            _regionTemplates.TryRemove(template.Key, out _);
        }
    }

    private void PerformAdaptation(object? state)
    {
        try
        {
            var recentHistory = GetRecentHistory(20);
            if (recentHistory.Count < 5) return; // 最低5回の履歴が必要
            
            // パフォーマンス分析
            var avgProcessingTime = recentHistory.Average(h => h.ProcessingTimeMs);
            var avgRegionCount = recentHistory.Average(h => h.FinalRegionCount);
            
            // 適応的パラメータ調整
            if (avgProcessingTime > 1000) // 1秒以上かかっている場合
            {
                var currentSensitivity = GetParameter<double>("AdaptiveSensitivity");
                SetParameter("AdaptiveSensitivity", Math.Max(0.3, currentSensitivity - 0.1));
                _logger.LogDebug("処理時間が長いため感度を下げました: {NewSensitivity}", GetParameter<double>("AdaptiveSensitivity"));
            }
            
            if (avgRegionCount < 5) // 検出領域が少ない場合
            {
                var currentMinArea = GetParameter<int>("AdaptiveMinArea");
                SetParameter("AdaptiveMinArea", Math.Max(50, currentMinArea - 20));
                _logger.LogDebug("検出領域が少ないため最小エリアを下げました: {NewMinArea}", GetParameter<int>("AdaptiveMinArea"));
            }
            
            _logger.LogTrace("適応処理完了: 平均処理時間={AvgTime}ms, 平均領域数={AvgRegions}", 
                avgProcessingTime, avgRegionCount);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "適応処理中にエラーが発生");
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// 🔥 [ULTRATHINK_PHASE7] IAdvancedImage を IImage に変換 - PNG round-trip回避
    ///
    /// SimpleAdvancedImageAdapterの場合:
    ///   - リフレクションでGetUnderlyingBitmap()を呼び出し（アーキテクチャクリーン）
    ///   - IWindowsImageFactory.CreateFromBitmap()で直接SafeImage生成
    ///   - PNG経由の劣化を完全回避（Format24bppRgb問題解決）
    ///
    /// その他のIAdvancedImage実装:
    ///   - 既存のPNG経由変換を維持（後方互換性）
    ///
    /// アーキテクチャノート:
    ///   - Baketa.Infrastructure → Baketa.Infrastructure.Platform参照を回避
    ///   - リフレクション使用でClean Architecture原則を維持
    /// </summary>
    private async Task<IImage> ConvertAdvancedImageToImageAsync(IAdvancedImage advancedImage)
    {
        if (_imageFactory == null)
        {
            throw new InvalidOperationException("ImageFactoryが注入されていません");
        }

        try
        {
            // 🔥 [ULTRATHINK_PHASE10_DEBUG] 変換ルート診断ログ
            var actualTypeName = advancedImage.GetType().Name;
            var hasWindowsFactory = _windowsImageFactory != null;
            _logger.LogDebug("🔍 [ULTRATHINK_PHASE10_DEBUG] 変換前診断 - 型名: {ActualTypeName}, 期待型名: {ExpectedTypeName}, 一致: {TypeMatch}, WindowsFactory: {HasWindowsFactory}",
                actualTypeName, SimpleAdvancedImageAdapterTypeName, actualTypeName == SimpleAdvancedImageAdapterTypeName, hasWindowsFactory);

            // 🔥 [ULTRATHINK_PHASE7] SimpleAdvancedImageAdapterの場合はPNG round-trip回避
            // 🔥 [ULTRATHINK_PHASE9] Gemini推奨改善: 型名定数使用でリネーム脆弱性解消
            if (advancedImage.GetType().Name == SimpleAdvancedImageAdapterTypeName && _windowsImageFactory != null)
            {
                _logger.LogDebug("🔥 [ULTRATHINK_PHASE7] SimpleAdvancedImageAdapter検出 - Bitmap直接変換開始（リフレクション使用）");

                // リフレクションでGetUnderlyingBitmap()を呼び出し（アーキテクチャクリーン）
                var method = advancedImage.GetType().GetMethod("GetUnderlyingBitmap", BindingFlags.Public | BindingFlags.Instance);
                if (method != null && method.ReturnType == typeof(Bitmap))
                {
                    var bitmap = (Bitmap?)method.Invoke(advancedImage, null);
                    if (bitmap != null)
                    {
                        var windowsImage = _windowsImageFactory.CreateFromBitmap(bitmap);

                        _logger.LogDebug("✅ [ULTRATHINK_PHASE7] Bitmap直接変換成功 - Size: {Width}x{Height}",
                            windowsImage.Width, windowsImage.Height);

                        // IWindowsImageはIImageではないため、明示的にIImageとして扱う
                        // CreateFromBitmapは内部でSafeImageを生成し、SafeImageAdapterでIWindowsImageを実装
                        return (IImage)windowsImage;
                    }
                }

                // GetUnderlyingBitmap()が利用できない場合はPNG経由にフォールバック
                _logger.LogWarning("⚠️ [ULTRATHINK_PHASE7] GetUnderlyingBitmap()メソッドが利用不可 - PNG経由にフォールバック");
            }

            // 従来のPNG経由変換（その他の実装またはフォールバック）
            _logger.LogDebug("🔄 PNG経由変換開始（非SimpleAdvancedImageAdapterまたはフォールバック）");
            var imageBytes = await advancedImage.ToByteArrayAsync().ConfigureAwait(false);
            return await _imageFactory.CreateFromBytesAsync(imageBytes).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ IAdvancedImage → IImage 変換失敗");
            throw;
        }
    }

    /// <summary>
    /// 全画面フォールバック処理
    /// </summary>
    private async Task<List<OCRTextRegion>> CreateFullScreenFallbackAsync(IAdvancedImage image)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        var fullScreenRegion = new Rectangle(0, 0, image.Width, image.Height);

        if (IsRegionValid(fullScreenRegion))
        {
            _logger.LogInformation("✅ フォールバック: 画面全体を単一領域として処理（テキスト分断回避）");
            return
            [
                new OCRTextRegion
                {
                    Bounds = fullScreenRegion,
                    Confidence = 0.8, // フォールバック用の固定信頼度
                    RegionType = TextRegionType.Unknown,
                    DetectionMethod = "FullScreenFallback"
                }
            ];
        }
        else
        {
            _logger.LogWarning("⚠️ 画面全体が処理対象外サイズ - テキスト検出をスキップ");
            return [];
        }
    }

    /// <summary>
    /// 領域の妥当性チェック（設定ベース）
    /// </summary>
    private bool IsRegionValid(Rectangle rect)
    {
        var minArea = GetParameter<int>("AdaptiveMinArea");

        // 最小サイズフィルタリング
        if (rect.Width < 8 || rect.Height < 8 || rect.Width * rect.Height < minArea)
        {
            return false;
        }

        // 🔧 [PHASE_ASPECTRATIO_FIX] アスペクト比チェック（極端な縦横比を除外）
        // 修正理由: スケール復元後の横長テキスト行（1100x90px）はaspectRatio=12.2となる
        // 実測データ: 3行テキストで領域1=12.2, 領域2=12.0, 領域3=5.1
        // 旧制限10.0では正常なテキスト行が除外されるため20.0に緩和（Gemini検証済み⭐⭐⭐⭐⭐）
        float aspectRatio = (float)rect.Width / rect.Height;
        if (aspectRatio < 0.05f || aspectRatio > 20.0f)
        {
            return false;
        }

        return true;
    }

    private static string GenerateImageKey(IAdvancedImage image)
    {
        return $"{image.Width}x{image.Height}";
    }

    private static string GenerateTemplateKey(OCRTextRegion region, IAdvancedImage image)
    {
        var relativeX = (double)region.Bounds.X / image.Width;
        var relativeY = (double)region.Bounds.Y / image.Height;
        return $"{relativeX:F2}_{relativeY:F2}_{region.DetectionMethod}";
    }

    private static double CalculateOverlap(Rectangle rect1, Rectangle rect2)
    {
        var intersection = Rectangle.Intersect(rect1, rect2);
        if (intersection.IsEmpty) return 0.0;
        
        var unionArea = rect1.Width * rect1.Height + rect2.Width * rect2.Height - intersection.Width * intersection.Height;
        return unionArea > 0 ? (double)(intersection.Width * intersection.Height) / unionArea : 0.0;
    }


    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        
        _adaptationTimer?.Dispose();
        _disposed = true;
        
        _logger.LogInformation("適応的テキスト領域検出器をクリーンアップ: テンプレート数={TemplateCount}, 履歴数={HistoryCount}", 
            _regionTemplates.Count, _detectionHistory.Count);
        
        GC.SuppressFinalize(this);
    }

    ~AdaptiveTextRegionDetector()
    {
        Dispose();
    }
}

#region Supporting Classes

/// <summary>
/// 検出履歴エントリ
/// </summary>
public class DetectionHistoryEntry
{
    public int DetectionId { get; set; }
    public DateTime Timestamp { get; set; }
    public Size ImageSize { get; set; }
    public List<OCRTextRegion> DetectedRegions { get; set; } = [];
    public double ProcessingTimeMs { get; set; }
    public int TemplateMatchCount { get; set; }
    public int AdaptiveDetectionCount { get; set; }
    public int FinalRegionCount { get; set; }
}

/// <summary>
/// 領域パターン
/// </summary>
public class RegionPattern
{
    public double RelativeX { get; set; }
    public double RelativeY { get; set; }
    public double RelativeWidth { get; set; }
    public double RelativeHeight { get; set; }
    public double Confidence { get; set; }
    public string DetectionMethod { get; set; } = string.Empty;
}

/// <summary>
/// 領域テンプレート
/// </summary>
public class RegionTemplate
{
    public string TemplateKey { get; set; } = string.Empty;
    public RegionPattern RegionPattern { get; set; } = new();
    public int UsageCount { get; private set; }
    public int SuccessCount { get; private set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastUsedAt { get; set; } = DateTime.Now;
    
    public double SuccessRate => UsageCount > 0 ? (double)SuccessCount / UsageCount : 0.0;
    
    public void UpdateSuccess()
    {
        UsageCount++;
        SuccessCount++;
        LastUsedAt = DateTime.Now;
    }
    
    public void UpdateFailure()
    {
        UsageCount++;
        LastUsedAt = DateTime.Now;
    }
    
    public bool IsCompatible(int imageWidth, int imageHeight)
    {
        // 画像サイズの互換性チェック（簡素化）
        return imageWidth > 0 && imageHeight > 0;
    }
    
    public List<Rectangle> GenerateRegions(int imageWidth, int imageHeight)
    {
        var x = (int)(RegionPattern.RelativeX * imageWidth);
        var y = (int)(RegionPattern.RelativeY * imageHeight);
        var width = (int)(RegionPattern.RelativeWidth * imageWidth);
        var height = (int)(RegionPattern.RelativeHeight * imageHeight);
        
        return [new Rectangle(x, y, width, height)];
    }
}

#endregion
