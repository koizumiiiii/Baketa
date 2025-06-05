using System.Collections.Generic;
using System.Threading.Tasks;

namespace Baketa.Core.Translation.Models;

/// <summary>
/// モデルローダーのインターフェース
/// </summary>
public interface IModelLoader
{
    /// <summary>
    /// モデルをロードする
    /// </summary>
    /// <param name="modelPath">モデルファイルのパス</param>
    /// <param name="options">モデルオプション</param>
    /// <returns>ロードが成功したかどうか</returns>
    Task<bool> LoadModelAsync(string modelPath, ModelOptions? options = null);
    
    /// <summary>
    /// モデルがロード済みかどうかを確認する
    /// </summary>
    /// <returns>ロード済みの場合はtrue</returns>
    bool IsModelLoaded();
    
    /// <summary>
    /// モデルをアンロードする
    /// </summary>
    /// <returns>アンロードが成功したかどうか</returns>
    Task<bool> UnloadModelAsync();
    
    /// <summary>
    /// 利用可能なデバイスを取得する
    /// </summary>
    /// <returns>利用可能なデバイスのリスト</returns>
    Task<IReadOnlyList<ComputeDevice>> GetAvailableDevicesAsync();
    
    /// <summary>
    /// 使用するデバイスを設定する
    /// </summary>
    /// <param name="device">使用するデバイス</param>
    /// <returns>設定が成功したかどうか</returns>
    Task<bool> SetDeviceAsync(ComputeDevice device);
    
    /// <summary>
    /// 現在使用中のデバイスを取得する
    /// </summary>
    /// <returns>使用中のデバイス</returns>
    ComputeDevice GetCurrentDevice();
}

/// <summary>
/// モデルオプション
/// </summary>
public class ModelOptions
{
    /// <summary>
    /// 最大シーケンス長
    /// </summary>
    public int MaxSequenceLength { get; set; } = 512;
    
    /// <summary>
    /// スレッド数（0=自動）
    /// </summary>
    public int ThreadCount { get; set; }
    
    /// <summary>
    /// グラフ最適化レベル
    /// </summary>
    public int OptimizationLevel { get; set; } = 3;
    
    /// <summary>
    /// 使用するメモリ量（MB、0=自動）
    /// </summary>
    public int MemoryLimit { get; set; }
    
    /// <summary>
    /// キャッシュを有効にするかどうか
    /// </summary>
    public bool EnableCache { get; set; } = true;
    
    /// <summary>
    /// バッチサイズ
    /// </summary>
    public int BatchSize { get; set; } = 1;
    
    /// <summary>
    /// モデル固有のオプション
    /// </summary>
    public Dictionary<string, object?> CustomOptions { get; } = [];
}