// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

// 一般的な警告の抑制
[assembly: SuppressMessage("Design", "CA1822:Mark members as static", Justification = "将来的なリファクタリングで対応予定", Scope = "module")]
[assembly: SuppressMessage("Usage", "CA2227:Collection properties should be read only", Justification = "将来的なリファクタリングで対応予定", Scope = "module")]
[assembly: SuppressMessage("Design", "CA1819:Properties should not return arrays", Justification = "将来的なリファクタリングで対応予定", Scope = "module")]
[assembly: SuppressMessage("Naming", "CA1725:Parameter names should match base declaration", Justification = "将来的なリファクタリングで対応予定", Scope = "module")]
// CA2254とCA2263: 修正済みのため抑制不要
// CA2263: 動的型解決が必要な箇所のみ限定的に抑制
[assembly: SuppressMessage("Design", "CA2263:Prefer generic overload", Justification = "動的型解決が必要なDIコンテナ使用箇所", Scope = "namespaceanddescendants", Target = "~N:Baketa.Application.Services.Events")]
[assembly: SuppressMessage("Design", "CA2263:Prefer generic overload", Justification = "動的型解決が必要なDIコンテナ使用箇所", Scope = "namespaceanddescendants", Target = "~N:Baketa.Infrastructure.Imaging.Pipeline")]
[assembly: SuppressMessage("Design", "CA2263:Prefer generic overload", Justification = "動的型解決が必要なType.GetType()使用箇所", Scope = "namespaceanddescendants", Target = "~N:Baketa.Application.DI.Extensions")]
[assembly: SuppressMessage("Style", "IDE0270:Use coalesce expression", Justification = "将来的なリファクタリングで対応予定", Scope = "module")]
[assembly: SuppressMessage("Naming", "CA1721:Property names should not match get methods", Justification = "将来的なリファクタリングで対応予定", Scope = "module")]

// IDE0060: 未使用パラメータの抑制 - 将来実装やインターフェース互換性のため
[assembly: SuppressMessage("Style", "IDE0060", Justification = "将来実装やインターフェース互換性のため必要なパラメータ", Scope = "module")]

// IDE0161: ファイルスコープ名前空間の抑制 - 既存コードとの一貫性のため
[assembly: SuppressMessage("Style", "IDE0161", Justification = "既存コードとの一貫性を保つため", Scope = "module")]

// IDE0044: readonlyフィールドの抑制 - テストコードでの動的変更のため
[assembly: SuppressMessage("Style", "IDE0044", Justification = "テストコードでの動的変更が必要", Scope = "module")]

// CA1852: 型はシールドできます
[assembly: SuppressMessage("Design", "CA1852:型はシールドできます", Justification = "プロジェクトの方針として、明示的に継承を制限する必要がある場合のみsealedを使用します")]