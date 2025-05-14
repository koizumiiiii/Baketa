using System;

namespace Baketa.Core.Translation.Events
{
    // 引き継ぎのためにデプリケート済みとマークします
    [Obsolete("ITranslationEventHandlerに移行してください", true)]
    public interface IEventHandler<TEvent>
    {
        // このインターフェースはデプリケートされました。
        // ITranslationEventHandler<TEvent>を使用してください。
    }
}