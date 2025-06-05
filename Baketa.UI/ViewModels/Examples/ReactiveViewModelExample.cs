using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Abstractions;
using ReactiveUI.Validation.Contexts;
using Baketa.UI.Framework.Events;

namespace Baketa.UI.ViewModels.Examples;

    /// <summary>
    /// ReactiveUIパターンの使用例を示すサンプルビューモデル
    /// </summary>
    internal sealed class ReactiveViewModelExample : global::Baketa.UI.Framework.ViewModelBase, IValidatableViewModel
    {
        // LoggerMessage デリゲートを定義
        private static readonly Action<ILogger, string, Exception?> _logCommandExecuted =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(1, "CommandExecuted"),
                "コマンド{CommandName}を実行しました");
                
        private static readonly Action<ILogger, string, Exception> _logCommandError =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(2, "CommandError"),
                "コマンド{CommandName}でエラーが発生しました");
                
        private static readonly Action<ILogger, string, Exception?> _logDataSave =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(3, "DataSave"),
                "データを保存します: {Name}");
                
        private static readonly Action<ILogger, Exception?> _logDataSaveComplete =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(4, "DataSaveComplete"),
                "データが正常に保存されました");
                
        private static readonly Action<ILogger, Exception> _logDataSaveError =
            LoggerMessage.Define(
                LogLevel.Error,
                new EventId(5, "DataSaveError"),
                "保存処理でエラーが発生しました");
                
        private static readonly Action<ILogger, Exception?> _logDataReset =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(6, "DataReset"),
                "データをリセットします");
                
        private static readonly Action<ILogger, Exception?> _logDataRequestReceived =
            LoggerMessage.Define(
                LogLevel.Information,
                new EventId(7, "DataRequestReceived"),
                "データリクエストイベントを受信しました");
        // Fodyによる自動プロパティ変更通知を使用したプロパティ
        [Reactive] public string Name { get; set; } = string.Empty;
        [Reactive] public string Description { get; set; } = string.Empty;
        [Reactive] public bool IsActive { get; set; }
        
        // IValidatableViewModelの実装
        public IValidationContext ValidationContext { get; } = new ValidationContext();
        
        // ObservableAsPropertyHelperを使用した派生プロパティ
        private readonly ObservableAsPropertyHelper<bool> _canSave;
        public bool CanSave => _canSave.Value;
        
        // コマンド
        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> ResetCommand { get; }
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="eventAggregator">イベント集約器</param>
        /// <param name="logger">ロガー</param>
        public ReactiveViewModelExample(IEventAggregator eventAggregator, ILogger? logger = null) 
            : base(eventAggregator, logger)
        {
            // バリデーションルールの追加
            var nameRule = this.ValidationRule(
                viewModel => viewModel.Name,
                name => !string.IsNullOrWhiteSpace(name),
                "名前は必須項目です");
            _disposables.Add(nameRule);
                
            var descRule = this.ValidationRule(
                viewModel => viewModel.Description,
                description => description?.Length <= 100,
                "説明は100文字以内で入力してください");
            _disposables.Add(descRule);
                
            // 派生プロパティの設定（Name, Descriptionの両方が有効な場合のみ保存可能）
            _canSave = this.WhenAnyValue(
                x => x.Name,
                x => x.Description,
                (name, desc) => 
                    !string.IsNullOrWhiteSpace(name) &&
                    !(desc?.Length > 100))
                .ToProperty(this, x => x.CanSave);
            _disposables.Add(_canSave);
                
            // コマンドの作成
            SaveCommand = global::Baketa.UI.Framework.ReactiveUI.ReactiveCommandFactory.Create(
                    ExecuteSaveAsync,
                    this.WhenAnyValue(x => x.CanSave));
            
            // コマンドにログ機能を追加
            SaveCommand.Subscribe(
                _ => {
                    if (_logger != null)
                        _logCommandExecuted(_logger, "Save", null);
                    MessageBus.Current.SendMessage(new global::Baketa.UI.Framework.ExecuteCommandMessage("Save"));
                });

            SaveCommand.ThrownExceptions.Subscribe(
                ex => {
                    if (_logger != null)
                        _logCommandError(_logger, "Save", ex);
                });
            
            _disposables.Add(SaveCommand);
                
            ResetCommand = global::Baketa.UI.Framework.ReactiveUI.ReactiveCommandFactory.Create(
                    ExecuteResetAsync);
                    
            // コマンドにログ機能を追加
            ResetCommand.Subscribe(
                _ => {
                    if (_logger != null)
                        _logCommandExecuted(_logger, "Reset", null);
                    MessageBus.Current.SendMessage(new global::Baketa.UI.Framework.ExecuteCommandMessage("Reset"));
                });

            ResetCommand.ThrownExceptions.Subscribe(
                ex => {
                    if (_logger != null)
                        _logCommandError(_logger, "Reset", ex);
                });
                
            _disposables.Add(ResetCommand);
        }
        
        /// <summary>
        /// リソース解放処理のオーバーライド
        /// </summary>
        /// <param name="disposing">マネージドリソースを解放するかどうか</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // IDisposableインターフェースを実装するフィールドのリソースを解放
                _canSave?.Dispose();
            }
            
            base.Dispose(disposing);
        }
        
        /// <summary>
        /// 保存コマンドの実行処理
        /// </summary>
        private async Task ExecuteSaveAsync()
        {
            try
            {
                IsLoading = true;
                ErrorMessage = null;
                
                if (_logger != null)
                    _logDataSave(_logger, Name, null);
                
                // 保存処理の模擬
                await Task.Delay(1000).ConfigureAwait(false);
                
                // イベントの発行
                await PublishEventAsync(new DataSavedEvent(this.Name)).ConfigureAwait(false);
                
                if (_logger != null)
                    _logDataSaveComplete(_logger, null);
            }
            catch (InvalidOperationException ex)
            {
                if (_logger != null)
                    _logDataSaveError(_logger, ex);
                ErrorMessage = "保存に失敗しました: " + ex.Message;
            }
            catch (TaskCanceledException ex)
            {
                if (_logger != null)
                    _logDataSaveError(_logger, ex);
                ErrorMessage = "保存処理がキャンセルされました: " + ex.Message;
            }
            catch (TimeoutException ex)
            {
                if (_logger != null)
                    _logDataSaveError(_logger, ex);
                ErrorMessage = "保存処理がタイムアウトしました: " + ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        /// <summary>
        /// リセットコマンドの実行処理
        /// </summary>
        private async Task ExecuteResetAsync()
        {
            if (_logger != null)
                _logDataReset(_logger, null);
            
            // リアクティブプログラミングのメリットを示すため、変更通知を遅延
            using (DelayChangeNotifications())
            {
                Name = string.Empty;
                Description = string.Empty;
                IsActive = false;
                ErrorMessage = null;
            }
            
            // 非同期処理の例示
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        /// <summary>
        /// アクティベーション時の処理
        /// </summary>
        protected override void HandleActivation()
        {
            base.HandleActivation();
            
            // イベント購読の例
            SubscribeToEvent<DataRequestEvent>(async _ => 
            {
                if (_logger != null)
                    _logDataRequestReceived(_logger, null);
                await ExecuteSaveAsync().ConfigureAwait(false);
            });
        }
    }
    
    /// <summary>
    /// データ保存イベント
    /// </summary>
    /// <param name="name">データ名</param>
    internal sealed class DataSavedEvent(string name) : UIEventBase
    {
        /// <summary>
        /// 保存されたデータ名
        /// </summary>
        public string DataName { get; } = name;
        
        /// <inheritdoc/>
        public override string Name => "DataSaved";
        
        /// <inheritdoc/>
        public override string Category => "Data";
    }
    
    /// <summary>
    /// データリクエストイベント
    /// </summary>
    internal sealed class DataRequestEvent : UIEventBase
    {
        /// <inheritdoc/>
        public override string Name => "DataRequest";
        
        /// <inheritdoc/>
        public override string Category => "Data";
    }
