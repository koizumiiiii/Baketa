using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.TranslationModes;

/// <summary>
/// 翻訳モード基底クラス（State Pattern）
/// </summary>
public abstract class TranslationModeBase(ILogger logger)
{
    /// <summary>
    /// ロガー
    /// </summary>
    protected ILogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// このモードの種類
    /// </summary>
    public abstract Core.Abstractions.Services.TranslationMode Mode { get; }

    /// <summary>
    /// モード開始時の処理
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>非同期タスク</returns>
    public virtual Task EnterAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("翻訳モード開始: {Mode}", Mode);
        return Task.CompletedTask;
    }

    /// <summary>
    /// モード終了時の処理
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>非同期タスク</returns>
    public virtual Task ExitAsync(CancellationToken cancellationToken = default)
    {
        Logger.LogInformation("翻訳モード終了: {Mode}", Mode);
        return Task.CompletedTask;
    }
}
