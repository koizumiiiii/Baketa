# Issue 15: ONNX翻訳エンジン統合

## 概要
ONNXベースの翻訳モデル実行環境を統合し、ローカルで動作する高品質な機械翻訳機能を実装します。ONNX（Open Neural Network Exchange）フォーマットを活用することで、様々な事前学習済み翻訳モデルをBaketaアプリケーションで利用できるようになり、オフライン環境でも高速かつ品質の高い翻訳が可能になります。

## 目的・理由
ONNX翻訳エンジン統合は以下の理由で重要です：

1. オフライン翻訳の実現：インターネット接続なしでも翻訳機能を使用可能になる
2. プライバシー保護：翻訳テキストがローカルで処理され、外部サーバーに送信されない
3. コスト削減：クラウドベースの翻訳APIの利用料金がかからない
4. レイテンシの削減：ネットワーク通信が不要になり、翻訳の応答時間が短縮される
5. カスタマイズ可能性：特定のドメインやゲームジャンル向けにモデルを調整可能
6. 多様なモデルのサポート：様々な翻訳モデルを利用可能（seq2seq、Transformer等）

## 詳細
- ONNX Runtimeの統合
- トークナイザーの実装
- 翻訳モデル管理システムの実装
- モデルのダウンロードと更新機能の実装
- 多言語翻訳モデルのサポート

## タスク分解
- [ ] ONNX Runtimeの統合
  - [ ] ONNX Runtimeパッケージの追加と設定
  - [ ] ONNX推論エンジンのラッパークラスの実装
  - [ ] モデル読み込みと初期化機能の実装
  - [ ] マルチスレッド推論のサポート
  - [ ] GPUアクセラレーションのサポート
- [ ] トークナイザーの実装
  - [ ] SentencePieceトークナイザーの実装
  - [ ] WordPieceトークナイザーの実装
  - [ ] BPEトークナイザーの実装
  - [ ] トークナイザーファクトリーの実装
  - [ ] 各トークナイザーのモデルファイル管理
- [ ] ONNX翻訳エンジンの実装
  - [ ] `IOnnxTranslationEngine`インターフェースの設計と実装
  - [ ] 翻訳パイプラインの実装（前処理→推論→後処理）
  - [ ] バッチ翻訳の最適化
  - [ ] Attention機構の可視化サポート
  - [ ] 翻訳品質メトリクスの実装
- [ ] モデル管理システム
  - [ ] モデルメタデータの設計
  - [ ] モデルリポジトリの実装
  - [ ] モデルのバージョン管理
  - [ ] モデルの依存関係管理
  - [ ] メモリ効率のためのモデル共有
- [ ] モデルダウンロードと更新
  - [ ] モデルダウンロードマネージャーの実装
  - [ ] モデルカタログの実装
  - [ ] 差分更新のサポート
  - [ ] ダウンロード進捗の追跡と表示
  - [ ] 自動更新の設定
- [ ] UIとの統合
  - [ ] 翻訳設定画面への統合
  - [ ] モデル管理UI
  - [ ] モデルパフォーマンス統計の表示
  - [ ] モデル選択とカスタマイズUI
  - [ ] ゲームプロファイルごとのモデル設定
- [ ] 単体テストの実装

## インターフェース設計例
```csharp
namespace Baketa.Translation.Onnx
{
    /// <summary>
    /// ONNX翻訳エンジンインターフェース
    /// </summary>
    public interface IOnnxTranslationEngine : ITranslationEngine
    {
        /// <summary>
        /// 利用可能なONNXモデルのリストを取得します
        /// </summary>
        /// <returns>モデルリスト</returns>
        Task<IReadOnlyList<TranslationModelInfo>> GetAvailableModelsAsync();
        
        /// <summary>
        /// 現在のモデル情報を取得します
        /// </summary>
        /// <returns>モデル情報</returns>
        TranslationModelInfo? GetCurrentModel();
        
        /// <summary>
        /// モデルを切り替えます
        /// </summary>
        /// <param name="modelId">モデルID</param>
        /// <returns>切り替えが成功したかどうか</returns>
        Task<bool> SwitchModelAsync(string modelId);
        
        /// <summary>
        /// モデルを検証します
        /// </summary>
        /// <param name="modelPath">モデルファイルパス</param>
        /// <returns>検証結果</returns>
        Task<ModelValidationResult> ValidateModelAsync(string modelPath);
        
        /// <summary>
        /// 新しいモデルをインストールします
        /// </summary>
        /// <param name="modelPath">モデルファイルパス</param>
        /// <param name="modelInfo">モデル情報</param>
        /// <returns>インストールが成功したかどうか</returns>
        Task<bool> InstallModelAsync(string modelPath, TranslationModelInfo modelInfo);
        
        /// <summary>
        /// モデルを削除します
        /// </summary>
        /// <param name="modelId">モデルID</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> DeleteModelAsync(string modelId);
        
        /// <summary>
        /// ONNX特有の設定を取得します
        /// </summary>
        /// <returns>ONNX設定</returns>
        OnnxTranslationSettings GetOnnxSettings();
        
        /// <summary>
        /// ONNX特有の設定を更新します
        /// </summary>
        /// <param name="settings">ONNX設定</param>
        /// <returns>更新が成功したかどうか</returns>
        Task<bool> UpdateOnnxSettingsAsync(OnnxTranslationSettings settings);
    }
    
    /// <summary>
    /// 翻訳モデル情報クラス
    /// </summary>
    public class TranslationModelInfo
    {
        /// <summary>
        /// モデルID
        /// </summary>
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// モデル名
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// モデルの説明
        /// </summary>
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// モデルバージョン
        /// </summary>
        public string Version { get; set; } = string.Empty;
        
        /// <summary>
        /// 元言語
        /// </summary>
        public string SourceLanguage { get; set; } = string.Empty;
        
        /// <summary>
        /// 対象言語
        /// </summary>
        public string TargetLanguage { get; set; } = string.Empty;
        
        /// <summary>
        /// モデルタイプ
        /// </summary>
        public string ModelType { get; set; } = string.Empty;
        
        /// <summary>
        /// モデルサイズ（バイト）
        /// </summary>
        public long SizeInBytes { get; set; }
        
        /// <summary>
        /// モデルファイルパス
        /// </summary>
        public string ModelFilePath { get; set; } = string.Empty;
        
        /// <summary>
        /// トークナイザーファイルパス
        /// </summary>
        public string TokenizerFilePath { get; set; } = string.Empty;
        
        /// <summary>
        /// ボキャブラリーファイルパス
        /// </summary>
        public string VocabularyFilePath { get; set; } = string.Empty;
        
        /// <summary>
        /// 必要なメモリ（MB）
        /// </summary>
        public int RequiredMemoryMB { get; set; }
        
        /// <summary>
        /// GPU推論をサポートするかどうか
        /// </summary>
        public bool SupportsGpuInference { get; set; }
        
        /// <summary>
        /// ライセンス情報
        /// </summary>
        public string License { get; set; } = string.Empty;
        
        /// <summary>
        /// 作者/提供者
        /// </summary>
        public string Author { get; set; } = string.Empty;
        
        /// <summary>
        /// URLやその他のメタデータ
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }
    
    /// <summary>
    /// ONNX翻訳設定クラス
    /// </summary>
    public class OnnxTranslationSettings
    {
        /// <summary>
        /// GPUアクセラレーションを有効化
        /// </summary>
        public bool EnableGpuAcceleration { get; set; } = false;
        
        /// <summary>
        /// デバイスID
        /// </summary>
        public int DeviceId { get; set; } = 0;
        
        /// <summary>
        /// スレッド数
        /// </summary>
        public int ThreadCount { get; set; } = 2;
        
        /// <summary>
        /// バッチサイズ
        /// </summary>
        public int BatchSize { get; set; } = 1;
        
        /// <summary>
        /// 最大シーケンス長
        /// </summary>
        public int MaxSequenceLength { get; set; } = 128;
        
        /// <summary>
        /// ビームサイズ
        /// </summary>
        public int BeamSize { get; set; } = 4;
        
        /// <summary>
        /// 量子化を有効化
        /// </summary>
        public bool EnableQuantization { get; set; } = false;
        
        /// <summary>
        /// メモリ使用量制限（MB）
        /// </summary>
        public int MemoryLimit { get; set; } = 512;
        
        /// <summary>
        /// 推論のタイムアウト（ミリ秒）
        /// </summary>
        public int InferenceTimeoutMs { get; set; } = 5000;
        
        /// <summary>
        /// キャッシュを有効化
        /// </summary>
        public bool EnableCache { get; set; } = true;
        
        /// <summary>
        /// 詳細ログを有効化
        /// </summary>
        public bool EnableVerboseLogging { get; set; } = false;
    }
    
    /// <summary>
    /// モデル検証結果クラス
    /// </summary>
    public class ModelValidationResult
    {
        /// <summary>
        /// 検証が成功したかどうか
        /// </summary>
        public bool IsValid { get; set; }
        
        /// <summary>
        /// 検証メッセージ
        /// </summary>
        public string Message { get; set; } = string.Empty;
        
        /// <summary>
        /// モデル情報（検証が成功した場合）
        /// </summary>
        public TranslationModelInfo? ModelInfo { get; set; }
        
        /// <summary>
        /// エラーリスト（検証が失敗した場合）
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();
        
        /// <summary>
        /// 警告リスト
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }
    
    /// <summary>
    /// ONNX推論セッションラッパークラス
    /// </summary>
    public interface IOnnxInferenceSession : IDisposable
    {
        /// <summary>
        /// セッションが有効かどうか
        /// </summary>
        bool IsValid { get; }
        
        /// <summary>
        /// モデルのメタデータ
        /// </summary>
        IReadOnlyDictionary<string, string> ModelMetadata { get; }
        
        /// <summary>
        /// 入力ノード情報
        /// </summary>
        IReadOnlyList<NodeInfo> InputNodes { get; }
        
        /// <summary>
        /// 出力ノード情報
        /// </summary>
        IReadOnlyList<NodeInfo> OutputNodes { get; }
        
        /// <summary>
        /// 推論を実行します
        /// </summary>
        /// <param name="inputs">入力テンソル</param>
        /// <returns>出力テンソル</returns>
        IDictionary<string, OnnxTensor> Run(IDictionary<string, OnnxTensor> inputs);
        
        /// <summary>
        /// 非同期で推論を実行します
        /// </summary>
        /// <param name="inputs">入力テンソル</param>
        /// <returns>出力テンソル</returns>
        Task<IDictionary<string, OnnxTensor>> RunAsync(IDictionary<string, OnnxTensor> inputs);
    }
    
    /// <summary>
    /// トークナイザーインターフェース
    /// </summary>
    public interface ITokenizer
    {
        /// <summary>
        /// トークナイザーのタイプ
        /// </summary>
        TokenizerType Type { get; }
        
        /// <summary>
        /// ボキャブラリーサイズ
        /// </summary>
        int VocabularySize { get; }
        
        /// <summary>
        /// テキストをトークン化します
        /// </summary>
        /// <param name="text">テキスト</param>
        /// <returns>トークンIDのリスト</returns>
        IReadOnlyList<int> Tokenize(string text);
        
        /// <summary>
        /// トークンからテキストに戻します
        /// </summary>
        /// <param name="tokens">トークンIDのリスト</param>
        /// <returns>テキスト</returns>
        string Detokenize(IEnumerable<int> tokens);
        
        /// <summary>
        /// モデルファイルからトークナイザーを読み込みます
        /// </summary>
        /// <param name="modelPath">モデルファイルパス</param>
        /// <returns>読み込みが成功したかどうか</returns>
        Task<bool> LoadFromFileAsync(string modelPath);
    }
    
    /// <summary>
    /// トークナイザータイプ列挙型
    /// </summary>
    public enum TokenizerType
    {
        /// <summary>
        /// SentencePieceトークナイザー
        /// </summary>
        SentencePiece,
        
        /// <summary>
        /// WordPieceトークナイザー
        /// </summary>
        WordPiece,
        
        /// <summary>
        /// BPEトークナイザー
        /// </summary>
        BPE,
        
        /// <summary>
        /// Unigram
        /// </summary>
        Unigram,
        
        /// <summary>
        /// 文字単位
        /// </summary>
        Character
    }
}
```

## ONNX翻訳エンジン実装例
```csharp
namespace Baketa.Translation.Onnx
{
    /// <summary>
    /// ONNX翻訳エンジン実装クラス
    /// </summary>
    public class OnnxTranslationEngine : IOnnxTranslationEngine
    {
        private readonly IModelRepository _modelRepository;
        private readonly ITokenizerFactory _tokenizerFactory;
        private readonly ILogger? _logger;
        private readonly SemaphoreSlim _sessionLock = new SemaphoreSlim(1, 1);
        private readonly OnnxTranslationSettings _settings;
        private IOnnxInferenceSession? _session;
        private ITokenizer? _tokenizer;
        private TranslationModelInfo? _currentModel;
        private bool _disposed;
        
        /// <summary>
        /// 新しいONNX翻訳エンジンを初期化します
        /// </summary>
        /// <param name="modelRepository">モデルリポジトリ</param>
        /// <param name="tokenizerFactory">トークナイザーファクトリー</param>
        /// <param name="settings">翻訳設定</param>
        /// <param name="logger">ロガー</param>
        public OnnxTranslationEngine(
            IModelRepository modelRepository,
            ITokenizerFactory tokenizerFactory,
            OnnxTranslationSettings settings,
            ILogger? logger = null)
        {
            _modelRepository = modelRepository ?? throw new ArgumentNullException(nameof(modelRepository));
            _tokenizerFactory = tokenizerFactory ?? throw new ArgumentNullException(nameof(tokenizerFactory));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger;
            
            _logger?.LogInformation("ONNX翻訳エンジンが初期化されました");
        }
        
        /// <inheritdoc />
        public string Name => "ONNX Translation Engine";
        
        /// <inheritdoc />
        public IReadOnlyList<string> SupportedSourceLanguages => 
            _currentModel != null ? new[] { _currentModel.SourceLanguage } : Array.Empty<string>();
            
        /// <inheritdoc />
        public IReadOnlyList<string> SupportedTargetLanguages => 
            _currentModel != null ? new[] { _currentModel.TargetLanguage } : Array.Empty<string>();
            
        /// <inheritdoc />
        public TranslationCapabilities Capabilities => new TranslationCapabilities
        {
            SupportsAutoDetection = false,
            SupportsOfflineTranslation = true,
            SupportsBatchTranslation = _settings.BatchSize > 1,
            SupportsFormattingPreservation = true
        };
        
        /// <inheritdoc />
        public async Task<TranslationResult> TranslateAsync(string text, string sourceLanguage, string targetLanguage, TranslationOptions? options = null)
        {
            if (string.IsNullOrEmpty(text))
                return new TranslationResult { OriginalText = text, TranslatedText = text };
                
            // モデルが読み込まれているか確認
            if (_session == null || _tokenizer == null || _currentModel == null)
            {
                await LoadDefaultModelAsync();
                
                if (_session == null || _tokenizer == null || _currentModel == null)
                {
                    throw new InvalidOperationException("翻訳モデルが読み込まれていません");
                }
            }
            
            // モデルの言語ペアを確認
            if (_currentModel.SourceLanguage != sourceLanguage || _currentModel.TargetLanguage != targetLanguage)
            {
                // 適切なモデルを探してロード
                var matchingModel = await _modelRepository.FindModelAsync(sourceLanguage, targetLanguage);
                if (matchingModel != null)
                {
                    await SwitchModelAsync(matchingModel.Id);
                }
                else
                {
                    throw new NotSupportedException($"言語ペア {sourceLanguage}-{targetLanguage} に対応するモデルがありません");
                }
            }
            
            try
            {
                // 翻訳処理を開始
                var sw = Stopwatch.StartNew();
                
                // テキストをトークン化
                var tokens = _tokenizer.Tokenize(text);
                
                // シーケンス長をチェック
                if (tokens.Count > _settings.MaxSequenceLength)
                {
                    _logger?.LogWarning("入力テキストが最大シーケンス長を超えています：{TokenCount}/{MaxLength}", 
                        tokens.Count, _settings.MaxSequenceLength);
                    
                    // 長すぎるテキストは分割して処理することも検討
                }
                
                // 入力テンソルを準備
                var inputs = PrepareInputs(tokens);
                
                // 推論を実行
                using var outputs = await _session.RunAsync(inputs);
                
                // 出力を処理
                var translatedTokens = ProcessOutputs(outputs);
                
                // トークンをテキストに戻す
                var translatedText = _tokenizer.Detokenize(translatedTokens);
                
                sw.Stop();
                
                return new TranslationResult
                {
                    OriginalText = text,
                    TranslatedText = translatedText,
                    SourceLanguage = sourceLanguage,
                    TargetLanguage = targetLanguage,
                    Provider = Name,
                    ElapsedMilliseconds = sw.ElapsedMilliseconds
                };
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ONNX翻訳中にエラーが発生しました");
                throw;
            }
        }
        
        /// <inheritdoc />
        public async Task<IReadOnlyList<TranslationModelInfo>> GetAvailableModelsAsync()
        {
            return await _modelRepository.GetAllModelsAsync();
        }
        
        /// <inheritdoc />
        public TranslationModelInfo? GetCurrentModel()
        {
            return _currentModel;
        }
        
        /// <inheritdoc />
        public async Task<bool> SwitchModelAsync(string modelId)
        {
            if (string.IsNullOrEmpty(modelId))
                throw new ArgumentException("モデルIDが空です", nameof(modelId));
                
            // 現在のモデルと同じ場合は何もしない
            if (_currentModel?.Id == modelId)
                return true;
                
            // モデルを取得
            var model = await _modelRepository.GetModelAsync(modelId);
            if (model == null)
            {
                _logger?.LogWarning("モデルが見つかりません: {ModelId}", modelId);
                return false;
            }
            
            // セマフォを取得
            await _sessionLock.WaitAsync();
            
            try
            {
                // 既存のセッションを破棄
                DisposeCurrentSession();
                
                // 新しいセッションを作成
                var sessionOptions = CreateSessionOptions();
                _session = await CreateInferenceSessionAsync(model.ModelFilePath, sessionOptions);
                
                // トークナイザーを読み込み
                _tokenizer = _tokenizerFactory.CreateTokenizer(GetTokenizerTypeFromModel(model));
                await _tokenizer.LoadFromFileAsync(model.TokenizerFilePath);
                
                // 現在のモデルを更新
                _currentModel = model;
                
                _logger?.LogInformation("モデルを切り替えました: {ModelId}", modelId);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "モデル切り替え中にエラーが発生しました: {ModelId}", modelId);
                
                // エラー発生時は状態をクリア
                DisposeCurrentSession();
                _currentModel = null;
                _tokenizer = null;
                
                return false;
            }
            finally
            {
                // セマフォを解放
                _sessionLock.Release();
            }
        }
        
        /// <inheritdoc />
        public OnnxTranslationSettings GetOnnxSettings()
        {
            return _settings;
        }
        
        /// <inheritdoc />
        public async Task<bool> UpdateOnnxSettingsAsync(OnnxTranslationSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
                
            // 重要な設定が変更された場合はセッションを再作成
            bool needsRecreation = settings.EnableGpuAcceleration != _settings.EnableGpuAcceleration ||
                                  settings.DeviceId != _settings.DeviceId ||
                                  settings.ThreadCount != _settings.ThreadCount;
                                  
            // 設定を更新
            _settings.EnableGpuAcceleration = settings.EnableGpuAcceleration;
            _settings.DeviceId = settings.DeviceId;
            _settings.ThreadCount = settings.ThreadCount;
            _settings.BatchSize = settings.BatchSize;
            _settings.MaxSequenceLength = settings.MaxSequenceLength;
            _settings.BeamSize = settings.BeamSize;
            _settings.EnableQuantization = settings.EnableQuantization;
            _settings.MemoryLimit = settings.MemoryLimit;
            _settings.InferenceTimeoutMs = settings.InferenceTimeoutMs;
            _settings.EnableCache = settings.EnableCache;
            _settings.EnableVerboseLogging = settings.EnableVerboseLogging;
            
            // セッションの再作成が必要な場合
            if (needsRecreation && _currentModel != null)
            {
                // 現在のモデルIDを保存
                string currentModelId = _currentModel.Id;
                
                // セッションを再作成
                await SwitchModelAsync(currentModelId);
            }
            
            return true;
        }
        
        // 他のインターフェース実装メソッドは省略
        
        /// <summary>
        /// デフォルトモデルを読み込みます
        /// </summary>
        private async Task LoadDefaultModelAsync()
        {
            // 利用可能なモデルを取得
            var models = await _modelRepository.GetAllModelsAsync();
            if (models.Count == 0)
            {
                throw new InvalidOperationException("利用可能なモデルがありません");
            }
            
            // デフォルトモデルを選択（例：日本語→英語）
            var defaultModel = models.FirstOrDefault(m => 
                m.SourceLanguage == "ja" && m.TargetLanguage == "en") ?? models[0];
                
            // モデルを読み込み
            await SwitchModelAsync(defaultModel.Id);
        }
        
        /// <summary>
        /// 現在のセッションを破棄します
        /// </summary>
        private void DisposeCurrentSession()
        {
            if (_session != null)
            {
                _session.Dispose();
                _session = null;
            }
        }
        
        /// <summary>
        /// セッションオプションを作成します
        /// </summary>
        /// <returns>セッションオプション</returns>
        private SessionOptions CreateSessionOptions()
        {
            var options = new SessionOptions();
            
            // GPUアクセラレーションの設定
            if (_settings.EnableGpuAcceleration)
            {
                options.AppendExecutionProvider_CUDA(_settings.DeviceId);
            }
            
            // スレッド数の設定
            options.IntraOpNumThreads = _settings.ThreadCount;
            
            // メモリ制限の設定
            if (_settings.MemoryLimit > 0)
            {
                options.AddSessionConfigEntry("session.memory.limit", _settings.MemoryLimit.ToString());
            }
            
            // ログレベルの設定
            if (_settings.EnableVerboseLogging)
            {
                options.LogSeverityLevel = OrtLoggingLevel.Verbose;
            }
            else
            {
                options.LogSeverityLevel = OrtLoggingLevel.Warning;
            }
            
            return options;
        }
        
        /// <summary>
        /// 推論セッションを作成します
        /// </summary>
        /// <param name="modelPath">モデルファイルパス</param>
        /// <param name="options">セッションオプション</param>
        /// <returns>推論セッション</returns>
        private async Task<IOnnxInferenceSession> CreateInferenceSessionAsync(string modelPath, SessionOptions options)
        {
            // モデルファイルが存在するか確認
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("モデルファイルが見つかりません", modelPath);
            }
            
            // 非同期でモデルを読み込む
            return await Task.Run(() => new OnnxInferenceSessionWrapper(new InferenceSession(modelPath, options)));
        }
        
        /// <summary>
        /// モデルからトークナイザータイプを取得します
        /// </summary>
        /// <param name="model">モデル情報</param>
        /// <returns>トークナイザータイプ</returns>
        private TokenizerType GetTokenizerTypeFromModel(TranslationModelInfo model)
        {
            // モデルのメタデータからトークナイザータイプを取得
            if (model.Metadata.TryGetValue("tokenizer_type", out var tokenizerTypeStr))
            {
                if (Enum.TryParse<TokenizerType>(tokenizerTypeStr, true, out var tokenizerType))
                {
                    return tokenizerType;
                }
            }
            
            // トークナイザーファイルの拡張子から推測
            if (model.TokenizerFilePath.EndsWith(".model") || model.TokenizerFilePath.EndsWith(".spm"))
            {
                return TokenizerType.SentencePiece;
            }
            else if (model.TokenizerFilePath.EndsWith(".vocab"))
            {
                return TokenizerType.WordPiece;
            }
            else if (model.TokenizerFilePath.EndsWith(".bpe"))
            {
                return TokenizerType.BPE;
            }
            
            // デフォルト
            return TokenizerType.SentencePiece;
        }
        
        /// <summary>
        /// 入力テンソルを準備します
        /// </summary>
        /// <param name="tokens">トークンIDのリスト</param>
        /// <returns>入力テンソル</returns>
        private IDictionary<string, OnnxTensor> PrepareInputs(IReadOnlyList<int> tokens)
        {
            // 入力テンソルの作成（モデルの仕様に依存）
            var inputs = new Dictionary<string, OnnxTensor>();
            
            // 入力ノード名を取得
            var inputNodeNames = _session!.InputNodes.Select(n => n.Name).ToList();
            
            // 一般的な入力テンソルの例（実際のモデルに合わせて調整が必要）
            if (inputNodeNames.Contains("input_ids"))
            {
                // トークンIDを入力テンソルに変換
                int[] paddedTokens = new int[_settings.MaxSequenceLength];
                int length = Math.Min(tokens.Count, _settings.MaxSequenceLength);
                
                for (int i = 0; i < length; i++)
                {
                    paddedTokens[i] = tokens[i];
                }
                
                // 1次元の場合
                //inputs["input_ids"] = OnnxValue.CreateTensorFromArray(paddedTokens).AsTensor<int>();
                
                // バッチ処理の場合（2次元）
                var inputArray = new int[1, _settings.MaxSequenceLength];
                for (int i = 0; i < length; i++)
                {
                    inputArray[0, i] = tokens[i];
                }
                
                inputs["input_ids"] = OnnxValue.CreateTensorFromArray(inputArray).AsTensor<int>();
            }
            
            // 注意マスクが必要な場合
            if (inputNodeNames.Contains("attention_mask"))
            {
                var attentionMask = new int[1, _settings.MaxSequenceLength];
                int length = Math.Min(tokens.Count, _settings.MaxSequenceLength);
                
                for (int i = 0; i < length; i++)
                {
                    attentionMask[0, i] = 1; // 1:トークンあり、0:パディング
                }
                
                inputs["attention_mask"] = OnnxValue.CreateTensorFromArray(attentionMask).AsTensor<int>();
            }
            
            // その他のモデル固有の入力（実際のモデルに合わせて調整）
            
            return inputs;
        }
        
        /// <summary>
        /// 出力を処理します
        /// </summary>
        /// <param name="outputs">出力テンソル</param>
        /// <returns>翻訳結果のトークンID</returns>
        private IReadOnlyList<int> ProcessOutputs(IDictionary<string, OnnxTensor> outputs)
        {
            // 出力ノード名を確認
            var outputNodeNames = _session!.OutputNodes.Select(n => n.Name).ToList();
            
            // 一般的な出力テンソルの例（実際のモデルに合わせて調整が必要）
            if (outputs.TryGetValue("output_ids", out var outputTensor))
            {
                // 出力テンソルからトークンIDを取得
                var outputShape = outputTensor.Dimensions;
                
                // バッチサイズ=1の場合の例
                if (outputShape.Length == 2) // [1, sequence_length]
                {
                    var resultTokens = new List<int>();
                    var outputArray = outputTensor.ToArray<int>();
                    
                    // 2次元配列から1次元リストへ変換
                    int batchIndex = 0; // バッチインデックス（通常は0）
                    for (int i = 0; i < outputShape[1]; i++)
                    {
                        int token = outputArray[batchIndex, i];
                        
                        // EOS（End of Sequence）トークンをチェック
                        if (token == 0 || token == 1 || token == 2) // 一般的なEOSトークンID（モデルによって異なる）
                        {
                            break;
                        }
                        
                        resultTokens.Add(token);
                    }
                    
                    return resultTokens;
                }
                else if (outputShape.Length == 1) // [sequence_length]
                {
                    var resultTokens = new List<int>();
                    var outputArray = outputTensor.ToArray<int>();
                    
                    // 1次元配列の処理
                    for (int i = 0; i < outputShape[0]; i++)
                    {
                        int token = outputArray[i];
                        
                        // EOS（End of Sequence）トークンをチェック
                        if (token == 0 || token == 1 || token == 2) // 一般的なEOSトークンID（モデルによって異なる）
                        {
                            break;
                        }
                        
                        resultTokens.Add(token);
                    }
                    
                    return resultTokens;
                }
            }
            
            // モデル固有の出力処理（実際のモデルに合わせて調整）
            
            // 何も見つからない場合は空のリストを返す
            return Array.Empty<int>();
        }
        
        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースも解放するかどうか</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    DisposeCurrentSession();
                    _sessionLock.Dispose();
                }
                
                _disposed = true;
            }
        }
    }
    
    /// <summary>
    /// OnnxInferenceSessionのラッパークラス
    /// </summary>
    internal class OnnxInferenceSessionWrapper : IOnnxInferenceSession
    {
        private readonly InferenceSession _session;
        private bool _disposed;
        
        /// <summary>
        /// 新しいOnnxInferenceSessionWrapperを初期化します
        /// </summary>
        /// <param name="session">ONNX推論セッション</param>
        public OnnxInferenceSessionWrapper(InferenceSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }
        
        /// <inheritdoc />
        public bool IsValid => !_disposed && _session != null;
        
        /// <inheritdoc />
        public IReadOnlyDictionary<string, string> ModelMetadata => _session.ModelMetadata;
        
        /// <inheritdoc />
        public IReadOnlyList<NodeInfo> InputNodes => _session.InputMetadata.Values.ToList();
        
        /// <inheritdoc />
        public IReadOnlyList<NodeInfo> OutputNodes => _session.OutputMetadata.Values.ToList();
        
        /// <inheritdoc />
        public IDictionary<string, OnnxTensor> Run(IDictionary<string, OnnxTensor> inputs)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OnnxInferenceSessionWrapper));
                
            return _session.Run(inputs);
        }
        
        /// <inheritdoc />
        public Task<IDictionary<string, OnnxTensor>> RunAsync(IDictionary<string, OnnxTensor> inputs)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OnnxInferenceSessionWrapper));
                
            // 非同期実行（ONNX Runtimeは直接的な非同期APIを提供していないため、Task.Runでラップ）
            return Task.Run(() => Run(inputs));
        }
        
        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースも解放するかどうか</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _session?.Dispose();
                }
                
                _disposed = true;
            }
        }
    }
}
```

## 実装上の注意点
- ONNX Runtimeのバージョン互換性に注意する（NuGetパッケージバージョンと依存関係）
- GPU対応のための環境設定とドライバ要件を明確にする
- メモリ使用量の最適化（量子化技術の活用、モデルの最適化）
- モデルファイルの管理と配布（モデルサイズが大きい場合の対策）
- 異なるトークナイザーのサポートと適切な選択
- クロスプラットフォーム対応（Windows限定ですが、将来の拡張性を考慮）
- パフォーマンスのベンチマークとプロファイリング
- モデルのバージョン管理と互換性確保
- 適切なエラーハンドリングとリカバリー戦略
- システムリソース使用状況の監視と制限機能
- ユーザーフレンドリーなUIとモデル管理機能
- 翻訳品質のモニタリングと評価機能
- プライバシーとセキュリティの考慮（ローカル処理のメリット）

## 関連Issue/参考
- 親Issue: なし（これが親Issue）
- 関連: #9 翻訳システム基盤の構築
- 関連: #12 設定画面
- 参照: E:\dev\Baketa\docs\3-architecture\translation-system\onnx-translation.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (6.2 ログレベルの適切な使用)
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (4.2 リソース解放とDisposable)

## マイルストーン
マイルストーン4: 機能拡張と最適化

## ラベル
- `type: feature`
- `priority: low`
- `component: translation`
