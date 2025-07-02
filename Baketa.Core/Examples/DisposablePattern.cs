using System;
using Baketa.Core.Common;

namespace Baketa.Core.Examples;

    /// <summary>
    /// IDisposableパターンの適切な実装例
    /// </summary>
    /// <remarks>
    /// この実装例はCA1063警告に対応しており、
    /// DisposableBaseクラスを継承した正しいDisposeパターンを示しています。
    /// </remarks>
    public class DisposableResourceExample : DisposableBase
    {
        // マネージドリソースの例 (IDisposableを実装するオブジェクト)
        private readonly IDisposable _managedResource;
        
        // アンマネージドリソースの例 (IntPtrや安全でないリソース)
        private IntPtr _unmanagedHandle;
        
        public DisposableResourceExample(IDisposable managedResource)
        {
            _managedResource = managedResource ?? throw new ArgumentNullException(nameof(managedResource));
            _unmanagedHandle = IntPtr.Zero; // 初期化例
        }
        
        /// <summary>
        /// マネージドリソースを解放します
        /// </summary>
        protected override void DisposeManagedResources()
        {
            // マネージドリソースの解放
            _managedResource?.Dispose();
        }
        
        /// <summary>
        /// アンマネージドリソースを解放します
        /// </summary>
        protected override void DisposeUnmanagedResources()
        {
            // アンマネージドリソースの解放（disposing=true/falseの両方で実行）
            if (_unmanagedHandle != IntPtr.Zero)
            {
                // アンマネージドリソースの解放ロジック
                // 例: Win32 CloseHandle()の呼び出しなど
                _unmanagedHandle = IntPtr.Zero;
            }
        }
        
        /// <summary>
        /// 破棄されたオブジェクトの使用からユーザーを保護するヘルパーメソッド
        /// </summary>
        protected new void ThrowIfDisposed()
        {
            // 基底クラスのメソッドを再利用
            base.ThrowIfDisposed();
        }
        
        // オブジェクトの使用例
        public void DoSomething()
        {
            // メソッド開始時に破棄チェック
            ThrowIfDisposed();
            
            // リソースを使用するロジック
        }
    }
