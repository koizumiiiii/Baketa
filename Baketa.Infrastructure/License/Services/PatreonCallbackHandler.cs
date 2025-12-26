using System.Web;
using Baketa.Core.Abstractions.License;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.License.Services;

/// <summary>
/// Patreon OAuth コールバックハンドラ
/// カスタムURIスキーム (baketa://patreon/callback) を処理する
/// </summary>
public sealed class PatreonCallbackHandler : IPatreonCallbackHandler
{
    private readonly IPatreonOAuthService _oauthService;
    private readonly ILogger<PatreonCallbackHandler> _logger;

    /// <summary>
    /// URIスキームプレフィックス
    /// </summary>
    public const string UriScheme = "baketa";

    /// <summary>
    /// コールバックパス
    /// </summary>
    public const string CallbackPath = "/patreon/callback";

    public PatreonCallbackHandler(
        IPatreonOAuthService oauthService,
        ILogger<PatreonCallbackHandler> logger)
    {
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool CanHandle(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        // baketa://patreon/callback?code=xxx&state=yyy 形式をチェック
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme.Equals(UriScheme, StringComparison.OrdinalIgnoreCase) &&
               uri.AbsolutePath.Equals(CallbackPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public async Task<PatreonAuthResult> HandleCallbackUrlAsync(
        string callbackUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callbackUrl);

        _logger.LogInformation("Patreonコールバック URL を処理開始");

        try
        {
            // URLをパース
            if (!Uri.TryCreate(callbackUrl, UriKind.Absolute, out var uri))
            {
                _logger.LogWarning("無効なコールバック URL: パースに失敗");
                return PatreonAuthResult.CreateFailure("INVALID_URL", "コールバックURLの形式が不正です。");
            }

            // スキームとパスを検証
            if (!uri.Scheme.Equals(UriScheme, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("無効なURIスキーム: {Scheme}", uri.Scheme);
                return PatreonAuthResult.CreateFailure("INVALID_SCHEME", $"URIスキーム '{uri.Scheme}' は対応していません。");
            }

            if (!uri.AbsolutePath.Equals(CallbackPath, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("無効なコールバックパス: {Path}", uri.AbsolutePath);
                return PatreonAuthResult.CreateFailure("INVALID_PATH", "コールバックパスが不正です。");
            }

            // クエリパラメータを抽出
            var queryParams = HttpUtility.ParseQueryString(uri.Query);
            var code = queryParams["code"];
            var state = queryParams["state"];
            var error = queryParams["error"];
            var errorDescription = queryParams["error_description"];

            // エラーチェック
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning("Patreonからエラー応答: {Error} - {Description}", error, errorDescription);
                return PatreonAuthResult.CreateFailure(
                    $"PATREON_ERROR_{error.ToUpperInvariant()}",
                    errorDescription ?? "Patreonで認証エラーが発生しました。");
            }

            // 必須パラメータチェック
            if (string.IsNullOrEmpty(code))
            {
                _logger.LogWarning("認証コードがありません");
                return PatreonAuthResult.CreateFailure("MISSING_CODE", "認証コードが見つかりませんでした。");
            }

            if (string.IsNullOrEmpty(state))
            {
                _logger.LogWarning("stateパラメータがありません");
                return PatreonAuthResult.CreateFailure("MISSING_STATE", "stateパラメータが見つかりませんでした。");
            }

            _logger.LogDebug("コールバックパラメータ抽出完了: code長={CodeLength}, state長={StateLength}",
                code.Length, state.Length);

            // OAuthサービスでトークン交換処理
            var result = await _oauthService.HandleCallbackAsync(code, state, cancellationToken)
                .ConfigureAwait(false);

            if (result.Success)
            {
                _logger.LogInformation("✅ Patreonコールバック処理成功: Plan={Plan}", result.Plan);
            }
            else
            {
                _logger.LogWarning("Patreonコールバック処理失敗: {ErrorCode} - {Message}",
                    result.ErrorCode, result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Patreonコールバック処理中に予期せぬエラー");
            return PatreonAuthResult.CreateFailure("CALLBACK_ERROR", $"コールバック処理中にエラーが発生しました: {ex.Message}");
        }
    }
}
