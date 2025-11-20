using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.TranslationModes;

/// <summary>
/// Live翻訳モード（常時監視型）
/// 画面変化を監視し続けて自動的に翻訳を実行
/// </summary>
public sealed class LiveTranslationMode(ILogger<LiveTranslationMode> logger) : TranslationModeBase(logger)
{
    /// <inheritdoc />
    public override Core.Abstractions.Services.TranslationMode Mode =>
        Core.Abstractions.Services.TranslationMode.Live;

    /// <inheritdoc />
    public override Task EnterAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("🔄 Live翻訳モード開始 - 常時監視を開始します");
        return base.EnterAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task ExitAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("⏸️ Live翻訳モード終了 - 常時監視を停止します");
        return base.ExitAsync(cancellationToken);
    }
}
