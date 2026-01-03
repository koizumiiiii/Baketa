using System.Collections.Generic;
using Baketa.UI.ViewModels;
using NetSparkleUpdater;
using NetSparkleUpdater.Interfaces;
using NetSparkleUpdater.UI.Avalonia;

namespace Baketa.UI.Services;

/// <summary>
/// [Issue #249] ローカライズ対応のUIFactory
/// カスタム更新ダイアログでi18n対応
/// </summary>
public sealed class LocalizedUIFactory : UIFactory
{
    private readonly string _appName;

    public LocalizedUIFactory(string appName = "Baketa") : base()
    {
        _appName = appName;
    }

    /// <summary>
    /// 更新利用可能ウィンドウを作成（カスタムi18n対応ダイアログ）
    /// </summary>
    public override IUpdateAvailable CreateUpdateAvailableWindow(
        List<AppCastItem> updates,
        ISignatureVerifier? signatureVerifier,
        string currentVersion,
        string appName = "the application",
        bool isUpdateAlreadyDownloaded = false)
    {
        var viewModel = new UpdateAvailableViewModel(updates, currentVersion, _appName);
        return new Views.UpdateAvailableWindow(viewModel);
    }
}
