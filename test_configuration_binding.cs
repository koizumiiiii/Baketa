using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Baketa.Core.Settings;
using System;
using System.IO;

namespace ConfigTest
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Configuration Binding Test");
            
            // 設定ファイルの読み込み
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
            
            // DIコンテナの構成
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            
            // 設定のバインディング
            services.Configure<AppSettings>(configuration);
            
            var serviceProvider = services.BuildServiceProvider();
            
            // 設定を取得
            var appSettingsOptions = serviceProvider.GetService<IOptions<AppSettings>>();
            var appSettings = appSettingsOptions?.Value;
            
            Console.WriteLine("=== Configuration Test Results ===");
            
            if (appSettings?.Translation?.Languages != null)
            {
                Console.WriteLine($"DefaultSourceLanguage: '{appSettings.Translation.Languages.DefaultSourceLanguage}'");
                Console.WriteLine($"DefaultTargetLanguage: '{appSettings.Translation.Languages.DefaultTargetLanguage}'");
                Console.WriteLine($"AutoDetectSourceLanguage: {appSettings.Translation.AutoDetectSourceLanguage}");
                
                var direction = $"{appSettings.Translation.Languages.DefaultSourceLanguage} -> {appSettings.Translation.Languages.DefaultTargetLanguage}";
                Console.WriteLine($"Direction: {direction}");
                
                if (appSettings.Translation.Languages.DefaultSourceLanguage == "en" && 
                    appSettings.Translation.Languages.DefaultTargetLanguage == "ja")
                {
                    Console.WriteLine("✅ CORRECT: en->ja direction");
                }
                else if (appSettings.Translation.Languages.DefaultSourceLanguage == "ja" && 
                         appSettings.Translation.Languages.DefaultTargetLanguage == "en")
                {
                    Console.WriteLine("❌ ERROR: ja->en direction (reversed)");
                }
                else
                {
                    Console.WriteLine($"🔍 CUSTOM: {direction}");
                }
            }
            else
            {
                Console.WriteLine("❌ ERROR: Translation settings not loaded properly");
                Console.WriteLine($"AppSettings null: {appSettings == null}");
                Console.WriteLine($"Translation null: {appSettings?.Translation == null}");
                Console.WriteLine($"Languages null: {appSettings?.Translation?.Languages == null}");
            }
        }
    }
}