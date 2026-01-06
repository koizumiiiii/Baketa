using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Settings;
using Baketa.UI.Resources;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

/// <summary>
/// [Issue #261] 同意ダイアログViewModel
/// 利用規約・プライバシーポリシーへの同意を取得
/// </summary>
public sealed class ConsentDialogViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly IConsentService _consentService;
    private bool _disposed;
    private bool _hasAcceptedPrivacyPolicy;
    private bool _hasAcceptedTermsOfService;

    /// <summary>
    /// ダイアログモード
    /// </summary>
    public ConsentDialogMode Mode { get; }

    /// <summary>
    /// ダイアログの結果
    /// </summary>
    public ConsentDialogResult Result { get; private set; } = ConsentDialogResult.Declined;

    /// <summary>
    /// 非同期ファクトリメソッド
    /// [Gemini Review] コンストラクタでの同期I/Oを回避するため非同期初期化
    /// </summary>
    /// <param name="consentService">同意サービス</param>
    /// <param name="mode">ダイアログモード</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>初期化済みViewModel</returns>
    public static async Task<ConsentDialogViewModel> CreateAsync(
        IConsentService consentService,
        ConsentDialogMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(consentService);

        var viewModel = new ConsentDialogViewModel(consentService, mode);
        await viewModel.InitializeAsync(cancellationToken).ConfigureAwait(false);
        return viewModel;
    }

    private ConsentDialogViewModel(
        IConsentService consentService,
        ConsentDialogMode mode)
    {
        _consentService = consentService;
        Mode = mode;

        // Accept可能条件を監視
        var canAccept = this.WhenAnyValue(
            vm => vm.HasAcceptedPrivacyPolicy,
            vm => vm.HasAcceptedTermsOfService,
            (privacy, terms) => mode switch
            {
                ConsentDialogMode.InitialLaunch => privacy,
                ConsentDialogMode.AccountCreation => privacy && terms,
                _ => false
            });

        AcceptCommand = ReactiveCommand.Create(OnAccept, canAccept);
        DeclineCommand = ReactiveCommand.Create(OnDecline);
        OpenPrivacyPolicyCommand = ReactiveCommand.CreateFromTask(OpenPrivacyPolicyAsync);
        OpenTermsOfServiceCommand = ReactiveCommand.CreateFromTask(OpenTermsOfServiceAsync);

        _disposables.Add(AcceptCommand);
        _disposables.Add(DeclineCommand);
        _disposables.Add(OpenPrivacyPolicyCommand);
        _disposables.Add(OpenTermsOfServiceCommand);
    }

    /// <summary>
    /// 非同期初期化（既存の同意状態をロード）
    /// </summary>
    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var currentState = await _consentService.GetConsentStateAsync(cancellationToken).ConfigureAwait(false);
        _hasAcceptedPrivacyPolicy = currentState.HasAcceptedPrivacyPolicy && !currentState.NeedsPrivacyPolicyReConsent;
        _hasAcceptedTermsOfService = currentState.HasAcceptedTermsOfService && !currentState.NeedsTermsOfServiceReConsent;
    }

    #region Binding Properties

    /// <summary>
    /// プライバシーポリシーに同意したか
    /// </summary>
    public bool HasAcceptedPrivacyPolicy
    {
        get => _hasAcceptedPrivacyPolicy;
        set => this.RaiseAndSetIfChanged(ref _hasAcceptedPrivacyPolicy, value);
    }

    /// <summary>
    /// 利用規約に同意したか
    /// </summary>
    public bool HasAcceptedTermsOfService
    {
        get => _hasAcceptedTermsOfService;
        set => this.RaiseAndSetIfChanged(ref _hasAcceptedTermsOfService, value);
    }

    /// <summary>
    /// プライバシーポリシーセクションを表示するか
    /// </summary>
    public bool ShowPrivacyPolicySection => Mode is ConsentDialogMode.InitialLaunch or ConsentDialogMode.AccountCreation;

    /// <summary>
    /// 利用規約セクションを表示するか
    /// </summary>
    public bool ShowTermsOfServiceSection => Mode == ConsentDialogMode.AccountCreation;

    #endregion

    #region Commands

    /// <summary>
    /// 同意コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> AcceptCommand { get; }

    /// <summary>
    /// 拒否コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> DeclineCommand { get; }

    /// <summary>
    /// プライバシーポリシーを開くコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenPrivacyPolicyCommand { get; }

    /// <summary>
    /// 利用規約を開くコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> OpenTermsOfServiceCommand { get; }

    #endregion

    #region i18n Properties

    public string WindowTitle => Mode switch
    {
        ConsentDialogMode.InitialLaunch => Strings.Consent_WindowTitle_Initial,
        ConsentDialogMode.AccountCreation => Strings.Consent_WindowTitle_Account,
        _ => Strings.Consent_WindowTitle_Initial
    };

    public string HeaderText => Mode switch
    {
        ConsentDialogMode.InitialLaunch => Strings.Consent_Header_Initial,
        ConsentDialogMode.AccountCreation => Strings.Consent_Header_Account,
        _ => Strings.Consent_Header_Initial
    };

    public string DescriptionText => Mode switch
    {
        ConsentDialogMode.InitialLaunch => Strings.Consent_Description_Initial,
        ConsentDialogMode.AccountCreation => Strings.Consent_Description_Account,
        _ => Strings.Consent_Description_Initial
    };

    public string PrivacyPolicyLabel => Strings.Consent_PrivacyPolicy_Label;
    public string PrivacyPolicyLinkText => Strings.Consent_PrivacyPolicy_Link;
    public string PrivacyPolicyCheckboxText => Strings.Consent_PrivacyPolicy_Checkbox;
    public string TermsOfServiceLabel => Strings.Consent_TermsOfService_Label;
    public string TermsOfServiceLinkText => Strings.Consent_TermsOfService_Link;
    public string TermsOfServiceCheckboxText => Strings.Consent_TermsOfService_Checkbox;
    public string AcceptButtonText => Strings.Consent_Button_Accept;
    public string DeclineButtonText => Strings.Consent_Button_Decline;

    public string PrivacyPolicyVersion => $"v{_consentService.GetCurrentVersion(ConsentType.PrivacyPolicy)}";
    public string TermsOfServiceVersion => $"v{_consentService.GetCurrentVersion(ConsentType.TermsOfService)}";

    #endregion

    #region Command Handlers

    private void OnAccept()
    {
        Result = ConsentDialogResult.Accepted;
    }

    private void OnDecline()
    {
        Result = ConsentDialogResult.Declined;
    }

    private async System.Threading.Tasks.Task OpenPrivacyPolicyAsync()
    {
        // プライバシーポリシーURLを開く
        var url = "https://baketa.app/privacy-policy";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // URL起動失敗は無視
        }

        await System.Threading.Tasks.Task.CompletedTask;
    }

    private async System.Threading.Tasks.Task OpenTermsOfServiceAsync()
    {
        // 利用規約URLを開く
        var url = "https://baketa.app/terms-of-service";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // URL起動失敗は無視
        }

        await System.Threading.Tasks.Task.CompletedTask;
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;

        _disposables.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// 同意ダイアログのモード
/// </summary>
public enum ConsentDialogMode
{
    /// <summary>
    /// 初回起動時（プライバシーポリシーのみ）
    /// </summary>
    InitialLaunch,

    /// <summary>
    /// アカウント作成時（プライバシーポリシー＋利用規約）
    /// </summary>
    AccountCreation
}

/// <summary>
/// 同意ダイアログの結果
/// </summary>
public enum ConsentDialogResult
{
    /// <summary>
    /// 同意
    /// </summary>
    Accepted,

    /// <summary>
    /// 拒否
    /// </summary>
    Declined
}
