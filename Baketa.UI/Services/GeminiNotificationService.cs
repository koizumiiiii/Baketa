using System.Threading.Tasks;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Translation;
using Baketa.UI.Resources;

namespace Baketa.UI.Services;

public sealed class GeminiNotificationService(IErrorNotificationService notifications) : IGeminiNotificationService
{
    public Task NotifyAsync(string resourceKey) => notifications.ShowErrorAsync(
        Strings.ResourceManager.GetString(resourceKey, Strings.Culture) ?? resourceKey);
}
