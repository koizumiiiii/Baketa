using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Core.Abstractions.DI;

/// <summary>
/// サービス登録モジュールのインターフェース。
/// モジュール化された依存性注入パターンを実現するための基盤となります。
/// </summary>
public interface IServiceModule
{
    /// <summary>
    /// このモジュールが提供するサービスを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    void RegisterServices(IServiceCollection services);

    /// <summary>
    /// このモジュールが依存する他のモジュールの型を取得します。
    /// デフォルトでは依存モジュールはありません。
    /// </summary>
    /// <returns>依存モジュールの型のコレクション</returns>
    IEnumerable<Type> GetDependentModules() => [];
}
