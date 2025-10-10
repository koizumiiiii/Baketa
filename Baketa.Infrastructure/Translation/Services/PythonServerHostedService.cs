using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// Pythonサーバーをアプリケーション起動時に事前起動するHostedService
/// </summary>
internal sealed class PythonServerHostedService : IHostedService
{
    private readonly IPythonServerManager _serverManager;
    private readonly ILogger<PythonServerHostedService> _logger;

    public PythonServerHostedService(
        IPythonServerManager serverManager,
        ILogger<PythonServerHostedService> logger)
    {
        _serverManager = serverManager ?? throw new ArgumentNullException(nameof(serverManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// アプリケーション起動時にPythonサーバーを起動
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("🚀 [PYTHON_SERVER_STARTUP] Pythonサーバー事前起動開始");
            Console.WriteLine("🚀 [PYTHON_SERVER_STARTUP] Pythonサーバー事前起動開始");

            // デフォルト言語ペア（ja-en）でサーバーを起動
            await _serverManager.StartServerAsync("ja-en").ConfigureAwait(false);

            _logger.LogInformation("✅ [PYTHON_SERVER_STARTUP] Pythonサーバー事前起動完了");
            Console.WriteLine("✅ [PYTHON_SERVER_STARTUP] Pythonサーバー事前起動完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PYTHON_SERVER_STARTUP] Pythonサーバー事前起動失敗");
            Console.WriteLine($"❌ [PYTHON_SERVER_STARTUP] Pythonサーバー事前起動失敗: {ex.Message}");
            // エラーが発生しても、アプリケーション起動は継続させる
        }
    }

    /// <summary>
    /// アプリケーション終了時にPythonサーバーを停止
    /// </summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("🛑 [PYTHON_SERVER_SHUTDOWN] Pythonサーバー停止開始");
            Console.WriteLine("🛑 [PYTHON_SERVER_SHUTDOWN] Pythonサーバー停止開始");

            await _serverManager.StopServerAsync("ja-en").ConfigureAwait(false);

            _logger.LogInformation("✅ [PYTHON_SERVER_SHUTDOWN] Pythonサーバー停止完了");
            Console.WriteLine("✅ [PYTHON_SERVER_SHUTDOWN] Pythonサーバー停止完了");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [PYTHON_SERVER_SHUTDOWN] Pythonサーバー停止時にエラー発生");
            Console.WriteLine($"⚠️ [PYTHON_SERVER_SHUTDOWN] Pythonサーバー停止時にエラー発生: {ex.Message}");
        }
    }
}
