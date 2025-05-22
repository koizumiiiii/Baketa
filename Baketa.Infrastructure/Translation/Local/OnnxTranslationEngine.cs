using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Exceptions;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Local;

/// <summary>
/// ONNX翻訳エンジンの基本実装
/// </summary>
public class OnnxTranslationEngine : TranslationEngineBase, ILocalTranslationEngine
{
    private readonly ILogger<OnnxTranslationEngine> _logger;
    private readonly OnnxTranslationOptions _options;
    private readonly IModelLoader _modelLoader;
    private readonly ITokenizer _tokenizer;
    
    /// <inheritdoc/>
    public override string Name => "ONNX Translation";
    
    /// <inheritdoc/>
    public override string Description => "ONNX形式のローカル翻訳モデルを使用した翻訳エンジン";
    
    /// <inheritdoc/>
    public override bool RequiresNetwork => false;
    
    /// <inheritdoc/>
    public string ModelPath { get; }
    
    /// <inheritdoc/>
    public ComputeDevice Device => _modelLoader.GetCurrentDevice();
    
    /// <inheritdoc/>
    public long MemoryUsage { get; private set; }
    
    /// <summary>
    /// モデルの言語ペア
    /// </summary>
    public LanguagePair ModelLanguagePair { get; }
    
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="modelPath">モデルパス</param>
    /// <param name="languagePair">言語ペア</param>
    /// <param name="modelLoader">モデルローダー</param>
    /// <param name="tokenizer">トークナイザー</param>
    /// <param name="options">オプション</param>
    /// <param name="logger">ロガー</param>
    public OnnxTranslationEngine(
        string modelPath,
        LanguagePair languagePair,
        IModelLoader modelLoader,
        ITokenizer tokenizer,
        OnnxTranslationOptions options,
        ILogger<OnnxTranslationEngine> logger) : base(logger)
    {
        ModelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
        ModelLanguagePair = languagePair ?? throw new ArgumentNullException(nameof(languagePair));
        _modelLoader = modelLoader ?? throw new ArgumentNullException(nameof(modelLoader));
        _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        if (!File.Exists(ModelPath))
        {
            throw new FileNotFoundException($"モデルファイルが見つかりません: {ModelPath}");
        }
    }
    
    /// <inheritdoc/>
    protected override async Task<bool> InitializeInternalAsync()
    {
        try
        {
            if (IsInitialized)
            {
                return true;
            }
            
            _logger.LogInformation("ONNXモデルの初期化を開始: {ModelPath}", ModelPath);
            
            // モデルローダーを使用してモデルをロード
            var modelOptions = new ModelOptions
            {
                MaxSequenceLength = _options.MaxSequenceLength,
                ThreadCount = _options.ThreadCount,
                OptimizationLevel = _options.OptimizationLevel,
                MemoryLimit = _options.MemoryLimitMb,
                BatchSize = _options.BatchSize,
                EnableCache = _options.EnableModelCache
            };
            
            var success = await _modelLoader.LoadModelAsync(ModelPath, modelOptions).ConfigureAwait(false);
            if (!success)
            {
                _logger.LogError("ONNXモデルのロードに失敗しました: {ModelPath}", ModelPath);
                return false;
            }
            
            IsInitialized = true;
            UpdateMemoryUsage();
            
            _logger.LogInformation("ONNXモデルの初期化に成功: {ModelPath}, デバイス: {Device}", 
                ModelPath, Device.Name);
            
            return true;
        }
        catch (System.IO.FileNotFoundException ex)
        {
            _logger.LogError(ex, "モデルファイルが見つかりません: {ModelPath}", ModelPath);
            return false;
        }
        catch (TranslationModelException ex)
        {
            _logger.LogError(ex, "モデルロード中にエラーが発生しました: {ErrorType}", ex.ErrorType);
            return false;
        }
        catch (System.IO.IOException ex)
        {
            _logger.LogError(ex, "モデルファイルの読み込み中に入出力エラーが発生しました: {Path}", ModelPath);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "モデルの初期化中に無効な操作が試行されました");
            return false;
        }
        catch (OutOfMemoryException ex)
        {
            _logger.LogError(ex, "ONNXモデルの初期化中にメモリ不足が発生しました");
            return false;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "ONNXモデルの初期化中に引数エラーが発生しました: {ExType}", ex.GetType().Name);
            return false;
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "ONNXモデルの初期化中に非対応の操作が実行されました");
            return false;
        }
    }
    
    /// <inheritdoc/>
    public override async Task<bool> IsReadyAsync()
    {
        if (IsInitialized)
            return true;
            
        try
        {
            return await InitializeAsync().ConfigureAwait(false);
        }
        catch (TranslationModelException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
    
    /// <inheritdoc/>
    protected override async Task<TranslationResponse> TranslateInternalAsync(
        TranslationRequest request, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request, nameof(request));
            
        if (!IsInitialized && !await InitializeAsync().ConfigureAwait(false))
        {
            return CreateErrorResponse(
                request, 
                TranslationErrorType.ServiceUnavailable, 
                "翻訳エンジンが初期化されていません");
        }
        
        // 言語ペアのチェック
        if (!await SupportsLanguagePairAsync(new LanguagePair 
            { 
                SourceLanguage = request.SourceLanguage, 
                TargetLanguage = request.TargetLanguage 
            }).ConfigureAwait(false))
        {
            return CreateErrorResponse(
                request,
                TranslationErrorType.UnsupportedLanguage,
                $"言語ペアがサポートされていません: {request.SourceLanguage.Code} -> {request.TargetLanguage.Code}");
        }
        
        try
        {
            int[] inputTokens;
            try
            {
                // テキストをトークン化
                inputTokens = _tokenizer.Tokenize(request.SourceText);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)
            {
                _logger.LogError(ex, "テキストのトークン化に失敗しました: {ExType}", ex.GetType().Name);
                return CreateErrorResponse(request, TranslationErrorType.InvalidRequest, $"テキストのトークン化に失敗しました: {ex.Message}");
            }
            
            // モデルの入力サイズ制限に調整
            if (inputTokens.Length > _options.MaxSequenceLength)
            {
                _logger.LogWarning("入力テキストが最大シーケンス長を超えています。切り詰めます: {Length} -> {MaxLength}",
                    inputTokens.Length, _options.MaxSequenceLength);
                
                var truncatedTokens = new int[_options.MaxSequenceLength];
                Array.Copy(inputTokens, truncatedTokens, _options.MaxSequenceLength);
                inputTokens = truncatedTokens;
            }
            
            int[] outputTokens;
            try
            {
                // トークンをモデルに入力し、翻訳を実行
                outputTokens = await TranslateTokensAsync(inputTokens, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("ユーザーにより翻訳処理がキャンセルされました");
                return CreateErrorResponse(request, TranslationErrorType.OperationCanceled, "操作がキャンセルされました");
            }
            catch (TranslationModelException ex)
            {
                _logger.LogError(ex, "モデル推論中にエラーが発生しました: {ErrorType}", ex.ErrorType);
                return CreateErrorResponse(request, ex.ErrorType, ex.Message);
            }
            catch (OutOfMemoryException ex)
            {
                _logger.LogError(ex, "モデル推論中にメモリ不足が発生しました");
                return CreateErrorResponse(request, TranslationErrorType.ProcessingError, $"メモリ不足: {ex.Message}");
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "モデル推論中に無効な操作が発生しました");
                return CreateErrorResponse(request, TranslationErrorType.ProcessingError, $"処理エラー: {ex.Message}");
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "モデル推論中に書式エラーが発生しました");
                return CreateErrorResponse(request, TranslationErrorType.ProcessingError, $"処理エラー: {ex.Message}");
            }
            
            string translatedText;
            try
            {
                // 結果をデコード
                translatedText = _tokenizer.Decode(outputTokens);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is FormatException)
            {
                _logger.LogError(ex, "翻訳結果のデコードに失敗しました: {ExType}", ex.GetType().Name);
                return CreateErrorResponse(request, TranslationErrorType.InvalidResponse, $"翻訳結果のデコードに失敗しました: {ex.Message}");
            }
            
            // 翻訳結果の作成
            var response = new TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = request.SourceText,
                TranslatedText = translatedText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = Name,
                IsSuccess = true
            };
            
            return response;
        }
        catch (TranslationException ex)
        {
            // カスタム翻訳例外の処理
            _logger.LogError(ex, "翻訳例外が発生しました: {ErrorType}", ex.ErrorType);
            return CreateErrorResponse(request, ex.ErrorType, ex.Message);
        }
        catch (System.IO.IOException ex)
        {
            // 入出力関連の例外
            _logger.LogError(ex, "入出力操作中にエラーが発生しました");
            return CreateErrorResponse(request, TranslationErrorType.ProcessingError, $"入出力エラー: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            // 無効な操作の例外
            _logger.LogError(ex, "無効な操作が発生しました");
            return CreateErrorResponse(request, TranslationErrorType.ProcessingError, $"処理エラー: {ex.Message}");
        }
        catch (OutOfMemoryException ex)
        {
            // メモリ不足の例外
            _logger.LogError(ex, "メモリ不足が発生しました");
            return CreateErrorResponse(request, TranslationErrorType.ProcessingError, $"リソース不足: {ex.Message}");
        }
        catch (FormatException ex)
        {
            // 書式関連の例外
            _logger.LogError(ex, "書式エラーが発生しました");
            return CreateErrorResponse(request, TranslationErrorType.InvalidRequest, $"無効なリクエスト: {ex.Message}");
        }
        catch (ArgumentOutOfRangeException ex)
        {
            // 引数の範囲外例外
            _logger.LogError(ex, "引数の範囲外エラーが発生しました");
            return CreateErrorResponse(request, TranslationErrorType.InvalidRequest, $"無効なリクエスト: {ex.Message}");
        }
    }
    
    /// <inheritdoc/>
    public override async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests, nameof(requests));
            
        var results = new List<TranslationResponse>(requests.Count);
        
        // バッチ翻訳の最適化
        // 本来はここでバッチ処理を行うが、簡略化のため単純なループで実装
        foreach (var request in requests)
        {
            try
            {
            var response = await TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            results.Add(response);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
            _logger.LogWarning(ex, "バッチ翻訳処理がキャンセルされました");
            results.Add(CreateErrorResponse(
                request, 
            TranslationErrorType.OperationCanceled, 
            "バッチ翻訳処理がキャンセルされました"));
            break;
            }
        catch (TranslationException ex)
        {
            _logger.LogError(ex, "バッチ翻訳処理中に翻訳例外が発生しました: {ErrorType}", ex.ErrorType);
            results.Add(CreateErrorResponse(
                request, 
                ex.ErrorType, 
                $"バッチ翻訳処理が失敗しました: {ex.Message}"));
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "バッチ翻訳処理中に引数エラーが発生しました");
            results.Add(CreateErrorResponse(
                request, 
                TranslationErrorType.InvalidRequest, 
                $"バッチ翻訳処理に失敗しました: {ex.Message}"));
        }
        catch (System.IO.IOException ex)
        {
            _logger.LogError(ex, "バッチ翻訳処理中に入出力エラーが発生しました");
            results.Add(CreateErrorResponse(
                request, 
                TranslationErrorType.ProcessingError, 
                $"バッチ翻訳処理に失敗しました: {ex.Message}"));
        }
        catch (InvalidOperationException ex)
        {
        _logger.LogError(ex, "バッチ翻訳処理中に無効な操作が発生しました");
        results.Add(CreateErrorResponse(
                request, 
            TranslationErrorType.ProcessingError, 
            $"バッチ翻訳処理に失敗しました: {ex.Message}"));
        }
        catch (OutOfMemoryException ex)
        {
            _logger.LogError(ex, "バッチ翻訳処理中にメモリ不足が発生しました");
            results.Add(CreateErrorResponse(
                request, 
                TranslationErrorType.ProcessingError, 
                $"バッチ翻訳処理に失敗しました: {ex.Message}"));
        }
            
            if (cancellationToken.IsCancellationRequested)
                break;
        }
        
        return results;
    }
    
    /// <inheritdoc/>
    public override async Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
    {
        ArgumentNullException.ThrowIfNull(languagePair, nameof(languagePair));
            
        // このエンジンはモデルごとに決まった言語ペアのみをサポート
        await Task.Delay(1).ConfigureAwait(false); // 非同期処理をシミュレート
        return string.Equals(ModelLanguagePair.SourceLanguage.Code, languagePair.SourceLanguage.Code, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(ModelLanguagePair.TargetLanguage.Code, languagePair.TargetLanguage.Code, StringComparison.OrdinalIgnoreCase);
    }
    
    /// <inheritdoc/>
    public override async Task<LanguageDetectionResult> DetectLanguageAsync(
        string text, 
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(text, nameof(text));
            
        // 基本実装では、モデルの言語ペアにマッチするか簡易チェック
        try
        {
            // 非同期処理をシミュレート
            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            
            // テキストの特徴に基づいて言語を推定
            var sourceLanguage = ModelLanguagePair.SourceLanguage;
            double confidence = 0.5; // 初期値は中程度の信頼度
            
            // 簡易言語推定（実際はより高度な手法を使用すべき）
            // 言語特有の文字パターンに基づく判定
            if (HasJapaneseCharacters(text))
            {
                confidence = string.Equals(sourceLanguage.Code, "ja", StringComparison.OrdinalIgnoreCase) ? 0.9 : 0.1;
                return LanguageDetectionResult.CreateSuccess(
                    new Language { Code = "ja", DisplayName = "Japanese" },
                    confidence,
                    confidence > 0.7
                );
            }
            else if (HasChineseCharacters(text))
            {
                confidence = string.Equals(sourceLanguage.Code, "zh", StringComparison.OrdinalIgnoreCase) ? 0.9 : 0.1;
                return LanguageDetectionResult.CreateSuccess(
                    new Language { Code = "zh", DisplayName = "Chinese" },
                    confidence,
                    confidence > 0.7
                );
            }
            else if (HasKoreanCharacters(text))
            {
                confidence = string.Equals(sourceLanguage.Code, "ko", StringComparison.OrdinalIgnoreCase) ? 0.9 : 0.1;
                return LanguageDetectionResult.CreateSuccess(
                    new Language { Code = "ko", DisplayName = "Korean" },
                    confidence,
                    confidence > 0.7
                );
            }
            
            // デフォルトはモデルのソース言語
            return LanguageDetectionResult.CreateSuccess(
                sourceLanguage,
                0.6,
                true
            );
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "言語検出中に引数エラーが発生しました");
            return LanguageDetectionResult.CreateErrorFromException(
                TranslationErrorType.InvalidRequest,
                "言語検出に失敗しました",
                ex
            );
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "言語検出中に無効な操作が発生しました");
            return LanguageDetectionResult.CreateErrorFromException(
                TranslationErrorType.ProcessingError,
                "言語検出に失敗しました",
                ex
            );
        }
        catch (Exception ex) when (ex is not ArgumentException && ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "言語検出中に予期しないエラーが発生しました: {ExType}", ex.GetType().Name);
            return LanguageDetectionResult.CreateErrorFromException(
                TranslationErrorType.ProcessingError,
                "言語検出に失敗しました",
                ex
            );
        }
    }
    
    /// <summary>
    /// トークンの翻訳を実行
    /// </summary>
    /// <remarks>
    /// 実際の実装クラスでオーバーライドされる
    /// </remarks>
    protected virtual async Task<int[]> TranslateTokensAsync(int[] inputTokens, CancellationToken cancellationToken)
    {
        // この基本実装ではダミーの結果を返す
        // 派生クラスで実際のモデル推論を実装
        _logger.LogWarning("基本実装では実際の翻訳処理は実行されません");
        
        // 非同期処理をシミュレートするためのDelay
        await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        
        return [];
    }
    
    /// <inheritdoc/>
    public IModelLoader GetModelLoader()
    {
        return _modelLoader;
    }
    
    /// <inheritdoc/>
    public ITokenizer GetTokenizer()
    {
        return _tokenizer;
    }
    
    /// <inheritdoc/>
    public async Task<bool> LoadModelToDeviceAsync(ComputeDevice device)
    {
        ArgumentNullException.ThrowIfNull(device, nameof(device));
                
        try
        {
            // 現在のデバイスと同じ場合は何もしない
            if (Device.DeviceId == device.DeviceId)
            {
                return true;
            }
            
            // いったんモデルをアンロード
            await UnloadModelAsync().ConfigureAwait(false);
            
            // 新しいデバイスを設定
            var success = await _modelLoader.SetDeviceAsync(device).ConfigureAwait(false);
            if (!success)
            {
                _logger.LogError("デバイスの変更に失敗しました: {DeviceId}", device.DeviceId);
                return false;
            }
            
            // モデルを再ロード
            return await InitializeAsync().ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "デバイス変更中に引数エラーが発生しました: {DeviceId}", device.DeviceId);
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "デバイス変更中に無効な操作が発生しました: {DeviceId}", device.DeviceId);
            return false;
        }
        catch (System.IO.IOException ex)
        {
            _logger.LogError(ex, "デバイス変更中に入出力エラーが発生しました: {DeviceId}", device.DeviceId);
            return false;
        }
        catch (OutOfMemoryException ex)
        {
            _logger.LogError(ex, "デバイス変更中にメモリ不足が発生しました: {DeviceId}", device.DeviceId);
            return false;
        }
        catch (NotSupportedException ex)
        {
            _logger.LogError(ex, "デバイス変更中に非対応機能が呼び出されました: {DeviceId}", device.DeviceId);
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "デバイス変更中にタスクがキャンセルされました: {DeviceId}", device.DeviceId);
            return false;
        }
    }
    
    /// <inheritdoc/>
    public async Task<bool> UnloadModelAsync()
    {
        if (!IsInitialized)
        {
            return true;
        }
        
        try
        {
            await Task.Run(() => 
            {
                // リソースの解放
                // 実際の実装ではここで具体的なリソース解放処理を行う
                IsInitialized = false;
                MemoryUsage = 0;
            }).ConfigureAwait(false);
            
            return true;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "モデルのアンロード処理がキャンセルされました");
            return false;
        }
        catch (OutOfMemoryException ex)
        {
            _logger.LogError(ex, "モデルのアンロード中にメモリ不足が発生しました");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "モデルのアンロード中に無効な操作が発生しました");
            return false;
        }
        catch (System.IO.IOException ex)
        {
            _logger.LogError(ex, "モデルのアンロード中に入出力エラーが発生しました");
            return false;
        }
    }
    
    /// <inheritdoc/>
    public override async Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        // このモデルでサポートする言語ペア
        await Task.Delay(1).ConfigureAwait(false); // 非同期処理をシミュレート
        return [ModelLanguagePair];
    }
    
    /// <summary>
    /// メモリ使用量の更新
    /// </summary>
    protected void UpdateMemoryUsage()
    {
        try
        {
            // 現在のメモリ使用量を計算
            // 実際の実装では、モデルの実際のメモリ使用量を取得
            MemoryUsage = EstimateModelSize();
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "メモリ使用量の計算中に入出力エラーが発生しました");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "メモリ使用量の計算中にアクセス権限エラーが発生しました");
        }
    }
    
    /// <summary>
    /// モデルサイズの推定
    /// </summary>
    private long EstimateModelSize()
    {
        try
        {
            // 実際の実装では、モデルのメモリ使用量を計算
            // ここでは簡易的にファイルサイズを返す
            var fileInfo = new FileInfo(ModelPath);
            return fileInfo.Exists ? fileInfo.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
        catch (SecurityException)
        {
            return 0;
        }
    }
    
    /// <summary>
    /// 日本語の文字を含むかどうかをチェック
    /// </summary>
    private bool HasJapaneseCharacters(string text)
    {
        // 日本語特有の文字（ひらがな、カタカナ）をチェック
        return text.Any(c => (c >= '\u3040' && c <= '\u309F') || (c >= '\u30A0' && c <= '\u30FF'));
    }
    
    /// <summary>
    /// 中国語の文字を含むかどうかをチェック
    /// </summary>
    private bool HasChineseCharacters(string text)
    {
        // 簡体字・繁体字の共通部分をチェック
        return text.Any(c => c >= '\u4E00' && c <= '\u9FFF');
    }
    
    /// <summary>
    /// 韓国語の文字を含むかどうかをチェック
    /// </summary>
    private bool HasKoreanCharacters(string text)
    {
        // ハングル文字をチェック
        return text.Any(c => (c >= '\uAC00' && c <= '\uD7A3') || (c >= '\u1100' && c <= '\u11FF'));
    }
    
    /// <inheritdoc/>
    protected override void DisposeManagedResources()
    {
        try
        {
            // モデルのアンロード処理を実行
            UnloadModelAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            
            // 基底クラスのDisposeManagedResourcesメソッドを呼び出し
            base.DisposeManagedResources();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "リソース解放中に無効な操作が発生しました");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "リソース解放中に入出力エラーが発生しました");
        }
    }
    
    /// <inheritdoc/>
    protected override async ValueTask DisposeAsyncCore()
    {
        try
        {
            // 非同期にモデルをアンロード
            await UnloadModelAsync().ConfigureAwait(false);
            
            // 基底クラスのDisposeAsyncCoreメソッドを呼び出し
            await base.DisposeAsyncCore().ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "非同期リソース解放中に無効な操作が発生しました");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "非同期リソース解放中に入出力エラーが発生しました");
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "非同期リソース解放処理がキャンセルされました");
        }
    }
}

/// <summary>
/// ONNX翻訳オプション
/// </summary>
public class OnnxTranslationOptions
{
    /// <summary>
    /// 最大シーケンス長
    /// </summary>
    public int MaxSequenceLength { get; set; } = 512;
    
    /// <summary>
    /// スレッド数（0=自動）
    /// </summary>
    public int ThreadCount { get; set; }
    
    /// <summary>
    /// グラフ最適化レベル
    /// </summary>
    public int OptimizationLevel { get; set; } = 3;
    
    /// <summary>
    /// メモリ制限(MB)
    /// </summary>
    public int MemoryLimitMb { get; set; }
    
    /// <summary>
    /// モデルキャッシュを有効にするかどうか
    /// </summary>
    public bool EnableModelCache { get; set; } = true;
    
    /// <summary>
    /// バッチサイズ
    /// </summary>
    public int BatchSize { get; set; } = 1;
    
    /// <summary>
    /// 出力シーケンスの最大長
    /// </summary>
    public int MaxOutputLength { get; set; } = 512;
    
    /// <summary>
    /// ビームサイズ
    /// </summary>
    public int BeamSize { get; set; } = 4;
    
    /// <summary>
    /// OPUSモデルの場合のソース言語
    /// </summary>
    public string OpusSourceLanguage { get; set; } = "en";
    
    /// <summary>
    /// OPUSモデルの場合のターゲット言語
    /// </summary>
    public string OpusTargetLanguage { get; set; } = "ja";
}

