using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services.Settings;

/// <summary>
/// 利用規約・プライバシーポリシー同意管理サービス実装
/// [Issue #261] GDPR/CCPA準拠のクリックラップ同意フローを提供
/// </summary>
public sealed class ConsentService : IConsentService, IDisposable
{
    private readonly ILogger<ConsentService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SemaphoreSlim _settingsLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;

    private ConsentSettings? _cachedSettings;
    private bool _disposed;

    /// <inheritdoc/>
    public event EventHandler<LegalConsentChangedEventArgs>? ConsentChanged;

    public ConsentService(
        ILogger<ConsentService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        _logger.LogDebug("[Issue #261] ConsentService initialized");
    }

    /// <inheritdoc/>
    public async Task<ConsentSettings> GetConsentStateAsync(CancellationToken cancellationToken = default)
    {
        // キャッシュがあればロック不要で即座に返却
        if (_cachedSettings is not null)
        {
            return _cachedSettings;
        }

        await _settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // [Gemini Review] 非同期ファイルI/OでUIスレッドブロックを回避
            return await LoadSettingsInternalAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> NeedsPrivacyPolicyReConsentAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetConsentStateAsync(cancellationToken).ConfigureAwait(false);
        return settings.NeedsPrivacyPolicyReConsent;
    }

    /// <inheritdoc/>
    public async Task<bool> NeedsTermsOfServiceReConsentAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetConsentStateAsync(cancellationToken).ConfigureAwait(false);
        return settings.NeedsTermsOfServiceReConsent;
    }

    /// <inheritdoc/>
    public async Task<bool> NeedsInitialConsentAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetConsentStateAsync(cancellationToken).ConfigureAwait(false);
        return settings.NeedsInitialConsent;
    }

    /// <inheritdoc/>
    public async Task<bool> CanCreateAccountAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetConsentStateAsync(cancellationToken).ConfigureAwait(false);
        return settings.CanCreateAccount;
    }

    /// <inheritdoc/>
    public string GetCurrentVersion(ConsentType consentType)
    {
        return consentType switch
        {
            ConsentType.PrivacyPolicy => ConsentSettings.CurrentPrivacyVersion,
            ConsentType.TermsOfService => ConsentSettings.CurrentTermsVersion,
            _ => throw new ArgumentOutOfRangeException(nameof(consentType), consentType, null)
        };
    }

    /// <inheritdoc/>
    public async Task AcceptPrivacyPolicyAsync(CancellationToken cancellationToken = default)
    {
        await _settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await LoadSettingsInternalAsync(cancellationToken).ConfigureAwait(false);
            settings.AcceptPrivacyPolicy();

            await SaveSettingsAsync(settings, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "[Issue #261] プライバシーポリシー同意を記録: Version={Version}",
                settings.PrivacyPolicyVersion);

            ConsentChanged?.Invoke(this, new LegalConsentChangedEventArgs
            {
                ConsentType = ConsentType.PrivacyPolicy,
                Version = settings.PrivacyPolicyVersion ?? ConsentSettings.CurrentPrivacyVersion,
                AcceptedAt = DateTime.UtcNow
            });
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task AcceptTermsOfServiceAsync(CancellationToken cancellationToken = default)
    {
        await _settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await LoadSettingsInternalAsync(cancellationToken).ConfigureAwait(false);
            settings.AcceptTermsOfService();

            await SaveSettingsAsync(settings, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "[Issue #261] 利用規約同意を記録: Version={Version}",
                settings.TermsOfServiceVersion);

            ConsentChanged?.Invoke(this, new LegalConsentChangedEventArgs
            {
                ConsentType = ConsentType.TermsOfService,
                Version = settings.TermsOfServiceVersion ?? ConsentSettings.CurrentTermsVersion,
                AcceptedAt = DateTime.UtcNow
            });
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task AcceptAllAsync(CancellationToken cancellationToken = default)
    {
        await _settingsLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var settings = await LoadSettingsInternalAsync(cancellationToken).ConfigureAwait(false);
            settings.AcceptAll();

            await SaveSettingsAsync(settings, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "[Issue #261] すべての同意を記録: PrivacyVersion={PrivacyVersion}, TermsVersion={TermsVersion}",
                settings.PrivacyPolicyVersion,
                settings.TermsOfServiceVersion);

            ConsentChanged?.Invoke(this, new LegalConsentChangedEventArgs
            {
                ConsentType = ConsentType.PrivacyPolicy,
                Version = settings.PrivacyPolicyVersion ?? ConsentSettings.CurrentPrivacyVersion,
                AcceptedAt = DateTime.UtcNow
            });

            ConsentChanged?.Invoke(this, new LegalConsentChangedEventArgs
            {
                ConsentType = ConsentType.TermsOfService,
                Version = settings.TermsOfServiceVersion ?? ConsentSettings.CurrentTermsVersion,
                AcceptedAt = DateTime.UtcNow
            });
        }
        finally
        {
            _settingsLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task RecordConsentToServerAsync(
        string userId,
        ConsentType consentType,
        string version,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        try
        {
            var client = _httpClientFactory.CreateClient("RelayServer");

            var payload = new
            {
                user_id = userId,
                consent_type = consentType.ToString().ToLowerInvariant(),
                version,
                accepted_at = DateTime.UtcNow.ToString("O"),
                client_version = GetClientVersion()
            };

            var response = await client.PostAsJsonAsync(
                "/api/consent/record",
                payload,
                _jsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "[Issue #261] サーバーに同意記録を送信: UserId={UserId}, Type={Type}, Version={Version}",
                    userId, consentType, version);
            }
            else
            {
                _logger.LogWarning(
                    "[Issue #261] サーバーへの同意記録送信に失敗: Status={StatusCode}",
                    response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            // サーバー記録の失敗はローカル同意をブロックしない
            _logger.LogWarning(ex,
                "[Issue #261] サーバーへの同意記録送信でエラーが発生しました。ローカル同意は有効です");
        }
    }

    /// <inheritdoc/>
    public async Task SyncLocalConsentToServerAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        try
        {
            var settings = await GetConsentStateAsync(cancellationToken).ConfigureAwait(false);

            // プライバシーポリシー同意をサーバーに同期
            if (settings.HasAcceptedPrivacyPolicy && !string.IsNullOrEmpty(settings.PrivacyPolicyVersion))
            {
                await RecordConsentToServerAsync(
                    userId,
                    ConsentType.PrivacyPolicy,
                    settings.PrivacyPolicyVersion,
                    cancellationToken).ConfigureAwait(false);
            }

            // 利用規約同意をサーバーに同期
            if (settings.HasAcceptedTermsOfService && !string.IsNullOrEmpty(settings.TermsOfServiceVersion))
            {
                await RecordConsentToServerAsync(
                    userId,
                    ConsentType.TermsOfService,
                    settings.TermsOfServiceVersion,
                    cancellationToken).ConfigureAwait(false);
            }

            _logger.LogInformation(
                "[Issue #261] ローカル同意状態をサーバーに同期完了: UserId={UserId}",
                userId);
        }
        catch (Exception ex)
        {
            // 同期失敗はアプリ動作をブロックしない
            _logger.LogWarning(ex,
                "[Issue #261] ローカル同意のサーバー同期に失敗しました（継続）");
        }
    }

    /// <summary>
    /// ロックを既に取得している状態で設定を読み込むための内部メソッド
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>同意設定</returns>
    private async Task<ConsentSettings> LoadSettingsInternalAsync(CancellationToken cancellationToken)
    {
        if (_cachedSettings is not null)
        {
            return _cachedSettings;
        }

        try
        {
            BaketaSettingsPaths.EnsureUserSettingsDirectoryExists();

            if (File.Exists(BaketaSettingsPaths.ConsentSettingsPath))
            {
                var json = await File.ReadAllTextAsync(
                    BaketaSettingsPaths.ConsentSettingsPath,
                    cancellationToken).ConfigureAwait(false);
                _cachedSettings = JsonSerializer.Deserialize<ConsentSettings>(json, _jsonOptions)
                    ?? new ConsentSettings();
            }
            else
            {
                _cachedSettings = new ConsentSettings();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #261] 同意設定の読み込みに失敗しました。デフォルト値を使用します");
            _cachedSettings = new ConsentSettings();
        }

        return _cachedSettings;
    }

    private async Task SaveSettingsAsync(ConsentSettings settings, CancellationToken cancellationToken)
    {
        BaketaSettingsPaths.EnsureUserSettingsDirectoryExists();

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        await File.WriteAllTextAsync(
            BaketaSettingsPaths.ConsentSettingsPath,
            json,
            cancellationToken).ConfigureAwait(false);

        _cachedSettings = settings;
    }

    private static string GetClientVersion()
    {
        var assembly = typeof(ConsentService).Assembly;
        var version = assembly.GetName().Version;
        return version?.ToString() ?? "0.0.0";
    }

    public void Dispose()
    {
        if (_disposed) return;

        _settingsLock.Dispose();
        _disposed = true;
    }
}
