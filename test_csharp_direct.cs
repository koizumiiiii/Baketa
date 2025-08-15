using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        await TestDirectConnection();
    }

    static async Task<bool> TestDirectConnection()
    {
        TcpClient? client = null;
        NetworkStream? stream = null;
        StreamWriter? writer = null;
        StreamReader? reader = null;

        try
        {
            Console.WriteLine("🔗 TCP接続開始...");
            client = new TcpClient();
            await client.ConnectAsync("127.0.0.1", 5555);
            Console.WriteLine("✅ TCP接続成功");

            stream = client.GetStream();
            stream.ReadTimeout = 10000;
            stream.WriteTimeout = 10000;

            writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            reader = new StreamReader(stream, Encoding.UTF8);

            Console.WriteLine("📤 翻訳リクエスト送信中...");
            var request = new
            {
                text = "こんにちは",
                source_lang = "ja",
                target_lang = "en"
            };

            var jsonRequest = JsonSerializer.Serialize(request);
            Console.WriteLine($"🔍 Request: {jsonRequest}");
            
            await writer.WriteLineAsync(jsonRequest);
            Console.WriteLine("✅ 送信完了");

            Console.WriteLine("📥 レスポンス受信中...");
            var jsonResponse = await reader.ReadLineAsync();
            Console.WriteLine($"🔍 Response: {jsonResponse}");

            if (string.IsNullOrEmpty(jsonResponse))
            {
                Console.WriteLine("❌ 空のレスポンス");
                return false;
            }

            var response = JsonSerializer.Deserialize<JsonElement>(jsonResponse);
            Console.WriteLine($"✅ 翻訳成功: {response}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ エラー: {ex}");
            return false;
        }
        finally
        {
            writer?.Dispose();
            reader?.Dispose();
            stream?.Dispose();
            client?.Dispose();
            Console.WriteLine("🧹 リソース解放完了");
        }
    }
}