using System;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.DI;
using Baketa.Infrastructure.Memory.Pools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.DI.Modules;

/// <summary>
/// オブジェクトプール関連サービスの依存関係注入モジュール
/// </summary>
public sealed class ObjectPoolModule : ServiceModuleBase
{
    public override void RegisterServices(IServiceCollection services)
    {
        // 🏊‍♂️ 画像オブジェクトプール
        services.AddSingleton<IAdvancedImagePool>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<AdvancedImagePool>>();
            return new AdvancedImagePool(logger, maxCapacity: 50);
        });

        // 📄 OCR結果オブジェクトプール（汎用型）
        // 具体的な型はサブクラスで登録

        // 📝 TextRegionオブジェクトプール
        services.AddSingleton<ITextRegionPool>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<TextRegionPool>>();
            return new TextRegionPool(logger, maxCapacity: 200);
        });

        // 🖼️ OpenCV Matオブジェクトプール
        services.AddSingleton<IObjectPool<IMatWrapper>>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<OpenCvMatPool>>();
            return new OpenCvMatPool(logger, maxCapacity: 30);
        });

        // 📊 オブジェクトプール統計レポーター（後で作成）
        services.AddSingleton<IObjectPoolStatisticsReporter, ObjectPoolStatisticsReporter>();

    }
}

/// <summary>
/// オブジェクトプール統計レポーター
/// </summary>
public interface IObjectPoolStatisticsReporter
{
    /// <summary>
    /// すべてのプールの統計情報を取得
    /// </summary>
    ObjectPoolReport GetReport();

    /// <summary>
    /// 統計情報をログに出力
    /// </summary>
    void LogStatistics();

    /// <summary>
    /// 統計情報をクリア
    /// </summary>
    void ClearStatistics();
}

/// <summary>
/// オブジェクトプール統計レポート
/// </summary>
public class ObjectPoolReport
{
    public DateTime ReportTime { get; init; } = DateTime.UtcNow;
    public ObjectPoolStatistics ImagePoolStats { get; init; } = new();
    public ObjectPoolStatistics TextRegionPoolStats { get; init; } = new();
    public ObjectPoolStatistics MatPoolStats { get; init; } = new();

    /// <summary>
    /// 全体的なメモリ効率
    /// </summary>
    public double OverallHitRate =>
        (ImagePoolStats.TotalGets + TextRegionPoolStats.TotalGets + MatPoolStats.TotalGets) > 0 ?
        (double)(ImagePoolStats.TotalGets - ImagePoolStats.TotalCreations +
                TextRegionPoolStats.TotalGets - TextRegionPoolStats.TotalCreations +
                MatPoolStats.TotalGets - MatPoolStats.TotalCreations) /
        (ImagePoolStats.TotalGets + TextRegionPoolStats.TotalGets + MatPoolStats.TotalGets) : 0.0;

    /// <summary>
    /// 回避されたオブジェクト作成数
    /// </summary>
    public long TotalObjectCreationsAvoided =>
        (ImagePoolStats.TotalGets - ImagePoolStats.TotalCreations) +
        (TextRegionPoolStats.TotalGets - TextRegionPoolStats.TotalCreations) +
        (MatPoolStats.TotalGets - MatPoolStats.TotalCreations);
}

/// <summary>
/// オブジェクトプール統計レポーター実装
/// </summary>
public sealed class ObjectPoolStatisticsReporter(
    IAdvancedImagePool imagePool,
    ITextRegionPool textRegionPool,
    IObjectPool<IMatWrapper> matPool,
    ILogger<ObjectPoolStatisticsReporter> logger) : IObjectPoolStatisticsReporter
{
    private readonly IAdvancedImagePool _imagePool = imagePool ?? throw new ArgumentNullException(nameof(imagePool));
    private readonly ITextRegionPool _textRegionPool = textRegionPool ?? throw new ArgumentNullException(nameof(textRegionPool));
    private readonly IObjectPool<IMatWrapper> _matPool = matPool ?? throw new ArgumentNullException(nameof(matPool));
    private readonly ILogger<ObjectPoolStatisticsReporter> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public ObjectPoolReport GetReport()
    {
        return new ObjectPoolReport
        {
            ImagePoolStats = _imagePool.Statistics,
            TextRegionPoolStats = _textRegionPool.Statistics,
            MatPoolStats = _matPool.Statistics
        };
    }

    public void LogStatistics()
    {
        var report = GetReport();

        _logger.LogInformation("📊 Object Pool Performance Report ({ReportTime:yyyy-MM-dd HH:mm:ss}):\n" +
            "  🌟 Overall Efficiency: HitRate={OverallHitRate:P1}, ObjectsAvoided={ObjectsAvoided}\n" +
            "  📸 ImagePool: {ImageStats}\n" +
            "  📝 TextRegionPool: {TextStats}\n" +
            "  🖼️ MatPool: {MatStats}\n" +
            "  💡 Memory Impact: {ObjectsAvoided} fewer object allocations = reduced GC pressure",
            report.ReportTime,
            report.OverallHitRate, report.TotalObjectCreationsAvoided,
            report.ImagePoolStats,
            report.TextRegionPoolStats,
            report.MatPoolStats,
            report.TotalObjectCreationsAvoided);
    }

    public void ClearStatistics()
    {
        _logger.LogInformation("🧹 Clearing all object pool statistics");

        _imagePool.Statistics.Clear();
        _textRegionPool.Statistics.Clear();
        _matPool.Statistics.Clear();

        _logger.LogInformation("✅ All object pool statistics cleared");
    }
}
