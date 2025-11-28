// Supabase接続テストスクリプト
// 実行: dotnet script test_supabase_connection.csx

#r "nuget: supabase-csharp, 0.16.2"

using Supabase;
using System;
using System.Threading.Tasks;

var supabaseUrl = "https://kajsoietcikivrwidqcs.supabase.co";
var supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImthanNvaWV0Y2lraXZyd2lkcWNzIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NjQwOTg3NTcsImV4cCI6MjA3OTY3NDc1N30.tVHVdX52liyAOmI18ub_tZp4D-rxCrvvrKVoCAlyPwU";

Console.WriteLine("🔌 Supabase接続テスト開始...");
Console.WriteLine($"   URL: {supabaseUrl}");

try
{
    var options = new SupabaseOptions
    {
        AutoConnectRealtime = false,
        AutoRefreshToken = true
    };

    var client = new Client(supabaseUrl, supabaseKey, options);

    Console.WriteLine("✅ Supabaseクライアント作成成功");

    // Auth設定確認
    var settings = await client.Auth.Settings();
    Console.WriteLine($"✅ Auth Settings取得成功");
    Console.WriteLine($"   - External Providers: Google={settings?.ExternalProviders?.Google ?? false}");

    Console.WriteLine("\n🎉 Supabase接続テスト完了！");
}
catch (Exception ex)
{
    Console.WriteLine($"❌ エラー: {ex.Message}");
    Console.WriteLine($"   詳細: {ex.InnerException?.Message ?? "なし"}");
}
