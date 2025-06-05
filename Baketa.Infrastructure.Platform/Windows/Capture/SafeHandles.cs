using System;
using System.Runtime.InteropServices;
using Baketa.Infrastructure.Platform.Windows.NativeMethods;

namespace Baketa.Infrastructure.Platform.Windows.Capture;

    /// <summary>
    /// デバイスコンテキストハンドルの安全な管理を行うクラス
    /// </summary>
    internal sealed class DeviceContextHandle : SafeHandle
    {
        public DeviceContextHandle(IntPtr hDC) : base(IntPtr.Zero, true)
        {
            SetHandle(hDC);
        }
        
        public override bool IsInvalid => handle == IntPtr.Zero;
        
        /// <summary>
        /// 内部ハンドルを取得（注意：安全でない操作）
        /// </summary>
        /// <returns>ハンドル値</returns>
        public new IntPtr DangerousGetHandle() => handle;
        
        protected override bool ReleaseHandle()
        {
            if (handle != IntPtr.Zero)
            {
                int result = User32Methods.ReleaseDC(IntPtr.Zero, handle);
                // リリース結果を使用（未使用変数の警告対策）
                return result != 0;
            }
            return true;
        }
    }
    
    /// <summary>
    /// ビットマップハンドルの安全な管理を行うクラス
    /// </summary>
    internal sealed class BitmapHandle : SafeHandle
    {
        public BitmapHandle(IntPtr hBitmap) : base(IntPtr.Zero, true)
        {
            SetHandle(hBitmap);
        }
        
        public override bool IsInvalid => handle == IntPtr.Zero;
        
        /// <summary>
        /// 内部ハンドルを取得（注意：安全でない操作）
        /// </summary>
        /// <returns>ハンドル値</returns>
        public new IntPtr DangerousGetHandle() => handle;
        
        protected override bool ReleaseHandle()
        {
            if (handle != IntPtr.Zero)
            {
                Gdi32Methods.DeleteObject(handle);
            }
            return true;
        }
    }
