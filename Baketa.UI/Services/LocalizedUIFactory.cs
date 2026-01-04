using System.Collections.Generic;
using System.Linq;
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
    private string? _latestVersion;

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
        // 最新バージョンを保存（ダウンロード進行ダイアログで使用）
        _latestVersion = updates.FirstOrDefault()?.Version;

        var viewModel = new UpdateAvailableViewModel(updates, currentVersion, _appName);
        return new Views.UpdateAvailableWindow(viewModel);
    }

    /// <summary>
    /// ダウンロード進行ウィンドウを作成（カスタムi18n対応ダイアログ）
    /// </summary>
    public override IDownloadProgress CreateProgressWindow(
        string downloadTitle,
        string actionButtonTitleAfterDownload)
    {
        // バージョン情報を取得（フォールバック対応）
        var version = _latestVersion ?? "?";
        if (version.StartsWith("v", System.StringComparison.OrdinalIgnoreCase))
        {
            version = version[1..];
        }

        var viewModel = new DownloadProgressViewModel(version);
        var window = new Views.DownloadProgressWindow(viewModel);

        // アプリケーションアイコンを設定
        try
        {
            var iconUri = new System.Uri("avares://Baketa/Assets/Icons/baketa.ico");
            window.Icon = new Avalonia.Controls.WindowIcon(Avalonia.Platform.AssetLoader.Open(iconUri));
        }
        catch
        {
            // アイコン設定失敗は無視
        }

        return window;
    }
}
