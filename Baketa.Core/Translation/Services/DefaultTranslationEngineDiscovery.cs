using Baketa.Core.Translation.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Factories;
using Microsoft.Extensions.Logging;

// 明示的なエイリアス定義
using CoreTranslationEngine = Baketa.Core.Abstractions.Translation.ITranslationEngine;
using TranslationEngineInterface = Baketa.Core.Translation.Abstractions.ITranslationEngine;

namespace Baketa.Core.Translation.Services
{
    /// <summary>
    /// 翻訳エンジン検出サービスの実装
    /// </summary>
    public class DefaultTranslationEngineDiscovery : Baketa.Core.Translation.Abstractions.ITranslationEngineDiscovery
    {
        private readonly ILogger<DefaultTranslationEngineDiscovery> _logger;
        private readonly ITranslationEngineFactory _engineFactory;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="logger">ロガー</param>
        /// <param name="engineFactory">翻訳エンジンファクトリー</param>
        public DefaultTranslationEngineDiscovery(
            ILogger<DefaultTranslationEngineDiscovery> logger,
            ITranslationEngineFactory engineFactory)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _engineFactory = engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
        }
        
        /// <summary>
        /// 利用可能な翻訳エンジン名の一覧を取得します
        /// </summary>
        /// <returns>翻訳エンジン名のリスト</returns>
        public async Task<IReadOnlyList<string>> GetAvailableEngineNamesAsync()
        {
            var engines = await _engineFactory.GetAvailableEnginesAsync().ConfigureAwait(false);
            return engines.Select(e => e.Name).ToList();
        }
        
        /// <summary>
        /// 指定した名前のエンジンを取得します
        /// </summary>
        /// <param name="engineName">エンジン名</param>
        /// <returns>見つかればエンジン、見つからなければnull</returns>
        public async Task<TranslationEngineInterface?> GetEngineByNameAsync(string engineName)
        {
            if (string.IsNullOrWhiteSpace(engineName))
            {
                throw new ArgumentException("エンジン名が無効です。", nameof(engineName));
            }
            
            var engine = await _engineFactory.GetEngineAsync(engineName).ConfigureAwait(false);
            
            // ファクトリから取得したエンジンをアダプト
            return engine != null ? CreateEngineAdapter(engine) : null;
        }
        
        /// <summary>
        /// 指定した言語ペアに最適なエンジンを取得します
        /// </summary>
        /// <param name="languagePair">言語ペア</param>
        /// <returns>最適なエンジン、見つからなければnull</returns>
        public async Task<TranslationEngineInterface?> GetBestEngineForLanguagePairAsync(Translation.Models.LanguagePair languagePair)
        {
            ArgumentNullException.ThrowIfNull(languagePair, nameof(languagePair));
            
            // 内部実装では先にSourceLanguageとTargetLanguageで呼び出しているため、
            // ここではそこを利用する
            return await GetBestEngineForLanguagePairAsync(languagePair.SourceLanguage, languagePair.TargetLanguage).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 指定した言語ペアに最適なエンジンを取得します
        /// </summary>
        /// <param name="sourceLang">元言語</param>
        /// <param name="targetLang">対象言語</param>
        /// <returns>見つかればエンジン、見つからなければnull</returns>
        public async Task<TranslationEngineInterface?> GetBestEngineForLanguagePairAsync(Translation.Models.Language sourceLang, Translation.Models.Language targetLang)
        {
            ArgumentNullException.ThrowIfNull(sourceLang, nameof(sourceLang));
            ArgumentNullException.ThrowIfNull(targetLang, nameof(targetLang));
            
            var languagePair = new Core.Models.Translation.LanguagePair
            {
                SourceLanguage = new Core.Models.Translation.Language
                {
                    Code = sourceLang.Code,
                    Name = sourceLang.Code // DisplayNameがないのでCodeを使用
                },
                TargetLanguage = new Core.Models.Translation.Language
                {
                    Code = targetLang.Code,
                    Name = targetLang.Code // DisplayNameがないのでCodeを使用
                }
            };
            
            var engine = await _engineFactory.GetBestEngineForLanguagePairAsync(languagePair).ConfigureAwait(false);
            return engine != null ? CreateEngineAdapter(engine) : null;
        }
        
        /// <summary>
        /// 指定した言語ペアをサポートする翻訳エンジン名の一覧を取得します
        /// </summary>
        /// <param name="sourceLang">元言語</param>
        /// <param name="targetLang">対象言語</param>
        /// <returns>サポートするエンジン名のリスト</returns>
        public async Task<IReadOnlyList<string>> GetSupportedEnginesForLanguagePairAsync(Translation.Models.Language sourceLang, Translation.Models.Language targetLang)
        {
            ArgumentNullException.ThrowIfNull(sourceLang, nameof(sourceLang));
            ArgumentNullException.ThrowIfNull(targetLang, nameof(targetLang));
            
            var languagePair = new Core.Models.Translation.LanguagePair
            {
                SourceLanguage = new Core.Models.Translation.Language
                {
                    Code = sourceLang.Code,
                    Name = sourceLang.Code // DisplayNameがないのでCodeを使用
                },
                TargetLanguage = new Core.Models.Translation.Language
                {
                    Code = targetLang.Code,
                    Name = targetLang.Code // DisplayNameがないのでCodeを使用
                }
            };
            
            var engines = await _engineFactory.GetEnginesForLanguagePairAsync(languagePair).ConfigureAwait(false);
            return engines.Select(e => e.Name).ToList();
        }
        
        /// <summary>
        /// 言語検出に最適なエンジンを取得します
        /// </summary>
        /// <returns>見つかればエンジン、見つからなければnull</returns>
        public async Task<TranslationEngineInterface?> GetBestLanguageDetectionEngineAsync()
        {
            // 現在の実装では、最初に利用可能なエンジンを返す
            var engines = await _engineFactory.GetAvailableEnginesAsync().ConfigureAwait(false);
            
            // 理想的にはここで言語検出機能をサポートしているエンジンをフィルタリングする
            // ただし、現在のインターフェースではこの機能をチェックする方法がないため、
            // 利用可能な最初のエンジンを返す
            
            // FirstOrDefaultを使うよりも、インデックスアクセスが効率的
            var engine = engines.Count > 0 ? engines[0] : null;
            
            if (engine != null)
            {
                // インターフェースを返すように変更し、CA1859警告を解消
                TranslationEngineInterface adapter = new TranslationEngineAdapter(engine);
                return adapter; // このオブジェクトは呼び出し元が破棄責任を持つ
            }
            
            return null;
        }
        
        /// <summary>
        /// コアの翻訳エンジンを翻訳アブストラクション層のエンジンにアダプトします
        /// </summary>
        /// <param name="coreEngine">コアの翻訳エンジン</param>
        /// <returns>アダプトされた翻訳エンジン</returns>
        private static TranslationEngineInterface CreateEngineAdapter(CoreTranslationEngine coreEngine)
        {            
            // インターフェースを返す形式に変更し、CA1859警告を解消
            TranslationEngineInterface adapter = new TranslationEngineAdapter(coreEngine);
            return adapter;
        }
    }
}