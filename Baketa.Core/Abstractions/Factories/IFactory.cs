namespace Baketa.Core.Abstractions.Factories;

    /// <summary>
    /// ファクトリーインターフェースの基本型
    /// </summary>
    /// <typeparam name="TEntity">生成するエンティティの型</typeparam>
    public interface IFactory<out TEntity> where TEntity : class
    {
        /// <summary>
        /// エンティティを作成
        /// </summary>
        /// <returns>作成されたエンティティ</returns>
        TEntity Create();
    }
    
    /// <summary>
    /// パラメータ付きファクトリーインターフェースの基本型
    /// </summary>
    /// <typeparam name="TEntity">生成するエンティティの型</typeparam>
    /// <typeparam name="TParameter">パラメータの型</typeparam>
    public interface IFactory<out TEntity, in TParameter> where TEntity : class
    {
        /// <summary>
        /// エンティティを作成
        /// </summary>
        /// <param name="parameter">作成パラメータ</param>
        /// <returns>作成されたエンティティ</returns>
        TEntity Create(TParameter parameter);
    }
    
    /// <summary>
    /// 複数パラメータ付きファクトリーインターフェースの基本型
    /// </summary>
    /// <typeparam name="TEntity">生成するエンティティの型</typeparam>
    /// <typeparam name="TParameter1">パラメータ1の型</typeparam>
    /// <typeparam name="TParameter2">パラメータ2の型</typeparam>
    public interface IFactory<out TEntity, in TParameter1, in TParameter2> where TEntity : class
    {
        /// <summary>
        /// エンティティを作成
        /// </summary>
        /// <param name="parameter1">作成パラメータ1</param>
        /// <param name="parameter2">作成パラメータ2</param>
        /// <returns>作成されたエンティティ</returns>
        TEntity Create(TParameter1 parameter1, TParameter2 parameter2);
    }
