using System.Threading.Tasks;

namespace Baketa.Core.Examples
{
    /// <summary>
    /// 非同期プログラミングのベストプラクティス実装例
    /// </summary>
    internal static class AsyncBestPractices
    {
        /// <summary>
        /// ConfigureAwait(false)を使用した正しい非同期実装例
        /// </summary>
        /// <remarks>
        /// ConfigureAwait(false)を使用すると、続行を元の同期コンテキストに戻す必要がなくなります。
        /// これにより、UIスレッドのデッドロックを回避し、パフォーマンスを向上させることができます。
        /// </remarks>
        public static async Task<byte[]> ProcessImageAsync(byte[] sourceData)
        {
            // 非同期処理を行う際に ConfigureAwait(false) を使用
            var processedData = await Task.Run(() => ProcessData(sourceData)).ConfigureAwait(false);
            
            // さらに別の非同期処理
            var optimizedData = await OptimizeDataAsync(processedData).ConfigureAwait(false);
            
            return optimizedData;
        }
        
        /// <summary>
        /// 複数の非同期タスクを並行実行する例
        /// </summary>
        public static async Task<(byte[] first, byte[] second)> ProcessMultipleImagesAsync(byte[] source1, byte[] source2)
        {
            // 複数のタスクを並行して開始
            var task1 = ProcessImageAsync(source1);
            var task2 = ProcessImageAsync(source2);
            
            // 両方のタスクが完了するのを待機
            // ConfigureAwait(false)は下位タスクですでに適用されているため、
            // ここでは明示的に指定する必要はないが、理解を促進するために記述
            await Task.WhenAll(task1, task2).ConfigureAwait(false);
            
            // 結果を返す
            return (await task1.ConfigureAwait(false), await task2.ConfigureAwait(false));
        }
        
        // ヘルパーメソッド
        private static byte[] ProcessData(byte[] data)
        {
            // 実際の処理ロジック
            return data;
        }
        
        private static Task<byte[]> OptimizeDataAsync(byte[] data)
        {
            // 非同期最適化処理
            return Task.FromResult(data);
        }
    }
}
