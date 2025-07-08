using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Extensions;
using Baketa.UI.Framework;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

/// <summary>
/// フィーチャーフラグ対応のViewModelベースクラス
/// </summary>
public abstract class FeatureAwareViewModelBase : ViewModelBase
{
    private readonly IFeatureFlagService FeatureFlagService;

    protected FeatureAwareViewModelBase(
        IFeatureFlagService featureFlagService,
        IEventAggregator eventAggregator) : base(eventAggregator)
    {
        FeatureFlagService = featureFlagService ?? throw new ArgumentNullException(nameof(featureFlagService));
        
        // フィーチャーフラグ変更時にUIの可視性を更新
        FeatureFlagService.FeatureFlagChanged += OnFeatureFlagChanged;
    }

    protected FeatureAwareViewModelBase(
        IFeatureFlagService featureFlagService,
        IEventAggregator eventAggregator,
        ILogger? logger) : base(eventAggregator, logger)
    {
        FeatureFlagService = featureFlagService ?? throw new ArgumentNullException(nameof(featureFlagService));
        
        // フィーチャーフラグ変更時にUIの可視性を更新
        FeatureFlagService.FeatureFlagChanged += OnFeatureFlagChanged;
    }

    /// <summary>
    /// 認証機能が有効かどうか
    /// </summary>
    public bool IsAuthenticationEnabled => FeatureFlagService.IsAuthenticationEnabled;

    /// <summary>
    /// クラウド翻訳が有効かどうか
    /// </summary>
    public bool IsCloudTranslationEnabled => FeatureFlagService.IsCloudTranslationEnabled;

    /// <summary>
    /// 高度なUI機能が有効かどうか
    /// </summary>
    public bool IsAdvancedUIEnabled => FeatureFlagService.IsAdvancedUIEnabled;

    /// <summary>
    /// 中国語OCRが有効かどうか
    /// </summary>
    public bool IsChineseOCREnabled => FeatureFlagService.IsChineseOCREnabled;

    /// <summary>
    /// フィードバック機能が有効かどうか
    /// </summary>
    public bool IsFeedbackEnabled => FeatureFlagService.IsFeedbackEnabled;

    /// <summary>
    /// デバッグ機能が有効かどうか
    /// </summary>
    public bool IsDebugFeaturesEnabled => FeatureFlagService.IsDebugFeaturesEnabled;

    /// <summary>
    /// アルファテスト制限が適用されているかどうか
    /// </summary>
    public bool IsAlphaTestRestricted => FeatureFlagService.IsAlphaTestRestricted();

    /// <summary>
    /// 指定した機能が有効かどうかをチェック
    /// </summary>
    /// <param name="featureName">機能名</param>
    /// <returns>機能が有効な場合true</returns>
    protected bool IsFeatureEnabled(string featureName) => FeatureFlagService.IsFeatureEnabled(featureName);

    /// <summary>
    /// フィーチャーフラグが有効な場合のみコマンドを実行
    /// </summary>
    /// <param name="featureName">機能名</param>
    /// <param name="action">実行するアクション</param>
    protected void ExecuteIfEnabled(string featureName, Action action)
    {
        FeatureFlagService.ExecuteIfEnabled(featureName, action);
    }

    /// <summary>
    /// フィーチャーフラグが有効な場合のみ非同期コマンドを実行
    /// </summary>
    /// <param name="featureName">機能名</param>
    /// <param name="asyncAction">実行する非同期アクション</param>
    protected async Task ExecuteIfEnabledAsync(string featureName, Func<Task> asyncAction)
    {
        await FeatureFlagService.ExecuteIfEnabledAsync(featureName, asyncAction).ConfigureAwait(false);
    }

    /// <summary>
    /// フィーチャーフラグ変更時のイベントハンドラ
    /// </summary>
    /// <param name="sender">送信者</param>
    /// <param name="e">イベント引数</param>
    protected virtual void OnFeatureFlagChanged(object? sender, FeatureFlagChangedEventArgs e)
    {
        // UIプロパティの変更通知
        this.RaisePropertyChanged(nameof(IsAuthenticationEnabled));
        this.RaisePropertyChanged(nameof(IsCloudTranslationEnabled));
        this.RaisePropertyChanged(nameof(IsAdvancedUIEnabled));
        this.RaisePropertyChanged(nameof(IsChineseOCREnabled));
        this.RaisePropertyChanged(nameof(IsFeedbackEnabled));
        this.RaisePropertyChanged(nameof(IsDebugFeaturesEnabled));
        this.RaisePropertyChanged(nameof(IsAlphaTestRestricted));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            FeatureFlagService.FeatureFlagChanged -= OnFeatureFlagChanged;
        }
        base.Dispose(disposing);
    }
}