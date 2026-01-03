using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Logging;
using NetSparkleUpdater;
using NetSparkleUpdater.Enums;
using NetSparkleUpdater.Interfaces;
using NetSparkleUpdater.SignatureVerifiers;
using NetSparkleUpdater.UI.Avalonia;

namespace Baketa.UI.Services;

/// <summary>
/// [Issue #249] アプリケーション自動アップデートサービス
/// NetSparkle を使用して GitHub Releases からアップデートを確認・適用
/// </summary>
public sealed class UpdateService : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// GitHub AppCast URL
    /// 自動アップデートのマニフェストファイル（release.ymlで自動生成）
    /// </summary>
    private const string AppCastUrl = "https://github.com/koizumiiiii/Baketa/releases/latest/download/appcast.json";

    /// <summary>
    /// Ed25519 公開鍵（Base64エンコード）
    ///
    /// 生成日: 2026-01-03
    /// 生成方法: scripts/generate-update-keys.ps1
    ///
    /// 対応する秘密鍵は GitHub Secrets に登録:
    /// - Secret名: NETSPARKLE_ED25519_PRIVATE_KEY
    /// </summary>
    private const string Ed25519PublicKey = "mhVnvb6OPFeEaId0GsB+p1CGrQBsbNlJRDhb5RryHd0=";

    private readonly ILogger<UpdateService>? _logger;
    private readonly IPythonServerManager? _pythonServerManager;
    private SparkleUpdater? _sparkle;
    private bool _disposed;

    /// <summary>
    /// 更新確認中かどうか
    /// </summary>
    public bool IsCheckingForUpdates { get; private set; }

    /// <summary>
    /// 利用可能な更新があるかどうか
    /// </summary>
    public bool UpdateAvailable { get; private set; }

    /// <summary>
    /// 最新バージョン情報
    /// </summary>
    public AppCastItem? LatestVersion { get; private set; }

    /// <summary>
    /// UpdateServiceを初期化します
    /// </summary>
    /// <param name="pythonServerManager">Pythonサーバー管理（更新前の終了処理用）</param>
    /// <param name="logger">ロガー</param>
    public UpdateService(
        IPythonServerManager? pythonServerManager = null,
        ILogger<UpdateService>? logger = null)
    {
        _pythonServerManager = pythonServerManager;
        _logger = logger;

        _logger?.LogDebug("[Issue #249] UpdateService初期化完了");
    }

    /// <summary>
    /// SparkleUpdaterを初期化
    /// App.axaml.csのOnFrameworkInitializationCompleted()から呼び出す
    /// </summary>
    public void Initialize()
    {
        if (_disposed || _sparkle != null)
        {
            return;
        }

        try
        {
            ISignatureVerifier signatureVerifier;

#if DEBUG
            // 開発ビルド: 公開鍵が設定されている場合のみ署名検証
            if (!string.IsNullOrEmpty(Ed25519PublicKey))
            {
                signatureVerifier = new Ed25519Checker(
                    SecurityMode.Strict,
                    Ed25519PublicKey);
            }
            else
            {
                // 開発中は署名検証をスキップ
                _logger?.LogWarning("[Issue #249] Ed25519公開鍵未設定 - 署名検証を無効化（DEBUGビルド）");
                signatureVerifier = new Ed25519Checker(
                    SecurityMode.Unsafe,
                    string.Empty);
            }
#else
            // リリースビルド: 公開鍵を必須とする（セキュリティ強化）
            if (string.IsNullOrEmpty(Ed25519PublicKey))
            {
                _logger?.LogCritical("[Issue #249] Ed25519公開鍵が設定されていません。リリースビルドでは必須です。");
                throw new InvalidOperationException("Ed25519 public key is missing in release build. Auto-update disabled.");
            }
            signatureVerifier = new Ed25519Checker(SecurityMode.Strict, Ed25519PublicKey);
#endif

            _sparkle = new SparkleUpdater(AppCastUrl, signatureVerifier)
            {
                UIFactory = new UIFactory(),
                RelaunchAfterUpdate = true,
            };

            // 更新前イベントハンドラ（Pythonサーバー終了処理）
            // NetSparkle 3.0ではCloseApplicationAsyncを使用
            _sparkle.CloseApplicationAsync += OnCloseApplicationAsync;

            // 更新チェック完了イベント
            _sparkle.UpdateCheckFinished += OnUpdateCheckFinished;

            _logger?.LogInformation("[Issue #249] SparkleUpdater初期化完了: AppCast={AppCastUrl}", AppCastUrl);
        }
        catch (InvalidOperationException)
        {
            // リリースビルドで公開鍵未設定の場合は再スロー
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Issue #249] SparkleUpdater初期化失敗");
        }
    }

    /// <summary>
    /// 更新を手動チェック（ユーザー操作時）
    /// </summary>
    /// <returns>更新が利用可能かどうか</returns>
    public async Task<bool> CheckForUpdatesAsync()
    {
        if (_disposed || _sparkle == null)
        {
            _logger?.LogWarning("[Issue #249] SparkleUpdater未初期化 - 更新チェックをスキップ");
            return false;
        }

        try
        {
            IsCheckingForUpdates = true;
            _logger?.LogInformation("[Issue #249] 更新チェック開始...");

            var result = await _sparkle.CheckForUpdatesAtUserRequest().ConfigureAwait(false);

            UpdateAvailable = result.Status == UpdateStatus.UpdateAvailable;

            if (UpdateAvailable && result.Updates?.Count > 0)
            {
                LatestVersion = result.Updates[0];
                _logger?.LogInformation(
                    "[Issue #249] 更新が利用可能: v{Version}",
                    LatestVersion.Version);
            }
            else
            {
                _logger?.LogInformation("[Issue #249] 更新なし - 最新バージョンです");
            }

            return UpdateAvailable;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Issue #249] 更新チェック中にエラー");
            return false;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>
    /// バックグラウンドで更新チェック（起動時）
    /// 更新が見つかった場合はUIダイアログを表示
    /// </summary>
    public async Task CheckForUpdatesInBackgroundAsync()
    {
        if (_disposed || _sparkle == null)
        {
            return;
        }

        try
        {
            IsCheckingForUpdates = true;
            _logger?.LogDebug("[Issue #249] サイレント更新チェック開始...");

            var result = await _sparkle.CheckForUpdatesQuietly().ConfigureAwait(false);

            UpdateAvailable = result.Status == UpdateStatus.UpdateAvailable;

            if (UpdateAvailable && result.Updates?.Count > 0)
            {
                LatestVersion = result.Updates[0];
                _logger?.LogInformation(
                    "[Issue #249] サイレントチェック: 更新利用可能 v{Version}",
                    LatestVersion.Version);

                // 更新が利用可能な場合のみUIダイアログを表示
                _sparkle.ShowUpdateNeededUI(result.Updates);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[Issue #249] サイレント更新チェック中にエラー（継続）");
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    /// <summary>
    /// 更新ダイアログを表示
    /// </summary>
    public void ShowUpdateDialog()
    {
        if (_disposed || _sparkle == null || LatestVersion == null)
        {
            return;
        }

        _sparkle.ShowUpdateNeededUI([LatestVersion]);
    }

    /// <summary>
    /// 更新前の終了処理（Pythonサーバー停止など）
    /// NetSparkle 3.0: CloseApplicationAsyncイベントハンドラ
    /// ⚠️ 90秒以内に終了する必要があります
    /// </summary>
    private async Task OnCloseApplicationAsync()
    {
        _logger?.LogInformation("[Issue #249] 更新適用のためアプリケーション終了準備開始...");

        try
        {
            // Pythonサーバーの停止
            // Note: サーバーの破棄はDIコンテナの責務なので、ここでは停止のみ行う
            if (_pythonServerManager != null)
            {
                _logger?.LogInformation("[Issue #249] Pythonサーバー停止中...");

                var servers = await _pythonServerManager.GetActiveServersAsync().ConfigureAwait(false);
                foreach (var server in servers)
                {
                    try
                    {
                        await _pythonServerManager.StopServerAsync(server.Port).ConfigureAwait(false);
                        _logger?.LogDebug("[Issue #249] サーバー停止完了: Port={Port}", server.Port);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "[Issue #249] サーバー停止失敗: Port={Port}", server.Port);
                    }
                }

                _logger?.LogInformation("[Issue #249] 全Pythonサーバー停止完了");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Issue #249] 更新前終了処理中にエラー");
        }

        // アプリケーションを終了
        _logger?.LogInformation("[Issue #249] アプリケーション終了");
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    /// <summary>
    /// 更新チェック完了イベントハンドラ
    /// </summary>
    private void OnUpdateCheckFinished(object? sender, UpdateStatus status)
    {
        _logger?.LogDebug("[Issue #249] 更新チェック完了: Status={Status}", status);
    }

    /// <summary>
    /// リソースを解放（同期）
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// リソースを解放（非同期）
    /// </summary>
    /// <remarks>
    /// 現在SparkleUpdaterはIAsyncDisposableを実装していないため、
    /// 同期的な破棄処理を呼び出しています。
    /// 将来SparkleUpdaterが非同期破棄に対応した際は見直しが必要です。
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 内部的なリソース解放処理
    /// </summary>
    private void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            if (_sparkle != null)
            {
                _sparkle.CloseApplicationAsync -= OnCloseApplicationAsync;
                _sparkle.UpdateCheckFinished -= OnUpdateCheckFinished;
                _sparkle.Dispose();
                _sparkle = null;
            }

            _logger?.LogDebug("[Issue #249] UpdateService破棄完了");
        }

        _disposed = true;
    }
}
