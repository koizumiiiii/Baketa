using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.TranslationModes;

/// <summary>
/// 翻訳モード管理サービス実装
/// State Patternを使用してLive翻訳とSingleshot翻訳のモード切り替えを管理
/// </summary>
public sealed class TranslationModeService : ITranslationModeService, IDisposable
{
    private readonly LiveTranslationMode _liveMode;
    private readonly SingleshotTranslationMode _singleshotMode;
    private readonly ILogger<TranslationModeService> _logger;
    private readonly SemaphoreSlim _modeSwitchLock = new(1, 1);

    private TranslationModeBase? _currentModeState;
    private Core.Abstractions.Services.TranslationMode _currentMode = Core.Abstractions.Services.TranslationMode.None;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="liveMode">Live翻訳モード</param>
    /// <param name="singleshotMode">Singleshotモード</param>
    /// <param name="logger">ロガー</param>
    public TranslationModeService(
        LiveTranslationMode liveMode,
        SingleshotTranslationMode singleshotMode,
        ILogger<TranslationModeService> logger)
    {
        _liveMode = liveMode ?? throw new ArgumentNullException(nameof(liveMode));
        _singleshotMode = singleshotMode ?? throw new ArgumentNullException(nameof(singleshotMode));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Core.Abstractions.Services.TranslationMode CurrentMode => _currentMode;

    /// <inheritdoc />
    public event EventHandler<Core.Abstractions.Services.TranslationMode>? ModeChanged;

    /// <inheritdoc />
    public async Task SwitchToLiveModeAsync(CancellationToken cancellationToken = default)
    {
        await SwitchModeAsync(_liveMode, Core.Abstractions.Services.TranslationMode.Live, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SwitchToSingleshotModeAsync(CancellationToken cancellationToken = default)
    {
        await SwitchModeAsync(_singleshotMode, Core.Abstractions.Services.TranslationMode.Singleshot, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResetModeAsync(CancellationToken cancellationToken = default)
    {
        await _modeSwitchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 現在のモードを終了
            if (_currentModeState != null)
            {
                await _currentModeState.ExitAsync(cancellationToken).ConfigureAwait(false);
                _currentModeState = null;
            }

            _currentMode = Core.Abstractions.Services.TranslationMode.None;
            _logger.LogInformation("🔄 翻訳モードをリセット: None");

            // イベント発行
            ModeChanged?.Invoke(this, _currentMode);
        }
        finally
        {
            _modeSwitchLock.Release();
        }
    }

    /// <summary>
    /// モード切り替えの共通処理
    /// </summary>
    private async Task SwitchModeAsync(
        TranslationModeBase newModeState,
        Core.Abstractions.Services.TranslationMode newMode,
        CancellationToken cancellationToken)
    {
        await _modeSwitchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 既に同じモードの場合はスキップ
            if (_currentMode == newMode)
            {
                _logger.LogDebug("既に{Mode}モードです - スキップ", newMode);
                return;
            }

            // 現在のモードを終了
            if (_currentModeState != null)
            {
                await _currentModeState.ExitAsync(cancellationToken).ConfigureAwait(false);
            }

            // 新しいモードに切り替え
            _currentModeState = newModeState;
            _currentMode = newMode;
            await _currentModeState.EnterAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("🔄 翻訳モード切り替え完了: {NewMode}", newMode);

            // イベント発行
            ModeChanged?.Invoke(this, _currentMode);
        }
        finally
        {
            _modeSwitchLock.Release();
        }
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        _modeSwitchLock?.Dispose();
    }
}
