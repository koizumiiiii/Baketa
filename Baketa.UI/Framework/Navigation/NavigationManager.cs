using System;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.Framework.Navigation
{
    /// <summary>
    /// ナビゲーション管理クラス
    /// </summary>
    internal class NavigationManager(System.IServiceProvider serviceProvider, ILogger<NavigationManager>? logger = null) : ReactiveObject, INavigationHost
    {
        private readonly System.IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        private readonly ILogger<NavigationManager>? _logger = logger;
        
        // LoggerMessage デリゲートを定義
        private static readonly Action<ILogger, string, Exception?> _logNavigateTo =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(1, "NavigateTo"),
                "画面遷移: {DestinationViewModel}");
                
        private static readonly Action<ILogger, string, Exception> _logNavigationError =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(2, "NavigationError"),
                "画面遷移エラー: {DestinationViewModel}");
                
        private static readonly Action<ILogger, string, Exception?> _logNavigateToWithParam =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(3, "NavigateToWithParam"),
                "画面遷移 (パラメータ付き): {DestinationViewModel}");
                
        private static readonly Action<ILogger, string, Exception> _logNavigationWithParamError =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(4, "NavigationWithParamError"),
                "パラメータ付き画面遷移エラー: {DestinationViewModel}");
                
        private static readonly Action<ILogger, Exception?> _logNavigateBack =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(5, "NavigateBack"),
                "前の画面に戻ります");
                
        private static readonly Action<ILogger, Exception> _logNavigateBackError =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(6, "NavigateBackError"),
                "前の画面への戻りでエラーが発生しました");
        
        /// <summary>
        /// ルーティング状態
        /// </summary>
        public RoutingState Router { get; } = new RoutingState();
        
        /// <summary>
        /// 現在表示中のビューモデル
        /// </summary>
        public IReactiveObject CurrentViewModel => Router.CurrentViewModel as IReactiveObject ?? throw new InvalidOperationException("CurrentViewModel is not available or not an IReactiveObject");
        
        /// <inheritdoc />
        public async Task NavigateToAsync<T>() where T : IRoutableViewModel
        {
            if (_logger != null)
                _logNavigateTo(_logger, typeof(T).Name, null);
            
            try
            {
                var viewModel = _serviceProvider.GetRequiredService<T>();
                await Router.Navigate.Execute(viewModel).ToTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (_logger != null)
                    _logNavigationError(_logger, typeof(T).Name, ex);
                throw;
            }
        }
        
        /// <inheritdoc />
        public async Task NavigateToAsync<T, TParam>(TParam parameter) where T : IRoutableViewModel
        {
            if (_logger != null)
                _logNavigateToWithParam(_logger, typeof(T).Name, null);
            
            try
            {
                // パラメータ付きビューモデルの作成方法に応じて実装
                // 例: ファクトリパターンやActivatorを使用
                
                // 方法1: ファクトリを使用する場合
                var factory = _serviceProvider.GetRequiredService<Func<TParam, T>>();
                var viewModel = factory(parameter);
                
                // 方法2: IActivatableな場合、作成後にパラメータを設定
                // var viewModel = _serviceProvider.GetRequiredService<T>();
                // if (viewModel is IParameterizedViewModel<TParam> paramViewModel)
                // {
                //     paramViewModel.Initialize(parameter);
                // }
                
                await Router.Navigate.Execute(viewModel).ToTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (_logger != null)
                    _logNavigationWithParamError(_logger, typeof(T).Name, ex);
                throw;
            }
        }
        
        /// <inheritdoc />
        public async Task NavigateBackAsync()
        {
            if (_logger != null)
                _logNavigateBack(_logger, null);
            
            try
            {
                await Router.NavigateBack.Execute().ToTask().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (_logger != null)
                    _logNavigateBackError(_logger, ex);
                throw;
            }
        }
    }
    
    /// <summary>
    /// パラメータ付きビューモデルのインターフェース
    /// </summary>
    /// <typeparam name="TParam">パラメータの型</typeparam>
    internal interface IParameterizedViewModel<in TParam>
    {
        /// <summary>
        /// パラメータでビューモデルを初期化します
        /// </summary>
        /// <param name="parameter">パラメータ</param>
        void Initialize(TParam parameter);
    }
}
