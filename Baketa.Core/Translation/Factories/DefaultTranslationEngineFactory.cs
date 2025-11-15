using System;
using System.Collections.Generic;
using System.Linq;
using Baketa.Core.Abstractions.Factories;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Translation.Factories;

/// <summary>
/// デフォルトの翻訳エンジンファクトリー実装
/// </summary>
public class DefaultTranslationEngineFactory : ITranslationEngineFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DefaultTranslationEngineFactory> _logger;
    private readonly Dictionary<TranslationEngine, ITranslationEngine> _engineCache = [];

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="serviceProvider">サービスプロバイダー</param>
    /// <param name="logger">ロガー</param>
    public DefaultTranslationEngineFactory(
        IServiceProvider serviceProvider,
        ILogger<DefaultTranslationEngineFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 利用可能なエンジンを検索して初期化
        InitializeEngines();
    }

    /// <summary>
    /// 指定されたタイプの翻訳エンジンを作成します
    /// </summary>
    /// <param name="engineType">翻訳エンジンの種類</param>
    /// <returns>翻訳エンジンインスタンス</returns>
    public ITranslationEngine CreateEngine(TranslationEngine engineType)
    {
        if (_engineCache.TryGetValue(engineType, out var engine))
        {
            return engine;
        }

        _logger.LogWarning("要求された翻訳エンジン {EngineType} は利用できません", engineType);
        throw new ArgumentException($"翻訳エンジン {engineType} は利用できません");
    }

    /// <summary>
    /// 指定されたタイプの翻訳エンジンがサポートされているかどうかを確認します
    /// </summary>
    /// <param name="engineType">翻訳エンジンの種類</param>
    /// <returns>サポートされている場合はtrue</returns>
    public bool IsEngineSupported(TranslationEngine engineType)
    {
        return _engineCache.ContainsKey(engineType);
    }

    /// <summary>
    /// 利用可能な翻訳エンジンの種類の配列を取得します
    /// </summary>
    /// <returns>利用可能な翻訳エンジンの種類の配列</returns>
    public TranslationEngine[] GetAvailableEngines()
    {
        return [.. _engineCache.Keys];
    }

    /// <summary>
    /// 利用可能なエンジンを初期化します
    /// </summary>
    private void InitializeEngines()
    {
        try
        {
            var engines = _serviceProvider.GetServices<ITranslationEngine>();

            foreach (var engine in engines)
            {
                if (_engineCache.TryAdd(engine.EngineType, engine))
                {
                    _logger.LogInformation("翻訳エンジン {EngineName} ({EngineType}) を登録しました",
                        engine.Name, engine.EngineType);
                }
            }

            _logger.LogInformation("合計 {Count} 個の翻訳エンジンが利用可能です", _engineCache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳エンジンの初期化中にエラーが発生しました");
            throw;
        }
    }
}
