# コード分析警告の対応ガイドライン

このドキュメントでは、Baketaプロジェクトでよく発生するコード分析警告の対応方法と、各警告への対応方針をまとめています。

## 1. 警告カテゴリと対応方針

### 1.1 CA2007: ConfigureAwait(false)に関する警告

非同期メソッドで`Task`を`await`する際に`ConfigureAwait(false)`の呼び出しがない場合に発生する警告です。

```csharp
// 警告が発生するコード
await SomeAsync();

// 警告が解消されるコード
await SomeAsync().ConfigureAwait(false);
```

**対応方針**:
- UIプロジェクト (Baketa.UI) では原則として**抑制**します
  - UIスレッドに戻る必要があるため、ConfigureAwait(false)を付けない方が安全
  - 以下の `.editorconfig` 設定で抑制できます:
  ```
  [*.cs]
  dotnet_diagnostic.CA2007.severity = none
  ```
- 非UIプロジェクトでは適用を検討

### 1.2 CA1031: 一般的な例外のキャッチを避ける

具体的な例外型を指定せずに一般的な`Exception`をキャッチしている場合に発生します。

```csharp
// 警告が発生するコード
try {
    // 処理
} catch (Exception ex) {
    // 例外処理
}

// 警告が解消されるコード
try {
    // 処理
} catch (InvalidOperationException ex) {
    // 例外処理
} catch (IOException ex) {
    // 例外処理
} catch (ArgumentException ex) {
    // 例外処理
}
```

**対応方針**:
- 可能な限り具体的な例外型を指定する
- 一般的な例外をキャッチする必要がある場合は、適切なコメントを付けるか、`when`キーワードで条件を追加
  ```csharp
  catch (Exception ex) when (ex is not SpecificExceptionType)
  ```

### 1.3 CA1805: 参照型フィールドの冗長な初期化

コンストラクタでフィールドを既定値で初期化している場合に発生します。

```csharp
// 警告が発生するコード
private string _name = string.Empty;
private List<string> _items = new();

// 警告が解消されるコード
private string _name;
private List<string> _items;

public MyClass()
{
    // 非既定値での初期化のみ記述
    _items = new(10); // 容量指定がある場合など
}
```

**対応方針**:
- 参照型フィールドの冗長な初期化は避ける
- 初期化が必要な場合（非既定値での初期化）のみコンストラクタで行う
- ただし、null参照例外を防ぐために初期化が必要な場合は、警告を抑制することも検討

### 1.4 IDE0290: プライマリコンストラクターの使用

C# 12の新機能であるプライマリコンストラクターを使用できる場合に発生します。

```csharp
// 警告が発生するコード
public class Person
{
    private readonly string _name;
    private readonly int _age;
    
    public Person(string name, int age)
    {
        _name = name;
        _age = age;
    }
}

// 警告が解消されるコード
public class Person(string name, int age)
{
    private readonly string _name = name;
    private readonly int _age = age;
}
```

**対応方針**:
- プロジェクトの言語バージョンがC# 12以上の場合のみ適用
- シンプルなデータクラスや、単純な値の保持が目的のクラスに適用する
- 複雑なロジックを持つクラスには適用しない

### 1.5 IDE0300/IDE0028: コレクションの初期化の簡素化

コレクション初期化子を使用できる場面で使用していない場合に発生します。

```csharp
// 警告が発生するコード
var list = new List<string>();
list.Add("item1");
list.Add("item2");

// 警告が解消されるコード
var list = new List<string> { "item1", "item2" };
```

**対応方針**:
- コレクション初期化子を使用してコードを簡潔に保つ
- 動的に要素を追加する場合は例外

### 1.6 その他の警告

#### IDE0057: Substringの簡素化 
```csharp
// 修正前
text.Substring(0, 5);

// 修正後
text[..5];
```

#### IDE0066: switch式の使用
```csharp
// 修正前
switch (value)
{
    case 1: return "one";
    case 2: return "two";
    default: return "other";
}

// 修正後
return value switch
{
    1 => "one",
    2 => "two",
    _ => "other"
};
```

## 2. 警告の抑制方法

### 2.1 コードレベルでの抑制

```csharp
#pragma warning disable CA2007
await SomeAsync();
#pragma warning restore CA2007
```

### 2.2 メソッドレベルでの抑制

```csharp
[SuppressMessage("Usage", "CA2007:非同期メソッドでConfigureAwait(false)を使用する")]
public async Task DoSomethingAsync()
{
    await SomeAsync();
}
```

### 2.3 プロジェクトレベルでの抑制

`.editorconfig`ファイルを使用した抑制：

```
[*.cs]
dotnet_diagnostic.CA2007.severity = none
```

## 3. 警告の優先度

1. エラー (Error) - 必ず修正する
2. 警告 (Warning) - 基本的に修正する
3. 情報 (Info/Message) - 時間があれば修正する

## 4. Baketaプロジェクト特有の方針

### 4.1 UIプロジェクトに関する方針

**Baketa.UI**:
- CA2007 (ConfigureAwait) は抑制する
  - UIスレッドコンテキストを維持する必要があるため
- IDE0290 (プライマリコンストラクター) は事例ごとに判断
  - 単純なデータモデルには適用
  - ViewModelや複雑なロジックを持つクラスには適用しない

### 4.2 非UIプロジェクトに関する方針

**Core/Infrastructure層**:
- CA2007 (ConfigureAwait) は積極的に適用
- CA1031 (一般的な例外のキャッチ) は厳格に守る
- コードスタイルの警告（IDE～）は一貫性を保つために修正

## 5. Issue #57実装時の具体的な対応

Issue #57「メインウィンドウUIデザインの実装」では、以下の方針で警告に対応しました：

1. **ViewLocator.csの例外処理修正**: 
   - 一般的な`Exception`ではなく、具体的な例外型に分けて対応

2. **TranslationViewModel.csのswitch式簡素化**:
   - 従来のswitch文をswitch式に置き換え
   - パターンマッチングの活用

3. **MainWindowViewModel.csのSubstring簡素化**:
   - `Substring`を範囲演算子を使った表現に変更

4. **CA2007への対応**:
   - UIプロジェクトのため、本来は抑制すべきだが、一部適用して警告を減らす取り組みを実施
   - 将来的には.editorconfigで抑制する方針

## 6. 参考資料

- [.NET コード品質分析](https://learn.microsoft.com/ja-jp/dotnet/fundamentals/code-analysis/quality-rules/)
- [.NET スタイル ルール](https://learn.microsoft.com/ja-jp/dotnet/fundamentals/code-analysis/style-rules/)
- [ConfigureAwait に関するベスト プラクティス](https://learn.microsoft.com/ja-jp/dotnet/fundamentals/code-analysis/quality-rules/ca2007)
