// このファイルはコード分析で使用される属性を定義するために使用されます。
// プロジェクト レベルでの抑制を設定します。

using System.Diagnostics.CodeAnalysis;

// P/Invokeの警告を抑制（将来的にLibraryImportAttributeに移行予定）
[assembly: SuppressMessage("Interoperability", "CA5392:P/InvokeメソッドでSecuritySafeCriticalを使用する",
    Justification = "システムディレクトリのパスを明示的に指定して対応", 
    Scope = "module")]

// LibraryImportAttribute移行の警告抑制
[assembly: SuppressMessage("Interoperability", "SYSLIB1054:コンパイル時に P/Invoke マーシャリング コードを生成するには、'DllImportAttribute' の代わりに 'LibraryImportAttribute' を使用します", 
    Justification = "将来的にLibraryImportAttributeに移行予定", 
    Scope = "module")]

// プライマリコンストラクターの警告抑制（コードの一貫性のため）
[assembly: SuppressMessage("Style", "IDE0290:プライマリ コンストラクターの使用", 
    Justification = "既存コードの一貫性を保つため", 
    Scope = "module")]
