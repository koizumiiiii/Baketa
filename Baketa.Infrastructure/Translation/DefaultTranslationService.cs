using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Logging;

// 名前空間エイリアスを定義
using CoreModels = Baketa.Core.Models.Translation;
using TransModels = Baketa.Core.Translation.Models;

namespace Baketa.Infrastructure.Translation
{
    /// <summary>
    /// 翻訳サービスの標準実装
    /// </summary>
    public class DefaultTranslationService : ITranslationService
    {
        private readonly ILogger<DefaultTranslationService> _logger;
        private readonly List<ITranslationEngine> _availableEngines;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="logger">ロガー</param>
        /// <param name="engines">利用可能な翻訳エンジンのコレクション</param>
        public DefaultTranslationService(
            ILogger<DefaultTranslationService> logger,
            IEnumerable<ITranslationEngine> engines)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _availableEngines = engines?.ToList() ?? throw new ArgumentNullException(nameof(engines));

            if (_availableEngines.Count == 0)
            {
                throw new ArgumentException("少なくとも1つの翻訳エンジンが必要です。", nameof(engines));
            }

            // 最初のエンジンをデフォルトのアクティブエンジンとして設定
            ActiveEngine = _availableEngines[0];
        }

        /// <summary>
        /// 利用可能な翻訳エンジンを取得します
        /// </summary>
        /// <returns>利用可能な翻訳エンジンのコレクション</returns>
        public IReadOnlyList<ITranslationEngine> GetAvailableEngines() => _availableEngines.AsReadOnly();

        /// <summary>
        /// 現在アクティブな翻訳エンジンを取得します
        /// </summary>
        public ITranslationEngine ActiveEngine { get; private set; }

        /// <summary>
        /// 指定された名前のエンジンをアクティブにします
        /// </summary>
        /// <param name="engineName">アクティブにするエンジン名</param>
        /// <returns>成功すればtrue</returns>
        public async Task<bool> SetActiveEngineAsync(string engineName)
        {
            if (string.IsNullOrEmpty(engineName))
            {
                throw new ArgumentException("エンジン名が無効です。", nameof(engineName));
            }

            var engine = _availableEngines.FirstOrDefault(e =>
                string.Equals(e.Name, engineName, StringComparison.OrdinalIgnoreCase));

            if (engine == null)
            {
                _logger.LogWarning("指定されたエンジン '{EngineName}' が見つかりません。", engineName);
                return false;
            }

            // エンジンが準備できているか確認
            var isReady = await engine.IsReadyAsync().ConfigureAwait(false);
            if (!isReady)
            {
                var initResult = await engine.InitializeAsync().ConfigureAwait(false);
                if (!initResult)
                {
                    _logger.LogError("エンジン '{EngineName}' の初期化に失敗しました。", engineName);
                    return false;
                }
            }

            ActiveEngine = engine;
            _logger.LogInformation("アクティブな翻訳エンジンを '{EngineName}' に変更しました。", engineName);
            return true;
        }

        /// <summary>
        /// テキストを翻訳します
        /// </summary>
        /// <param name="text">翻訳元テキスト</param>
        /// <param name="sourceLang">元言語</param>
        /// <param name="targetLang">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳結果</returns>
        public async Task<TransModels.TranslationResponse> TranslateAsync(
            string text,
            TransModels.Language sourceLang,
            TransModels.Language targetLang,
            string? context = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(text, nameof(text));
            ArgumentNullException.ThrowIfNull(sourceLang, nameof(sourceLang));
            ArgumentNullException.ThrowIfNull(targetLang, nameof(targetLang));

            // TransModelsをそのまま使用
            var request = new TransModels.TranslationRequest
            {
                SourceText = text,
                SourceLanguage = sourceLang,
                TargetLanguage = targetLang,
                Context = context != null ? new TransModels.TranslationContext { DialogueId = context } : null
            };

            // 翻訳実行
            return await ActiveEngine.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// 複数のテキストをバッチ翻訳します
        /// </summary>
        /// <param name="texts">翻訳元テキストのコレクション</param>
        /// <param name="sourceLang">元言語</param>
        /// <param name="targetLang">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳結果のコレクション</returns>
        public async Task<IReadOnlyList<TransModels.TranslationResponse>> TranslateBatchAsync(
            IReadOnlyList<string> texts,
            TransModels.Language sourceLang,
            TransModels.Language targetLang,
            string? context = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(texts, nameof(texts));
            ArgumentNullException.ThrowIfNull(sourceLang, nameof(sourceLang));
            ArgumentNullException.ThrowIfNull(targetLang, nameof(targetLang));

            if (texts.Count == 0)
            {
                throw new ArgumentException("テキストのコレクションが空です。", nameof(texts));
            }

            // リクエスト作成
            var transRequests = new List<TransModels.TranslationRequest>();
            foreach (var text in texts)
            {
                transRequests.Add(new TransModels.TranslationRequest
                {
                    SourceText = text,
                    SourceLanguage = sourceLang,
                    TargetLanguage = targetLang,
                    Context = context != null ? new TransModels.TranslationContext { DialogueId = context } : null
                });
            }

            // 翻訳実行
            return await ActiveEngine.TranslateBatchAsync(transRequests, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}