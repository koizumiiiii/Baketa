using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Models.Capture;
using Baketa.Core.Abstractions.GPU;
using Baketa.Infrastructure.Platform.Windows.Capture.Strategies;

namespace Baketa.Infrastructure.Platform.Windows.Capture;

/// <summary>
/// キャプチャ戦略ファクトリーの実装
/// </summary>
public class CaptureStrategyFactory : ICaptureStrategyFactory
{
    private readonly ILogger<CaptureStrategyFactory> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<CaptureStrategyUsed, Func<ICaptureStrategy>> _strategyCreators;

    public CaptureStrategyFactory(
        ILogger<CaptureStrategyFactory> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        
        // 戦略作成関数の初期化
        _strategyCreators = InitializeStrategyCreators();
    }

    public ICaptureStrategy GetOptimalStrategy(GpuEnvironmentInfo environment, IntPtr hwnd)
    {
        try
        {
            _logger.LogDebug("最適戦略選択開始: GPU={GpuName}, 統合={IsIntegrated}, 専用={IsDedicated}", 
                environment.GpuName, environment.IsIntegratedGpu, environment.IsDedicatedGpu);

            var strategies = GetStrategiesInOrder();
            
            foreach (var strategy in strategies)
            {
                if (strategy.CanApply(environment, hwnd))
                {
                    _logger.LogInformation("戦略選択: {StrategyName}", strategy.StrategyName);
                    return strategy;
                }
            }

            // フォールバック戦略（常に利用可能）
            var fallbackStrategy = GetStrategy(CaptureStrategyUsed.PrintWindowFallback);
            if (fallbackStrategy != null)
            {
                _logger.LogWarning("すべての戦略が不適用のため、フォールバック戦略を使用");
                return fallbackStrategy;
            }

            throw new InvalidOperationException("利用可能なキャプチャ戦略が見つかりません");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "最適戦略選択中にエラー");
            throw;
        }
    }

    public IList<ICaptureStrategy> GetStrategiesInOrder(ICaptureStrategy? primaryStrategy = null)
    {
        var strategies = new List<ICaptureStrategy>();

        try
        {
            // プライマリ戦略が指定されている場合は最優先
            if (primaryStrategy != null)
            {
                strategies.Add(primaryStrategy);
            }

            // 優先順位順に戦略を追加（統合GPU優先の設計）
            var strategyTypes = new[]
            {
                CaptureStrategyUsed.DirectFullScreen,   // 統合GPU向け（最高効率）
                CaptureStrategyUsed.ROIBased,          // 専用GPU向け（バランス）
                CaptureStrategyUsed.PrintWindowFallback, // 確実動作保証
                CaptureStrategyUsed.GDIFallback        // 最終手段
            };

            foreach (var strategyType in strategyTypes)
            {
                var strategy = GetStrategy(strategyType);
                if (strategy != null && !strategies.Any(s => s.StrategyName == strategy.StrategyName))
                {
                    strategies.Add(strategy);
                }
            }

            // 優先度でソート
            strategies.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            _logger.LogDebug("戦略順序生成完了: {StrategyCount}個の戦略", strategies.Count);
            return strategies;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "戦略順序生成中にエラー");
            return [];
        }
    }

    public ICaptureStrategy? GetStrategy(CaptureStrategyUsed strategyType)
    {
        try
        {
            if (_strategyCreators.TryGetValue(strategyType, out var creator))
            {
                return creator();
            }

            _logger.LogWarning("未サポートの戦略タイプ: {StrategyType}", strategyType);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "戦略生成中にエラー: {StrategyType}", strategyType);
            return null;
        }
    }

    public IList<CaptureStrategyUsed> GetAvailableStrategyTypes()
    {
        return [.. _strategyCreators.Keys];
    }

    public async Task<bool> ValidateStrategyAsync(ICaptureStrategy strategy, GpuEnvironmentInfo environment, IntPtr hwnd)
    {
        try
        {
            _logger.LogDebug("戦略検証開始: {StrategyName}", strategy.StrategyName);

            // 基本的な適用可能性チェック
            if (!strategy.CanApply(environment, hwnd))
            {
                _logger.LogDebug("戦略適用不可: {StrategyName}", strategy.StrategyName);
                return false;
            }

            // 事前条件チェック
            var prerequisitesValid = await strategy.ValidatePrerequisitesAsync(hwnd).ConfigureAwait(false);
            if (!prerequisitesValid)
            {
                _logger.LogDebug("戦略事前条件不満足: {StrategyName}", strategy.StrategyName);
                return false;
            }

            _logger.LogDebug("戦略検証成功: {StrategyName}", strategy.StrategyName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "戦略検証中にエラー: {StrategyName}", strategy.StrategyName);
            return false;
        }
    }

    private Dictionary<CaptureStrategyUsed, Func<ICaptureStrategy>> InitializeStrategyCreators()
    {
        return new Dictionary<CaptureStrategyUsed, Func<ICaptureStrategy>>
        {
            [CaptureStrategyUsed.DirectFullScreen] = () => 
                _serviceProvider.GetService(typeof(DirectFullScreenCaptureStrategy)) as ICaptureStrategy ?? 
                throw new InvalidOperationException("DirectFullScreenCaptureStrategy が登録されていません"),
                
            [CaptureStrategyUsed.ROIBased] = () => 
                _serviceProvider.GetService(typeof(ROIBasedCaptureStrategy)) as ICaptureStrategy ?? 
                throw new InvalidOperationException("ROIBasedCaptureStrategy が登録されていません"),
                
            [CaptureStrategyUsed.PrintWindowFallback] = () => 
                _serviceProvider.GetService(typeof(PrintWindowFallbackStrategy)) as ICaptureStrategy ?? 
                throw new InvalidOperationException("PrintWindowFallbackStrategy が登録されていません"),
                
            [CaptureStrategyUsed.GDIFallback] = () => 
                _serviceProvider.GetService(typeof(GDIFallbackStrategy)) as ICaptureStrategy ?? 
                throw new InvalidOperationException("GDIFallbackStrategy が登録されていません")
        };
    }
}