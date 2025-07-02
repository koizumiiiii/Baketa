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
[assembly: SuppressMessage("Usage", "CA2254:Template should be a static expression", Justification = "将来的なリファクタリングで対応予定", Scope = "module")]
[assembly: SuppressMessage("Design", "CA2263:Prefer generic overload", Justification = "将来的なリファクタリングで対応予定", Scope = "module")]
[assembly: SuppressMessage("Style", "IDE0270:Use coalesce expression", Justification = "将来的なリファクタリングで対応予定", Scope = "module")]
[assembly: SuppressMessage("Naming", "CA1721:Property names should not match get methods", Justification = "将来的なリファクタリングで対応予定", Scope = "module")]

// CA1852: 型はシールドできます
[assembly: SuppressMessage("Design", "CA1852:型はシールドできます", Justification = "プロジェクトの方針として、明示的に継承を制限する必要がある場合のみsealedを使用します")]