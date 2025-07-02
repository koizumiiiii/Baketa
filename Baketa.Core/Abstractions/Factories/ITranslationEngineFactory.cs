using Baketa.Core.Abstractions.Services;

namespace Baketa.Core.Abstractions.Factories;

    /// <summary>
    /// 翻訳エンジンファクトリーインターフェース
    /// </summary>
    public interface ITranslationEngineFactory
    {
        /// <summary>
        /// 指定されたタイプの翻訳エンジンを作成します
        /// </summary>
        /// <param name="engineType">翻訳エンジンの種類</param>
        /// <returns>翻訳エンジンインスタンス</returns>
        ITranslationEngine CreateEngine(TranslationEngine engineType);
        
        /// <summary>
        /// 指定されたタイプの翻訳エンジンがサポートされているかどうかを確認します
        /// </summary>
        /// <param name="engineType">翻訳エンジンの種類</param>
        /// <returns>サポートされている場合はtrue</returns>
        bool IsEngineSupported(TranslationEngine engineType);
        
        /// <summary>
        /// 利用可能な翻訳エンジンの種類の配列を取得します
        /// </summary>
        /// <returns>利用可能な翻訳エンジンの種類の配列</returns>
        TranslationEngine[] GetAvailableEngines();
    }
    
    /// <summary>
    /// 翻訳エンジンインターフェース
    /// </summary>
    public interface ITranslationEngine
    {
        /// <summary>
        /// 翻訳エンジンの種類
        /// </summary>
        TranslationEngine EngineType { get; }
        
        /// <summary>
        /// 翻訳エンジンの名前
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// 翻訳エンジンの初期化状態
        /// </summary>
        bool IsInitialized { get; }
        
        /// <summary>
        /// 翻訳エンジンを初期化します
        /// </summary>
        /// <param name="settings">翻訳設定</param>
        /// <returns>初期化に成功した場合はtrue</returns>
        bool Initialize(TranslationSettings settings);
        
        /// <summary>
        /// 翻訳エンジンの終了処理を行います
        /// </summary>
        void Shutdown();
        
        /// <summary>
        /// 指定された言語がサポートされているかを確認します
        /// </summary>
        /// <param name="languageCode">言語コード</param>
        /// <param name="asSource">ソース言語としてサポートされているか</param>
        /// <returns>サポートされている場合はtrue</returns>
        bool IsLanguageSupported(string languageCode, bool asSource = true);
    }
