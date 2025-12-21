using System.Runtime.InteropServices;

namespace Baketa.Infrastructure.Platform.Windows.GPU;

/// <summary>
/// [Issue #222] BaketaCaptureNative.dll DXGI GPU検出 P/Invoke インターフェース
/// WMI（System.Management）からの移行 - IL Trimming対応、高速、正確なVRAM
/// </summary>
public static class NativeDxgiGpuDetector
{
    private const string DllName = "BaketaCaptureNative.dll";

    /// <summary>
    /// GPU情報構造体（C++側のDxgiGpuInfoと一致）
    /// [Gemini Review] bool → byte に変更（マーシャリングの堅牢性向上）
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DxgiGpuInfo
    {
        /// <summary>GPU名（最大128文字）</summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;

        /// <summary>ベンダーID (NVIDIA=0x10DE, AMD=0x1002, Intel=0x8086)</summary>
        public uint VendorId;

        /// <summary>デバイスID</summary>
        public uint DeviceId;

        /// <summary>専用VRAM (bytes)</summary>
        public ulong DedicatedVideoMemory;

        /// <summary>専用システムメモリ (bytes)</summary>
        public ulong DedicatedSystemMemory;

        /// <summary>共有システムメモリ (bytes)</summary>
        public ulong SharedSystemMemory;

        /// <summary>D3D Feature Level (0xc000=12.0, 0xc100=12.1, etc.)</summary>
        public uint FeatureLevel;

        /// <summary>統合GPU判定 (0=false, 1=true)</summary>
        private byte _isIntegrated;

        /// <summary>情報が有効か (0=false, 1=true)</summary>
        private byte _isValid;

        /// <summary>明示的パディング（C++側と一致）</summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        private byte[] _padding;

        /// <summary>統合GPUかどうか</summary>
        public bool IsIntegrated => _isIntegrated != 0;

        /// <summary>情報が有効かどうか</summary>
        public bool IsValid => _isValid != 0;
    }

    /// <summary>
    /// プライマリGPU情報を取得
    /// 専用GPUがあれば専用GPU、なければ統合GPUの情報を返す
    /// </summary>
    /// <param name="outInfo">GPU情報（出力）</param>
    /// <returns>成功時はtrue</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "Public API for platform integration")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool GetPrimaryGpuInfo(out DxgiGpuInfo outInfo);

    /// <summary>
    /// 全GPU情報を取得
    /// </summary>
    /// <param name="outInfos">GPU情報配列（呼び出し側で確保）</param>
    /// <param name="maxCount">配列の最大要素数</param>
    /// <returns>実際に取得したGPU数</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "Public API for platform integration")]
    public static extern int GetAllGpuInfos(
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] DxgiGpuInfo[] outInfos,
        int maxCount);

    /// <summary>
    /// DirectX Feature Levelを取得（D3D12優先、D3D11フォールバック）
    /// </summary>
    /// <returns>Feature Level値（例: 0xc100 = D3D_FEATURE_LEVEL_12_1）</returns>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1401:P/Invokes should not be visible", Justification = "Public API for platform integration")]
    public static extern uint GetDirectXFeatureLevelDxgi();

    /// <summary>
    /// 既知のGPUベンダーID
    /// </summary>
    public static class VendorIds
    {
        public const uint Nvidia = 0x10DE;
        public const uint Amd = 0x1002;
        public const uint Intel = 0x8086;
        public const uint Microsoft = 0x1414;
    }
}
