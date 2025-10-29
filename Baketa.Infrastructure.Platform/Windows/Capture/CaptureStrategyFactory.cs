using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Models.Capture;
using Baketa.Core.Abstractions.GPU;
using Baketa.Infrastructure.Platform.Windows.Capture.Strategies;
// 🔥 [PHASE_K-29-G] CaptureOptions統合: CaptureStrategyUsedのみ使用（CaptureOptionsは不使用）

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

            // 🔥 [FIX7_PHASE2] 各戦略のCanApply結果を詳細ログ出力
            foreach (var strategy in strategies)
            {
                var canApply = strategy.CanApply(environment, hwnd);
                _logger.LogDebug("🔍 [FIX7_PHASE2] 戦略適用チェック: {StrategyName} → CanApply={CanApply}",
                    strategy.StrategyName, canApply);

                if (canApply)
                {
                    _logger.LogInformation("✅ [FIX7_PHASE2] 戦略選択完了: {StrategyName} (Priority={Priority})",
                        strategy.StrategyName, strategy.Priority);
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
            // 🎯 [PRIMARY_STRATEGY_FIX] primaryStrategy を最優先で確保
            ICaptureStrategy? reservedPrimary = null;
            if (primaryStrategy != null)
            {
                reservedPrimary = primaryStrategy;
                _logger.LogDebug("primaryStrategy予約: {StrategyName} (Priority: {Priority})", 
                    primaryStrategy.StrategyName, primaryStrategy.Priority);
            }

            // フォールバック戦略を優先順位順に追加（統合GPU優先の設計）
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
                if (strategy != null)
                {
                    strategies.Add(strategy);
                }
            }

            // フォールバック戦略を優先度でソート
            strategies.Sort((a, b) => b.Priority.CompareTo(a.Priority));

            // 🔥 [FIX7_PHASE2] ソート後の戦略優先順位を明確にログ出力
            _logger.LogInformation("🎯 [FIX7_PHASE2] 戦略優先順位（降順ソート後）: [{StrategiesByPriority}]",
                string.Join(", ", strategies.Select(s => $"{s.StrategyName}(P:{s.Priority})")));

            // 🎯 [PRIMARY_FIRST] primaryStrategyを最優先に配置
            if (reservedPrimary != null)
            {
                // primaryStrategyと同じ戦略をフォールバックから除去（重複回避）
                strategies.RemoveAll(s => s.StrategyName == reservedPrimary.StrategyName);
                
                // primaryStrategyを最優先に配置
                strategies.Insert(0, reservedPrimary);
                
                _logger.LogDebug("🎯 primaryStrategy最優先配置完了: {PrimaryName} → フォールバック: [{FallbackStrategies}]", 
                    reservedPrimary.StrategyName, 
                    string.Join(", ", strategies.Skip(1).Select(s => s.StrategyName)));
            }
            else
            {
                _logger.LogDebug("primaryStrategy未指定 - 優先度順: [{StrategiesByPriority}]", 
                    string.Join(", ", strategies.Select(s => $"{s.StrategyName}({s.Priority})")));
            }

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