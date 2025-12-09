using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.SafeHandles;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// Windows Job Objectを使用したプロセス管理ヘルパー
/// Issue #189: ゾンビプロセス対策 - 親プロセス終了時に子プロセスを自動終了
///
/// 機能:
/// - JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE: Jobハンドルがクローズされると関連プロセスを自動終了
/// - 親プロセスのクラッシュ時もOSレベルで子プロセスを確実に終了
/// </summary>
public sealed class ProcessJobObject : IDisposable
{
    private readonly ILogger? _logger;
    private SafeJobObjectHandle? _jobHandle;
    private bool _disposed;

    /// <summary>
    /// Job Objectが有効かどうか
    /// </summary>
    public bool IsValid => _jobHandle is { IsInvalid: false };

    /// <summary>
    /// Job Objectに関連付けられたプロセス数
    /// </summary>
    public int AssociatedProcessCount { get; private set; }

    public ProcessJobObject(ILogger? logger = null)
    {
        _logger = logger;
        Initialize();
    }

    /// <summary>
    /// Job Objectを初期化
    /// </summary>
    private void Initialize()
    {
        try
        {
            // Job Objectを作成（名前なし = このプロセス専用）
            _jobHandle = NativeMethods.CreateJobObject(IntPtr.Zero, null);

            if (_jobHandle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                _logger?.LogWarning("⚠️ [JobObject] 作成失敗: Win32Error={Error}", error);
                return;
            }

            // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE を設定
            // これにより、Jobハンドルがクローズ（プロセス終了含む）されると
            // Job内の全プロセスが自動終了される
            var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT.KILL_ON_JOB_CLOSE
                }
            };

            int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);

            try
            {
                Marshal.StructureToPtr(extendedInfo, extendedInfoPtr, false);

                if (!NativeMethods.SetInformationJobObject(
                    _jobHandle,
                    JOBOBJECTINFOCLASS.ExtendedLimitInformation,
                    extendedInfoPtr,
                    (uint)length))
                {
                    var error = Marshal.GetLastWin32Error();
                    _logger?.LogWarning("⚠️ [JobObject] SetInformationJobObject失敗: Win32Error={Error}", error);
                }
                else
                {
                    _logger?.LogDebug("✅ [JobObject] 初期化完了: KILL_ON_JOB_CLOSE設定済み");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(extendedInfoPtr);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "⚠️ [JobObject] 初期化エラー - Job Object機能は無効");
        }
    }

    /// <summary>
    /// プロセスをJob Objectに関連付け
    /// </summary>
    /// <param name="process">関連付けるプロセス</param>
    /// <returns>成功した場合はtrue</returns>
    public bool AssignProcess(Process process)
    {
        if (_disposed || _jobHandle == null || _jobHandle.IsInvalid)
        {
            _logger?.LogDebug("⚠️ [JobObject] Job Objectが無効なため、プロセス関連付けをスキップ");
            return false;
        }

        try
        {
            if (process.HasExited)
            {
                _logger?.LogDebug("⚠️ [JobObject] プロセス(PID:{PID})は既に終了しています", process.Id);
                return false;
            }

            if (!NativeMethods.AssignProcessToJobObject(_jobHandle, process.Handle))
            {
                var error = Marshal.GetLastWin32Error();

                // ERROR_ACCESS_DENIED (5): プロセスが既に別のJobに属している可能性
                // .NET Hostが作成したJobなど
                if (error == 5)
                {
                    _logger?.LogDebug("ℹ️ [JobObject] プロセス(PID:{PID})は既に別のJobに属しています（正常）", process.Id);
                    return false;
                }

                _logger?.LogWarning("⚠️ [JobObject] AssignProcessToJobObject失敗: PID={PID}, Win32Error={Error}",
                    process.Id, error);
                return false;
            }

            AssociatedProcessCount++;
            _logger?.LogInformation("✅ [JobObject] プロセス関連付け成功: PID={PID}, 関連プロセス数={Count}",
                process.Id, AssociatedProcessCount);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "⚠️ [JobObject] プロセス関連付けエラー: PID={PID}", process.Id);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _jobHandle?.Dispose();
            _logger?.LogDebug("✅ [JobObject] 破棄完了: 関連プロセス数={Count}", AssociatedProcessCount);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "⚠️ [JobObject] 破棄エラー");
        }
    }

    #region Native Methods & Structures

    /// <summary>
    /// SafeHandleラッパー for Job Object
    /// </summary>
    private sealed class SafeJobObjectHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeJobObjectHandle() : base(true) { }

        protected override bool ReleaseHandle()
        {
            return NativeMethods.CloseHandle(handle);
        }
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeJobObjectHandle CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetInformationJobObject(
            SafeJobObjectHandle hJob,
            JOBOBJECTINFOCLASS JobObjectInfoClass,
            IntPtr lpJobObjectInfo,
            uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AssignProcessToJobObject(SafeJobObjectHandle hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);
    }

    private enum JOBOBJECTINFOCLASS
    {
        BasicLimitInformation = 2,
        ExtendedLimitInformation = 9
    }

    [Flags]
    private enum JOB_OBJECT_LIMIT : uint
    {
        KILL_ON_JOB_CLOSE = 0x00002000
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public JOB_OBJECT_LIMIT LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    #endregion
}
