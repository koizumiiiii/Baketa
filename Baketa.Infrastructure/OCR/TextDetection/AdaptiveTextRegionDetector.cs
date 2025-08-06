using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.OCR.TextDetection;
using Baketa.Core.Abstractions.Imaging;
using OCRTextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;
using Timer = System.Threading.Timer;

namespace Baketa.Infrastructure.OCR.TextDetection;

/// <summary>
/// 適応的テキスト領域検出器 - 履歴ベース最適化と動的パラメータ調整
/// 1-B2: テキスト領域検出高度化の実装
/// </summary>
public sealed class AdaptiveTextRegionDetector : ITextRegionDetector, IDisposable
{
    private readonly ILogger<AdaptiveTextRegionDetector> _logger;
    private readonly Dictionary<string, object> _parameters = [];
    private readonly ConcurrentQueue<DetectionHistoryEntry> _detectionHistory = [];
    private readonly ConcurrentDictionary<string, RegionTemplate> _regionTemplates = [];
    private readonly Timer _adaptationTimer;
    
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private bool _disposed;
    private int _detectionCount;
    private const int MaxHistorySize = 100;
    private const int AdaptationIntervalMs = 5000; // 5秒間隔で適応
    
    public string Name => "AdaptiveTextRegionDetector";
    public string Description => "適応的テキスト領域検出器 - 履歴ベース最適化と動的調整";
    public TextDetectionMethod Method => TextDetectionMethod.Adaptive;

    public AdaptiveTextRegionDetector(ILogger<AdaptiveTextRegionDetector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        InitializeDefaultParameters();
        
        // 定期的な適応処理を開始
        _adaptationTimer = new Timer(PerformAdaptation, null, 
            TimeSpan.FromMilliseconds(AdaptationIntervalMs), 
            TimeSpan.FromMilliseconds(AdaptationIntervalMs));
            
        _logger.LogInformation("適応的テキスト領域検出器を初期化");
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
        var regions = new List<OCRTextRegion>();
        
        await Task.Run(() =>
        {
            // 適応的パラメータの取得
            var sensitivity = GetParameter<double>("AdaptiveSensitivity");
            var minArea = GetParameter<int>("AdaptiveMinArea");
            var maxRegions = GetParameter<int>("MaxRegionsPerImage");
            
            // エッジベース検出（適応的感度）
            var edgeRegions = DetectEdgeBasedRegions(image, sensitivity);
            
            // 輝度変化ベース検出
            var luminanceRegions = DetectLuminanceBasedRegions(image, minArea);
            
            // テクスチャベース検出
            var textureRegions = DetectTextureBasedRegions(image);
            
            // 結果の統合と重複除去
            List<OCRTextRegion> allRegions = [.. edgeRegions, .. luminanceRegions, .. textureRegions];
            var uniqueRegions = MergeOverlappingRegions(allRegions);
            
            // 上位候補のみを選択
            regions.AddRange(uniqueRegions
                .OrderByDescending(r => r.Confidence)
                .Take(maxRegions));
                
            _logger.LogDebug("適応的パラメータ検出完了: {RegionCount}個の領域（エッジ:{Edge}, 輝度:{Luminance}, テクスチャ:{Texture}）", 
                regions.Count, edgeRegions.Count, luminanceRegions.Count, textureRegions.Count);
        }, cancellationToken).ConfigureAwait(false);
        
        return regions;
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
    /// エッジベース領域検出
    /// </summary>
    private List<OCRTextRegion> DetectEdgeBasedRegions(IAdvancedImage image, double sensitivity)
    {
        var regions = new List<OCRTextRegion>();
        
        // 簡素化されたエッジ検出アルゴリズム
        var gridSize = Math.Max(20, Math.Min(image.Width, image.Height) / 20);
        var threshold = sensitivity * 100;
        
        for (int y = 0; y < image.Height - gridSize; y += gridSize / 2)
        {
            for (int x = 0; x < image.Width - gridSize; x += gridSize / 2)
            {
                var region = new Rectangle(x, y, gridSize, gridSize);
                var confidence = CalculateEdgeConfidence(image, region, threshold);
                
                if (confidence > 0.2) // 閾値を下げて文字列領域をより広く検出
                {
                    regions.Add(new OCRTextRegion
                    {
                        Bounds = region,
                        Confidence = confidence,
                        RegionType = TextRegionType.Edge,
                        DetectionMethod = "EdgeBased"
                    });
                }
            }
        }
        
        return regions;
    }

    /// <summary>
    /// 輝度変化ベース領域検出
    /// </summary>
    private List<OCRTextRegion> DetectLuminanceBasedRegions(IAdvancedImage image, int minArea)
    {
        var regions = new List<OCRTextRegion>();
        
        // 輝度変化の激しい領域を検出（テキストの特徴）
        var blockSize = Math.Max(15, Math.Min(image.Width, image.Height) / 30);
        
        for (int y = 0; y < image.Height - blockSize; y += blockSize)
        {
            for (int x = 0; x < image.Width - blockSize; x += blockSize)
            {
                var region = new Rectangle(x, y, blockSize, blockSize);
                if (region.Width * region.Height < minArea) continue;
                
                var luminanceVariance = CalculateLuminanceVariance(image, region);
                var confidence = Math.Min(1.0, luminanceVariance / 50.0); // 正規化
                
                if (confidence > 0.25)
                {
                    regions.Add(new OCRTextRegion
                    {
                        Bounds = region,
                        Confidence = confidence,
                        RegionType = TextRegionType.Luminance,
                        DetectionMethod = "LuminanceBased"
                    });
                }
            }
        }
        
        return regions;
    }

    /// <summary>
    /// テクスチャベース領域検出
    /// </summary>
    private List<OCRTextRegion> DetectTextureBasedRegions(IAdvancedImage image)
    {
        var regions = new List<OCRTextRegion>();
        
        // テキストの特徴的なテクスチャパターンを検出
        var patternSize = Math.Max(25, Math.Min(image.Width, image.Height) / 25);
        
        for (int y = 0; y < image.Height - patternSize; y += patternSize)
        {
            for (int x = 0; x < image.Width - patternSize; x += patternSize)
            {
                var region = new Rectangle(x, y, patternSize, patternSize);
                var textureScore = CalculateTextureScore(image, region);
                
                if (textureScore > 0.35)
                {
                    regions.Add(new OCRTextRegion
                    {
                        Bounds = region,
                        Confidence = textureScore,
                        RegionType = TextRegionType.Texture,
                        DetectionMethod = "TextureBased"
                    });
                }
            }
        }
        
        return regions;
    }

    /// <summary>
    /// 重複する領域をマージ
    /// </summary>
    private List<OCRTextRegion> MergeOverlappingRegions(List<OCRTextRegion> regions)
    {
        if (regions.Count <= 1) return regions;
        
        var merged = new List<OCRTextRegion>();
        var processed = new bool[regions.Count];
        var overlapThreshold = GetParameter<double>("OverlapThreshold");
        
        for (int i = 0; i < regions.Count; i++)
        {
            if (processed[i]) continue;
            
            var currentRegion = regions[i];
            processed[i] = true;
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
                }
            }
            
            // 平均信頼度を計算
            currentRegion = new OCRTextRegion(currentRegion.Bounds, (float)(mergedConfidence / mergeCount))
            {
                RegionType = currentRegion.RegionType,
                DetectionMethod = currentRegion.DetectionMethod,
                ProcessedImage = currentRegion.ProcessedImage
            };
            merged.Add(currentRegion);
        }
        
        return merged;
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

    private static double CalculateEdgeConfidence(IAdvancedImage image, Rectangle region, double threshold)
    {
        // 簡素化されたエッジ密度計算（スタブ実装）
        _ = image; _ = region; _ = threshold;
        return Random.Shared.NextDouble() * 0.8 + 0.2;
    }

    private static double CalculateLuminanceVariance(IAdvancedImage image, Rectangle region)
    {
        // 簡素化された輝度分散計算（スタブ実装）
        _ = image; _ = region;
        return Random.Shared.NextDouble() * 60 + 20;
    }

    private static double CalculateTextureScore(IAdvancedImage image, Rectangle region)
    {
        // 簡素化されたテクスチャスコア計算（スタブ実装）
        _ = image; _ = region;
        return Random.Shared.NextDouble() * 0.7 + 0.3;
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
