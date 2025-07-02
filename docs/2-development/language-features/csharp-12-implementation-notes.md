# C# 12実装ノート

## 1. 概要

BaketaプロジェクトへのC# 12機能導入にあたって発生した問題と解決策をまとめたドキュメントです。
このドキュメントは開発者が遭遇した具体的な問題と解決手順を記録し、今後の参考とするために作成されました。

## 2. 直面した主な問題

### 2.1 MSBuild構造エラー

**問題**: Directory.Build.propsファイルにおいて`<ItemGroup>`タグが`<PropertyGroup>`内に配置されていた

**エラーメッセージ**:
```
MSB4004: "ItemGroup" プロパティは予約されており、変更することはできません。
```

**解決策**:
`<ItemGroup>`タグを`<PropertyGroup>`の外に移動し、同レベルに配置する

**正しい構造**:
```xml
<Project>
  <PropertyGroup>
    <!-- プロパティ設定 -->
  </PropertyGroup>
  
  <ItemGroup>
    <!-- アイテム設定 -->
  </ItemGroup>
</Project>
```

### 2.2 コレクション式のターゲット型問題

**問題**: 型情報のないコレクション式がCS9176エラーを発生させる

**エラーメッセージ**:
```
CS9176: コレクション式のターゲット型がありません。
```

**解決策**:
コレクション式を使用する際は、以下のいずれかの方法で型情報を提供する：

1. 明示的な型指定: `byte[] newData = [];`
2. ターゲット型推論: `return new[] { 1, 2, 3 };`
3. 要素からの推論: `var numbers = [1, 2, 3];`

### 2.3 Dictionary初期化構文の問題

**問題**: Dictionary用のコレクション式が構文エラーを発生させる

**エラーメッセージ**:
```
CS1002: ; が必要です
CS1513: } が必要です
```

**解決策**:
現時点では従来のDictionary初期化構文を使用する：

```csharp
Dictionary<int, string> dict = new()
{
    { 1, "one" },
    { 2, "two" },
    { 3, "three" }
};
```

### 2.4 型エイリアスの配置問題

**問題**: 型エイリアス（`using Type = AnotherType`）をメソッド内に配置するとエラーが発生

**解決策**:
型エイリアスはネームスペースレベルでのみ定義する：

```csharp
namespace MyNamespace
{
    using Point = (int X, int Y);
    
    public class MyClass { ... }
}
```

### 2.5 Interceptors機能のサポート問題

**問題**: InterceptsLocation属性が見つからないエラーが発生

**エラーメッセージ**:
```
CS0234: 型または名前空間の名前 'InterceptsLocation' が名前空間 'System.Runtime.CompilerServices' に存在しません
```

**解決策**:
Interceptorsはまだプレビュー機能であるため、本番環境での使用は避ける。テスト環境では`<Features>InterceptorsPreview</Features>`設定と適切なNuGet参照が必要。

## 3. 実装プロセス

1. Directory.Build.propsの作成と構造修正
2. C# 12機能テスト用ファイル（Csharp12FeatureTests.cs）の作成
3. コレクション式を使用するコードの型情報追加
4. プロジェクト参照の解決
5. ドキュメント更新（C# 12サポートガイド、README.md）
6. 環境チェックスクリプトの拡張

## 4. 継続的な警告への対応

現在も複数の警告が出力されていますが、これらは以下のカテゴリに分類できます：

1. **CA2007**: 非同期メソッドでのConfigureAwait未使用警告
2. **CA1063**: IDisposableパターンの実装に関する警告
3. **CA1805**: 冗長な初期化に関する警告
4. **CA1851**: IEnumerableの複数列挙に関する警告
5. **IDE0059**: 不要な値代入に関する警告

これらの警告は優先度に応じて段階的に対応する予定です。

## 5. 参考資料

- [C# 12言語仕様](https://learn.microsoft.com/ja-jp/dotnet/csharp/whats-new/csharp-12)
- [MSBuildスキーマリファレンス](https://learn.microsoft.com/ja-jp/visualstudio/msbuild/msbuild-project-file-schema-reference)
- [.NET 8プロジェクト設定](https://learn.microsoft.com/ja-jp/dotnet/core/project-sdk/overview)
