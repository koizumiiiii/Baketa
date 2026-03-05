using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// モデルのオンデマンドアンロードが可能な翻訳エンジン
/// </summary>
public interface IUnloadableTranslationEngine : ITranslationEngine
{
    /// <summary>
    /// ロード済みモデルをメモリからアンロードする。
    /// 次回の TranslateAsync 呼び出し時に自動で再ロードされる。
    /// </summary>
    Task UnloadModelsAsync();
}
