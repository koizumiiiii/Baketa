# 中国語翻訳機能の使用例

## 基本的な使用方法

### 1. 翻訳エンジンの初期化

```csharp
using Baketa.Infrastructure.Translation.Local.Onnx;
using Baketa.Infrastructure.Translation.Local.Onnx.Chinese;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

// ロガーファクトリーの準備
var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());

// 言語ペアの設定（英語→中国語）
var languagePair = new LanguagePair
{
    SourceLanguage = Language.English,
    TargetLanguage = Language.ChineseSimplified  // 簡体字を指定
};

// 翻訳エンジンの作成
var translationEngine = new OpusMtOnnxEngine(
    modelPath: "path/to/opus-mt-en-zh.onnx",
    tokenizerPath: "path/to/opus-mt-en-zh.model",
    languagePair: languagePair,
    options: new OnnxTranslationOptions(),
    loggerFactory: loggerFactory
);
```

### 2. 中国語への翻訳（簡体字指定）

```csharp
// 簡体字への翻訳
var request = new TranslationRequest
{
    SourceText = "Hello, how are you?",
    SourceLanguage = Language.English,
    TargetLanguage = Language.ChineseSimplified  // zh-CN
};

var response = await translationEngine.TranslateAsync(request);
// 実際の内部処理: ">>cmn_Hans<< Hello, how are you?" がモデルに送信される
// 結果: "你好，你好吗？" (簡体字)
```

### 3. 中国語への翻訳（繁体字指定）

```csharp
// 繁体字への翻訳
var request = new TranslationRequest
{
    SourceText = "Hello, how are you?",
    SourceLanguage = Language.English,
    TargetLanguage = Language.ChineseTraditional  // zh-TW
};

var response = await translationEngine.TranslateAsync(request);
// 実際の内部処理: ">>cmn_Hant<< Hello, how are you?" がモデルに送信される
// 結果: "你好，你好嗎？" (繁体字)
```

### 4. 中国語の文字体系自動判別

```csharp
// 中国語文字体系の自動判別
var chineseText1 = "我爱学习中文"; // 簡体字
var chineseText2 = "我愛學習中文"; // 繁体字

var detectedLanguage1 = translationEngine.DetectChineseScriptType(chineseText1);
// 結果: Language.ChineseSimplified

var detectedLanguage2 = translationEngine.DetectChineseScriptType(chineseText2);  
// 結果: Language.ChineseTraditional
```

### 5. 拡張メソッドの活用

```csharp
using Baketa.Infrastructure.Translation.Local.Onnx.Chinese;

// 言語の種類判定
var language = Language.ChineseSimplified;

bool isChinese = language.IsChinese();           // true
bool isSimplified = language.IsSimplifiedChinese();  // true
bool isTraditional = language.IsTraditionalChinese(); // false

string description = language.GetChineseScriptDescription(); // "簡体字"
```

### 6. サポートされている中国語言語の取得

```csharp
// サポートされている中国語言語の一覧を取得
var supportedLanguages = ChineseLanguageExtensions.GetSupportedChineseLanguages();

foreach (var lang in supportedLanguages)
{
    Console.WriteLine($"{lang.Code}: {lang.DisplayName} ({lang.NativeName})");
}

// 出力例:
// zh-CN: 简体中文 (中文（简体）)
// zh-TW: 繁體中文 (中文（繁體）)
// zh: 中国語（自動判別） (中文)
// yue: 広東語 (粵語)
// ...
```

### 7. 言語コードからの言語オブジェクト取得

```csharp
// 言語コードから言語オブジェクトを取得
var language1 = ChineseLanguageExtensions.GetChineseLanguageByCode("zh-CN");
var language2 = ChineseLanguageExtensions.GetChineseLanguageByCode("zh-Hant");
var language3 = ChineseLanguageExtensions.GetChineseLanguageByCode("yue-HK");

// 推奨言語の自動判定
var recommendedLang = ChineseLanguageExtensions.GetRecommendedChineseLanguage("我爱学习");
// 結果: Language.ChineseSimplified（簡体字）
```

## 高度な使用例

### 1. カスタム言語ペアの設定

```csharp
// カスタム中国語言語の作成
var customTraditionalChinese = new Language
{
    Code = "zh-Hant",
    DisplayName = "繁体字中国語",
    NativeName = "繁體中文",
    RegionCode = "TW"
};

// カスタム言語ペアで翻訳エンジンを初期化
var customLanguagePair = new LanguagePair
{
    SourceLanguage = Language.English,
    TargetLanguage = customTraditionalChinese
};
```

### 2. バッチ翻訳での中国語対応

```csharp
// 複数の翻訳リクエストを一括処理
var requests = new List<TranslationRequest>
{
    new TranslationRequest
    {
        SourceText = "Good morning",
        SourceLanguage = Language.English,
        TargetLanguage = Language.ChineseSimplified
    },
    new TranslationRequest
    {
        SourceText = "Good evening", 
        SourceLanguage = Language.English,
        TargetLanguage = Language.ChineseTraditional
    }
};

var responses = await translationEngine.TranslateBatchAsync(requests);

foreach (var response in responses)
{
    Console.WriteLine($"{response.SourceText} -> {response.TranslatedText}");
    Console.WriteLine($"Target: {response.TargetLanguage.GetChineseScriptDescription()}");
}
```

### 3. エラーハンドリング

```csharp
try
{
    var request = new TranslationRequest
    {
        SourceText = "Hello world",
        SourceLanguage = Language.English,
        TargetLanguage = Language.ChineseSimplified
    };

    var response = await translationEngine.TranslateAsync(request);
    
    if (response.IsSuccess)
    {
        Console.WriteLine($"翻訳成功: {response.TranslatedText}");
    }
    else
    {
        Console.WriteLine($"翻訳失敗: {response.ErrorMessage}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"エラーが発生しました: {ex.Message}");
}
```

## 設定オプション

### 1. OPUS-MT専用オプション

```csharp
var options = new OnnxTranslationOptions
{
    MaxSequenceLength = 512,      // 最大シーケンス長
    ThreadCount = 4,              // 使用スレッド数
    OptimizationLevel = 3,        // 最適化レベル
    EnableModelCache = true,      // モデルキャッシュを有効化
    BatchSize = 1,                // バッチサイズ
    BeamSize = 4                  // ビームサーチのサイズ
};
```

### 2. 中国語特有の設定

```csharp
// 中国語プロセッサーの直接使用
var chineseProcessor = new ChineseLanguageProcessor(logger);

// サポートされている言語コードの確認
var supportedCodes = chineseProcessor.GetSupportedLanguageCodes();
Console.WriteLine($"サポートされている言語コード: {string.Join(", ", supportedCodes)}");

// 特定の言語コードが中国語かどうかの確認
bool isChineseCode = chineseProcessor.IsChineseLanguageCode("zh-CN");  // true
bool isNotChineseCode = chineseProcessor.IsChineseLanguageCode("en");  // false
```

## 実装上の注意点

### 1. プレフィックスの自動付与

- 中国語系の言語コード（zh-*, cmn*, yue*）が検出された場合、自動的に適切なプレフィックスが付与されます
- プレフィックスは既存の場合は重複追加されません
- 非中国語の場合はプレフィックスは付与されません

### 2. 文字体系の判定精度

- 文字体系の自動判定は基本的な文字パターンに基づいています
- より高精度な判定が必要な場合は、専用の文字体系判定ライブラリの使用を検討してください
- 混合テキストの場合は簡体字がデフォルトとして選択されます

### 3. パフォーマンス考慮事項

- プレフィックス処理は軽量ですが、大量のテキスト処理時は考慮が必要です
- 文字体系の自動判定は文字単位で行われるため、長いテキストでは処理時間が増加します
- キャッシュ機能を有効化することで、繰り返し処理のパフォーマンスが向上します
