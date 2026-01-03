using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Disposables;
using Baketa.Core.Abstractions.CrashReporting;
using Baketa.UI.Resources;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

/// <summary>
/// [Issue #252] クラッシュレポートダイアログViewModel
/// 前回クラッシュ時のレポート送信を促すダイアログ
/// </summary>
public sealed class CrashReportDialogViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly IReadOnlyList<CrashReportSummary> _crashReports;
    private bool _disposed;
    private bool _includeSystemInfo = true;
    private bool _includeLogs = true;

    public CrashReportDialogViewModel(IReadOnlyList<CrashReportSummary> crashReports)
    {
        _crashReports = crashReports ?? throw new ArgumentNullException(nameof(crashReports));

        // コマンド初期化
        SendCommand = ReactiveCommand.Create(OnSend);
        DontSendCommand = ReactiveCommand.Create(OnDontSend);

        _disposables.Add(SendCommand);
        _disposables.Add(DontSendCommand);
    }

    /// <summary>
    /// クラッシュレポートのリスト
    /// </summary>
    public IReadOnlyList<CrashReportSummary> CrashReports => _crashReports;

    /// <summary>
    /// クラッシュレポートの数
    /// </summary>
    public int CrashCount => _crashReports.Count;

    /// <summary>
    /// 単一のクラッシュかどうか
    /// </summary>
    public bool IsSingleCrash => CrashCount == 1;

    /// <summary>
    /// システム情報を含めるかどうか
    /// </summary>
    public bool IncludeSystemInfo
    {
        get => _includeSystemInfo;
        set => this.RaiseAndSetIfChanged(ref _includeSystemInfo, value);
    }

    /// <summary>
    /// ログを含めるかどうか
    /// </summary>
    public bool IncludeLogs
    {
        get => _includeLogs;
        set => this.RaiseAndSetIfChanged(ref _includeLogs, value);
    }

    /// <summary>
    /// ユーザーの選択結果
    /// </summary>
    public CrashReportDialogResult Result { get; private set; } = CrashReportDialogResult.None;

    #region i18n Properties

    /// <summary>
    /// ウィンドウタイトル
    /// </summary>
    public string WindowTitle => Strings.CrashReport_WindowTitle;

    /// <summary>
    /// ヘッダーテキスト
    /// </summary>
    public string HeaderText => Strings.CrashReport_HeaderText;

    /// <summary>
    /// 説明テキスト
    /// </summary>
    public string DescriptionText => IsSingleCrash
        ? Strings.CrashReport_DescriptionSingle
        : string.Format(Strings.CrashReport_DescriptionMultiple, CrashCount);

    /// <summary>
    /// プライバシーノート
    /// </summary>
    public string PrivacyNote => Strings.CrashReport_PrivacyNote;

    /// <summary>
    /// システム情報チェックボックスラベル
    /// </summary>
    public string IncludeSystemInfoLabel => Strings.CrashReport_IncludeSystemInfo;

    /// <summary>
    /// ログチェックボックスラベル
    /// </summary>
    public string IncludeLogsLabel => Strings.CrashReport_IncludeLogs;

    /// <summary>
    /// 送信ボタンテキスト
    /// </summary>
    public string SendButtonText => Strings.CrashReport_SendButton;

    /// <summary>
    /// 送信しないボタンテキスト
    /// </summary>
    public string DontSendButtonText => Strings.CrashReport_DontSendButton;

    /// <summary>
    /// クラッシュ詳細セクションタイトル
    /// </summary>
    public string CrashDetailsTitle => Strings.CrashReport_CrashDetailsTitle;

    #endregion

    #region Commands

    /// <summary>
    /// 送信コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> SendCommand { get; }

    /// <summary>
    /// 送信しないコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> DontSendCommand { get; }

    #endregion

    #region Events

    /// <summary>
    /// ウィンドウを閉じるリクエスト
    /// </summary>
    public event EventHandler<CrashReportDialogResult>? CloseRequested;

    #endregion

    #region Private Methods

    private void OnSend()
    {
        Result = CrashReportDialogResult.Send;
        CloseRequested?.Invoke(this, Result);
    }

    private void OnDontSend()
    {
        Result = CrashReportDialogResult.DontSend;
        CloseRequested?.Invoke(this, Result);
    }

    #endregion

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _disposables.Dispose();
    }
}

/// <summary>
/// クラッシュレポートダイアログの結果
/// </summary>
public enum CrashReportDialogResult
{
    /// <summary>
    /// 未選択
    /// </summary>
    None,

    /// <summary>
    /// レポートを送信
    /// </summary>
    Send,

    /// <summary>
    /// 送信しない
    /// </summary>
    DontSend
}
