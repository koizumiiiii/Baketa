# ONNX翻訳エンジンの実装例

このドキュメントでは、Baketaプロジェクトにおける ONNX 翻訳エンジンの実装例を示します。ONNX モデルを使用した翻訳サービスの実装方法と、Windows プラットフォーム向けの最適化について説明します。

## 1. ONNX翻訳モデルの実装

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Models.Onnx;
using Baketa.Core.Translation.Models.Tokenization;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Onnx
{
    /// <summary>
    /// ONNX翻訳モデルの実装クラス
    /// </summary>
    public class OnnxTranslationModel : IOnnxTranslationModel
    {
        private readonly ILogger<OnnxTranslationModel> _logger;
        private readonly OnnxModelOptions _options;
        private bool _isDisposed = false;
        
        /// <inheritdoc/>
        public string ModelId { get; }
        
        /// <inheritdoc/>
        public string Name { get; }
        
        /// <inheritdoc/>
        public string Version { get; }
        
        /// <inheritdoc/>
        public string Provider { get; } = "ONNX Runtime";
        
        /// <inheritdoc/>
        public string ModelPath { get; }
        
        /// <inheritdoc/>
        public string VocabularyPath { get; }
        
        /// <inheritdoc/>
        public IReadOnlyList<string> SupportedSourceLanguages { get; }
        
        /// <inheritdoc/>
        public IReadOnlyList<string> SupportedTargetLanguages { get; }
        
        /// <inheritdoc/>
        public bool IsInitialized { get; private set; }
        
        /// <inheritdoc/>
        public IOnnxSession Session { get; private set; }
        
        /// <inheritdoc/>
        public IOnnxSessionOptions SessionOptions { get; private set; }
        
        /// <inheritdoc/>
        public IReadOnlyDictionary<string, string> Metadata { get; }
        
        /// <inheritdoc/>
        public IReadOnlyList<string> InputNames { get; private set; }
        
        /// <inheritdoc/>
        public IReadOnlyList<string> OutputNames { get; private set; }
        
        /// <inheritdoc/>
        public ITokenizer Tokenizer { get; }
        
        /// <inheritdoc/>
        public OnnxExecutionProvider CurrentExecutionProvider { get; private set; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        public OnnxTranslationModel(
            string modelId,
            string modelPath,
            string vocabularyPath,
            ITokenizer tokenizer,
            IReadOnlyDictionary<string, string> metadata,
            IReadOnlyList<string> supportedSourceLanguages,
            IReadOnlyList<string> supportedTargetLanguages,
            OnnxModelOptions options,
            ILogger<OnnxTranslationModel> logger)
        {
            ModelId = modelId ?? throw new ArgumentNullException(nameof(modelId));
            ModelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
            VocabularyPath = vocabularyPath ?? throw new ArgumentNullException(nameof(vocabularyPath));
            Tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
            Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            SupportedSourceLanguages = supportedSourceLanguages ?? throw new ArgumentNullException(nameof(supportedSourceLanguages));
            SupportedTargetLanguages = supportedTargetLanguages ?? throw new ArgumentNullException(nameof(supportedTargetLanguages));
            _options = options ?? new OnnxModelOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            
            // メタデータから名前とバージョンを取得
            Name = metadata.TryGetValue("name", out var name) ? name : "Unknown Model";
            Version = metadata.TryGetValue("version", out var version) ? version : "1.0.0";
            
            CurrentExecutionProvider = _options.ExecutionProvider;
        }
        
        /// <inheritdoc/>
        public async Task<bool> InitializeAsync()
        {
            if (IsInitialized)
            {
                return true;
            }
            
            if (!File.Exists(ModelPath))
            {
                _logger.LogError("モデルファイルが見つかりません: {ModelPath}", ModelPath);
                return false;
            }
            
            try
            {
                // セッションオプションの作成
                SessionOptions = await CreateSessionOptionsAsync();
                
                // セッションの作成
                Session = await CreateSessionAsync(ModelPath, SessionOptions);
                
                // モデルの入出力情報を取得
                InputNames = await GetInputNamesAsync(Session);
                OutputNames = await GetOutputNamesAsync(Session);
                
                IsInitialized = true;
                _logger.LogInformation("ONNXモデルを初期化しました: {ModelId}, 実行プロバイダー: {Provider}", ModelId, CurrentExecutionProvider);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ONNXモデルの初期化に失敗しました: {ModelId}", ModelId);
                return false;
            }
        }
        
        /// <inheritdoc/>
        public bool SupportsLanguagePair(string sourceLanguage, string targetLanguage)
        {
            return SupportedSourceLanguages.Contains(sourceLanguage) && 
                   SupportedTargetLanguages.Contains(targetLanguage);
        }
        
        /// <inheritdoc/>
        public bool IsGpuAvailable()
        {
            // ここで特定のプラットフォーム実装を使用してGPUの可用性をチェック
            return OnnxEnvironment.IsGpuAvailable();
        }
        
        /// <inheritdoc/>
        public async Task<bool> SetExecutionProviderAsync(OnnxExecutionProvider provider)
        {
            if (CurrentExecutionProvider == provider)
            {
                return true;
            }
            
            // 現在のセッションをクリーンアップ
            DisposeSession();
            
            try
            {
                CurrentExecutionProvider = provider;
                
                // 新しいセッションオプションを作成
                SessionOptions = await CreateSessionOptionsAsync();
                
                // 新しいセッションを作成
                Session = await CreateSessionAsync(ModelPath, SessionOptions);
                
                _logger.LogInformation("実行プロバイダーを変更しました: {Provider}", provider);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "実行プロバイダーの変更に失敗しました: {Provider}", provider);
                
                // 元の設定に戻す試行
                try
                {
                    CurrentExecutionProvider = OnnxExecutionProvider.Cpu;
                    SessionOptions = await CreateSessionOptionsAsync();
                    Session = await CreateSessionAsync(ModelPath, SessionOptions);
                }
                catch
                {
                    // 回復失敗時は初期化済みフラグをリセット
                    IsInitialized = false;
                }
                
                return false;
            }
        }
        
        /// <summary>
        /// セッションオプションを作成
        /// </summary>
        private Task<IOnnxSessionOptions> CreateSessionOptionsAsync()
        {
            // ここではプラットフォーム抽象化レイヤーを使用
            var options = OnnxEnvironment.CreateSessionOptions();
            
            // グラフ最適化レベルの設定
            options.SetGraphOptimizationLevel(_options.OptimizationLevel);
            
            // スレッド数の設定
            if (_options.ThreadCount > 0)
            {
                options.SetIntraOpNumThreads(_options.ThreadCount);
                options.SetInterOpNumThreads(_options.ThreadCount);
            }
            
            // 実行プロバイダーの設定
            switch (CurrentExecutionProvider)
            {
                case OnnxExecutionProvider.Cuda:
                    if (OnnxEnvironment.IsCudaAvailable())
                    {
                        options.AppendCudaExecutionProvider();
                    }
                    else
                    {
                        _logger.LogWarning("CUDA実行プロバイダーが利用できません。CPUにフォールバックします。");
                        CurrentExecutionProvider = OnnxExecutionProvider.Cpu;
                    }
                    break;
                    
                case OnnxExecutionProvider.DirectML:
                    if (OnnxEnvironment.IsDirectMLAvailable())
                    {
                        options.AppendDirectMLExecutionProvider();
                    }
                    else
                    {
                        _logger.LogWarning("DirectML実行プロバイダーが利用できません。CPUにフォールバックします。");
                        CurrentExecutionProvider = OnnxExecutionProvider.Cpu;
                    }
                    break;
                    
                case OnnxExecutionProvider.OpenVINO:
                    if (OnnxEnvironment.IsOpenVINOAvailable())
                    {
                        options.AppendOpenVINOExecutionProvider();
                    }
                    else
                    {
                        _logger.LogWarning("OpenVINO実行プロバイダーが利用できません。CPUにフォールバックします。");
                        CurrentExecutionProvider = OnnxExecutionProvider.Cpu;
                    }
                    break;
                    
                case OnnxExecutionProvider.Cpu:
                default:
                    // CPUはデフォルトで常に利用可能
                    options.AppendCpuExecutionProvider();
                    break;
            }
            
            return Task.FromResult(options);
        }
        
        /// <summary>
        /// セッションを作成
        /// </summary>
        private Task<IOnnxSession> CreateSessionAsync(string modelPath, IOnnxSessionOptions options)
        {
            return Task.FromResult(OnnxEnvironment.CreateSession(modelPath, options));
        }
        
        /// <summary>
        /// 入力ノード名を取得
        /// </summary>
        private Task<IReadOnlyList<string>> GetInputNamesAsync(IOnnxSession session)
        {
            // 実際の実装ではセッションから入力ノード名を取得
            return Task.FromResult<IReadOnlyList<string>>(
                new List<string> { "input_ids", "attention_mask" });
        }
        
        /// <summary>
        /// 出力ノード名を取得
        /// </summary>
        private Task<IReadOnlyList<string>> GetOutputNamesAsync(IOnnxSession session)
        {
            // 実際の実装ではセッションから出力ノード名を取得
            return Task.FromResult<IReadOnlyList<string>>(
                new List<string> { "logits" });
        }
        
        /// <summary>
        /// セッションを破棄
        /// </summary>
        private void DisposeSession()
        {
            Session?.Dispose();
            Session = null;
            
            SessionOptions?.Dispose();
            SessionOptions = null;
        }
        
        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// リソースの破棄
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
            {
                return;
            }
            
            if (disposing)
            {
                DisposeSession();
            }
            
            _isDisposed = true;
        }
    }
}
```

## 2. ONNX翻訳サービスの実装

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Events;
using Baketa.Core.Translation;
using Baketa.Core.Translation.Models.Onnx;
using Baketa.Core.Translation.Results;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Onnx
{
    /// <summary>
    /// ONNX翻訳サービス
    /// </summary>
    public class OnnxTranslationService : ITranslationService
    {
        private readonly IOnnxTranslationModel _model;
        private readonly IEventAggregator _eventAggregator;
        private readonly ILogger<OnnxTranslationService> _logger;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        public OnnxTranslationService(
            IOnnxTranslationModel model,
            IEventAggregator eventAggregator,
            ILogger<OnnxTranslationService> logger)
        {
            _model = model ?? throw new ArgumentNullException(nameof(model));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        /// <inheritdoc/>
        public async Task<ITranslationResult> TranslateAsync(
            string sourceText, 
            string sourceLanguage, 
            string targetLanguage, 
            TranslationOptions options = null)
        {
            options ??= new TranslationOptions();
            var requestId = Guid.NewGuid().ToString();
            
            // 翻訳リクエストイベントを発行
            await _eventAggregator.PublishAsync(new TranslationRequestedEvent(
                requestId,
                sourceText,
                sourceLanguage,
                targetLanguage,
                options.Context));
            
            try
            {
                // モデルが初期化されていない場合は初期化
                if (!_model.IsInitialized)
                {
                    var initialized = await _model.InitializeAsync();
                    if (!initialized)
                    {
                        throw new TranslationException("ONNXモデルの初期化に失敗しました。");
                    }
                }
                
                // 言語ペアがサポートされているか確認
                if (!_model.SupportsLanguagePair(sourceLanguage, targetLanguage))
                {
                    throw new TranslationException(
                        $"言語ペアがサポートされていません: {sourceLanguage} -> {targetLanguage}");
                }
                
                // 翻訳の実行時間を計測
                var stopwatch = Stopwatch.StartNew();
                
                // 入力テキストのトークン化
                int[] inputTokens = _model.Tokenizer.Tokenize(sourceText);
                
                // 入力テンソルの作成
                var inputTensor = await CreateInputTensorAsync(inputTokens, options.MaxSequenceLength);
                var attentionMaskTensor = await CreateAttentionMaskTensorAsync(inputTokens.Length, options.MaxSequenceLength);
                
                // 入力辞書の作成
                var inputs = new Dictionary<string, IOnnxTensor>
                {
                    { _model.InputNames[0], inputTensor },
                    { _model.InputNames[1], attentionMaskTensor }
                };
                
                // 推論の実行
                var outputs = await _model.Session.RunAsync(inputs);
                
                // 出力テンソルの取得
                var outputTensor = outputs[_model.OutputNames[0]];
                
                // 出力テンソルの処理
                string translatedText = await ProcessOutputTensorAsync(outputTensor, sourceLanguage, targetLanguage);
                
                stopwatch.Stop();
                
                // 翻訳結果の作成
                var result = new TranslationResult
                {
                    RequestId = requestId,
                    OriginalText = sourceText,
                    TranslatedText = translatedText,
                    SourceLanguage = sourceLanguage,
                    TargetLanguage = targetLanguage,
                    TranslationEngine = $"ONNX ({_model.Name})",
                    ProcessingTimeMs = stopwatch.ElapsedMilliseconds,
                    Context = options.Context
                };
                
                // 翻訳完了イベントを発行
                await _eventAggregator.PublishAsync(new TranslationCompletedEvent(
                    requestId,
                    sourceText,
                    translatedText,
                    sourceLanguage,
                    targetLanguage,
                    stopwatch.ElapsedMilliseconds,
                    $"ONNX ({_model.Name})",
                    options.Context));
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "翻訳処理に失敗しました: {SourceLanguage} -> {TargetLanguage}", 
                    sourceLanguage, targetLanguage);
                
                // 翻訳失敗イベントを発行
                await _eventAggregator.PublishAsync(new TranslationFailedEvent(
                    requestId,
                    sourceText,
                    sourceLanguage,
                    targetLanguage,
                    ex.Message,
                    TranslationErrorType.ProcessingError));
                
                throw new TranslationException("ONNX翻訳処理に失敗しました", ex);
            }
        }
        
        /// <summary>
        /// 入力テンソルの作成
        /// </summary>
        private Task<IOnnxTensor> CreateInputTensorAsync(int[] tokens, int maxLength)
        {
            // シーケンス長の制限
            int actualLength = Math.Min(tokens.Length, maxLength);
            
            // バッチ次元を含むテンソル形状
            long[] dimensions = new long[] { 1, actualLength };
            
            // テンソルデータの作成（パディングを含む）
            var tensorData = new int[actualLength];
            Array.Copy(tokens, tensorData, actualLength);
            
            // テンソルの作成
            return Task.FromResult(
                OnnxEnvironment.CreateTensor(tensorData, dimensions));
        }
        
        /// <summary>
        /// アテンションマスクテンソルの作成
        /// </summary>
        private Task<IOnnxTensor> CreateAttentionMaskTensorAsync(int tokenCount, int maxLength)
        {
            // シーケンス長の制限
            int actualLength = Math.Min(tokenCount, maxLength);
            
            // バッチ次元を含むテンソル形状
            long[] dimensions = new long[] { 1, actualLength };
            
            // マスクデータの作成（すべて1）
            var maskData = new int[actualLength];
            for (int i = 0; i < actualLength; i++)
            {
                maskData[i] = 1;
            }
            
            // テンソルの作成
            return Task.FromResult(
                OnnxEnvironment.CreateTensor(maskData, dimensions));
        }
        
        /// <summary>
        /// 出力テンソルの処理
        /// </summary>
        private Task<string> ProcessOutputTensorAsync(IOnnxTensor tensor, string sourceLanguage, string targetLanguage)
        {
            // 実際には、モデルによって出力の処理方法が異なる
            // ここでは一般的な方法を示す
            
            // 出力データの取得
            float[] logits = tensor.GetData<float>();
            
            // テンソルの形状（例: [batch_size, sequence_length, vocab_size]）
            var shape = tensor.Dimensions;
            
            // トークンIDシーケンスの取得
            List<int> outputTokens = new List<int>();
            
            // ロジットから最も確率の高いトークンを選択
            int vocabSize = (int)shape[2];
            int seqLength = (int)shape[1];
            
            for (int i = 0; i < seqLength; i++)
            {
                int maxIndex = 0;
                float maxValue = float.MinValue;
                
                for (int j = 0; j < vocabSize; j++)
                {
                    int index = i * vocabSize + j;
                    if (index < logits.Length && logits[index] > maxValue)
                    {
                        maxValue = logits[index];
                        maxIndex = j;
                    }
                }
                
                // トークンの追加（特殊なEOSトークンに到達したら終了）
                if (maxIndex == _model.Tokenizer.SpecialTokens["eos_token"])
                {
                    break;
                }
                
                outputTokens.Add(maxIndex);
            }
            
            // トークンIDをテキストにデコード
            string translatedText = _model.Tokenizer.Decode(outputTokens.ToArray());
            
            return Task.FromResult(translatedText);
        }
    }
}
```

## 3. Windows固有の実装（アダプターレイヤー）

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models.Onnx;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Onnx
{
    /// <summary>
    /// Windows固有のONNX環境実装
    /// </summary>
    public class WindowsOnnxEnvironment
    {
        private readonly ILogger<WindowsOnnxEnvironment> _logger;
        
        public WindowsOnnxEnvironment(ILogger<WindowsOnnxEnvironment> logger)
        {
            _logger = logger;
        }
        
        /// <summary>
        /// ONNXランタイムが利用可能かどうかを確認
        /// </summary>
        public bool IsRuntimeAvailable()
        {
            try
            {
                // ONNXランタイムバージョンの取得でチェック
                var version = OrtEnv.Instance().GetVersion();
                _logger.LogInformation("ONNX Runtimeバージョン: {Version}", version);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ONNX Runtimeが利用できません");
                return false;
            }
        }
        
        /// <summary>
        /// GPU (CUDA) が利用可能かどうかを確認
        /// </summary>
        public bool IsCudaAvailable()
        {
            try
            {
                // セッションオプションを作成してCUDAプロバイダーを追加
                var sessionOptions = new SessionOptions();
                sessionOptions.AppendExecutionProvider_CUDA();
                
                // テスト用の小さなモデルをロード
                using var session = new InferenceSession(GetEmptyModelBytes(), sessionOptions);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "CUDA実行プロバイダーが利用できません");
                return false;
            }
        }
        
        /// <summary>
        /// DirectML が利用可能かどうかを確認
        /// </summary>
        public bool IsDirectMLAvailable()
        {
            try
            {
                // セッションオプションを作成してDirectMLプロバイダーを追加
                var sessionOptions = new SessionOptions();
                sessionOptions.AppendExecutionProvider_DML();
                
                // テスト用の小さなモデルをロード
                using var session = new InferenceSession(GetEmptyModelBytes(), sessionOptions);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "DirectML実行プロバイダーが利用できません");
                return false;
            }
        }
        
        /// <summary>
        /// セッションオプションを作成
        /// </summary>
        public IOnnxSessionOptions CreateSessionOptions()
        {
            return new WindowsOnnxSessionOptions(new SessionOptions());
        }
        
        /// <summary>
        /// セッションを作成
        /// </summary>
        public IOnnxSession CreateSession(string modelPath, IOnnxSessionOptions options)
        {
            if (options is not WindowsOnnxSessionOptions windowsOptions)
            {
                throw new ArgumentException("無効なセッションオプションタイプ", nameof(options));
            }
            
            var session = new InferenceSession(modelPath, windowsOptions.Options);
            return new WindowsOnnxSession(session);
        }
        
        /// <summary>
        /// テンソルを作成
        /// </summary>
        public IOnnxTensor CreateTensor<T>(T[] data, long[] dimensions)
        {
            var tensor = new DenseTensor<T>(data, dimensions);
            return new WindowsOnnxTensor<T>(tensor);
        }
        
        /// <summary>
        /// テスト用の空のモデルバイトを取得
        /// </summary>
        private byte[] GetEmptyModelBytes()
        {
            // 実際の実装では小さなテストモデルのバイトを返す
            // ここではダミーとして空のバイト配列を返す
            return new byte[0];
        }
    }
    
    /// <summary>
    /// Windows固有のONNXセッションオプションアダプター
    /// </summary>
    public class WindowsOnnxSessionOptions : IOnnxSessionOptions
    {
        public SessionOptions Options { get; }
        
        public WindowsOnnxSessionOptions(SessionOptions options)
        {
            Options = options;
        }
        
        public void SetGraphOptimizationLevel(GraphOptimizationLevel level)
        {
            Options.GraphOptimizationLevel = ConvertOptimizationLevel(level);
        }
        
        public void SetIntraOpNumThreads(int threadCount)
        {
            Options.IntraOpNumThreads = threadCount;
        }
        
        public void SetInterOpNumThreads(int threadCount)
        {
            Options.InterOpNumThreads = threadCount;
        }
        
        public void AppendCpuExecutionProvider()
        {
            // CPUはデフォルトのプロバイダーなので何もしない
        }
        
        public void AppendCudaExecutionProvider()
        {
            Options.AppendExecutionProvider_CUDA();
        }
        
        public void AppendDirectMLExecutionProvider()
        {
            Options.AppendExecutionProvider_DML();
        }
        
        public void AppendOpenVINOExecutionProvider()
        {
            // Windows上でのOpenVINOプロバイダー追加
            // 実際にはNuGetパッケージの制約があるかもしれない
            try
            {
                Options.AppendExecutionProvider_OpenVINO();
            }
            catch
            {
                // OpenVINOが利用できない場合は無視
            }
        }
        
        private GraphOptimizationLevel ConvertOptimizationLevel(Core.Translation.Models.Onnx.GraphOptimizationLevel level)
        {
            return level switch
            {
                Core.Translation.Models.Onnx.GraphOptimizationLevel.None => GraphOptimizationLevel.DisableAll,
                Core.Translation.Models.Onnx.GraphOptimizationLevel.Basic => GraphOptimizationLevel.Basic,
                Core.Translation.Models.Onnx.GraphOptimizationLevel.Extended => GraphOptimizationLevel.Extended,
                Core.Translation.Models.Onnx.GraphOptimizationLevel.All => GraphOptimizationLevel.All,
                _ => GraphOptimizationLevel.All
            };
        }
        
        public void Dispose()
        {
            Options.Dispose();
        }
    }
    
    /// <summary>
    /// Windows固有のONNXセッションアダプター
    /// </summary>
    public class WindowsOnnxSession : IOnnxSession
    {
        private readonly InferenceSession _session;
        
        public WindowsOnnxSession(InferenceSession session)
        {
            _session = session;
        }
        
        public async Task<IDictionary<string, IOnnxTensor>> RunAsync(IDictionary<string, IOnnxTensor> inputs)
        {
            var nativeInputs = new Dictionary<string, NamedOnnxValue>();
            
            foreach (var input in inputs)
            {
                if (input.Value is WindowsOnnxTensor<int> intTensor)
                {
                    nativeInputs.Add(input.Key, NamedOnnxValue.CreateFromTensor(input.Key, intTensor.Tensor));
                }
                else if (input.Value is WindowsOnnxTensor<float> floatTensor)
                {
                    nativeInputs.Add(input.Key, NamedOnnxValue.CreateFromTensor(input.Key, floatTensor.Tensor));
                }
                // 他のデータ型のケースも必要に応じて追加
            }
            
            // 実際のセッション実行
            // 非同期対応のため、Task.Runでラップ
            var results = await Task.Run(() => _session.Run(nativeInputs));
            
            var outputs = new Dictionary<string, IOnnxTensor>();
            
            foreach (var result in results)
            {
                if (result.Value is Tensor<float> floatTensor)
                {
                    outputs.Add(result.Name, new WindowsOnnxTensor<float>(floatTensor));
                }
                else if (result.Value is Tensor<int> intTensor)
                {
                    outputs.Add(result.Name, new WindowsOnnxTensor<int>(intTensor));
                }
                // 他のデータ型のケースも必要に応じて追加
            }
            
            return outputs;
        }
        
        public async Task<IOnnxTensor[]> RunAsync(string[] inputNames, IOnnxTensor[] inputs, string[] outputNames)
        {
            if (inputNames.Length != inputs.Length)
            {
                throw new ArgumentException("入力名と入力テンソルの数が一致しません");
            }
            
            var inputValues = new List<NamedOnnxValue>();
            
            for (int i = 0; i < inputNames.Length; i++)
            {
                if (inputs[i] is WindowsOnnxTensor<int> intTensor)
                {
                    inputValues.Add(NamedOnnxValue.CreateFromTensor(inputNames[i], intTensor.Tensor));
                }
                else if (inputs[i] is WindowsOnnxTensor<float> floatTensor)
                {
                    inputValues.Add(NamedOnnxValue.CreateFromTensor(inputNames[i], floatTensor.Tensor));
                }
                // 他のデータ型のケースも必要に応じて追加
            }
            
            // 実際のセッション実行
            // 非同期対応のため、Task.Runでラップ
            var results = await Task.Run(() => _session.Run(inputValues, outputNames));
            
            var outputs = new IOnnxTensor[results.Count];
            
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i].Value is Tensor<float> floatTensor)
                {
                    outputs[i] = new WindowsOnnxTensor<float>(floatTensor);
                }
                else if (results[i].Value is Tensor<int> intTensor)
                {
                    outputs[i] = new WindowsOnnxTensor<int>(intTensor);
                }
                // 他のデータ型のケースも必要に応じて追加
            }
            
            return outputs;
        }
        
        public void Dispose()
        {
            _session.Dispose();
        }
    }
    
    /// <summary>
    /// Windows固有のONNXテンソルアダプター
    /// </summary>
    public class WindowsOnnxTensor<T> : IOnnxTensor
    {
        public Tensor<T> Tensor { get; }
        
        public WindowsOnnxTensor(Tensor<T> tensor)
        {
            Tensor = tensor;
        }
        
        public long[] Dimensions => Tensor.Dimensions.ToArray();
        
        public long ElementCount => Tensor.Length;
        
        public Type ElementType => typeof(T);
        
        public TData[] GetData<TData>()
        {
            if (typeof(TData) != typeof(T))
            {
                throw new InvalidOperationException($"要求されたデータ型 {typeof(TData)} が実際のテンソル型 {typeof(T)} と一致しません");
            }
            
            return (TData[])(object)Tensor.ToArray();
        }
        
        public void Dispose()
        {
            // DenseTensorはIDisposableを実装していないため、何もしない
        }
    }
}
```

## 4. モジュール登録の例

```csharp
using System;
using Baketa.Core.Translation;
using Baketa.Core.Translation.Models.Onnx;
using Baketa.Infrastructure.Platform.Windows.Onnx;
using Baketa.Infrastructure.Translation.Onnx;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.DependencyInjection
{
    /// <summary>
    /// ONNX翻訳サービスの登録モジュール
    /// </summary>
    public class OnnxTranslationModule : IServiceModule
    {
        public void RegisterServices(IServiceCollection services)
        {
            // Windows固有のONNX環境をシングルトンとして登録
            services.AddSingleton<WindowsOnnxEnvironment>();
            
            // ONNXモデルファクトリーの登録
            services.AddSingleton<IOnnxModelFactory, OnnxModelFactory>();
            
            // 利用可能なONNXモデルの登録
            services.AddSingleton<IOnnxTranslationModel>(provider =>
            {
                // モデルファクトリーの取得
                var factory = provider.GetRequiredService<IOnnxModelFactory>();
                
                // モデルとボキャブラリーのパス
                var modelPath = "path/to/translation/model.onnx";
                var vocabPath = "path/to/vocabulary/file.json";
                
                // デフォルトのモデルオプション
                var options = new OnnxModelOptions
                {
                    ExecutionProvider = OnnxExecutionProvider.Cpu,
                    OptimizationLevel = GraphOptimizationLevel.All,
                    MaxSequenceLength = 512
                };
                
                // モデルの作成と返却
                return factory.CreateModelAsync(modelPath, vocabPath, options).GetAwaiter().GetResult();
            });
            
            // 翻訳サービスの登録
            services.AddSingleton<ITranslationService, OnnxTranslationService>();
        }
    }
    
    /// <summary>
    /// ONNXモデルファクトリーの実装
    /// </summary>
    public class OnnxModelFactory : IOnnxModelFactory
    {
        private readonly WindowsOnnxEnvironment _environment;
        private readonly ILoggerFactory _loggerFactory;
        
        public OnnxModelFactory(
            WindowsOnnxEnvironment environment, 
            ILoggerFactory loggerFactory)
        {
            _environment = environment;
            _loggerFactory = loggerFactory;
        }
        
        public async Task<IOnnxTranslationModel> CreateModelAsync(
            string modelPath, 
            string vocabularyPath, 
            OnnxModelOptions options = null)
        {
            options ??= new OnnxModelOptions();
            
            // モデルIDの生成
            var modelId = $"{Path.GetFileNameWithoutExtension(modelPath)}_{Guid.NewGuid():N}";
            
            // モデルメタデータの読み取り
            var metadata = await ReadModelMetadataAsync(modelPath);
            
            // トークナイザーの作成
            var tokenizer = await CreateTokenizerAsync(vocabularyPath, options.ModelType);
            
            // サポートされている言語リストの取得
            var supportedSourceLanguages = GetSupportedSourceLanguages(metadata);
            var supportedTargetLanguages = GetSupportedTargetLanguages(metadata);
            
            // ロガーの取得
            var logger = _loggerFactory.CreateLogger<OnnxTranslationModel>();
            
            // モデルの作成
            var model = new OnnxTranslationModel(
                modelId,
                modelPath,
                vocabularyPath,
                tokenizer,
                metadata,
                supportedSourceLanguages,
                supportedTargetLanguages,
                options,
                logger);
            
            // モデルの初期化
            await model.InitializeAsync();
            
            return model;
        }
        
        public Task<IReadOnlyDictionary<string, string>> ReadModelMetadataAsync(string modelPath)
        {
            // 実際の実装ではONNXモデルからメタデータを読み取る
            // この例では仮のメタデータを返す
            var metadata = new Dictionary<string, string>
            {
                { "name", "TranslationModel" },
                { "version", "1.0.0" },
                { "source_languages", "en,ja,de,fr,es" },
                { "target_languages", "en,ja,de,fr,es" },
                { "model_type", "transformer" }
            };
            
            return Task.FromResult<IReadOnlyDictionary<string, string>>(metadata);
        }
        
        public Task<ITokenizer> CreateTokenizerAsync(string vocabularyPath, string modelType)
        {
            // モデルタイプに応じたトークナイザーの作成
            // 実際の実装では、モデルタイプに応じた適切なトークナイザーを返す
            ITokenizer tokenizer = modelType.ToLowerInvariant() switch
            {
                "transformer" => new TransformerTokenizer(vocabularyPath),
                "seq2seq" => new Seq2SeqTokenizer(vocabularyPath),
                _ => new GenericTokenizer(vocabularyPath)
            };
            
            return Task.FromResult(tokenizer);
        }
        
        private IReadOnlyList<string> GetSupportedSourceLanguages(IReadOnlyDictionary<string, string> metadata)
        {
            if (metadata.TryGetValue("source_languages", out var languages))
            {
                return languages.Split(',');
            }
            
            return new[] { "en" };
        }
        
        private IReadOnlyList<string> GetSupportedTargetLanguages(IReadOnlyDictionary<string, string> metadata)
        {
            if (metadata.TryGetValue("target_languages", out var languages))
            {
                return languages.Split(',');
            }
            
            return new[] { "en" };
        }
    }
}
```