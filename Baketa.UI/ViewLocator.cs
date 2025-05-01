using System;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Baketa.UI.Framework;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Baketa.UI
{
    /// <summary>
    /// ビューとビューモデルの対応関係を解決するビューロケータ
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses",
        Justification = "Instantiated by Avalonia XAML processor")]
    internal sealed class ViewLocator : IDataTemplate
    {
        private static readonly ILogger? _logger = Program.ServiceProvider?.GetService(typeof(ILogger<ViewLocator>)) as ILogger<ViewLocator>;

        // LoggerMessageデリゲートの定義
        private static readonly Action<ILogger, string, Exception?> LogViewNotFound =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(1, nameof(LogViewNotFound)),
                "ビューが見つかりません: {ViewName}");

        private static readonly Action<ILogger, string, Exception> LogTypeLoadFailed =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(2, nameof(LogTypeLoadFailed)),
                "ビュー型の読み込みに失敗しました: {ExceptionMessage}");

        private static readonly Action<ILogger, string, Exception> LogConstructorNotFound =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(3, nameof(LogConstructorNotFound)),
                "ビューのコンストラクタが見つかりません: {ExceptionMessage}");

        private static readonly Action<ILogger, string, Exception> LogInvalidCast =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(4, nameof(LogInvalidCast)),
                "ビューの型変換に失敗しました: {ExceptionMessage}");

        private static readonly Action<ILogger, string, Exception> LogBuildFailed =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(5, nameof(LogBuildFailed)),
                "ビューの構築に失敗しました: {ExceptionMessage}");

        /// <summary>
        /// ビューモデルからビューを構築します
        /// </summary>
        /// <param name="param">ビューモデル</param>
        /// <returns>対応するビュー</returns>
        public Control? Build(object? param)
        {
            if (param is null)
            {
                return null;
            }

            try
            {
            // 命名規則：名前空間.ViewModels.XXXViewModel → 名前空間.Views.XXXView
            var viewModelName = param.GetType().FullName!;
            var viewName = viewModelName.Replace("ViewModels", "Views", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);
            
            var viewType = Type.GetType(viewName);
            
            if (viewType != null)
            {
            return (Control)Activator.CreateInstance(viewType)!;
            }
            
            // 別の方法でビュー型を探す
            var viewTypeName = param.GetType().Name.Replace("ViewModel", "View", StringComparison.Ordinal);
            var viewTypeFullName = $"Baketa.UI.Views.{viewTypeName}";
            viewType = Type.GetType(viewTypeFullName) ?? 
            Type.GetType(viewTypeFullName + ", Baketa.UI");
            
            if (viewType != null)
            {
            return (Control)Activator.CreateInstance(viewType)!;
            }
            
            LogViewNotFound(_logger ?? NullLogger.Instance, viewName, null);
            return new TextBlock { Text = $"ビューが見つかりません: {viewName}" };
            }
            catch (TypeLoadException ex)
            {
            LogTypeLoadFailed(_logger ?? NullLogger.Instance, ex.Message, ex);
            return new TextBlock { Text = $"ビュー型の読み込みに失敗しました: {ex.Message}" };
            }
        catch (MissingMethodException ex)
        {
            LogConstructorNotFound(_logger ?? NullLogger.Instance, ex.Message, ex);
            return new TextBlock { Text = $"ビューのコンストラクタが見つかりません: {ex.Message}" };
        }
        catch (InvalidCastException ex)
        {
            LogInvalidCast(_logger ?? NullLogger.Instance, ex.Message, ex);
            return new TextBlock { Text = $"ビューの型変換に失敗しました: {ex.Message}" };
        }
        catch (Exception ex) when (ex is not TypeLoadException && ex is not MissingMethodException && ex is not InvalidCastException)
        {
            LogBuildFailed(_logger ?? NullLogger.Instance, ex.Message, ex);
            return new TextBlock { Text = $"ビューの構築に失敗しました: {ex.Message}" };
        }
        }

        /// <summary>
        /// このテンプレートがデータ型に一致するかを確認します
        /// </summary>
        /// <param name="data">データオブジェクト</param>
        /// <returns>一致する場合はtrue</returns>
        public bool Match(object? data)
        {
            return data is ViewModelBase;
        }
    }
}