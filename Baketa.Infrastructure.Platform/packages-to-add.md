# 追加すべきNuGetパッケージ

以下のパッケージをプロジェクトに追加する必要があります：

```powershell
# プロジェクトディレクトリで以下を実行
dotnet add package OpenCvSharp4 -v 4.8.0
dotnet add package OpenCvSharp4.runtime.win -v 4.8.0
dotnet add package Microsoft.Extensions.Options -v 8.0.0
dotnet add package Microsoft.Extensions.DependencyInjection.Abstractions -v 8.0.0
```

これらのパッケージは以下の機能を提供します：

1. **OpenCVSharp4**: OpenCVの.NET向けラッパー
2. **OpenCVSharp4.runtime.win**: Windows向けのOpenCVネイティブランタイム
3. **Microsoft.Extensions.Options**: オプションパターンのサポート
4. **Microsoft.Extensions.DependencyInjection.Abstractions**: DIコンテナサポート
