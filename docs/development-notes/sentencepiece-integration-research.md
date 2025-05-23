# SentencePiece統合 - 技術調査結果と選定推奨

## 📋 エグゼクティブサマリー

Baketaプロジェクトにおける実際のSentencePiece統合について技術調査を実施した結果、**Microsoft.ML.Tokenizers**パッケージの使用を推奨します。

### 推奨理由
1. 既にプロジェクトで使用中（v0.21.0）
2. Microsoft公式サポート
3. SentencePieceTokenizerクラスの完全実装
4. ONNX統合に最適化
5. 追加の依存関係なし

---

## 🔍 技術調査結果

### 1. Microsoft.ML.Tokenizers（推奨）

#### 概要
- **パッケージ**: Microsoft.ML.Tokenizers
- **バージョン**: 0.21.0（既にインストール済み）
- **提供元**: Microsoft
- **ライセンス**: MIT License

#### 主要機能
```csharp
// SentencePieceトークナイザーの作成
using var modelStream = File.OpenRead("sentencepiece.model");
var tokenizer = SentencePieceTokenizer.Create(
    modelStream,
    addBeginOfSentence: true,
    addEndOfSentence: false
);

// トークン化
var encoded = tokenizer.Encode("テスト文章");
var tokenIds = encoded.Ids;

// デコード
var decoded = tokenizer.Decode(tokenIds);
```

#### メリット
- ✅ **既存依存関係の活用** - 追加パッケージ不要
- ✅ **公式サポート** - Microsoftによる継続的メンテナンス
- ✅ **ONNX最適化** - OPUS-MTモデルとの統合が容易
- ✅ **包括的なAPI** - エンコード/デコード、正規化機能を完備
- ✅ **ドキュメント充実** - Microsoft Learnでの公式ドキュメント

#### デメリット
- ❌ **SentencePieceTokenizerの利用制限**
  - 正式版1.0.2ではSentencePieceTokenizerが含まれていない
  - プレビュー版（0.21.0）を使用する必要がある
  - プロジェクトで既に0.21.0を使用中のため問題なし
- ❌ **機能制限**
  - サブワード正則化（subword regularization）未対応
  - カスタム正規化ルールの制限
  - 一部の高度なSentencePiece機能が利用不可

#### 注意事項
- **バージョン情報**: Microsoft.ML.Tokenizers 1.0.2（2024年11月リリース）は正式版だが、SentencePieceTokenizerはプレビュー版のみ
- **SentencePieceNormalizer**: プレビュー版0.21.0に含まれており、NFKC正規化をサポート
- **推奨アプローチ**: 当面はプレビュー版0.21.0を使用し、SentencePieceTokenizerが正式版に含まれた際に移行

#### 実装例
```csharp
public class MicrosoftMLSentencePieceTokenizer : ITokenizer
{
    private readonly SentencePieceTokenizer _tokenizer;
    
    public MicrosoftMLSentencePieceTokenizer(string modelPath)
    {
        using var stream = File.OpenRead(modelPath);
        _tokenizer = SentencePieceTokenizer.Create(stream);
    }
    
    public int[] Tokenize(string text)
    {
        var result = _tokenizer.Encode(text);
        return result.Ids.ToArray();
    }
    
    public string Decode(int[] tokens)
    {
        return _tokenizer.Decode(tokens);
    }
}
```

### 2. BlingFire（代替案）

#### 概要
- **パッケージ**: BlingFireNuget
- **バージョン**: 0.1.8
- **提供元**: Microsoft Bing Team
- **ライセンス**: MIT License

#### 特徴
- SentencePieceより**2倍高速**と公称
- BPE、Unigram LMサポート
- Windows/Linux/macOS対応

#### メリット
- ✅ **高速処理** - パフォーマンス重視の設計
- ✅ **Microsoft製** - 品質保証
- ✅ **軽量** - 小さなフットプリント

#### デメリット
- ❌ **モデル変換の複雑さ**
  - SentencePiece → BlingFire形式への変換ツールなし
  - 変換実装には2-3人日の開発コストが必要
  - 語彙マッピングの不整合リスク
- ❌ 追加の依存関係
- ❌ OPUS-MTモデルとの互換性検証が必要

### 3. ONNX Runtime Extensions（高度な選択肢）

#### 概要
- **パッケージ**: Microsoft.ML.OnnxRuntimeExtensions
- **用途**: カスタムオペレーターとしてのトークナイザー統合

#### 適用シナリオ
- **高スループットサーバー**: 秒間1000+リクエストの処理
- **GPUアクセラレーション**: CUDA対応トークナイザー
- **エンドツーエンド最適化**: 前処理から推論まで一体化
- **マイクロサービス**: トークン化専用サービスの構築

#### 特徴
```csharp
// カスタムオペレーターとしてSentencePieceを登録
sessionOptions.RegisterCustomOpLibraryV2("ortextensions.dll");
```

#### メリット
- ✅ ONNXグラフ内でのトークン化
- ✅ エンドツーエンドの推論パイプライン
- ✅ C++実装による高速処理

#### デメリット
- ❌ 複雑な実装
- ❌ プラットフォーム依存のDLL管理
- ❌ デバッグが困難

### 4. SentencePieceWrapper（コミュニティ選択肢）

#### 概要
- **リポジトリ**: wang1ang/SentencePieceWrapper
- **状態**: メンテナンス不明

#### デメリット
- ❌ ドキュメント不足
- ❌ 更新頻度が低い
- ❌ 品質保証なし

### 5. P/Invoke直接実装（非推奨）

#### 概要
- Google SentencePieceのC++ライブラリを直接呼び出し

#### デメリット
- ❌ プラットフォーム依存性が高い
- ❌ メモリ管理が複雑
- ❌ 実装コストが高い

---

## 📊 比較マトリックス

| 項目 | Microsoft.ML.Tokenizers | BlingFire | ONNX Extensions | Wrapper | P/Invoke |
|------|------------------------|-----------|-----------------|---------|----------|
| **実装難易度** | ⭐（簡単） | ⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| **パフォーマンス** | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ |
| **保守性** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐ | ⭐ |
| **互換性** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐ |
| **ドキュメント** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐ | ⭐⭐ |

### 評価基準の説明
- **実装難易度**: ⭐=非常に簡単（1日以内）、⭐⭐⭐=標準（3-5日）、⭐⭐⭐⭐⭐=非常に困難（2週間以上）
- **パフォーマンス**: ⭐=低速、⭐⭐⭐=実用的、⭐⭐⭐⭐⭐=最高速
- **保守性**: ⭐=メンテナンス困難、⭐⭐⭐=通常、⭐⭐⭐⭐⭐=優れた保守性
- **互換性**: ⭐=互換性問題多数、⭐⭐⭐=一部制限、⭐⭐⭐⭐⭐=完全互換
- **ドキュメント**: ⭐=ドキュメントなし、⭐⭐⭐=基本的、⭐⭐⭐⭐⭐=包括的

### Baketaプロジェクトにおける重要度
1. **保守性**（最重要）: 長期運用を前提とした開発のため
2. **実装難易度**（重要）: 迅速な開発サイクルが要求される
3. **互換性**（重要）: OPUS-MTモデルとの統合が必須
4. **パフォーマンス**（中程度）: リアルタイム処理だが、ゲーム翻訳では許容範囲が広い
5. **ドキュメント**（中程度）: チーム開発での知識共有に必要

---

## 🎯 推奨実装アプローチ

### フェーズ1: Microsoft.ML.Tokenizers統合（推奨）

1. **暫定実装の置き換え**
```csharp
public class RealSentencePieceTokenizer : ITokenizer, IDisposable
{
    private readonly SentencePieceTokenizer _innerTokenizer;
    private readonly SentencePieceNormalizer _normalizer;
    private readonly ILogger<RealSentencePieceTokenizer> _logger;
    private readonly string _modelName;
    private readonly int _maxInputLength;
    
    public RealSentencePieceTokenizer(
        string modelPath, 
        ILogger<RealSentencePieceTokenizer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _modelName = Path.GetFileNameWithoutExtension(modelPath);
        _maxInputLength = 10000; // 設定可能にすることも検討
        
        try
        {
            using var stream = File.OpenRead(modelPath);
            _innerTokenizer = SentencePieceTokenizer.Create(
                stream,
                addBeginOfSentence: true,
                addEndOfSentence: false
            );
            _normalizer = new SentencePieceNormalizer();
            
            _logger.LogInformation(
                "SentencePieceトークナイザーを初期化しました: {ModelPath}", 
                modelPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, 
                "SentencePieceトークナイザーの初期化に失敗しました: {ModelPath}", 
                modelPath);
            throw;
        }
    }
    
    public int[] Tokenize(string text)
    {
        // 正規化（NFKC: 互換性のある正規化形式）
        // OPUS-MTモデルの学習時設定と一致させる必要がある
        var normalized = _normalizer.Normalize(text);
        
        // トークン化
        var result = _innerTokenizer.Encode(normalized);
        return result.Ids.ToArray();
    }
    
    // 正規化設定の確認メソッド
    public void ValidateNormalization()
    {
        // テストケースで正規化が期待通りか確認
        var testCases = new Dictionary<string, string>
        {
            { "①②③", "123" },  // 数字の正規化
            { "ｱｲｳ", "アイウ" },  // カタカナの正規化
            { "Ａ", "A" }       // 全角英字の正規化
        };
        
        foreach (var test in testCases)
        {
            var normalized = _normalizer.Normalize(test.Key);
            if (normalized != test.Value)
            {
                _logger.LogWarning("正規化の不一致: {Input} → {Actual} (期待値: {Expected})", 
                    test.Key, normalized, test.Value);
            }
        }
    }
}
```

2. **特殊トークン管理の実装**
```csharp
public SpecialTokens GetSpecialTokens()
{
    // 注意: デフォルト値は暫定的なもの
    // OPUS-MTモデルの仕様に基づいて検証が必要
    var specialTokens = new SpecialTokens();
    
    // モデルから取得できない場合は例外をスロー
    if (_innerTokenizer.UnknownTokenId == null)
        throw new InvalidOperationException("モデルに<unk>トークンが定義されていません");
        
    specialTokens.UnknownId = _innerTokenizer.UnknownTokenId.Value;
    specialTokens.BeginOfSentenceId = _innerTokenizer.BeginningOfSentenceTokenId ?? 
        throw new InvalidOperationException("モデルに<s>トークンが定義されていません");
    specialTokens.EndOfSentenceId = _innerTokenizer.EndOfSentenceTokenId ?? 
        throw new InvalidOperationException("モデルに</s>トークンが定義されていません");
    
    // パディングトークンはオプショナル
    specialTokens.PaddingId = _innerTokenizer.PaddingTokenId ?? -1;
    
    return specialTokens;
}
```

**重要**: OPUS-MTモデルの仕様書を確認し、必須トークンと任意トークンを明確化すること

### フェーズ2: パフォーマンス最適化（条件付き）

#### 移行判断基準
以下のいずれかの条件を満たした場合に移行を検討：
- **レイテンシ**: 平均処理時間が100ms/文を超える
- **スループット**: 10文/秒を下回る
- **CPU使用率**: トークン化処理で50%以上を占める
- **メモリ使用量**: 1GBを超える常駐メモリ

パフォーマンスが問題になった場合のみ：

1. **BlingFireへの移行を検討**
   - モデル変換ツールの開発
   - ベンチマークテストの実施

2. **ONNX Runtime Extensionsの評価**
   - エンドツーエンドパイプラインの構築
   - GPU最適化の検証

---

## 🔧 実装手順

### 1. 既存コードの更新
```csharp
// appsettings.json
{
  "SentencePiece": {
    "ModelsDirectory": "Models/SentencePiece",
    "DefaultModel": "opus-mt-ja-en",
    "DownloadUrl": "https://your-storage.blob.core.windows.net/models/{0}.model"
  }
}

// Baketa.Infrastructure.Platform.Windows.InfrastructureModule.cs
services.Configure<SentencePieceOptions>(configuration.GetSection("SentencePiece"));
services.AddSingleton<ITokenizer>(sp =>
{
    var options = sp.GetRequiredService<IOptions<SentencePieceOptions>>();
    var modelManager = sp.GetRequiredService<SentencePieceModelManager>();
    var modelPath = modelManager.GetModelPathAsync(options.Value.DefaultModel).Result;
    var logger = sp.GetRequiredService<ILogger<RealSentencePieceTokenizer>>();
    return new RealSentencePieceTokenizer(modelPath, logger);
});
```

### 2. モデルファイル管理
```csharp
// 設定クラス
public class SentencePieceOptions
{
    public string ModelsDirectory { get; set; } = "Models/SentencePiece";
    public string DefaultModel { get; set; } = "opus-mt-ja-en";
    public string DownloadUrl { get; set; } = "https://your-storage.blob.core.windows.net/models/{0}.model";
    public int ModelCacheDays { get; set; } = 30;
    public int MaxDownloadRetries { get; set; } = 3;
}

// モデルメタデータ
public class ModelMetadata
{
    public string ModelName { get; set; } = string.Empty;
    public DateTime DownloadedAt { get; set; }
    public string Version { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Checksum { get; set; } = string.Empty;
}

// モデル管理クラス
public class SentencePieceModelManager
{
    private readonly SentencePieceOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SentencePieceModelManager> _logger;
    private readonly SemaphoreSlim _downloadSemaphore = new(1);
    
    public SentencePieceModelManager(
        IOptions<SentencePieceOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<SentencePieceModelManager> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // モデルディレクトリの作成
        Directory.CreateDirectory(_options.ModelsDirectory);
    }
    
    public async Task<string> GetModelPathAsync(string modelName)
    {
        var modelPath = Path.Combine(_options.ModelsDirectory, $"{modelName}.model");
        var metadataPath = Path.Combine(_options.ModelsDirectory, $"{modelName}.metadata.json");
        
        // モデルの存在とバージョンチェック
        if (File.Exists(modelPath) && await IsModelValidAsync(modelPath, metadataPath))
        {
            return modelPath;
        }
        
        // 同時ダウンロード防止
        await _downloadSemaphore.WaitAsync();
        try
        {
            // 再チェック（他のスレッドがダウンロード済みの可能性）
            if (File.Exists(modelPath) && await IsModelValidAsync(modelPath, metadataPath))
            {
                return modelPath;
            }
            
            await DownloadModelAsync(modelName, modelPath, metadataPath);
        }
        finally
        {
            _downloadSemaphore.Release();
        }
        
        return modelPath;
    }
    
    private async Task DownloadModelAsync(string modelName, string modelPath, string metadataPath)
    {
        var url = string.Format(_options.DownloadUrl, modelName);
        
        using var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromMinutes(5);
        
        // プログレス付きダウンロード
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        
        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var tempPath = $"{modelPath}.tmp";
        
        using (var fileStream = File.Create(tempPath))
        using (var httpStream = await response.Content.ReadAsStreamAsync())
        {
            await CopyWithProgressAsync(httpStream, fileStream, totalBytes);
        }
        
        // チェックサム計算
        var checksum = await CalculateChecksumAsync(tempPath);
        
        // メタデータ保存
        var metadata = new ModelMetadata
        {
            ModelName = modelName,
            DownloadedAt = DateTime.UtcNow,
            Version = response.Headers.ETag?.Tag ?? "unknown",
            Size = new FileInfo(tempPath).Length,
            Checksum = checksum
        };
        
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        
        // アトミックな移動
        File.Move(tempPath, modelPath, true);
        
        _logger.LogInformation("モデルダウンロード完了: {ModelName} ({Size:N0} bytes)", 
            modelName, metadata.Size);
    }
    
    // プログレス付きコピー
    private async Task CopyWithProgressAsync(Stream source, Stream destination, long totalBytes)
    {
        var buffer = new byte[81920]; // 80KB buffer
        var totalBytesRead = 0L;
        var lastProgressReport = DateTime.UtcNow;
        int bytesRead;
        
        while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length))) != 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead));
            totalBytesRead += bytesRead;
            
            // 1秒ごとに進捗報告
            if (DateTime.UtcNow - lastProgressReport > TimeSpan.FromSeconds(1))
            {
                var progress = totalBytes > 0 
                    ? (double)totalBytesRead / totalBytes * 100 
                    : 0;
                    
                _logger.LogInformation(
                    "ダウンロード進捗: {Progress:F1}% ({BytesRead:N0}/{TotalBytes:N0} bytes)",
                    progress, totalBytesRead, totalBytes);
                    
                lastProgressReport = DateTime.UtcNow;
            }
        }
    }
    
    // SHA256チェックサム計算
    private async Task<string> CalculateChecksumAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
    
    private async Task<bool> IsModelValidAsync(string modelPath, string metadataPath)
    {
        if (!File.Exists(metadataPath))
            return false;
            
        try
        {
            var json = await File.ReadAllTextAsync(metadataPath);
            var metadata = JsonSerializer.Deserialize<ModelMetadata>(json);
            
            if (metadata == null)
                return false;
                
            // 設定された日数以上古い場合は再ダウンロード
            if (metadata.DownloadedAt < DateTime.UtcNow.AddDays(-_options.ModelCacheDays))
            {
                _logger.LogInformation("モデルが古いため更新が必要: {ModelName}", metadata.ModelName);
                return false;
            }
            
            // ファイルサイズチェック
            var actualSize = new FileInfo(modelPath).Length;
            if (actualSize != metadata.Size)
            {
                _logger.LogWarning("モデルファイルサイズ不一致: {Expected} != {Actual}", 
                    metadata.Size, actualSize);
                return false;
            }
            
            // チェックサム検証（オプション）
            if (!string.IsNullOrEmpty(metadata.Checksum))
            {
                var actualChecksum = await CalculateChecksumAsync(modelPath);
                if (actualChecksum != metadata.Checksum)
                {
                    _logger.LogWarning("モデルチェックサム不一致");
                    return false;
                }
            }
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "モデルメタデータの検証エラー");
            return false;
        }
    }
}
```

#### キャッシュ戦略
- **ローカルキャッシュ**: `%LOCALAPPDATA%/Baketa/Models`
- **共有キャッシュ**: ネットワークドライブまたはAzure Blob Storage
- **バージョン管理**: ETタグベースの更新チェック
- **自動クリーンアップ**: 90日以上未使用のモデルを削除

### 3. エラーハンドリング強化
```csharp
// カスタム例外定義
public class TokenizationException : Exception
{
    public string InputText { get; init; }
    public int? CharacterPosition { get; init; }
    public string ModelName { get; init; }
    
    public TokenizationException(
        string message, 
        string inputText, 
        string modelName,
        Exception? innerException = null) 
        : base(message, innerException)
    {
        InputText = inputText;
        ModelName = modelName;
    }
}

// エラーハンドリング実装
public int[] Tokenize(string text)
{
    try
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<int>();
        
        // 入力検証
        if (text.Length > _maxInputLength)
        {
            throw new TokenizationException(
                $"入力テキストが最大長({_maxInputLength}文字)を超えています",
                text,
                _modelName);
        }
            
        var result = _innerTokenizer.Encode(text);
        return result.Ids.ToArray();
    }
    catch (OutOfMemoryException ex)
    {
        _logger.LogError(ex, "メモリ不足: テキスト長={Length}", text.Length);
        throw new TokenizationException(
            "トークン化中にメモリ不足が発生しました",
            text,
            _modelName,
            ex);
    }
    catch (Exception ex) when (ex is not TokenizationException)
    {
        _logger.LogError(ex, "トークン化エラー: {Text}", text);
        throw new TokenizationException(
            $"テキストのトークン化に失敗しました: {ex.Message}",
            text,
            _modelName,
            ex);
    }
}

// 呼び出し側でのハンドリング例
public class TranslationService
{
    private readonly ITokenizer _tokenizer;
    private readonly ILogger<TranslationService> _logger;
    
    public async Task<string> TranslateAsync(string text)
    {
        const int maxRetries = 3;
        
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                var tokens = _tokenizer.Tokenize(text);
                // 翻訳処理...
                return translatedText;
            }
            catch (TokenizationException ex) when (i < maxRetries - 1)
            {
                _logger.LogWarning(ex, 
                    "トークン化エラー (試行 {Attempt}/{MaxRetries}): {Model}", 
                    i + 1, maxRetries, ex.ModelName);
                    
                // 入力テキストの簡易正規化を試みる
                text = SimplifyText(text);
                await Task.Delay(100 * (i + 1)); // バックオフ
            }
            catch (TokenizationException ex)
            {
                // 最終試行でも失敗
                _logger.LogError(ex, "トークン化が失敗しました");
                
                // ユーザーへの通知
                return $"[翻訳エラー: テキストを処理できませんでした]";
            }
        }
    }
}
```

---

## 📈 ベンチマーク計画

### テスト項目
1. **トークン化速度**: 1000文のバッチ処理時間
2. **メモリ使用量**: ピークメモリ使用量
3. **精度**: 元のSentencePieceとの出力比較
4. **スレッドセーフティ**: 並行処理時の安定性

### テストデータセット
```csharp
public class BenchmarkDataset
{
    public static IEnumerable<TestCase> GetTestCases()
    {
        // 短文（10文字以下）
        yield return new TestCase("こんにちは", "short_ja");
        yield return new TestCase("Hello", "short_en");
        
        // 中文（50-100文字）
        yield return new TestCase(
            "本日は晴天なり。絶好の行楽日和です。", 
            "medium_ja");
            
        // 長文（500文字以上）
        yield return new TestCase(LoadLongText(), "long_mixed");
        
        // 特殊文字
        yield return new TestCase("😀🎌①②③", "special_chars");
        
        // 多言語混在
        yield return new TestCase(
            "Hello世界！Привет", 
            "multilingual");
            
        // エッジケース
        yield return new TestCase("", "empty");
        yield return new TestCase(" \t\n ", "whitespace");
        yield return new TestCase(new string('あ', 10000), "repetitive");
    }
}
```

### ベースライン比較
```python
# Python reference implementation
import sentencepiece as spm

def generate_baseline():
    sp = spm.SentencePieceProcessor()
    sp.load('opus-mt-ja-en.model')
    
    results = {}
    for test_case in test_cases:
        tokens = sp.encode_as_ids(test_case.text)
        results[test_case.id] = {
            'text': test_case.text,
            'tokens': tokens,
            'pieces': sp.encode_as_pieces(test_case.text)
        }
    
    with open('baseline.json', 'w', encoding='utf-8') as f:
        json.dump(results, f, ensure_ascii=False, indent=2)
```

### 測定コード例
```csharp
[MemoryDiagnoser]
[ThreadingDiagnoser]
public class SentencePieceBenchmark
{
    private RealSentencePieceTokenizer _tokenizer;
    private List<string> _testTexts;
    
    [GlobalSetup]
    public void Setup()
    {
        _tokenizer = new RealSentencePieceTokenizer("opus-mt-ja-en.model");
        _testTexts = BenchmarkDataset.GetTestCases()
            .Select(tc => tc.Text)
            .ToList();
    }
    
    [Benchmark(Baseline = true)]
    public void TokenizeBatch()
    {
        foreach (var text in _testTexts)
        {
            _ = _tokenizer.Tokenize(text);
        }
    }
    
    [Benchmark]
    public void TokenizeParallel()
    {
        Parallel.ForEach(_testTexts, text =>
        {
            _ = _tokenizer.Tokenize(text);
        });
    }
    
    [Benchmark]
    public void TokenizeWithCache()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        foreach (var text in _testTexts)
        {
            _ = cache.GetOrCreate(text, entry =>
            {
                entry.Size = text.Length;
                return _tokenizer.Tokenize(text);
            });
        }
    }
}
```

### 実行環境
```yaml
# benchmark-environment.yml
hardware:
  cpu: Intel Core i7-10700K @ 3.80GHz
  memory: 32GB DDR4-3200
  gpu: NVIDIA GeForce RTX 3070
  storage: NVMe SSD

software:
  os: Windows 11 Pro 22H2
  dotnet: 7.0.14
  configuration: Release
  
settings:
  gc_mode: Server
  tiered_compilation: true
  ready_to_run: true
```

---

## ⚠️ リスクと対策

### リスク1: モデル互換性
- **問題**: OPUS-MTモデルとの互換性問題
- **対策**: 
  - 事前検証スクリプトの作成
  - フォールバック実装（暫定トークナイザーへの切り替え）
  - モデル別の設定プロファイル

### リスク2: パフォーマンス
- **問題**: リアルタイム処理に不適切な速度
- **対策**: 
  - LRUキャッシュの実装（最大1000エントリ）
  - バッチ処理API の提供
  - 非同期処理の最適化

### リスク3: メモリリーク
- **問題**: 長時間動作時のメモリ増加
- **対策**: 
  - 適切なDispose実装
  - WeakReferenceを使用したキャッシュ
  - 定期的なメモリプロファイリング

### リスク4: Microsoft.ML.Tokenizersのバージョン変更
- **問題**: プレビュー版からの破壊的変更
- **対策**:
  - バージョン固定（0.21.0-preview）
  - 正式版1.0.2ではSentencePieceTokenizer未実装のため移行不可
  - 将来のバージョンでSentencePieceサポートが追加されるまで待機
  - 抽象化レイヤーの強化により影響を最小化
  - CI/CDでの互換性テスト

### リスク5: ライセンスコンプライアンス
- **問題**: OSSライセンスの不適切な使用
- **対策**:
  - MITライセンスの条件確認
  - NOTICE.txtへの記載
  - 法務レビューの実施

### リスク6: モデル品質
- **問題**: 不適切なSentencePieceモデルによる翻訳品質低下
- **対策**:
  - モデル評価指標の定義（語彙カバレッジ、OOV率）
  - A/Bテストの実施
  - モデル更新プロセスの確立
  - ユーザーフィードバックの収集

---

## 🎯 結論と次のステップ

### 推奨事項
1. **Microsoft.ML.Tokenizers**を使用した実装を進める
2. 暫定実装からの段階的移行
3. 十分なテストカバレッジの確保（目標: 90%以上）
4. パフォーマンスベースラインの確立

### アクションアイテム
1. [ ] **基礎実装**（3日）
   - [ ] RealSentencePieceTokenizerクラスの実装
   - [ ] 設定クラス（SentencePieceOptions）の作成
   - [ ] DIコンテナへの登録

2. [ ] **モデル管理**（2日）
   - [ ] SentencePieceModelManagerの実装
   - [ ] モデルダウンロード機能
   - [ ] キャッシュ戦略の実装

3. [ ] **テスト作成**（3日）
   - [ ] 単体テスト（正常系・異常系）
   - [ ] 統合テスト（OPUS-MTモデルとの連携）
   - [ ] パフォーマンステスト

4. [ ] **検証と最適化**（2日）
   - [ ] Python版SentencePieceとの出力比較
   - [ ] メモリプロファイリング
   - [ ] 必要に応じた最適化

5. [ ] **ドキュメント整備**（1日）
   - [ ] APIドキュメント
   - [ ] 使用ガイド
   - [ ] トラブルシューティングガイド

### タイムライン
- **週1（5/24-5/30）**: 基礎実装とモデル管理
- **週2（5/31-6/6）**: テスト作成と検証
- **週3（6/7-6/13）**: 最適化とドキュメント整備
- **週4（6/14-6/20）**: 本番環境への展開準備

### 成功指標
- ✅ Python版SentencePieceとの出力一致率: 99.9%以上
- ✅ 平均処理時間: 50ms/文以下
- ✅ メモリ使用量: 500MB以下
- ✅ テストカバレッジ: 90%以上
- ✅ ゼロダウンタイムでの移行完了

---

*最終更新: 2025年5月23日 - 実装詳細とリスク管理を強化、Microsoft.ML.Tokenizers 1.0.2正式版の状況を反映*