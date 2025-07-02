// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

// テスト用のアンダースコア記法は一般的なため警告を抑制
[assembly: SuppressMessage("Naming", "CA1707:アンダースコアをメンバー名から削除してください", Justification = "テスト名にはMethod_Scenario_Resultパターンを使用", Scope = "module")]

// テストクラスの公開APIを抑制
[assembly: SuppressMessage("Design", "CA1515:クラス ライブラリとは異なり、アプリケーションの API は通常は公開参照されないため、型を内部としてマークできます", Justification = "テストクラスは公開する必要がある", Scope = "module")]

// カスタム属性を使用するための警告を抑制
[assembly: SuppressMessage("Usage", "CA1801:未使用のパラメーター", Justification = "テストコードではパラメータを使用しないことが一般的", Scope = "module")]

// テスト用の暗黙的初期化警告を抑制
[assembly: SuppressMessage("Performance", "CA1805:.NET ランタイムは、コンストラクターを実行する前に、参照型のすべてのフィールドを既定値に初期化します", Justification = "テストコードでは必要性が低い", Scope = "module")]

// テスト用の静的メソッド警告を抑制
[assembly: SuppressMessage("Performance", "CA1822:インスタンス データにアクセスしないメンバーまたはインスタンス メソッドを呼び出さないメンバーは、static にマークできます", Justification = "テストコードではセマンティックを優先", Scope = "module")]

// テスト用のIDisposableオブジェクトの破棄警告を抑制
[assembly: SuppressMessage("Reliability", "CA2000:破棄可能なオブジェクトは、自身へのすべての参照がスコープ外になる前に明示的に破棄されなかった場合、ガベージ コレクターがそのオブジェクトのファイナライザーを実行した際に不特定の時点で破棄されます", Justification = "テストコードではusingで対応済み", Scope = "module")]

// ConfigureAwaitとxUnitの競合警告を抑制
[assembly: SuppressMessage("Usage", "CA2007:非同期メソッドがタスクを直接待機すると、タスクを作成したのと同じスレッドで継続が発生します", Justification = "xUnitでは非同期テストが必要", Scope = "module")]

// スローヘルパーの使用推奨に関する警告を抑制
[assembly: SuppressMessage("Design", "CA1510:スロー ヘルパーは、if ブロックが新しい例外インスタンスを構築する場合よりもシンプルで効率的です", Justification = "テストコードでは明確さを優先", Scope = "module")]

// テストでの文字列比較パラメータ警告を抑制
[assembly: SuppressMessage("Globalization", "CA1307:文字列比較操作では、StringComparison パラメーターを設定しないメソッド オーバーロードを使用します", Justification = "テストコードでは単純化を優先", Scope = "module")]
