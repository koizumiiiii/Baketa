// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

// テストプロジェクトのためのConfigureAwait警告抑制
[assembly: SuppressMessage("Usage", "CA2007:Consider calling ConfigureAwait on the awaited task", 
    Justification = "テストプロジェクトでは同期コンテキストの問題が発生しないため、ConfigureAwaitは不要。")]

// IDisposableの実装に関する警告抑制（テストクラスの場合は簡易実装を許可）
[assembly: SuppressMessage("Design", "CA1063:Implement IDisposable Correctly", 
    Justification = "テストクラスでは簡略化したDisposableパターンを使用。")]

// テストメソッドにおけるConfigureAwait(false)の使用に関する警告抑制
[assembly: SuppressMessage("xUnit", "xUnit1030:Test methods should not call ConfigureAwait", 
    Justification = "CA2007との競合を避けるため、テストメソッドでもConfigureAwaitを使用。")]
