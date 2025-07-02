using System;

namespace Baketa.Core.Common;

    /// <summary>
    /// IDisposableパターンを正しく実装するための基底クラス
    /// </summary>
    public abstract class DisposableBase : IDisposable
    {
        /// <summary>
        /// オブジェクトが破棄されたかどうかを示すフラグ
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// オブジェクトが破棄されたかどうかを取得します
        /// </summary>
        /// <returns>破棄されている場合はtrue</returns>
        protected bool IsDisposed() => _disposed;

        /// <summary>
        /// マネージドリソースとアンマネージドリソースの両方を解放します
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// リソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースも解放する場合はtrue</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                // マネージドリソースの解放
                DisposeManagedResources();
            }

            // アンマネージドリソースの解放
            DisposeUnmanagedResources();

            _disposed = true;
        }

        /// <summary>
        /// マネージドリソースを解放します
        /// </summary>
        protected virtual void DisposeManagedResources() { }

        /// <summary>
        /// アンマネージドリソースを解放します
        /// </summary>
        protected virtual void DisposeUnmanagedResources() { }

        /// <summary>
        /// オブジェクトが破棄されている場合に例外をスローします
        /// </summary>
        /// <exception cref="ObjectDisposedException">オブジェクトが破棄されている場合</exception>
        protected void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, GetType().Name);
        }

        /// <summary>
        /// ファイナライザー
        /// </summary>
        ~DisposableBase()
        {
            Dispose(false);
        }
    }
