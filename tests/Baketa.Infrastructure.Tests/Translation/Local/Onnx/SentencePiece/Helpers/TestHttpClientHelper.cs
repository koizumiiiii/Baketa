using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Moq.Protected;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece.Helpers;

/// <summary>
/// SentencePieceテスト用のHTTPクライアントヘルパー
/// </summary>
public static class TestHttpClientHelper
{
    /// <summary>
    /// 成功レスポンスを返すモックHTTPクライアントファクトリーを作成
    /// </summary>
    public static IHttpClientFactory CreateMockHttpClientFactory(
        HttpStatusCode statusCode = HttpStatusCode.OK,
        string content = "Mock model content",
        string etag = "test-etag")
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
#pragma warning disable CA2000 // Dispose objects before losing scope - HttpResponseMessageはMock経由で管理される
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content),
                Headers = { ETag = new EntityTagHeaderValue($"\"{etag}\"") }
            });
#pragma warning restore CA2000

        var httpClient = new HttpClient(mockHandler.Object);
        return new TestHttpClientFactory(httpClient);
    }
    
    /// <summary>
    /// 404 Not Foundレスポンスを返すHTTPクライアントファクトリーを作成
    /// </summary>
    public static IHttpClientFactory CreateNotFoundHttpClientFactory()
    {
        return CreateMockHttpClientFactory(HttpStatusCode.NotFound, "", "");
    }
    
    /// <summary>
    /// 遅延レスポンスを返すHTTPクライアントファクトリーを作成（キャンセレーションテスト用）
    /// </summary>
    public static IHttpClientFactory CreateDelayedHttpClientFactory(int delayMs = 5000)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken ct) =>
            {
                await Task.Delay(delayMs, ct).ConfigureAwait(false);
#pragma warning disable CA2000 // Dispose objects before losing scope - HttpResponseMessageはMock経由で管理される
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("Delayed content")
                };
#pragma warning restore CA2000
            });

        var httpClient = new HttpClient(mockHandler.Object);
        return new TestHttpClientFactory(httpClient);
    }
}

/// <summary>
/// テスト用のHttpClientFactory実装
/// Moqで拡張メソッドをモックできない問題を回避
/// </summary>
public class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    private readonly HttpClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public HttpClient CreateClient(string name)
    {
        return _client;
    }
}
