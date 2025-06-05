using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.Translation;

// IHttpClientFactory を自前で定義
public interface IMyHttpClientFactory
{
    HttpClient CreateClient(string name);
}

/// <summary>
/// HttpClientFactoryに関するヘルパークラス
/// </summary>
public static class HttpClientFactoryHelper
{
    /// <summary>
    /// HttpClientFactoryの場所を説明する
    /// </summary>
    /// <remarks>
    /// これはデモンストレーション用のメソッドです
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters",
        Justification = "デモンストレーション用のメソッドであり、ローカライズの必要がない")]
    public static void ShowWhereIsHttpClientFactory()
    {
        // IHttpClientFactory の名前空間の確認方法
        // 参考: Microsoft.Extensions.Http.IHttpClientFactory
        // メッセージを直接定義
        var msgInfo = "HttpClientFactoryは Microsoft.Extensions.Http 名前空間にあります";
        var msgPackage = "Microsoft.Extensions.Http パッケージが必要です";
        
        Console.WriteLine(msgInfo);
        Console.WriteLine(msgPackage);
        
        // DIサービスとしても登録
        var services = new ServiceCollection();
        services.AddHttpClient(); // これにより IHttpClientFactory が登録される
        
        // 使用例
        var serviceProvider = services.BuildServiceProvider();
        using (serviceProvider)
        {
            _ = serviceProvider.GetService<IHttpClientFactory>();
        }
    }
}
