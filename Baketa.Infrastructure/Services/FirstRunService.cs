using System;
using System.IO;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// 初回起動判定サービス
/// </summary>
public interface IFirstRunService
{
    /// <summary>
    /// 初回起動かどうかを判定
    /// </summary>
    bool IsFirstRun();

    /// <summary>
    /// 初回起動フラグをクリア（2回目以降の起動として記録）
    /// </summary>
    void MarkAsRun();
}

/// <summary>
/// 初回起動判定サービスの実装
/// </summary>
public class FirstRunService : IFirstRunService
{
    private readonly ILogger<FirstRunService> _logger;
    private readonly string _flagFilePath;

    public FirstRunService(ILogger<FirstRunService> logger)
    {
        _logger = logger;

        // [Issue #459] BaketaSettingsPaths経由に統一
        _flagFilePath = BaketaSettingsPaths.FirstRunFlagPath;

        _logger.LogDebug("FirstRunService initialized. Flag file path: {FlagFilePath}", _flagFilePath);
    }

    public bool IsFirstRun()
    {
        var isFirst = !File.Exists(_flagFilePath);
        _logger.LogInformation("🔍 Initial startup check: {IsFirstRun}", isFirst ? "First run" : "Not first run");
        return isFirst;
    }

    public void MarkAsRun()
    {
        try
        {
            var directory = Path.GetDirectoryName(_flagFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_flagFilePath, $"First run completed at {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            _logger.LogInformation("✅ First run flag created: {FlagFilePath}", _flagFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Failed to create first run flag file: {FlagFilePath}", _flagFilePath);
        }
    }
}
