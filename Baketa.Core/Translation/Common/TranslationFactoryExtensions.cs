using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Factories;

using CoreTranslationEngine = Baketa.Core.Abstractions.Translation.ITranslationEngine;
using FactoryTranslationEngine = Baketa.Core.Abstractions.Factories.ITranslationEngine;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Common;

    /// <summary>
    /// 翻訳エンジンファクトリーの拡張メソッド
    /// </summary>
    public static class TranslationFactoryExtensions
    {
        /// <summary>
        /// 利用可能なエンジンを非同期で取得します
        /// </summary>
        /// <param name="factory">翻訳エンジンファクトリー</param>
        /// <returns>利用可能なエンジンのリスト</returns>
#pragma warning disable CA1849 // 非同期メソッド内での同期メソッド呼び出しを許可
        public static async Task<IReadOnlyList<CoreTranslationEngine>> GetAvailableEnginesAsync(this ITranslationEngineFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);

            // 将来的にここは実際に非同期処理を行う可能性があるため非同期メソッドとして準備
            // リリース後の実装で非同期APIを呼び出す予定のため、ここではConfigureAwait(false)を使用
            await Task.CompletedTask.ConfigureAwait(false);
            
            // GetAvailableEnginesの結果を同期的に取得
            var engineTypes = factory.GetAvailableEngines();
            var engines = new List<CoreTranslationEngine>();
            
            // エンジンを作成して変換
            foreach (var engineType in engineTypes)
            {
                var engine = factory.CreateEngine(engineType);
                engines.Add((CoreTranslationEngine)engine);
            }

            return engines;
        }
#pragma warning restore CA1849

        /// <summary>
        /// 指定した名前のエンジンを非同期で取得します
        /// </summary>
        /// <param name="factory">翻訳エンジンファクトリー</param>
        /// <param name="engineName">エンジン名</param>
        /// <returns>見つかればエンジン、見つからなければnull</returns>
#pragma warning disable CA1849 // 非同期メソッド内での同期メソッド呼び出しを許可
        public static async Task<CoreTranslationEngine?> GetEngineAsync(this ITranslationEngineFactory factory, string engineName)
        {
            ArgumentNullException.ThrowIfNull(factory);
            
            if (string.IsNullOrWhiteSpace(engineName))
            {
                throw new ArgumentException("エンジン名が無効です", nameof(engineName));
            }

            // 将来的にここは実際に非同期処理を行う可能性があるため非同期メソッドとして準備
            await Task.CompletedTask.ConfigureAwait(false);

            // 利用可能なエンジンタイプを取得
            var engineTypes = factory.GetAvailableEngines();
            
            // 名前が一致するエンジンを検索
            foreach (var engineType in engineTypes)
            {
                var engine = factory.CreateEngine(engineType);
                if (string.Equals(engine.Name, engineName, StringComparison.OrdinalIgnoreCase))
                {
                    return (CoreTranslationEngine)engine;
                }
            }

            // 一致するエンジンが見つからなかった場合
            return null;
        }
#pragma warning restore CA1849

        /// <summary>
        /// 指定した言語ペアに最適なエンジンを非同期で取得します
        /// </summary>
        /// <param name="factory">翻訳エンジンファクトリー</param>
        /// <param name="languagePair">言語ペア</param>
        /// <returns>見つかればエンジン、見つからなければnull</returns>
#pragma warning disable CA1849 // 非同期メソッド内での同期メソッド呼び出しを許可
        public static async Task<CoreTranslationEngine?> GetBestEngineForLanguagePairAsync(
            this ITranslationEngineFactory factory, 
            LanguagePair languagePair)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentNullException.ThrowIfNull(languagePair);

            // 将来的に非同期APIを呼び出す予定のため、ConfigureAwait(false)を使用
            await Task.CompletedTask.ConfigureAwait(false);

            // ランク付けと適合度を考慮した最適エンジン選択
            var engineTypes = factory.GetAvailableEngines();
            FactoryTranslationEngine? bestEngine = null;
            int bestRanking = -1;
            
            // すべてのエンジンを評価
            foreach (var engineType in engineTypes)
            {
                var engine = factory.CreateEngine(engineType);
                
                // 基本的な言語ペア対応チェック
                if (engine.IsLanguageSupported(languagePair.SourceLanguage.Code, asSource: true) &&
                    engine.IsLanguageSupported(languagePair.TargetLanguage.Code, asSource: false))
                {
                    // 将来的にはエンジンの品質や速度などを考慮してランク付けを行う
                    // 現時点では最初に見つかったものを返す
                    int ranking = 1; // デフォルトランク
                    
                    // より高ランクのエンジンであれば更新
                    if (ranking > bestRanking)
                    {
                        bestRanking = ranking;
                        bestEngine = engine;
                    }
                }
            }

            // 最適なエンジンが見つかった場合は変換して返す
            return bestEngine != null ? (CoreTranslationEngine)bestEngine : null;
        }
#pragma warning restore CA1849

        /// <summary>
        /// 指定した言語ペアをサポートするエンジンを非同期で取得します
        /// </summary>
        /// <param name="factory">翻訳エンジンファクトリー</param>
        /// <param name="languagePair">言語ペア</param>
        /// <returns>エンジンのリスト</returns>
#pragma warning disable CA1849 // 非同期メソッド内での同期メソッド呼び出しを許可
        public static async Task<IReadOnlyList<CoreTranslationEngine>> GetEnginesForLanguagePairAsync(
            this ITranslationEngineFactory factory, 
            LanguagePair languagePair)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentNullException.ThrowIfNull(languagePair);

            // 将来的にここは実際に非同期処理を行う可能性があるので、非同期メソッドに変更
            await Task.CompletedTask.ConfigureAwait(false);

            var supportedEngines = new List<CoreTranslationEngine>();
            var engines = factory.GetAvailableEngines();
            
            foreach (var engineType in engines)
            {
                var engine = factory.CreateEngine(engineType);
                if (engine.IsLanguageSupported(languagePair.SourceLanguage.Code, asSource: true) &&
                    engine.IsLanguageSupported(languagePair.TargetLanguage.Code, asSource: false))
                {
                    supportedEngines.Add((CoreTranslationEngine)engine);
                }
            }

            return supportedEngines;
        }
#pragma warning restore CA1849
    }
