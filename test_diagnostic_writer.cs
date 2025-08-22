using System;
using System.IO;
using Baketa.Core.Utilities;

namespace DiagnosticTest;

public class TestDiagnosticWriter
{
    public static void TestSafeFileWriter()
    {
        Console.WriteLine("🧪 SafeFileWriter直接テスト開始");
        
        // DiagnosticReportGeneratorと同じパスを使用
        var reportsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "Baketa", "Reports");
        
        Console.WriteLine($"📁 Reports ディレクトリ: {reportsDirectory}");
        Console.WriteLine($"📁 ディレクトリ存在: {Directory.Exists(reportsDirectory)}");
        
        var testFileName = $"test_report_{DateTime.Now:yyyyMMdd_HHmmss}.json";
        var testFilePath = Path.Combine(reportsDirectory, testFileName);
        
        Console.WriteLine($"📄 テストファイルパス: {testFilePath}");
        
        var testContent = "{\n  \"test\": \"SafeFileWriter Test\",\n  \"timestamp\": \"" + DateTime.UtcNow.ToString("O") + "\"\n}";
        
        Console.WriteLine("🔄 SafeFileWriter.AppendTextSafely実行中...");
        
        try
        {
            SafeFileWriter.AppendTextSafely(testFilePath, testContent);
            Console.WriteLine("✅ SafeFileWriter.AppendTextSafely完了");
            
            // ファイル存在確認
            if (File.Exists(testFilePath))
            {
                Console.WriteLine("✅ ファイル作成成功");
                var fileInfo = new FileInfo(testFilePath);
                Console.WriteLine($"📏 ファイルサイズ: {fileInfo.Length} bytes");
                
                // ファイル内容確認
                var content = File.ReadAllText(testFilePath);
                Console.WriteLine($"📖 ファイル内容: {content.Substring(0, Math.Min(100, content.Length))}...");
            }
            else
            {
                Console.WriteLine("❌ ファイル作成失敗 - ファイルが存在しません");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 例外発生: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"❌ スタックトレース: {ex.StackTrace}");
        }
        
        Console.WriteLine("🧪 SafeFileWriter直接テスト完了");
    }
}