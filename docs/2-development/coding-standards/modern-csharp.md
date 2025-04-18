# C# コーディング標準規約 - モダンC#機能の活用

このドキュメントでは、BaketaプロジェクトにおけるモダンC# 12機能の活用方法について詳しく説明します。アプリケーションの可読性、効率性、保守性を向上させるための最新C#機能の使い方を示します。

> **関連ドキュメント**:
> - [基本原則](./csharp-standards.md)
> - [パフォーマンス最適化ガイドライン](./performance.md)
> - [プラットフォーム間相互運用](./platform-interop.md)
> - [Avalonia UIガイドライン](../../3-architecture/ui-system/avalonia-guidelines.md)

## 目次

1. [コレクション初期化の簡素化](#1-コレクション初期化の簡素化)
2. ['new' 式の簡素化](#2-new-式の簡素化)
3. [プライマリコンストラクタの使用](#3-プライマリコンストラクタの使用)
4. [変数宣言のインライン化](#4-変数宣言のインライン化)
5. [パターンマッチングの活用](#5-パターンマッチングの活用)
6. [ターゲット型の推論](#6-ターゲット型の推論)
7. [ファイルスコープとグローバルusing](#7-ファイルスコープとグローバルusing)
8. [複数の入れ子型変数の破棄](#8-複数の入れ子型変数の破棄)
9. [レコード型の活用](#9-レコード型の活用)

## 1. コレクション初期化の簡素化

C# 12では、コレクション初期化のより簡潔な構文が導入されました。（IDE0028, IDE0300, IDE0305, IDE0306）

### 1.1 空のコレクション

```csharp
// ❌ 避けるべき書き方
private readonly List<string> _items = new List<string>();
private readonly Dictionary<string, int> _cache = new Dictionary<string, int>();
private readonly HashSet<int> _set = new HashSet<int>();

// ✅ 推奨される書き方
private readonly List<string> _items = [];
private readonly Dictionary<string, int> _cache = [];
private readonly HashSet<int> _set = [];
```

### 1.2 初期値を持つコレクション

```csharp
// ❌ 避けるべき書き方
private readonly List<string> _defaultValues = new List<string> { "one", "two", "three" };
private readonly Dictionary<string, int> _initialCounts = new Dictionary<string, int>
{
    { "one", 1 },
    { "two", 2 }
};

// ✅ 推奨される書き方
private readonly List<string> _defaultValues = ["one", "two", "three"];
private readonly Dictionary<string, int> _initialCounts = new()
{
    ["one"] = 1,
    ["two"] = 2
};

// または完全な簡素化
private readonly Dictionary<string, int> _initialCounts = [
    ["one"] = 1, 
    ["two"] = 2
];
```

### 1.3 配列の初期化

```csharp
// ❌ 避けるべき書き方
private static readonly string[] Tags = new string[] { "Baketa", "OCR", "Translation" };
private readonly int[] _dimensions = new int[3] { 800, 600, 32 };

// ✅ 推奨される書き方
private static readonly string[] Tags = ["Baketa", "OCR", "Translation"];
private readonly int[] _dimensions = [800, 600, 32];
```

### 1.4 コレクションのスライスと範囲

コレクションのスライスや範囲指定にも新しい構文を使用します：

```csharp
// ❌ 避けるべき書き方
var copy = new List<string>(items);
var firstThree = items.Take(3).ToList();

// ✅ 推奨される書き方
var copy = [.. items];
var firstThree = items[0..3];
```

## 2. 'new' 式の簡素化

型が明らかな場合は、new演算子の型引数を省略します。（IDE0090）

```csharp
// ❌ 避けるべき書き方
private readonly object _lock = new object();
private readonly JsonSerializerOptions _options = new JsonSerializerOptions();
using var bitmap = new Bitmap(800, 600);
using var stream = new MemoryStream();

// ✅ 推奨される書き方
private readonly object _lock = new();
private readonly JsonSerializerOptions _options = new();
using var bitmap = new(800, 600);
using var stream = new();
```

ただし、型が明示的に必要な場合は例外とします：

```csharp
// ✅ 型の明示が必要な場合は型引数を含める
var dict = new Dictionary<string, List<int>>();  // 型推論ができない場合
var specific = new SpecificJsonConverter<MyType>();  // ジェネリック型引数が必要な場合
```

## 3. プライマリコンストラクタの使用

C# 12で導入されたプライマリコンストラクタを使用して、コードをより簡潔にします。（IDE0290）

### 3.1 基本的な使用方法

```csharp
// ❌ 避けるべき書き方
public class ImageProcessor : IImageProcessor
{
    private readonly ILogger _logger;
    private readonly IImageCache _cache;

    public ImageProcessor(ILogger logger, IImageCache cache)
    {
        _logger = logger;
        _cache = cache;
    }
}

// ✅ 推奨される書き方
public class ImageProcessor(ILogger logger, IImageCache cache) : IImageProcessor
{
    private readonly ILogger _logger = logger;
    private readonly IImageCache _cache = cache;
}
```

### 3.2 初期化と検証を含むケース

プライマリコンストラクタではパラメータの検証や追加の初期化が必要な場合は、コンストラクタ本体を追加することもできます：

```csharp
public class TranslationService(ITranslationEngine engine, ILogger? logger = null) : ITranslationService
{
    private readonly ITranslationEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly Dictionary<string, int> _requestCounts = [];

    // 追加の初期化が必要な場合はコンストラクタ本体を追加
    public TranslationService(ITranslationEngine engine) : this(engine, null)
    {
        // 追加の初期化
    }
}
```

## 4. 変数宣言のインライン化

変数宣言と初期化をまとめる場合、より簡潔な形式を使用します。（IDE0018）

```csharp
// ❌ 避けるべき書き方
RECT rect;
if (!GetWindowRect(handle, out rect))
{
    return null;
}

string text;
if (!TryGetText(id, out text))
{
    return string.Empty;
}

// ✅ 推奨される書き方
if (!GetWindowRect(handle, out RECT rect))
{
    return null;
}

if (!TryGetText(id, out string text))
{
    return string.Empty;
}
```

### 4.1 switch文でのパターンマッチングと組み合わせ

```csharp
// ❌ 避けるべき書き方
object value = GetValue();
if (value is string)
{
    string text = (string)value;
    ProcessText(text);
}
else if (value is int)
{
    int number = (int)value;
    ProcessNumber(number);
}

// ✅ 推奨される書き方
object value = GetValue();
switch (value)
{
    case string text:
        ProcessText(text);
        break;
    case int number:
        ProcessNumber(number);
        break;
}

// または if 文でのパターンマッチング
if (value is string text)
{
    ProcessText(text);
}
else if (value is int number)
{
    ProcessNumber(number);
}
```

## 5. パターンマッチングの活用

C# 9以降で導入されたパターンマッチング機能を活用して、より表現力豊かなコードを書きます。

### 5.1 型テストと変数割り当て

```csharp
// ❌ 避けるべき書き方
if (obj is string)
{
    string str = (string)obj;
    // strを使用
}

// ✅ 推奨される書き方
if (obj is string str)
{
    // strを使用
}
```

### 5.2 switch式

条件分岐が単純な値の返却に使われる場合は、switch式を使用します：

```csharp
// ❌ 避けるべき書き方
string GetStatusMessage(Status status)
{
    switch (status)
    {
        case Status.Pending:
            return "保留中";
        case Status.Processing:
            return "処理中";
        case Status.Completed:
            return "完了";
        case Status.Failed:
            return "失敗";
        default:
            return "不明";
    }
}

// ✅ 推奨される書き方
string GetStatusMessage(Status status) => status switch
{
    Status.Pending => "保留中",
    Status.Processing => "処理中",
    Status.Completed => "完了",
    Status.Failed => "失敗",
    _ => "不明"
};
```

### 5.3 プロパティパターン

オブジェクトのプロパティに基づいた条件分岐ではプロパティパターンを使用します：

```csharp
// ❌ 避けるべき書き方
if (result.IsSuccess && result.Count > 0)
{
    // 処理
}

// ✅ 推奨される書き方
if (result is { IsSuccess: true, Count: > 0 })
{
    // 処理
}
```

## 6. ターゲット型の推論

メソッド引数や変数の代入で型が明らかな場合は、ターゲット型の推論を使用します。

```csharp
// ❌ 避けるべき書き方
Task<TranslationResult> task = Task.FromResult<TranslationResult>(null);
List<string> items = new List<string>();

// ✅ 推奨される書き方
Task<TranslationResult> task = Task.FromResult(null);
List<string> items = new();
```

特に匿名オブジェクトや配列の初期化でターゲット型の推論が役立ちます：

```csharp
// ❌ 避けるべき書き方
ProcessItems(new List<int> { 1, 2, 3 });
SendData(new Dictionary<string, object> 
{ 
    { "id", 1 }, 
    { "name", "Baketa" } 
});

// ✅ 推奨される書き方
ProcessItems(new() { 1, 2, 3 });
SendData(new() 
{ 
    ["id"] = 1, 
    ["name"] = "Baketa" 
});
```

## 7. ファイルスコープとグローバルusing

名前空間の宣言とusingディレクティブを最適化します。

### 7.1 ファイルスコープの名前空間

```csharp
// ❌ 避けるべき書き方
namespace Baketa.OCR.Services
{
    public class OcrService
    {
        // ...
    }
}

// ✅ 推奨される書き方
namespace Baketa.OCR.Services;

public class OcrService
{
    // ...
}
```

### 7.2 グローバルusing

プロジェクト全体で使用される共通の名前空間は、グローバルusingを使用します。
`.csproj`ファイルや`GlobalUsings.cs`ファイルに定義することをお勧めします：

```csharp
// GlobalUsings.cs
global using System;
global using System.Collections.Generic;
global using System.Threading;
global using System.Threading.Tasks;
global using Microsoft.Extensions.Logging;
global using Baketa.Core.Models;
```

## 8. 複数の入れ子型変数の破棄

複数の入れ子型のオブジェクトから特定の値だけを抽出する場合は、分解宣言を使用します：

```csharp
// ❌ 避けるべき書き方
var response = await client.GetAsync(url);
var content = await response.Content.ReadFromJsonAsync<ApiResponse>();
var items = content.Data.Items;
foreach (var item in items)
{
    // 処理
}

// ✅ 推奨される書き方
var items = (await (await client.GetAsync(url)).Content
    .ReadFromJsonAsync<ApiResponse>()).Data.Items;
foreach (var item in items)
{
    // 処理
}

// または変数の可読性も保つならこのように分解する
var response = await client.GetAsync(url);
var (_, _, items) = await response.Content.ReadFromJsonAsync<ApiResponse>();
foreach (var item in items)
{
    // 処理
}
```

ただし、可読性を損なう場合はこの方法は避けてください。

## 9. レコード型の活用

データを表現するだけのクラスには、レコード型を使用することを検討します：

```csharp
// ❌ 避けるべき書き方（データクラス）
public class TranslationRequest
{
    public string Text { get; }
    public string SourceLanguage { get; }
    public string TargetLanguage { get; }
    
    public TranslationRequest(string text, string sourceLanguage, string targetLanguage)
    {
        Text = text;
        SourceLanguage = sourceLanguage;
        TargetLanguage = targetLanguage;
    }
    
    // Equals、GetHashCode、ToString実装...
}

// ✅ 推奨される書き方
public record TranslationRequest(string Text, string SourceLanguage, string TargetLanguage);
```

レコード型を使用すると、値ベースの等価性、不変性、ToString()、分解などの機能が自動的に提供されます。複雑なビジネスロジックを持つオブジェクトには従来のクラスを使用し、単純なデータ構造にはレコードを使用することを推奨します。