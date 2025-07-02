using System.Runtime.CompilerServices;

// InternalsVisibleTo 属性を使用してテストアセンブリからの internal メンバーアクセスを許可
[assembly: InternalsVisibleTo("Baketa.Application.Tests")]
[assembly: InternalsVisibleTo("Baketa.Integration.Tests")]

// Moq フレームワーク用の DynamicProxyGenAssembly2 に対する InternalsVisibleTo 属性
// これにより Moq が internal クラスのプロキシを作成できるようになります
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
