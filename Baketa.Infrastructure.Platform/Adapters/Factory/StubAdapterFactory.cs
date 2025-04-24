using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.Platform.Adapters.Factory;

/// <summary>
/// 開発環境用のスタブアダプターファクトリー実装
/// </summary>
public class StubAdapterFactory(IServiceProvider serviceProvider) : IAdapterFactory
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    
    /// <inheritdoc/>
    public IWindowsImageAdapter CreateImageAdapter() => 
        _serviceProvider.GetRequiredService<IWindowsImageAdapter>();
        
    /// <inheritdoc/>
    public ICaptureAdapter CreateCaptureAdapter() => 
        _serviceProvider.GetRequiredService<ICaptureAdapter>();
        
    /// <inheritdoc/>
    public IWindowManagerAdapter CreateWindowManagerAdapter() => 
        _serviceProvider.GetRequiredService<IWindowManagerAdapter>();
}
