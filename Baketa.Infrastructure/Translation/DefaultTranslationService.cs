using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using TransModels = Baketa.Core.Translation.Models;

namespace Baketa.Infrastructure.Translation;

    /// <summary>
    /// 翻訳サービスの標準実装
    /// </summary>
    public class DefaultTranslationService : ITranslationService
    {
        private readonly ILogger<DefaultTranslationService> _logger;
        private readonly List<ITranslationEngine> _availableEngines;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="logger">ロガー</param>
        /// <param name="engines">利用可能な翻訳エンジンのコレクション</param>
        /// <param name="configuration">設定サービス</param>
        public DefaultTranslationService(
            ILogger<DefaultTranslationService> logger,
            IEnumerable<ITranslationEngine> engines,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _availableEngines = engines?.ToList() ?? throw new ArgumentNullException(nameof(engines));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            
            Console.WriteLine($"🔧 [DEBUG] DefaultTranslationService作成 - エンジン数: {_availableEngines.Count}");
            _logger.LogInformation("DefaultTranslationService作成 - エンジン数: {Count}", _availableEngines.Count);
            
            foreach (var engine in _availableEngines)
            {
                Console.WriteLine($"🔧 [DEBUG] 登録エンジン: {engine.Name} ({engine.GetType().Name})");
                _logger.LogInformation("登録エンジン: {Name} ({Type})", engine.Name, engine.GetType().Name);
            }

            if (_availableEngines.Count == 0)
            {
                throw new ArgumentException("少なくとも1つの翻訳エンジンが必要です。", nameof(engines));
            }

            // 設定から翻訳エンジンを選択
            ActiveEngine = SelectEngineFromConfiguration();
            Console.WriteLine($"🎯 [CONFIG] アクティブエンジン設定完了: {ActiveEngine.Name} ({ActiveEngine.GetType().Name})");
            _logger.LogInformation("アクティブエンジン設定完了: {Name} ({Type})", ActiveEngine.Name, ActiveEngine.GetType().Name);
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
        /// 設定ファイルからデフォルトエンジンを選択します
        /// </summary>
        /// <returns>選択されたエンジン（設定にマッチしない場合は最初のエンジン）</returns>
        private ITranslationEngine SelectEngineFromConfiguration()
        {
            var defaultEngineName = _configuration["Translation:DefaultEngine"];
            
            Console.WriteLine($"🔍 [CONFIG] appsettings.json設定読み込み: Translation:DefaultEngine = '{defaultEngineName}'");
            _logger.LogInformation("設定からデフォルトエンジン読み込み: {DefaultEngine}", defaultEngineName);
            
            if (!string.IsNullOrEmpty(defaultEngineName))
            {
                // 設定されたエンジン名に基づいてマッチングを試行
                var matchedEngine = FindEngineByName(defaultEngineName);
                if (matchedEngine != null)
                {
                    Console.WriteLine($"✅ [CONFIG] 設定マッチ成功: {matchedEngine.Name} を使用");
                    _logger.LogInformation("設定マッチ成功: {EngineName} を使用", matchedEngine.Name);
                    return matchedEngine;
                }
                
                Console.WriteLine($"⚠️ [CONFIG] 設定エンジン '{defaultEngineName}' が見つかりません。フォールバック実行");
                _logger.LogWarning("設定エンジン '{DefaultEngine}' が見つかりません。フォールバックします", defaultEngineName);
            }
            
            // フォールバック: 最初のエンジンを使用
            var fallbackEngine = _availableEngines[0];
            Console.WriteLine($"🔄 [FALLBACK] デフォルトエンジン選択: {fallbackEngine.Name}");
            _logger.LogInformation("フォールバックでデフォルトエンジン選択: {EngineName}", fallbackEngine.Name);
            return fallbackEngine;
        }
        
        /// <summary>
        /// エンジン名に基づいてマッチするエンジンを検索します
        /// </summary>
        /// <param name="engineName">検索するエンジン名</param>
        /// <returns>マッチしたエンジン（見つからない場合はnull）</returns>
        private ITranslationEngine? FindEngineByName(string engineName)
        {
            // 1. 完全一致検索
            var exactMatch = _availableEngines.FirstOrDefault(e => 
                string.Equals(e.Name, engineName, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null) 
            {
                Console.WriteLine($"📍 [MATCH] 完全一致: {exactMatch.Name}");
                return exactMatch;
            }
            
            // 2. 部分一致検索（NLLB-200 → 'NLLB' 含むエンジン）
            var partialMatch = _availableEngines.FirstOrDefault(e => 
                e.Name.Contains(engineName, StringComparison.OrdinalIgnoreCase) || 
                engineName.Contains(e.Name, StringComparison.OrdinalIgnoreCase));
            if (partialMatch != null) 
            {
                Console.WriteLine($"📍 [MATCH] 部分一致: {partialMatch.Name}");
                return partialMatch;
            }
            
            // 3. エンジンタイプ名による検索（OptimizedPythonTranslationEngine等）
            var typeMatch = _availableEngines.FirstOrDefault(e => 
                e.GetType().Name.Contains(engineName, StringComparison.OrdinalIgnoreCase));
            if (typeMatch != null) 
            {
                Console.WriteLine($"📍 [MATCH] タイプ名一致: {typeMatch.GetType().Name}");
                return typeMatch;
            }
            
            Console.WriteLine($"❌ [MATCH] マッチ失敗: '{engineName}' に該当するエンジンが見つかりません");
            return null;
        }

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

            _logger.LogInformation("翻訳開始 - テキスト: '{Text}', エンジン: {Engine}", text, ActiveEngine.Name);

            // TransModelsをそのまま使用
            var request = new TransModels.TranslationRequest
            {
                SourceText = text,
                SourceLanguage = sourceLang,
                TargetLanguage = targetLang,
                Context = context != null ? new TransModels.TranslationContext { DialogueId = context } : null
            };

            // 翻訳実行
            var result = await ActiveEngine.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("翻訳結果 - IsSuccess: {IsSuccess}, Text: '{Text}'", result?.IsSuccess, result?.TranslatedText);
            
            return result!;
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

            _logger.LogInformation("バッチ翻訳開始 - テキスト数: {Count}, エンジン: {Engine}", texts.Count, ActiveEngine.Name);

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
            var result = await ActiveEngine.TranslateBatchAsync(transRequests, cancellationToken)
                .ConfigureAwait(false);
                
            _logger.LogInformation("バッチ翻訳完了 - 結果数: {Count}", result?.Count ?? 0);
            return result!;
        }
    }
