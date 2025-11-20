using System;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// 翻訳モード管理サービス
/// State Patternを使用してLive翻訳とSingleshot翻訳のモードを管理
/// </summary>
public interface ITranslationModeService
{
    /// <summary>
    /// 現在の翻訳モード
    /// </summary>
    TranslationMode CurrentMode { get; }

    /// <summary>
    /// モード変更イベント
    /// </summary>
    event EventHandler<TranslationMode>? ModeChanged;

    /// <summary>
    /// Live翻訳モードに切り替え
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>非同期タスク</returns>
    Task SwitchToLiveModeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// シングルショットモードに切り替え
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>非同期タスク</returns>
    Task SwitchToSingleshotModeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// モード未設定状態に戻す（停止時）
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>非同期タスク</returns>
    Task ResetModeAsync(CancellationToken cancellationToken = default);
}
