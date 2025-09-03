using System;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("🔥 TEST: Console.WriteLine出力テスト開始");
        System.Console.WriteLine("🔥 TEST: System.Console.WriteLine出力テスト開始");
        System.Diagnostics.Debug.WriteLine("🔥 TEST: Debug.WriteLine出力テスト開始");
        
        Console.WriteLine("これがコンソールに表示されるはずです");
        Console.WriteLine("テスト終了");
    }
}