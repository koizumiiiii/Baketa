# .editorconfig ガイドライン

## 概要

`.editorconfig`ファイルは、プロジェクト全体のコードスタイルとコード分析設定を統一するための設定ファイルです。このファイルはプロジェクトのルートディレクトリに配置され、サブディレクトリのファイルにも自動的に適用されます。

## 目的

- すべての開発者間で一貫したコーディングスタイルを維持する
- IDE（Visual Studioなど）の警告レベルを統一する
- コード分析ルールの適用と抑制を明示的に管理する
- プロジェクト固有の規約を自動的に適用する

## 使用方法

### 開発プロセスでの確認

1. 新規開発やイシュー対応を開始する前に、必ず`.editorconfig`ファイルの内容を確認する
2. コーディング規約と合わせて参照することで、一貫した実装を維持する
3. 警告が出た際に、`.editorconfig`の設定を参照して適切に対応する

### 設定の変更

`.editorconfig`ファイルの設定変更が必要な場合：

1. チーム内でのディスカッションを行い、変更の影響範囲を確認する
2. PRで変更を提案し、レビュープロセスを経る
3. 変更内容をドキュメントに反映する

## 主要な設定項目

### 基本設定

```editorconfig
indent_style = space         # インデントにスペースを使用
indent_size = 4              # インデントは4スペース
end_of_line = crlf           # 改行コードはCRLF
charset = utf-8              # エンコーディングはUTF-8
trim_trailing_whitespace = true  # 行末の空白を削除
insert_final_newline = true  # ファイル末尾に改行を挿入
```

### コードスタイル設定

```editorconfig
# Null条件演算子の使用を推奨
dotnet_style_null_propagation = true:warning

# Null合体演算子の使用を推奨
dotnet_style_coalesce_expression = true:warning

# 自動プロパティの使用を推奨
dotnet_style_prefer_auto_properties = true:warning
```

### 警告レベル設定

```editorconfig
# 未使用パラメータに関する警告
dotnet_diagnostic.IDE0060.severity = suggestion

# 不要な値代入に関する警告
dotnet_diagnostic.IDE0059.severity = suggestion

# 非同期メソッドでのConfigureAwait使用に関する警告
dotnet_diagnostic.CA2007.severity = warning
```

## Baketaプロジェクト固有の設定

Baketaプロジェクトでは、以下の設定を特に重視しています：

1. **未使用パラメータの処理**: インターフェース実装時に必要となる未使用パラメータは、`_`プレフィックスを使用（IDE0060）
2. **非同期処理の最適化**: `ConfigureAwait(false)`の使用を推奨（CA2007）
3. **コレクション初期化の簡素化**: 明示的なコレクション初期化を推奨（IDE0028）

テストプロジェクトでは一部の警告（例：IDE0060）を抑制し、テストコードの可読性を優先しています。

## ファイルの所在

`.editorconfig`ファイルはプロジェクトのルートディレクトリ（`E:\dev\Baketa\.editorconfig`）に配置されています。Visual Studioや他のIDE（Visual Studio Code, Rider等）は自動的にこの設定を検出し適用します。

## 注意事項

- 設定変更はソリューション全体に影響するため、慎重に行ってください
- プロジェクト特有の要件がある場合は、サブディレクトリに追加の`.editorconfig`ファイルを配置することで上書き可能です
- コード分析抑制（`dotnet_diagnostic.*.severity = none`）は必要最小限にとどめ、理由をコメントで明記してください