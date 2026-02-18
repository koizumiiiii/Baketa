using System;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// ローカル翻訳エンジンのインターフェース
/// </summary>
public interface ILocalTranslationEngine : ITranslationEngine
{
    /// <summary>
    /// モデルのパス
    /// </summary>
    string ModelPath { get; }

    /// <summary>
    /// 使用中のデバイス
    /// </summary>
    ComputeDevice Device { get; }

    /// <summary>
    /// モデルのメモリ使用量
    /// </summary>
    long MemoryUsage { get; }

    /// <summary>
    /// モデルローダーの取得
    /// </summary>
    IModelLoader GetModelLoader();

    /// <summary>
    /// トークナイザーの取得
    /// </summary>
    ITokenizer GetTokenizer();

    /// <summary>
    /// モデルを指定デバイスにロード
    /// </summary>
    Task<bool> LoadModelToDeviceAsync(ComputeDevice device);

    /// <summary>
    /// モデルをアンロード
    /// </summary>
    Task<bool> UnloadModelAsync();
}
