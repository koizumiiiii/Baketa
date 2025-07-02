# コード分析設定ガイド

## 概要

このドキュメントでは、Baketaプロジェクトでのコード分析の設定と適用方法について説明します。コード分析ツールを効果的に活用することで、コードの品質と保守性を継続的に向上させることができます。

## ソリューションレベルの設定

### Directory.Build.props の設定

プロジェクトルートに `Directory.Build.props` ファイルを作成することで、すべてのプロジェクトに共通の設定を適用できます。

```xml
<Project>
  <PropertyGroup>
    <!-- .NET SDK コード分析を有効化 -->
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest</AnalysisLevel>
    <AnalysisMode>All</AnalysisMode>
    
    <!-- 警告をエラーとして扱う -->
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    
    <!-- 特定の警告は例外的に許可 -->
    <WarningsNotAsErrors>CA1014;CA1031;CA1303</WarningsNotAsErrors>
    
    <!-- コードスタイル分析を有効化 -->
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
  
  <!-- スタイルルールの詳細設定 -->
  <ItemGroup>
    <EditorConfigFiles Include="$(MSBuildThisFileDirectory).editorconfig" />
  </ItemGroup>
</Project>
```

### .editorconfig の設定

`.editorconfig` ファイルを使用して、より詳細なコーディングスタイルと分析ルールを定義します。

```editorconfig
# 最上位EditorConfigファイル
root = true

# すべてのファイルに適用
[*]
charset = utf-8
end_of_line = crlf
indent_style = space
indent_size = 4
insert_final_newline = true
trim_trailing_whitespace = true

# C#ファイル
[*.cs]
# C#特有の設定
csharp_style_namespace_declarations = file_scoped:suggestion
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_var_elsewhere = true:suggestion
csharp_style_expression_bodied_methods = when_on_single_line:suggestion
csharp_style_expression_bodied_properties = true:suggestion
csharp_prefer_static_local_function = true:warning
csharp_style_unused_value_assignment_preference = discard_variable:suggestion
csharp_style_readonly_struct = true:suggestion

# 命名規則
dotnet_naming_rule.interface_should_be_begins_with_i.severity = warning
dotnet_naming_rule.interface_should_be_begins_with_i.symbols = interface
dotnet_naming_rule.interface_should_be_begins_with_i.style = begins_with_i

dotnet_naming_symbols.interface.applicable_kinds = interface
dotnet_naming_symbols.interface.applicable_accessibilities = public, internal, private, protected, protected_internal, private_protected
dotnet_naming_symbols.interface.required_modifiers =

dotnet_naming_style.begins_with_i.required_prefix = I
dotnet_naming_style.begins_with_i.required_suffix =
dotnet_naming_style.begins_with_i.word_separator =
dotnet_naming_style.begins_with_i.capitalization = pascal_case

# CA警告の設定
dotnet_diagnostic.CA1822.severity = warning # Make member static
dotnet_diagnostic.CA1848.severity = warning # Use LoggerMessage delegates
dotnet_diagnostic.CA1031.severity = suggestion # Do not catch general exception types
dotnet_diagnostic.CA1303.severity = suggestion # Do not pass literals as localized parameters

# IDE警告の設定
dotnet_diagnostic.IDE0059.severity = suggestion # Unnecessary value assignment
dotnet_diagnostic.IDE0290.severity = suggestion # Use primary constructor
dotnet_diagnostic.IDE0305.severity = suggestion # Simplify collection initialization
dotnet_diagnostic.IDE0270.severity = suggestion # Simplify null check

# 例外的に無効化するルール
dotnet_diagnostic.CA1014.severity = none # Mark assemblies with CLSCompliant

# テストプロジェクト用の設定
[*Test*.cs]
dotnet_diagnostic.CA1707.severity = none # Identifiers should not contain underscores
```

## プロジェクトレベルでの抑制

特定のプロジェクトやファイルで特定の警告を抑制する必要がある場合は、以下の方法を使用します。

### プロジェクトファイル (.csproj) での抑制

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);CA1014;CA1303</NoWarn>
</PropertyGroup>
```

### ソースコードでの抑制

```csharp
// ファイルレベルでの抑制
#pragma warning disable CA1303
// コード
#pragma warning restore CA1303

// メソッドレベルでの抑制
[System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1303:Do not pass literals as localized parameters", 
    Justification = "サンプルコードに静的リソースを使用")]
public void SomeMethod() { }
```

## CI/CDでの静的解析の統合

継続的インテグレーションで静的解析を実行するGitHub Actionsの例：

```yaml
name: Code Analysis

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main, develop ]

jobs:
  analyze:
    runs-on: ubuntu-latest
    steps:
    - uses: actions/checkout@v3
    - name: Setup .NET
      uses: actions/setup-dotnet@v3
      with:
        dotnet-version: 8.0.x
    - name: Restore dependencies
      run: dotnet restore
    - name: Build with Analysis
      run: dotnet build --no-restore /p:EnforceCodeStyleInBuild=true /p:EnableNETAnalyzers=true /p:AnalysisLevel=latest
```

## 推奨される対応基準

以下は、警告に対する推奨対応基準です：

1. **エラーとして扱う警告**：
   - パフォーマンスに重大な影響を与えるもの (CA1822, CA1848など)
   - 潜在的なバグの原因となるもの
   - セキュリティ上のリスクがあるもの

2. **修正すべき警告**：
   - コードの可読性や保守性に影響するもの (IDE0059, IDE0305など)
   - コーディング規約に違反するもの

3. **抑制を検討できる警告**：
   - インターフェース実装上の制約によるもの
   - ローカライズ関連の警告（CA1303）でサンプルコードや内部使用コードの場合
   - テストコードに対する特定の警告

## 関連ドキュメント

- [CA警告修正ガイド](./ca-warnings-fixes.md)
- [CA警告修正適用履歴](./ca-warnings-fixes-applied.md)
- [C#12の新機能と利用上の注意点](../language-features/csharp12-features.md)
