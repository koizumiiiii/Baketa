namespace Baketa.Core.Abstractions.DI
{
    /// <summary>
    /// サービス登録モジュールインターフェース
    /// </summary>
    public interface IServiceModule
    {
        /// <summary>
        /// サービスの登録
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        void RegisterServices(object services);
        
        /// <summary>
        /// モジュール名
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// モジュールの説明
        /// </summary>
        string Description { get; }
        
        /// <summary>
        /// モジュールの優先順位（低い値ほど先に登録される）
        /// </summary>
        int Priority { get; }
        
        /// <summary>
        /// 依存するモジュール名の配列
        /// </summary>
        string[] Dependencies { get; }
    }
}