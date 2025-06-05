using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

// テストプロジェクトからの内部メンバーへのアクセスを許可
[assembly: InternalsVisibleTo("Baketa.Infrastructure.Platform.Tests")]

// Windowsプラットフォームのみをサポートすることを明示
[assembly: SupportedOSPlatform("windows")]
