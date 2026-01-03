using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive;
using Baketa.UI.Resources;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Interfaces;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

/// <summary>
/// [Issue #249] 更新ダイアログViewModel
/// i18n対応
/// </summary>
public sealed class UpdateAvailableViewModel : ReactiveObject
{
    private readonly List<AppCastItem> _updates;

    public UpdateAvailableViewModel(
        List<AppCastItem> updates,
        string currentVersion,
        string appName = "Baketa")
    {
        _updates = updates;
        CurrentVersion = currentVersion;
        AppName = appName;

        // 最新の更新アイテムを設定
        CurrentItem = updates.FirstOrDefault();

        // 更新アイテムをViewModelに変換
        Updates = updates.Select(u => new UpdateItemViewModel(u)).ToList();

        // コマンド初期化
        SkipCommand = ReactiveCommand.Create(OnSkip);
        RemindLaterCommand = ReactiveCommand.Create(OnRemindLater);
        DownloadCommand = ReactiveCommand.Create(OnDownload);
    }

    /// <summary>
    /// 現在のバージョン
    /// </summary>
    public string CurrentVersion { get; }

    /// <summary>
    /// アプリ名
    /// </summary>
    public string AppName { get; }

    /// <summary>
    /// 現在選択されているアイテム
    /// </summary>
    public AppCastItem? CurrentItem { get; }

    /// <summary>
    /// 更新結果
    /// </summary>
    public UpdateAvailableResult Result { get; private set; } = UpdateAvailableResult.None;

    /// <summary>
    /// 更新リスト
    /// </summary>
    public List<UpdateItemViewModel> Updates { get; }

    /// <summary>
    /// ウィンドウタイトル
    /// </summary>
    public string WindowTitle => Strings.Update_WindowTitle;

    /// <summary>
    /// ヘッダーテキスト
    /// </summary>
    public string HeaderText => string.Format(Strings.Update_HeaderText, AppName);

    /// <summary>
    /// 情報テキスト
    /// </summary>
    public string InfoText => string.Format(
        Strings.Update_InfoText,
        AppName,
        CurrentItem?.Version ?? "?",
        GetSimpleVersion(CurrentVersion));

    /// <summary>
    /// スキップボタンテキスト
    /// </summary>
    public string SkipButtonText => Strings.Update_SkipButton;

    /// <summary>
    /// 後で通知ボタンテキスト
    /// </summary>
    public string RemindLaterButtonText => Strings.Update_RemindLaterButton;

    /// <summary>
    /// ダウンロードボタンテキスト
    /// </summary>
    public string DownloadButtonText => Strings.Update_DownloadButton;

    /// <summary>
    /// スキップコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> SkipCommand { get; }

    /// <summary>
    /// 後で通知コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> RemindLaterCommand { get; }

    /// <summary>
    /// ダウンロードコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> DownloadCommand { get; }

    /// <summary>
    /// ウィンドウを閉じるリクエスト
    /// </summary>
    public event EventHandler<UpdateAvailableResult>? CloseRequested;

    private void OnSkip()
    {
        Result = UpdateAvailableResult.SkipUpdate;
        CloseRequested?.Invoke(this, Result);
    }

    private void OnRemindLater()
    {
        Result = UpdateAvailableResult.RemindMeLater;
        CloseRequested?.Invoke(this, Result);
    }

    private void OnDownload()
    {
        Result = UpdateAvailableResult.InstallUpdate;
        CloseRequested?.Invoke(this, Result);
    }

    /// <summary>
    /// gitハッシュを除いたシンプルなバージョンを取得
    /// </summary>
    private static string GetSimpleVersion(string version)
    {
        // "0.2.0+abc123..." → "0.2.0"
        var plusIndex = version.IndexOf('+');
        return plusIndex > 0 ? version[..plusIndex] : version;
    }
}

/// <summary>
/// 更新アイテムViewModel
/// </summary>
public sealed class UpdateItemViewModel
{
    private readonly AppCastItem _item;

    public UpdateItemViewModel(AppCastItem item)
    {
        _item = item;
    }

    /// <summary>
    /// タイトル（アプリ名 + バージョン）
    /// </summary>
    public string Title => $"Baketa {_item.Version}";

    /// <summary>
    /// 日付テキスト
    /// </summary>
    public string DateText
    {
        get
        {
            if (_item.PublicationDate == DateTime.MinValue)
                return string.Empty;

            // ユーザーのカルチャで日付をフォーマット
            var culture = CultureInfo.CurrentUICulture;
            return _item.PublicationDate.ToString("D", culture);
        }
    }
}
