using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;

namespace Baketa.Infrastructure.Translation.Local.Onnx;

/// <summary>
/// ONNX Runtime を使用したモデルローダーの実装
/// </summary>
public class OnnxModelLoader : IModelLoader, IDisposable
{
    private readonly ILogger<OnnxModelLoader> _logger;
    private InferenceSession? _session;
    private SessionOptions? _sessionOptions;
    private ComputeDevice _currentDevice;
    private bool _disposed;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="logger">ロガー</param>
    public OnnxModelLoader(ILogger<OnnxModelLoader> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _currentDevice = ComputeDevice.CreateCpu();
    }

    /// <inheritdoc/>
    public async Task<bool> LoadModelAsync(string modelPath, ModelOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(modelPath, nameof(modelPath));

        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            _logger.LogInformation("ONNXモデルのロードを開始: {ModelPath}", modelPath);

            if (!File.Exists(modelPath))
            {
                _logger.LogError("モデルファイルが見つかりません: {ModelPath}", modelPath);
                return false;
            }

            // 既存のセッションがある場合は閉じる
            await DisposeSessionAsync().ConfigureAwait(false);

            // セッションオプションの設定
            _sessionOptions = CreateSessionOptions(options);

            // ONNXセッションの作成
            _session = new InferenceSession(modelPath, _sessionOptions);

            _logger.LogInformation("ONNXモデルのロードに成功: {ModelPath}, デバイス: {Device}", 
                modelPath, _currentDevice.Name);

            return true;
        }
        catch (OnnxRuntimeException ex)
        {
            _logger.LogError(ex, "ONNX Runtime エラーが発生しました: {Message}", ex.Message);
            return false;
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "モデルファイルが見つかりません: {ModelPath}", modelPath);
            return false;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "モデルファイルの読み込み中に入出力エラーが発生しました: {ModelPath}", modelPath);
            return false;
        }
        catch (OutOfMemoryException ex)
        {
            _logger.LogError(ex, "モデルロード中にメモリ不足が発生しました");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "モデルファイルへのアクセスが拒否されました");
            return false;
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex, "引数エラーが発生しました");
            return false;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "無効な操作が発生しました");
            return false;
        }
#pragma warning disable CA1031 // 一般的な例外をキャッチしない
        catch (Exception ex)
        {
            _logger.LogError(ex, "予期しないエラーが発生しました: {ExceptionType}", ex.GetType().Name);
            return false;
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc/>
    public bool IsModelLoaded()
    {
        return _session != null && !_disposed;
    }

    /// <inheritdoc/>
    public async Task<bool> UnloadModelAsync()
    {
        if (_disposed)
        {
            return true;
        }

        try
        {
            await DisposeSessionAsync().ConfigureAwait(false);
            _logger.LogInformation("ONNXモデルのアンロードが完了しました");
            return true;
        }
#pragma warning disable CA1031 // 一般的な例外をキャッチしない
        catch (Exception ex)
        {
            _logger.LogError(ex, "モデルアンロード中にエラーが発生しました: {ExceptionType}", ex.GetType().Name);
            return false;
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ComputeDevice>> GetAvailableDevicesAsync()
    {
        await Task.Delay(1).ConfigureAwait(false); // 非同期処理をシミュレート

        var devices = new List<ComputeDevice>
        {
            ComputeDevice.CreateCpu()
        };

        try
        {
            // GPU が利用可能かどうかをチェック
            // 簡易実装: システム情報からGPUの存在を確認
            if (HasGpuSupport())
            {
                devices.Add(ComputeDevice.CreateGpu(0, "CUDA GPU"));
            }
        }
#pragma warning disable CA1031 // 一般的な例外をキャッチしない
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GPU の検出中にエラーが発生しました");
        }
#pragma warning restore CA1031

        return devices;
    }

    /// <inheritdoc/>
    public async Task<bool> SetDeviceAsync(ComputeDevice device)
    {
        ArgumentNullException.ThrowIfNull(device, nameof(device));
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            _logger.LogInformation("デバイスを変更中: {CurrentDevice} -> {NewDevice}", 
                _currentDevice.Name, device.Name);

            _currentDevice = device;
            
            // セッションが既にロードされている場合は再ロードが必要
            // この実装では設定のみ変更し、次回ロード時に反映
            await Task.Delay(1).ConfigureAwait(false);

            _logger.LogInformation("デバイス設定を変更しました: {DeviceName}", device.Name);
            return true;
        }
#pragma warning disable CA1031 // 一般的な例外をキャッチしない
        catch (Exception ex)
        {
            _logger.LogError(ex, "デバイス設定の変更中にエラーが発生しました: {DeviceId}", device.DeviceId);
            return false;
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc/>
    public ComputeDevice GetCurrentDevice()
    {
        return _currentDevice;
    }

    /// <summary>
    /// 推論を実行する
    /// </summary>
    /// <param name="inputTensors">入力テンソル</param>
    /// <returns>出力テンソル</returns>
    public IDisposableReadOnlyCollection<DisposableNamedOnnxValue> Run(IReadOnlyCollection<NamedOnnxValue> inputTensors)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_session == null)
        {
            throw new InvalidOperationException("モデルがロードされていません");
        }

        try
        {
            return _session.Run(inputTensors);
        }
        catch (OnnxRuntimeException ex)
        {
            _logger.LogError(ex, "ONNX 推論実行中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// セッションオプションの作成
    /// </summary>
    /// <param name="options">モデルオプション</param>
    /// <returns>セッションオプション</returns>
    private SessionOptions CreateSessionOptions(ModelOptions? options)
    {
        var sessionOptions = new SessionOptions();

        try
        {
            // 基本設定
            sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            sessionOptions.ExecutionMode = ExecutionMode.ORT_PARALLEL;

            if (options != null)
            {
                // スレッド数の設定
                if (options.ThreadCount > 0)
                {
                    sessionOptions.IntraOpNumThreads = options.ThreadCount;
                }

                // メモリ制限の設定
                if (options.MemoryLimit > 0)
                {
                    // ORT では直接的なメモリ制限設定はないため、セッション設定で調整
                    _logger.LogInformation("メモリ制限が指定されました: {MemoryLimitMB}MB", options.MemoryLimit);
                }
            }

            // デバイス固有の設定
            ConfigureExecutionProvider(sessionOptions);

            return sessionOptions;
        }
        catch (Exception ex)
        {
            sessionOptions.Dispose();
            _logger.LogError(ex, "セッションオプション作成中にエラーが発生しました");
            throw;
        }
    }

    /// <summary>
    /// 実行プロバイダーの設定
    /// </summary>
    /// <param name="sessionOptions">セッションオプション</param>
    private void ConfigureExecutionProvider(SessionOptions sessionOptions)
    {
        try
        {
            if (_currentDevice.DeviceType == ComputeDeviceType.Cuda)
            {
                // GPU使用時はCUDA実行プロバイダーを追加
                // DeviceIdから数値部分を抽出
                if (int.TryParse(_currentDevice.DeviceId.Replace("gpu-", "", StringComparison.Ordinal), out int deviceIndex))
                {
                    sessionOptions.AppendExecutionProvider_CUDA(deviceIndex);
                    _logger.LogInformation("CUDA実行プロバイダーを設定しました: GPU {DeviceId}", _currentDevice.DeviceId);
                }
                else
                {
                    // デフォルトでGPU 0を使用
                    sessionOptions.AppendExecutionProvider_CUDA(0);
                    _logger.LogInformation("CUDA実行プロバイダーを設定しました: デフォルトGPU 0");
                }
            }
            else
            {
                // CPU使用時はCPU実行プロバイダー（デフォルト）
                sessionOptions.AppendExecutionProvider_CPU();
                _logger.LogInformation("CPU実行プロバイダーを設定しました");
            }
        }
#pragma warning disable CA1031 // 一般的な例外をキャッチしない
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "実行プロバイダーの設定中にエラーが発生しました。デフォルト設定を使用します");
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// GPU サポートの確認
    /// </summary>
    /// <returns>GPU がサポートされている場合は true</returns>
    private bool HasGpuSupport()
    {
        try
        {
            // 簡易実装: CUDA が利用可能かどうかを確認
            // 実際の実装では、より詳細なチェックが必要
            var providers = OrtEnv.Instance().GetAvailableProviders();
            return providers.Contains("CUDAExecutionProvider");
        }
#pragma warning disable CA1031 // 一般的な例外をキャッチしない
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GPU サポートの確認中にエラーが発生しました");
            return false;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// セッションの非同期破棄
    /// </summary>
    private async Task DisposeSessionAsync()
    {
        if (_session != null)
        {
            await Task.Run(() =>
            {
                try
                {
                    _session.Dispose();
                    _session = null;
                }
#pragma warning disable CA1031 // 一般的な例外をキャッチしない
                catch (Exception ex)
                {
                    _logger.LogError(ex, "セッション破棄中にエラーが発生しました");
                }
#pragma warning restore CA1031
            }).ConfigureAwait(false);
        }

        if (_sessionOptions != null)
        {
            await Task.Run(() =>
            {
                try
                {
                    _sessionOptions.Dispose();
                    _sessionOptions = null;
                }
#pragma warning disable CA1031 // 一般的な例外をキャッチしない
                catch (Exception ex)
                {
                    _logger.LogError(ex, "セッションオプション破棄中にエラーが発生しました");
                }
#pragma warning restore CA1031
            }).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// リソースの破棄
    /// </summary>
    /// <param name="disposing">マネージドリソースを破棄するかどうか</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            try
            {
                // 同期的にセッションを破棄
                _session?.Dispose();
                _sessionOptions?.Dispose();
            }
#pragma warning disable CA1031 // 一般的な例外をキャッチしない
            catch (Exception ex)
            {
                _logger.LogError(ex, "リソース破棄中にエラーが発生しました");
            }
#pragma warning restore CA1031

            _session = null;
            _sessionOptions = null;
            _disposed = true;
        }
    }
}
