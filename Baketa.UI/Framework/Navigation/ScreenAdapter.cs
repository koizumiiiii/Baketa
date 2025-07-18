using System;
using System.Reactive;
using System.Reactive.Linq;
using ReactiveUI;

namespace Baketa.UI.Framework.Navigation;

    /// <summary>
    /// RoutingStateをIScreenとして使用するためのアダプター
    /// ReactiveUI 20.1.63用に最適化
    /// </summary>
    /// <param name="routingState">ルーティングステート</param>
    internal sealed class ScreenAdapter(RoutingState routingState) : ReactiveObject, IScreen
    {
        /// <summary>
        /// 画面遷移に使用するルーター
        /// IScreenの要件に準拠
        /// </summary>
        public RoutingState Router { get; } = routingState ?? throw new ArgumentNullException(nameof(routingState));
        
        // IScreenインターフェースにはRouter以外のプロパティは存在しない
        // ReactiveUI 20.xでは、IScreenインターフェースにはRouter一つのみがあり、
        // 以前のバージョンにあったNavigateやNavigateBackはなくなった
    }
