using System;

namespace Baketa.Core.Translation.Events
{
    // 引き継ぎのためにデプリケート済みとマークします
    [Obsolete("ITranslationEventHandler<TEvent>に移行してください", true)]
    public abstract class EventHandlerLegacy<TEvent>
    {
        // このクラスはデプリケートされました。
        // ITranslationEventHandler<TEvent>を使用してください。
    }
}