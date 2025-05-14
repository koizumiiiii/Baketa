using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

// 名前空間エイリアスを定義して曖昧さを回避
using CoreModels = Baketa.Core.Models.Translation;
using TransModels = Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Testing
{
    /// <summary>
    /// 極めて単純な実装テスト用
    /// </summary>
    public class SimpleEngine : TranslationEngineBase
    {
        private static readonly List<CoreModels.LanguagePair> _supportedLanguagePairs = new()
        {
            new CoreModels.LanguagePair 
            { 
                SourceLanguage = new CoreModels.Language { Code = "ja", Name = "Japanese" }, 
                TargetLanguage = new CoreModels.Language { Code = "en", Name = "English" } 
            },
            new CoreModels.LanguagePair 
            { 
                SourceLanguage = new CoreModels.Language { Code = "en", Name = "English" }, 
                TargetLanguage = new CoreModels.Language { Code = "ja", Name = "Japanese" } 
            },
        };

        /// <summary>
        /// 翻訳エンジン名
        /// </summary>
        public override string Name => "SimpleEngine";

        /// <summary>
        /// 翻訳エンジンの説明
        /// </summary>
        public override string Description => "単純なテスト実装";

        /// <summary>
        /// ネットワーク接続が必要か
        /// </summary>
        public override bool RequiresNetwork => false;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public SimpleEngine() 
            : base(NullLogger<TranslationEngineBase>.Instance)
        {
        }

        /// <summary>
        /// サポートされている言語ペアを取得
        /// </summary>
        public override Task<IReadOnlyCollection<CoreModels.LanguagePair>> GetSupportedLanguagePairsAsync()
        {
            return Task.FromResult<IReadOnlyCollection<CoreModels.LanguagePair>>(_supportedLanguagePairs);
        }

        /// <summary>
        /// エンジン固有の翻訳処理を実装
        /// </summary>
        protected override Task<CoreModels.TranslationResponse> TranslateInternalAsync(
            CoreModels.TranslationRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            // 非常に単純なダミー実装
            string translated = request.SourceLanguage.Code switch
            {
                "ja" => $"[JA→EN Simple] {request.SourceText}",
                "en" => $"[EN→JA Simple] {request.SourceText}",
                _ => $"[UNK Simple] {request.SourceText}"
            };

            var response = new CoreModels.TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                TranslatedText = translated,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = Name,
                IsSuccess = true
            };

            return Task.FromResult(response);
        }

        /// <summary>
        /// エンジン固有の初期化処理
        /// </summary>
        protected override Task<bool> InitializeInternalAsync()
        {
            // 常に初期化成功
            return Task.FromResult(true);
        }
    }
}