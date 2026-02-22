using System.IO;
using System.Text.Json;
using Baketa.Core.Settings;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// トークン使用量のJSONファイルベース永続化リポジトリ
/// </summary>
public sealed class TokenUsageRepository : ITokenUsageRepository, IDisposable
{
    private readonly ILogger<TokenUsageRepository> _logger;
    private readonly string _dataDirectory;
    private readonly string _summaryFilePath;
    private readonly string _recordsFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    // UsageType.Total時の入出力トークン按分比率
    // 翻訳APIの一般的な使用パターンに基づく推定値
    // (入力: プロンプト+画像, 出力: 翻訳テキスト)
    private const double InputTokenRatio = 0.7;
    private const double OutputTokenRatio = 0.3;

    // デバウンス設定（ファイルI/O最適化）
    private const int DebounceDelayMs = 5000; // 5秒間の変更をバッチ化

    // インメモリキャッシュ
    private MonthlyUsageSummary? _cachedSummary;
    private readonly object _memoryLock = new();

    // デバウンス用タイマー
    private System.Threading.Timer? _debounceTimer;
    private volatile bool _pendingWrite;

    /// <summary>
    /// TokenUsageRepositoryを初期化
    /// </summary>
    public TokenUsageRepository(ILogger<TokenUsageRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // [Issue #459] BaketaSettingsPaths経由に統一
        _dataDirectory = BaketaSettingsPaths.TokenUsageDirectory;
        _summaryFilePath = Path.Combine(_dataDirectory, "monthly-summary.json");
        _recordsFilePath = Path.Combine(_dataDirectory, "usage-records.json");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        // データディレクトリを作成
        EnsureDataDirectoryExists();

        // 起動時にキャッシュを読み込み
        _ = Task.Run(LoadSummaryFromFileAsync);
    }

    /// <inheritdoc/>
    public async Task SaveRecordAsync(TokenUsageRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // サマリーを更新
            var summary = await LoadOrCreateSummaryAsync(record.YearMonth, cancellationToken)
                .ConfigureAwait(false);

            // トークン数を加算
            summary.TotalTokens += record.TokensUsed;
            summary.LastUpdated = DateTime.UtcNow;

            // 使用タイプ別に加算
            if (record.UsageType == "Input")
            {
                summary.InputTokens += record.TokensUsed;
            }
            else if (record.UsageType == "Output")
            {
                summary.OutputTokens += record.TokensUsed;
            }
            else
            {
                // Total の場合は入出力比率で按分（推定）
                summary.InputTokens += (long)(record.TokensUsed * InputTokenRatio);
                summary.OutputTokens += (long)(record.TokensUsed * OutputTokenRatio);
            }

            // プロバイダー別集計
            if (!summary.ByProvider.TryGetValue(record.ProviderId, out var providerTokens))
            {
                providerTokens = 0;
            }
            summary.ByProvider[record.ProviderId] = providerTokens + record.TokensUsed;

            // メモリキャッシュを更新
            lock (_memoryLock)
            {
                _cachedSummary = summary;
            }

            // デバウンス付きファイル保存（連続書き込みを最適化）
            ScheduleDebouncedWrite(summary);

            _logger.LogDebug(
                "トークン使用量を記録: YearMonth={YearMonth}, Tokens={Tokens}, Provider={Provider}, Total={Total}",
                record.YearMonth, record.TokensUsed, record.ProviderId, summary.TotalTokens);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<MonthlyUsageSummary?> GetMonthlySummaryAsync(
        string yearMonth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(yearMonth);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // まずメモリキャッシュをチェック
        lock (_memoryLock)
        {
            if (_cachedSummary is not null && _cachedSummary.YearMonth == yearMonth)
            {
                return _cachedSummary;
            }
        }

        // ファイルから読み込み
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadSummaryFromFileInternalAsync(yearMonth, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task ClearMonthAsync(string yearMonth, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(yearMonth);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // メモリキャッシュをクリア
            lock (_memoryLock)
            {
                if (_cachedSummary?.YearMonth == yearMonth)
                {
                    _cachedSummary = null;
                }
            }

            // ファイルを削除または空のサマリーで上書き
            var emptySummary = new MonthlyUsageSummary
            {
                YearMonth = yearMonth,
                TotalTokens = 0,
                InputTokens = 0,
                OutputTokens = 0,
                ByProvider = [],
                LastUpdated = DateTime.UtcNow
            };

            await SaveSummaryToFileAsync(emptySummary, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("月間使用量をクリア: YearMonth={YearMonth}", yearMonth);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// [Issue #296] サーバーのトークン使用量でローカルを同期するためのメソッド。
    /// サーバーの値を正（authoritative）として、ローカルの合計トークン数を上書きします。
    /// 入力/出力トークンの内訳は保持されず、合計のみが更新されます。
    /// </remarks>
    public async Task SetMonthlySummaryAsync(string yearMonth, long totalTokens, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(yearMonth);
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 既存のサマリーを読み込む（入力/出力比率を保持するため）
            var existingSummary = await LoadSummaryFromFileInternalAsync(yearMonth, cancellationToken)
                .ConfigureAwait(false);

            MonthlyUsageSummary updatedSummary;

            if (existingSummary is not null)
            {
                // 既存の入出力比率を維持しつつ合計を更新
                var ratio = existingSummary.TotalTokens > 0
                    ? (double)existingSummary.InputTokens / existingSummary.TotalTokens
                    : InputTokenRatio;

                updatedSummary = new MonthlyUsageSummary
                {
                    YearMonth = existingSummary.YearMonth,
                    TotalTokens = totalTokens,
                    InputTokens = (long)(totalTokens * ratio),
                    OutputTokens = totalTokens - (long)(totalTokens * ratio),
                    ByProvider = existingSummary.ByProvider,
                    LastUpdated = DateTime.UtcNow
                };
            }
            else
            {
                // 新規作成（デフォルト比率を使用）
                updatedSummary = new MonthlyUsageSummary
                {
                    YearMonth = yearMonth,
                    TotalTokens = totalTokens,
                    InputTokens = (long)(totalTokens * InputTokenRatio),
                    OutputTokens = (long)(totalTokens * OutputTokenRatio),
                    ByProvider = new Dictionary<string, long> { ["primary"] = totalTokens },
                    LastUpdated = DateTime.UtcNow
                };
            }

            // メモリキャッシュを更新
            lock (_memoryLock)
            {
                _cachedSummary = updatedSummary;
            }

            // 即座にファイルに保存（同期処理なのでデバウンスなし）
            await SaveSummaryToFileAsync(updatedSummary, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "[Issue #296] サーバーからローカルにトークン使用量を同期: YearMonth={YearMonth}, TotalTokens={TotalTokens}",
                yearMonth, totalTokens);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// 起動時のサマリー読み込み
    /// </summary>
    private async Task LoadSummaryFromFileAsync()
    {
        try
        {
            var yearMonth = GetCurrentYearMonth();
            await _fileLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var summary = await LoadSummaryFromFileInternalAsync(yearMonth, default)
                    .ConfigureAwait(false);

                if (summary is not null)
                {
                    lock (_memoryLock)
                    {
                        _cachedSummary = summary;
                    }
                    _logger.LogDebug(
                        "月間サマリーを読み込み: YearMonth={YearMonth}, Total={Total}",
                        yearMonth, summary.TotalTokens);
                }
            }
            finally
            {
                _fileLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "月間サマリーの読み込みに失敗しました");
        }
    }

    /// <summary>
    /// ファイルからサマリーを読み込む（ロック取得済み前提）
    /// </summary>
    private async Task<MonthlyUsageSummary?> LoadSummaryFromFileInternalAsync(
        string yearMonth,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_summaryFilePath))
            {
                return null;
            }

            var json = await File.ReadAllTextAsync(_summaryFilePath, cancellationToken)
                .ConfigureAwait(false);
            var summary = JsonSerializer.Deserialize<MonthlyUsageSummary>(json, _jsonOptions);

            // 年月が一致するかチェック
            if (summary is not null && summary.YearMonth == yearMonth)
            {
                lock (_memoryLock)
                {
                    _cachedSummary = summary;
                }
                return summary;
            }

            // 年月が異なる場合は新しい月なのでnullを返す
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "サマリーファイルのパースに失敗しました");
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "サマリーファイルの読み込みに失敗しました");
            return null;
        }
    }

    /// <summary>
    /// サマリーを読み込むか新規作成（ロック取得済み前提）
    /// </summary>
    private async Task<MonthlyUsageSummary> LoadOrCreateSummaryAsync(
        string yearMonth,
        CancellationToken cancellationToken)
    {
        var existing = await LoadSummaryFromFileInternalAsync(yearMonth, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return existing;
        }

        // 新規作成
        return new MonthlyUsageSummary
        {
            YearMonth = yearMonth,
            TotalTokens = 0,
            InputTokens = 0,
            OutputTokens = 0,
            ByProvider = [],
            LastUpdated = DateTime.UtcNow
        };
    }

    /// <summary>
    /// デバウンス付きファイル書き込みをスケジュール
    /// 連続した書き込みリクエストを5秒間バッチ化してI/Oを最適化
    /// </summary>
    private void ScheduleDebouncedWrite(MonthlyUsageSummary summary)
    {
        _pendingWrite = true;

        // 既存のタイマーをキャンセルして新しいタイマーを設定
        _debounceTimer?.Dispose();
        _debounceTimer = new System.Threading.Timer(
            async _ => await FlushPendingWriteAsync().ConfigureAwait(false),
            null,
            DebounceDelayMs,
            System.Threading.Timeout.Infinite);
    }

    /// <summary>
    /// 保留中の書き込みをフラッシュ
    /// </summary>
    private async Task FlushPendingWriteAsync()
    {
        if (!_pendingWrite || _disposed)
        {
            return;
        }

        MonthlyUsageSummary? summaryToWrite;
        lock (_memoryLock)
        {
            summaryToWrite = _cachedSummary;
        }

        if (summaryToWrite is null)
        {
            return;
        }

        await _fileLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveSummaryToFileAsync(summaryToWrite, default).ConfigureAwait(false);
            _pendingWrite = false;
            _logger.LogDebug("トークン使用量をファイルに永続化: Total={Total}", summaryToWrite.TotalTokens);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// サマリーをファイルに保存（ロック取得済み前提）
    /// </summary>
    private async Task SaveSummaryToFileAsync(
        MonthlyUsageSummary summary,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(summary, _jsonOptions);
        await WriteFileWithRetryAsync(_summaryFilePath, json, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// リトライ付きアトミックファイル書き込み
    /// </summary>
    private async Task WriteFileWithRetryAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken)
    {
        const int maxRetries = 3;
        const int retryDelayMs = 50;

        var tempPath = filePath + ".tmp";

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // 1. 一時ファイルに書き込み
                await using (var stream = new FileStream(
                    tempPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None))
                await using (var writer = new StreamWriter(stream))
                {
                    await writer.WriteAsync(content).ConfigureAwait(false);
                }

                // 2. アトミックにファイルを置き換え
                File.Move(tempPath, filePath, overwrite: true);

                return;
            }
            catch (IOException ex) when (attempt < maxRetries - 1)
            {
                _logger.LogWarning(
                    ex,
                    "ファイル保存リトライ {Attempt}/{MaxRetries}: {FilePath}",
                    attempt + 1, maxRetries, filePath);

                // 一時ファイルが残っている場合は削除
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch { /* クリーンアップ失敗は無視 */ }

                await Task.Delay(retryDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// データディレクトリの存在を確認
    /// </summary>
    private void EnsureDataDirectoryExists()
    {
        try
        {
            if (!Directory.Exists(_dataDirectory))
            {
                Directory.CreateDirectory(_dataDirectory);
                _logger.LogInformation("トークン使用量データディレクトリを作成: {Directory}", _dataDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "データディレクトリの作成に失敗しました");
            throw;
        }
    }

    private static string GetCurrentYearMonth()
    {
        var now = DateTime.UtcNow;
        return $"{now.Year:D4}-{now.Month:D2}";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // デバウンスタイマーを停止
        _debounceTimer?.Dispose();

        // 保留中の書き込みをフラッシュ（同期的に実行）
        if (_pendingWrite)
        {
            try
            {
                FlushPendingWriteAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dispose時の保留書き込みフラッシュに失敗");
            }
        }

        _fileLock.Dispose();
        _disposed = true;
    }
}
