# C#12 の新機能と利用上の注意点

## 概要

このドキュメントでは、Baketaプロジェクトで使用可能なC#12の新機能と、それらを使用する際の注意点について説明します。C#12は.NET 8と共に導入された多くの便利な機能を提供していますが、一部の機能には制約があるため注意が必要です。

## コレクション式

### 基本的な使い方

コレクション式は角括弧 `[]` を使用して、さまざまなコレクション型を簡潔に初期化する機能です。

```csharp
// 配列の初期化
int[] numbers = [1, 2, 3, 4, 5];

// リストの初期化
List<string> words = ["hello", "world"];

// 辞書の初期化
Dictionary<int, string> dict = [];
```

### 注意点

1. **型情報が必須**:
   - コレクション式を使用する際は、明示的な型情報が必要です
   - ディスカード変数 (`_`) に直接割り当てることはできません

   ```csharp
   // エラー: コレクション式のターゲット型がありません
   _ = [];
   
   // 正しい使用方法
   int[] _ = [];
   ```

2. **スプレッド演算子**:
   - 既存のコレクションを展開するために `..` を使用できます
   - この場合も型情報が必要です

   ```csharp
   int[] firstHalf = [1, 2, 3];
   int[] secondHalf = [4, 5, 6];
   int[] combined = [..firstHalf, ..secondHalf];
   ```

## インライン配列

インライン配列は、固定サイズの配列を値型として処理する機能を提供します。

```csharp
[System.Runtime.CompilerServices.InlineArray(10)]
internal struct Buffer10<T>
{
    private T _element;
}
```

### 制約と注意点

1. **readonlyとの競合**:
   - インライン配列のフィールドはreadonlyとして宣言できません
   - readonly構造体にする場合、すべてのフィールドもreadonlyである必要があるため、矛盾が発生します

   ```csharp
   // エラー: readonly構造体のインスタンスフィールドは、readonly宣言が必要
   [InlineArray(10)]
   internal readonly struct Buffer10<T>
   {
       private T _element; // readonly宣言ができないためエラー
   }

   // 正しい使用方法: 構造体自体をreadonlyにしない
   [InlineArray(10)]
   internal struct Buffer10<T>
   {
       private T _element;
   }
   ```

2. **メソッドのreadonly修飾子**:
   - 構造体のメソッドにはreadonly修飾子を使用できます
   - これにより、メソッドが構造体のフィールドを変更しないことを保証します

   ```csharp
   public readonly bool Equals(Buffer10<T> other)
   {
       return _element.Equals(other._element);
   }
   ```

3. **静的演算子とreadonly**:
   - 静的演算子にはreadonly修飾子を使用できません

   ```csharp
   // エラー: 修飾子 'readonly' がこの項目に対して有効ではありません
   public static readonly bool operator ==(Buffer10<T> left, Buffer10<T> right)

   // 正しい使用方法
   public static bool operator ==(Buffer10<T> left, Buffer10<T> right)
   ```

## プライマリコンストラクタ

プライマリコンストラクタはクラスやレコードのコンストラクタ記述を簡略化する機能です。

```csharp
// 従来の書き方
public class Person
{
    private readonly string _name;
    
    public Person(string name)
    {
        _name = name;
    }
    
    public string Name => _name;
}

// プライマリコンストラクタを使用した書き方
public class Person(string name)
{
    public string Name => name;
}
```

### 注意点

1. **デフォルト値の指定**:
   - プライマリコンストラクタのパラメータにはデフォルト値を指定できます

   ```csharp
   internal class ConfigOptions(string name, bool enabled = true, int timeout = 30)
   {
       public string Name { get; } = name;
       public bool Enabled { get; } = enabled;
       public int Timeout { get; } = timeout;
   }
   ```

## その他の注意点とベストプラクティス

1. **コード分析警告に注意**:
   - 特にIDE0059（不必要な値代入）の警告が発生することがあります
   - 変数を定義したら必ず使用するか、使用されない場合はディスカード変数や適切な命名の変数を使用してください

2. **コードの可読性**:
   - 新機能を使用する際も、コードの可読性を優先してください
   - チーム内でC#12の機能の使用基準を共有しましょう

## 参考資料

- [Microsoft C#12 の新機能](https://learn.microsoft.com/ja-jp/dotnet/csharp/whats-new/csharp-12)
- [インライン配列](https://learn.microsoft.com/ja-jp/dotnet/csharp/language-reference/proposals/csharp-12.0/inline-arrays)
- [コレクション式](https://learn.microsoft.com/ja-jp/dotnet/csharp/language-reference/operators/collection-expressions)
