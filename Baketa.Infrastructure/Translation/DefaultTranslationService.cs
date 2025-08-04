using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
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

            // 最初のエンジンをデフォルトのアクティブエンジンとして設定
            ActiveEngine = _availableEngines[0];
            Console.WriteLine($"🔧 [DEBUG] アクティブエンジン設定: {ActiveEngine.Name} ({ActiveEngine.GetType().Name})");
            _logger.LogInformation("アクティブエンジン設定: {Name} ({Type})", ActiveEngine.Name, ActiveEngine.GetType().Name);
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
            Console.WriteLine($"🚀 [DEBUG] DefaultTranslationService.TranslateAsync メソッド開始");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 [DEBUG] DefaultTranslationService.TranslateAsync メソッド開始{Environment.NewLine}");
            
            ArgumentNullException.ThrowIfNull(text, nameof(text));
            ArgumentNullException.ThrowIfNull(sourceLang, nameof(sourceLang));
            ArgumentNullException.ThrowIfNull(targetLang, nameof(targetLang));

            Console.WriteLine($"🔧 [DEBUG] DefaultTranslationService.TranslateAsync - テキスト: '{text}', アクティブエンジン: {ActiveEngine.Name}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 [DEBUG] DefaultTranslationService.TranslateAsync - テキスト: '{text}', アクティブエンジン: {ActiveEngine.Name}{Environment.NewLine}");
            _logger.LogInformation("翻訳開始 - テキスト: '{Text}', エンジン: {Engine}", text, ActiveEngine.Name);

            // TransModelsをそのまま使用
            var request = new TransModels.TranslationRequest
            {
                SourceText = text,
                SourceLanguage = sourceLang,
                TargetLanguage = targetLang,
                Context = context != null ? new TransModels.TranslationContext { DialogueId = context } : null
            };

            Console.WriteLine($"🔧 [DEBUG] ActiveEngine.TranslateAsync呼び出し開始 - エンジン: {ActiveEngine.GetType().Name}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 [DEBUG] ActiveEngine.TranslateAsync呼び出し開始 - エンジン: {ActiveEngine.GetType().Name}{Environment.NewLine}");
            // 翻訳実行
            var result = await ActiveEngine.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"🔧 [DEBUG] ActiveEngine.TranslateAsync呼び出し完了 - 結果: {result?.TranslatedText ?? "null"}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 [DEBUG] ActiveEngine.TranslateAsync呼び出し完了 - 結果: {result?.TranslatedText ?? "null"}{Environment.NewLine}");
            
            Console.WriteLine($"🚀 [DEBUG] DefaultTranslationService.TranslateAsync メソッド終了");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 [DEBUG] DefaultTranslationService.TranslateAsync メソッド終了{Environment.NewLine}");
            return result;
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
