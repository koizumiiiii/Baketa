using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.Platform.Adapters.Factory;

/// <summary>
/// テスト環境用のモックアダプターファクトリー実装
/// </summary>
public class MockAdapterFactory(IServiceProvider serviceProvider) : IAdapterFactory
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
