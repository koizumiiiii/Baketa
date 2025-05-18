using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Common;

    /// <summary>
    /// 初期化可能なオブジェクトを表すインターフェース
    /// </summary>
    public interface IInitializable
    {
        /// <summary>
        /// 初期化状態を取得します
        /// </summary>
        bool IsInitialized { get; }
        
        /// <summary>
        /// オブジェクトを初期化します
        /// </summary>
        /// <returns>初期化が成功した場合はtrue</returns>
        bool Initialize();
        
        /// <summary>
        /// オブジェクトの終了処理を行います
        /// </summary>
        void Shutdown();
    }
    
    /// <summary>
    /// 非同期で初期化可能なオブジェクトを表すインターフェース
    /// </summary>
    public interface IAsyncInitializable
    {
        /// <summary>
        /// 初期化状態を取得します
        /// </summary>
        bool IsInitialized { get; }
        
        /// <summary>
        /// オブジェクトを非同期で初期化します
        /// </summary>
        /// <returns>初期化が成功した場合はtrue</returns>
        Task<bool> InitializeAsync();
        
        /// <summary>
        /// オブジェクトの終了処理を非同期で行います
        /// </summary>
        Task ShutdownAsync();
    }
