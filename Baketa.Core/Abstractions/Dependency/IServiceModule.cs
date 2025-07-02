using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Core.Abstractions.Dependency;

    /// <summary>
    /// サービス登録を行うモジュールを表すインターフェース
    /// </summary>
    public interface IServiceModule
    {
        /// <summary>
        /// サービスを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        void RegisterServices(IServiceCollection services);
    }
