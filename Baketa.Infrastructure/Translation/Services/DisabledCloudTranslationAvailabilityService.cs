using Baketa.Core.Abstractions.Translation;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>Cloud providers are unavailable in the local-only edition, regardless of saved preferences.</summary>
public sealed class DisabledCloudTranslationAvailabilityService : ICloudTranslationAvailabilityService
{
    public bool IsEntitled => true;
    public bool IsPreferred => false;
    public bool IsEffectivelyEnabled => false;
    public event EventHandler<CloudTranslationAvailabilityChangedEventArgs>? AvailabilityChanged { add { } remove { } }
    public Task RefreshStateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SetPreferredAsync(bool preferred, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
