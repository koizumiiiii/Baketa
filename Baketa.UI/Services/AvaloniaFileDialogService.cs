using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// Avalonia専用ファイルダイアログサービス実装
/// </summary>
public sealed class AvaloniaFileDialogService : IFileDialogService
{
    private readonly ILogger<AvaloniaFileDialogService> _logger;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public AvaloniaFileDialogService(ILogger<AvaloniaFileDialogService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<string?> ShowSaveFileDialogAsync(
        string title, 
        string? defaultFileName = null, 
        IReadOnlyList<FileTypeFilter>? fileTypeFilters = null)
    {
        try
        {
            var window = GetActiveWindow();
            if (window?.StorageProvider == null)
            {
                _logger.LogError("アクティブなウィンドウまたはStorageProviderが見つかりません");
                return null;
            }

            var options = new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = defaultFileName
            };

            if (fileTypeFilters != null && fileTypeFilters.Count > 0)
            {
                options.FileTypeChoices = ConvertToStorageFileTypes(fileTypeFilters);
            }

            var result = await window.StorageProvider.SaveFilePickerAsync(options).ConfigureAwait(false);
            var filePath = result?.TryGetLocalPath();

            _logger.LogDebug("ファイル保存ダイアログ結果: {FilePath}", filePath ?? "キャンセル");
            return filePath;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "ファイル保存ダイアログの表示中に操作エラーが発生しました");
            return null;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "ファイル保存ダイアログの表示中に予期しないエラーが発生しました");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>?> ShowOpenFileDialogAsync(
        string title, 
        IReadOnlyList<FileTypeFilter>? fileTypeFilters = null, 
        bool allowMultiple = false)
    {
        try
        {
            var window = GetActiveWindow();
            if (window?.StorageProvider == null)
            {
                _logger.LogError("アクティブなウィンドウまたはStorageProviderが見つかりません");
                return null;
            }

            var options = new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = allowMultiple
            };

            if (fileTypeFilters != null && fileTypeFilters.Count > 0)
            {
                options.FileTypeFilter = ConvertToStorageFileTypes(fileTypeFilters);
            }

            var results = await window.StorageProvider.OpenFilePickerAsync(options).ConfigureAwait(false);
            if (results == null || results.Count == 0)
            {
                _logger.LogDebug("ファイル選択ダイアログ: キャンセルされました");
                return null;
            }

            var filePaths = results
                .Select(file => file.TryGetLocalPath())
                .Where(path => !string.IsNullOrEmpty(path))
                .Cast<string>()
                .ToList();

            _logger.LogDebug("ファイル選択ダイアログ結果: {FileCount}個のファイル", filePaths.Count);
            return filePaths;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "ファイル選択ダイアログの表示中に操作エラーが発生しました");
            return null;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "ファイル選択ダイアログの表示中に予期しないエラーが発生しました");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<string?> ShowOpenFolderDialogAsync(string title)
    {
        try
        {
            var window = GetActiveWindow();
            if (window?.StorageProvider == null)
            {
                _logger.LogError("アクティブなウィンドウまたはStorageProviderが見つかりません");
                return null;
            }

            var options = new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false
            };

            var results = await window.StorageProvider.OpenFolderPickerAsync(options).ConfigureAwait(false);
            var folderPath = results.Count > 0 ? results[0].TryGetLocalPath() : null;

            _logger.LogDebug("フォルダ選択ダイアログ結果: {FolderPath}", folderPath ?? "キャンセル");
            return folderPath;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "フォルダ選択ダイアログの表示中に操作エラーが発生しました");
            return null;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "フォルダ選択ダイアログの表示中に予期しないエラーが発生しました");
            return null;
        }
    }

    /// <summary>
    /// アクティブなウィンドウを取得します
    /// </summary>
    private static Window? GetActiveWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        return desktop.MainWindow ?? (desktop.Windows.Count > 0 ? desktop.Windows[0] : null);
    }

    /// <summary>
    /// FileTypeFilterをAvalonia用のFilePickerFileTypeに変換します
    /// </summary>
    private static List<FilePickerFileType> ConvertToStorageFileTypes(
        IReadOnlyList<FileTypeFilter> filters)
    {
        var result = new List<FilePickerFileType>();

        foreach (var filter in filters)
        {
            if (filter.Extensions.Count == 0) continue;

            var patterns = filter.Extensions
                .Select(ext => ext.StartsWith('.') ? ext : $".{ext}")
                .ToList();

            var fileType = new FilePickerFileType(filter.Name)
            {
                Patterns = patterns,
                AppleUniformTypeIdentifiers = [],
                MimeTypes = []
            };

            result.Add(fileType);
        }

        return result;
    }
}
