using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.TranslationModes;

/// <summary>
/// シングルショット翻訳モード（単発実行型）
/// ユーザーがボタンを押したタイミングで1回だけ翻訳を実行
/// </summary>
public sealed class SingleshotTranslationMode(ILogger<SingleshotTranslationMode> logger) : TranslationModeBase(logger)
{
    /// <inheritdoc />
    public override Core.Abstractions.Services.TranslationMode Mode =>
        Core.Abstractions.Services.TranslationMode.Singleshot;

    /// <inheritdoc />
    public override Task EnterAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("📸 Singleshotモード開始 - 単発実行モードに切り替えます");
        return base.EnterAsync(cancellationToken);
    }

    /// <inheritdoc />
    public override Task ExitAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("✅ Singleshotモード終了");
        return base.ExitAsync(cancellationToken);
    }
}
