using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Settings;

namespace Baketa.Core.Settings.Handlers;

/// <summary>
/// αテスト用設定ハンドラーの基底クラス
/// </summary>
public abstract class AlphaTestSettingsHandlerBase(ILogger logger) : ISettingsHandler
{
    protected ILogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public abstract int Priority { get; }

    /// <inheritdoc />
    public abstract IReadOnlyList<string> HandledCategories { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public virtual bool CanHandle(string category)
    {
        return HandledCategories.Contains(category);
    }

    /// <inheritdoc />
    public abstract Task<SettingsApplicationResult> ApplySettingsAsync(AppSettings oldSettings, AppSettings newSettings, string? changedCategory = null);

    /// <inheritdoc />
    public abstract Task<SettingsApplicationResult> RollbackSettingsAsync(AppSettings currentSettings, AppSettings previousSettings, string? changedCategory = null);

    /// <inheritdoc />
    public virtual Task<SettingsValidationResult> ValidateChangesAsync(AppSettings oldSettings, AppSettings newSettings, string? changedCategory = null)
    {
        // デフォルトでは検証なし（各ハンドラーで必要に応じてオーバーライド）
        return Task.FromResult(SettingsValidationResult.CreateSuccess());
    }

    /// <summary>
    /// 実行時間を測定してアクションを実行します
    /// </summary>
    protected async Task<SettingsApplicationResult> ExecuteWithTimingAsync(Func<Task<SettingsApplicationResult>> action, string category)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await action().ConfigureAwait(false);
            stopwatch.Stop();
            
            Logger.LogDebug("{HandlerName}による{Category}設定適用完了: {ElapsedMs}ms", Name, category, stopwatch.ElapsedMilliseconds);
            
            return new SettingsApplicationResult(
                result.IsSuccess,
                result.ErrorMessage,
                result.WarningMessage,
                category,
                stopwatch.ElapsedMilliseconds,
                result.RequiresRestart,
                result.AdditionalInfo);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Logger.LogError(ex, "{HandlerName}による{Category}設定適用中にエラーが発生: {ElapsedMs}ms", Name, category, stopwatch.ElapsedMilliseconds);
            
            return new SettingsApplicationResult(
                false,
                $"設定適用中にエラーが発生しました: {ex.Message}",
                null,
                category,
                stopwatch.ElapsedMilliseconds);
        }
    }
}

/// <summary>
/// αテスト翻訳設定ハンドラー
/// 翻訳エンジン設定の適用を処理
/// </summary>
public sealed class AlphaTestTranslationSettingsHandler(ILogger<AlphaTestTranslationSettingsHandler> logger) : AlphaTestSettingsHandlerBase(logger)
{
    public override int Priority => 10; // 高優先度
    public override IReadOnlyList<string> HandledCategories => ["Translation"];
    public override string Name => "αテスト翻訳設定ハンドラー";
    public override string Description => "αテスト用翻訳エンジン設定の適用とロールバック";

    public override Task<SettingsApplicationResult> ApplySettingsAsync(AppSettings oldSettings, AppSettings newSettings, string? changedCategory = null)
    {
        return ExecuteWithTimingAsync(async () =>
        {
            // エンジン変更の検出
            if (oldSettings.Translation.DefaultEngine != newSettings.Translation.DefaultEngine)
            {
                Logger.LogInformation("翻訳エンジンを変更: {OldEngine} → {NewEngine}", 
                    oldSettings.Translation.DefaultEngine, newSettings.Translation.DefaultEngine);
                
                // αテストではNLLB-200のみなので実際のエンジン切り替えは不要
                // ただし設定変更を記録
                await Task.Delay(50).ConfigureAwait(false); // 設定適用のシミュレート
                
                return SettingsApplicationResult.Success("Translation", 
                    "翻訳エンジン設定を適用しました", requiresRestart: false);
            }

            // 言語ペア変更の検出
            if (oldSettings.Translation.DefaultSourceLanguage != newSettings.Translation.DefaultSourceLanguage ||
                oldSettings.Translation.DefaultTargetLanguage != newSettings.Translation.DefaultTargetLanguage)
            {
                Logger.LogInformation("言語ペアを変更: {OldPair} → {NewPair}", 
                    $"{oldSettings.Translation.DefaultSourceLanguage}→{oldSettings.Translation.DefaultTargetLanguage}",
                    $"{newSettings.Translation.DefaultSourceLanguage}→{newSettings.Translation.DefaultTargetLanguage}");
                
                // 言語モデルの準備（シミュレート）
                await Task.Delay(100).ConfigureAwait(false);
                
                return SettingsApplicationResult.Success("Translation", 
                    "言語ペア設定を適用しました");
            }

            return SettingsApplicationResult.Success("Translation");
        }, "Translation");
    }

    public override Task<SettingsApplicationResult> RollbackSettingsAsync(AppSettings currentSettings, AppSettings previousSettings, string? changedCategory = null)
    {
        return ExecuteWithTimingAsync(async () =>
        {
            Logger.LogInformation("翻訳設定をロールバック中");
            
            // ロールバック処理（シミュレート）
            await Task.Delay(30).ConfigureAwait(false);
            
            return SettingsApplicationResult.Success("Translation", "翻訳設定をロールバックしました");
        }, "Translation");
    }
}

/// <summary>
/// αテストUI設定ハンドラー
/// フォント・透明度の即時適用を処理
/// </summary>
public sealed class AlphaTestUISettingsHandler(ILogger<AlphaTestUISettingsHandler> logger) : AlphaTestSettingsHandlerBase(logger)
{
    public override int Priority => 20; // 中優先度
    public override IReadOnlyList<string> HandledCategories => ["MainUi", "Overlay"];
    public override string Name => "αテストUI設定ハンドラー";
    public override string Description => "αテスト用UI設定の即時適用";

    public override Task<SettingsApplicationResult> ApplySettingsAsync(AppSettings oldSettings, AppSettings newSettings, string? changedCategory = null)
    {
        return ExecuteWithTimingAsync(async () =>
        {
            var changed = false;
            var warnings = new List<string>();

            // パネルサイズ変更の検出（フォントサイズの代わり）
            if (oldSettings.MainUi.PanelSize != newSettings.MainUi.PanelSize)
            {
                Logger.LogInformation("パネルサイズを変更: {OldSize} → {NewSize}", 
                    oldSettings.MainUi.PanelSize, newSettings.MainUi.PanelSize);
                
                // パネルサイズ適用（シミュレート）
                await Task.Delay(20).ConfigureAwait(false);
                changed = true;
            }

            // 透明度変更（MainUiのPanelOpacityを使用）
            if (Math.Abs(oldSettings.MainUi.PanelOpacity - newSettings.MainUi.PanelOpacity) > 0.01)
            {
                Logger.LogInformation("パネル透明度を変更: {OldOpacity:F1}% → {NewOpacity:F1}%", 
                    oldSettings.MainUi.PanelOpacity * 100, newSettings.MainUi.PanelOpacity * 100);
                
                // 透明度適用（シミュレート）
                await Task.Delay(10).ConfigureAwait(false);
                changed = true;

                // 極端な値の警告
                if (newSettings.MainUi.PanelOpacity < 0.2 || newSettings.MainUi.PanelOpacity > 0.8)
                {
                    warnings.Add($"透明度{newSettings.MainUi.PanelOpacity * 100:F1}%は推奨範囲外です");
                }
            }

            if (!changed)
            {
                return SettingsApplicationResult.Success(changedCategory ?? "UI");
            }

            var warningMessage = warnings.Count > 0 ? string.Join("; ", warnings) : null;
            return SettingsApplicationResult.Success(changedCategory ?? "UI", warningMessage);
        }, changedCategory ?? "UI");
    }

    public override Task<SettingsApplicationResult> RollbackSettingsAsync(AppSettings currentSettings, AppSettings previousSettings, string? changedCategory = null)
    {
        return ExecuteWithTimingAsync(async () =>
        {
            Logger.LogInformation("UI設定をロールバック中");
            
            // ロールバック処理（シミュレート）
            await Task.Delay(15).ConfigureAwait(false);
            
            return SettingsApplicationResult.Success(changedCategory ?? "UI", "UI設定をロールバックしました");
        }, changedCategory ?? "UI");
    }
}

/// <summary>
/// αテストキャプチャ設定ハンドラー
/// キャプチャ間隔・OCR設定の適用を処理
/// </summary>
public sealed class AlphaTestCaptureSettingsHandler(ILogger<AlphaTestCaptureSettingsHandler> logger) : AlphaTestSettingsHandlerBase(logger)
{
    public override int Priority => 30; // 低優先度
    public override IReadOnlyList<string> HandledCategories => ["Capture", "Ocr"];
    public override string Name => "αテストキャプチャ設定ハンドラー";
    public override string Description => "αテスト用キャプチャ・OCR設定の適用";

    public override Task<SettingsApplicationResult> ApplySettingsAsync(AppSettings oldSettings, AppSettings newSettings, string? changedCategory = null)
    {
        return ExecuteWithTimingAsync(async () =>
        {
            // キャプチャ間隔変更
            if (oldSettings.Capture.CaptureIntervalMs != newSettings.Capture.CaptureIntervalMs)
            {
                Logger.LogInformation("キャプチャ間隔を変更: {OldInterval}ms → {NewInterval}ms", 
                    oldSettings.Capture.CaptureIntervalMs, newSettings.Capture.CaptureIntervalMs);
                
                // キャプチャサービスの再設定（シミュレート）
                await Task.Delay(50).ConfigureAwait(false);
                
                var warning = newSettings.Capture.CaptureIntervalMs < 500 
                    ? "短いキャプチャ間隔はシステム負荷を高める可能性があります" 
                    : null;
                
                return SettingsApplicationResult.Success("Capture", warning);
            }

            // OCR設定変更の検出（将来の拡張用）
            // 現在はシンプルな処理のみ
            
            return SettingsApplicationResult.Success(changedCategory ?? "Capture");
        }, changedCategory ?? "Capture");
    }

    public override Task<SettingsApplicationResult> RollbackSettingsAsync(AppSettings currentSettings, AppSettings previousSettings, string? changedCategory = null)
    {
        return ExecuteWithTimingAsync(async () =>
        {
            Logger.LogInformation("キャプチャ設定をロールバック中");
            
            // ロールバック処理（シミュレート）
            await Task.Delay(25).ConfigureAwait(false);
            
            return SettingsApplicationResult.Success(changedCategory ?? "Capture", "キャプチャ設定をロールバックしました");
        }, changedCategory ?? "Capture");
    }
}
