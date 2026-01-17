using Baketa.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Services;

/// <summary>
/// ApiRequestDeduplicator のユニットテスト
/// Issue #299: API重複呼び出し削減機能のテスト
/// </summary>
public sealed class ApiRequestDeduplicatorTests : IDisposable
{
    private readonly Mock<ILogger<ApiRequestDeduplicator>> _loggerMock;
    private readonly ApiRequestDeduplicator _deduplicator;

    public ApiRequestDeduplicatorTests()
    {
        _loggerMock = new Mock<ILogger<ApiRequestDeduplicator>>();
        _deduplicator = new ApiRequestDeduplicator(_loggerMock.Object);
    }

    public void Dispose()
    {
        _deduplicator.Dispose();
    }

    #region ExecuteOnceAsync Tests

    [Fact]
    public async Task ExecuteOnceAsync_SingleRequest_ExecutesFactory()
    {
        // Arrange
        var factoryCallCount = 0;
        var expectedResult = new TestResult { Value = "test" };

        // Act
        var result = await _deduplicator.ExecuteOnceAsync(
            "test-key",
            async () =>
            {
                factoryCallCount++;
                await Task.Delay(10);
                return expectedResult;
            });

        // Assert
        Assert.Equal(expectedResult, result);
        Assert.Equal(1, factoryCallCount);
    }

    [Fact]
    public async Task ExecuteOnceAsync_ParallelRequests_ExecutesFactoryOnce()
    {
        // Arrange
        var factoryCallCount = 0;
        var expectedResult = new TestResult { Value = "parallel-test" };

        // Act - 並列で5回同時にリクエストを送信
        var tasks = Enumerable.Range(0, 5).Select(_ =>
            _deduplicator.ExecuteOnceAsync(
                "parallel-key",
                async () =>
                {
                    Interlocked.Increment(ref factoryCallCount);
                    await Task.Delay(100); // 処理に時間がかかることをシミュレート
                    return expectedResult;
                })).ToList();

        var results = await Task.WhenAll(tasks);

        // Assert
        Assert.All(results, r => Assert.Equal(expectedResult, r));
        Assert.Equal(1, factoryCallCount); // ファクトリーは1回だけ呼ばれる
    }

    [Fact]
    public async Task ExecuteOnceAsync_DifferentKeys_ExecutesBothFactories()
    {
        // Arrange
        var factory1CallCount = 0;
        var factory2CallCount = 0;

        // Act
        var task1 = _deduplicator.ExecuteOnceAsync(
            "key-1",
            async () =>
            {
                Interlocked.Increment(ref factory1CallCount);
                await Task.Delay(50);
                return new TestResult { Value = "result-1" };
            });

        var task2 = _deduplicator.ExecuteOnceAsync(
            "key-2",
            async () =>
            {
                Interlocked.Increment(ref factory2CallCount);
                await Task.Delay(50);
                return new TestResult { Value = "result-2" };
            });

        var results = await Task.WhenAll(task1, task2);

        // Assert
        Assert.Equal("result-1", results[0]?.Value);
        Assert.Equal("result-2", results[1]?.Value);
        Assert.Equal(1, factory1CallCount);
        Assert.Equal(1, factory2CallCount);
    }

    [Fact]
    public async Task ExecuteOnceAsync_CachedResult_ReturnsWithoutExecutingFactory()
    {
        // Arrange
        var factoryCallCount = 0;
        var cacheDuration = TimeSpan.FromSeconds(30);

        // Act - 最初のリクエスト
        var result1 = await _deduplicator.ExecuteOnceAsync(
            "cached-key",
            () =>
            {
                factoryCallCount++;
                return Task.FromResult<TestResult?>(new TestResult { Value = "cached" });
            },
            cacheDuration);

        // 少し待機して2回目のリクエスト
        await Task.Delay(10);

        var result2 = await _deduplicator.ExecuteOnceAsync(
            "cached-key",
            () =>
            {
                factoryCallCount++;
                return Task.FromResult<TestResult?>(new TestResult { Value = "should-not-execute" });
            },
            cacheDuration);

        // Assert
        Assert.Equal("cached", result1?.Value);
        Assert.Equal("cached", result2?.Value);
        Assert.Equal(1, factoryCallCount); // キャッシュが使われるので1回のみ
    }

    [Fact]
    public async Task ExecuteOnceAsync_FactoryThrows_RemovesCacheAndRethrows()
    {
        // Arrange
        var factoryCallCount = 0;

        // Act & Assert - 最初のリクエストは例外をスロー
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _deduplicator.ExecuteOnceAsync<TestResult>(
                "exception-key",
                () =>
                {
                    factoryCallCount++;
                    throw new InvalidOperationException("Test exception");
                });
        });

        // 2回目のリクエストは再試行される
        var result = await _deduplicator.ExecuteOnceAsync(
            "exception-key",
            () =>
            {
                factoryCallCount++;
                return Task.FromResult<TestResult?>(new TestResult { Value = "retry-success" });
            });

        // Assert
        Assert.Equal("retry-success", result?.Value);
        Assert.Equal(2, factoryCallCount); // 例外後に再試行されるので2回
    }

    #endregion

    #region InvalidateByPrefix Tests

    [Fact]
    public async Task InvalidateByPrefix_RemovesMatchingEntries()
    {
        // Arrange - キャッシュを作成
        await _deduplicator.ExecuteOnceAsync(
            "user-1-data",
            () => Task.FromResult<TestResult?>(new TestResult { Value = "user1" }));

        await _deduplicator.ExecuteOnceAsync(
            "user-2-data",
            () => Task.FromResult<TestResult?>(new TestResult { Value = "user2" }));

        await _deduplicator.ExecuteOnceAsync(
            "system-config",
            () => Task.FromResult<TestResult?>(new TestResult { Value = "config" }));

        var factoryCallCount = 0;

        // Act - "user-" プレフィックスのキャッシュを無効化
        _deduplicator.InvalidateByPrefix("user-");

        // Assert - user-1-data は再実行される
        var result1 = await _deduplicator.ExecuteOnceAsync(
            "user-1-data",
            () =>
            {
                factoryCallCount++;
                return Task.FromResult<TestResult?>(new TestResult { Value = "user1-new" });
            });

        // system-config はキャッシュされたまま
        var result2 = await _deduplicator.ExecuteOnceAsync(
            "system-config",
            () =>
            {
                factoryCallCount++;
                return Task.FromResult<TestResult?>(new TestResult { Value = "config-new" });
            });

        Assert.Equal("user1-new", result1?.Value);
        Assert.Equal("config", result2?.Value); // キャッシュから返される
        Assert.Equal(1, factoryCallCount); // user-1-dataのみ再実行
    }

    #endregion

    #region InvalidateAll Tests

    [Fact]
    public async Task InvalidateAll_RemovesAllEntries()
    {
        // Arrange - キャッシュを作成
        await _deduplicator.ExecuteOnceAsync(
            "key-a",
            () => Task.FromResult<TestResult?>(new TestResult { Value = "a" }));

        await _deduplicator.ExecuteOnceAsync(
            "key-b",
            () => Task.FromResult<TestResult?>(new TestResult { Value = "b" }));

        var factoryCallCount = 0;

        // Act
        _deduplicator.InvalidateAll();

        // Assert - 両方とも再実行される
        var result1 = await _deduplicator.ExecuteOnceAsync(
            "key-a",
            () =>
            {
                factoryCallCount++;
                return Task.FromResult<TestResult?>(new TestResult { Value = "a-new" });
            });

        var result2 = await _deduplicator.ExecuteOnceAsync(
            "key-b",
            () =>
            {
                factoryCallCount++;
                return Task.FromResult<TestResult?>(new TestResult { Value = "b-new" });
            });

        Assert.Equal("a-new", result1?.Value);
        Assert.Equal("b-new", result2?.Value);
        Assert.Equal(2, factoryCallCount);
    }

    #endregion

    #region Cache Duration Tests

    [Fact]
    public async Task ExecuteOnceAsync_UsesCacheDurationsFromStaticClass()
    {
        // Assert - ApiCacheDurationsの各値が適切に設定されているか確認
        Assert.Equal(TimeSpan.FromSeconds(90), ApiCacheDurations.BonusTokens);
        Assert.Equal(TimeSpan.FromSeconds(45), ApiCacheDurations.QuotaStatus);
        Assert.Equal(TimeSpan.FromMinutes(5), ApiCacheDurations.PromotionStatus);
        Assert.Equal(TimeSpan.FromMinutes(10), ApiCacheDurations.ConsentStatus);
        Assert.Equal(TimeSpan.FromSeconds(30), ApiCacheDurations.Default);
    }

    #endregion

    /// <summary>
    /// テスト用結果クラス
    /// </summary>
    private sealed class TestResult
    {
        public string Value { get; init; } = string.Empty;
    }
}
