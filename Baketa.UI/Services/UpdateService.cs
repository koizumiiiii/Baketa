using System;
using System.Reflection;
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
/// NetSparkle用ロガーアダプター
/// </summary>
internal sealed class SparkleLogger : NetSparkleUpdater.Interfaces.ILogger
{
    private readonly Microsoft.Extensions.Logging.ILogger? _logger;

    public SparkleLogger(Microsoft.Extensions.Logging.ILogger? logger) => _logger = logger;

    public void PrintMessage(string message, params object[]? arguments)
    {
        _logger?.LogInformation("[NetSparkle] " + message, arguments ?? []);
    }
}

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
                // OnlyVerifySoftwareDownloads: AppCast署名検証をスキップ、ZIPファイルの署名のみ検証
                signatureVerifier = new Ed25519Checker(
                    SecurityMode.OnlyVerifySoftwareDownloads,
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
            // OnlyVerifySoftwareDownloads: AppCast署名検証をスキップ、ZIPファイルの署名のみ検証
            signatureVerifier = new Ed25519Checker(SecurityMode.OnlyVerifySoftwareDownloads, Ed25519PublicKey);
#endif

            // Single-file app対応: バージョン取得が失敗しないようにフォールバック
            // Assembly.Locationはsingle-fileでは空になるため、プロセスのメインモジュールのパスを使用
            var currentVersion = GetCurrentAppVersion();

            // Single-file appでも動作するパスを取得（Geminiレビュー指摘対応）
            var referenceAssemblyPath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(referenceAssemblyPath) || !System.IO.File.Exists(referenceAssemblyPath))
            {
                _logger?.LogWarning("[Issue #249] MainModule.FileNameから有効なパスを取得できませんでした。NetSparkleのフォールバックに任せます。");
                referenceAssemblyPath = null;
            }

            _sparkle = new SparkleUpdater(AppCastUrl, signatureVerifier, referenceAssemblyPath)
            {
                UIFactory = new LocalizedUIFactory("Baketa"),
                RelaunchAfterUpdate = true,
            };

            _logger?.LogInformation("[Issue #249] Single-file対応: バージョン {Version}、ReferenceAssembly={Path}",
                currentVersion, referenceAssemblyPath ?? "(null - will use fallback)");

            // JSON形式のAppCastを使用（デフォルトはXML）
            _sparkle.AppCastGenerator = new NetSparkleUpdater.AppCastHandlers.JsonAppCastGenerator(_sparkle.LogWriter);

            // 更新前イベントハンドラ（Pythonサーバー終了処理）
            // NetSparkle 3.0ではCloseApplicationAsyncを使用
            _sparkle.CloseApplicationAsync += OnCloseApplicationAsync;

            // 更新チェック完了イベント
            _sparkle.UpdateCheckFinished += OnUpdateCheckFinished;

            // ログダウンロード/解析イベント（デバッグ用）
            _sparkle.LogWriter = new SparkleLogger(_logger);

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
            _logger?.LogWarning("[Issue #249] UpdateService未初期化のためスキップ: disposed={Disposed}, sparkle={Sparkle}",
                _disposed, _sparkle == null ? "null" : "not null");
            return;
        }

        try
        {
            IsCheckingForUpdates = true;
            _logger?.LogInformation("[Issue #249] サイレント更新チェック開始: URL={Url}", AppCastUrl);

            var result = await _sparkle.CheckForUpdatesQuietly().ConfigureAwait(false);
            _logger?.LogInformation("[Issue #249] 更新チェック結果: Status={Status}", result.Status);

            UpdateAvailable = result.Status == UpdateStatus.UpdateAvailable;

            if (UpdateAvailable && result.Updates?.Count > 0)
            {
                LatestVersion = result.Updates[0];

                // [Fix] 同一バージョンへの更新を防止
                // NetSparkleがバージョン形式の違い（0.2.2 vs 0.2.2.0）で誤判定する場合がある
                var currentVersion = GetCurrentAppVersion();
                var latestVersionStr = LatestVersion.Version?.TrimStart('v', 'V') ?? "";

                if (IsVersionEqual(currentVersion, latestVersionStr))
                {
                    _logger?.LogInformation(
                        "[Issue #249] 同一バージョンのため更新スキップ: Current={Current}, Latest={Latest}",
                        currentVersion, latestVersionStr);
                    UpdateAvailable = false;
                    return;
                }

                _logger?.LogInformation(
                    "[Issue #249] サイレントチェック: 更新利用可能 v{Version} (現在: {Current})",
                    LatestVersion.Version, currentVersion);

                // 更新が利用可能な場合のみUIダイアログを表示（UIスレッドで実行）
                _logger?.LogInformation("[Issue #249] 更新ダイアログ表示開始...");
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        _logger?.LogInformation("[Issue #249] ShowUpdateNeededUI呼び出し中...");
                        _sparkle?.ShowUpdateNeededUI(result.Updates);
                        _logger?.LogInformation("[Issue #249] ShowUpdateNeededUI呼び出し完了");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "[Issue #249] ダイアログ表示エラー: {Message}", ex.Message);
                    }
                });
            }
            else
            {
                _logger?.LogInformation("[Issue #249] 更新なし: Status={Status}, UpdateCount={Count}",
                    result.Status, result.Updates?.Count ?? 0);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Issue #249] サイレント更新チェック中にエラー: {Message}", ex.Message);
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
    /// [Fix] 現在のアプリバージョンを取得
    /// Single-file app対応: AssemblyInformationalVersionを優先使用
    /// </summary>
    private static string GetCurrentAppVersion()
    {
        var appAssembly = Assembly.GetEntryAssembly();
        if (appAssembly == null) return "0.0.0";

        // MinVerが設定するAssemblyInformationalVersionを優先
        var infoVersion = appAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(infoVersion))
        {
            // "0.2.3+abc123" 形式から "0.2.3" を抽出
            var plusIndex = infoVersion.IndexOf('+');
            return plusIndex > 0 ? infoVersion[..plusIndex] : infoVersion;
        }

        // フォールバック: AssemblyVersion
        var version = appAssembly.GetName().Version;
        if (version == null) return "0.0.0";

        // Major.Minor.Build 形式で返す（Revision は省略）
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>
    /// [Fix] バージョン文字列を比較（形式の違いを吸収）
    /// "0.2.2" と "0.2.2.0" を同一とみなす
    /// </summary>
    private static bool IsVersionEqual(string version1, string version2)
    {
        if (string.IsNullOrEmpty(version1) || string.IsNullOrEmpty(version2))
            return false;

        // バージョン文字列を正規化してVersion型で比較
        try
        {
            var v1 = NormalizeVersion(version1);
            var v2 = NormalizeVersion(version2);
            return v1 == v2;
        }
        catch
        {
            // パース失敗時は文字列比較
            return string.Equals(version1.Trim(), version2.Trim(), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// [Fix] バージョン文字列を正規化（Major.Minor.Build形式）
    /// </summary>
    private static Version NormalizeVersion(string versionStr)
    {
        var cleaned = versionStr.TrimStart('v', 'V').Trim();
        var parts = cleaned.Split('.');

        int major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
        int build = parts.Length > 2 && int.TryParse(parts[2], out var b) ? b : 0;

        return new Version(major, minor, build);
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
