// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a module target.

using System.Diagnostics.CodeAnalysis;

// テストメソッド名のアンダースコア使用を許可
[assembly: SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores",
    Justification = "Test method names commonly use underscores for readability",
    Scope = "namespaceanddescendants", Target = "~N:Baketa.Infrastructure.Tests")]

// テストプロジェクトでのObsolete APIの使用を許可（テスト対象のため）
[assembly: SuppressMessage("Usage", "CS0618:Type or member is obsolete",
    Justification = "Testing obsolete APIs may be necessary",
    Scope = "namespaceanddescendants", Target = "~N:Baketa.Infrastructure.Tests")]

// xUnitでのnull値使用を許可（テストシナリオのため）
[assembly: SuppressMessage("Usage", "xUnit1012:Null should not be used for type parameter",
    Justification = "Null values are used intentionally in test scenarios",
    Scope = "namespaceanddescendants", Target = "~N:Baketa.Infrastructure.Tests")]
