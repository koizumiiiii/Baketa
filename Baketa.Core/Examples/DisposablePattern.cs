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
    public class DisposableResourceExample(IDisposable managedResource) : DisposableBase
    {
        // マネージドリソースの例 (IDisposableを実装するオブジェクト)
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA2213:Disposable fields should be disposed", Justification = "DisposeManagedResourcesでDisposeされています")]
        private readonly IDisposable _managedResource = managedResource ?? throw new ArgumentNullException(nameof(managedResource));
        
        // アンマネージドリソースの例 (IntPtrや安全でないリソース)
        private IntPtr _unmanagedHandle = IntPtr.Zero;

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
