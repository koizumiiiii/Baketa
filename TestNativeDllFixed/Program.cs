using System;
using System.Runtime.InteropServices;

// Fixed test to get detailed HRESULT error messages
class TestNativeDllFixed 
{
    private const string DllName = "BaketaCaptureNative.dll";
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int BaketaCapture_Initialize();
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int BaketaCapture_IsSupported();
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int BaketaCapture_CreateSession([In] IntPtr hwnd, [Out] out int sessionId);
    
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int BaketaCapture_GetLastError([Out] IntPtr buffer, int bufferSize);
    
    static void Main()
    {
        Console.WriteLine("=== FIXED BaketaCaptureNative.dll Test with Detailed HRESULT ===");
        
        try
        {
            // Step 1: Initialize
            Console.WriteLine("Step 1: Calling BaketaCapture_Initialize()...");
            int initResult = BaketaCapture_Initialize();
            Console.WriteLine($"Initialize result: {initResult}");
            
            if (initResult != 0)
            {
                Console.WriteLine("❌ Initialize failed!");
                GetAndPrintLastError();
                return;
            }
            
            // Step 2: Check Support
            Console.WriteLine("Step 2: Calling BaketaCapture_IsSupported()...");
            int supportResult = BaketaCapture_IsSupported();
            Console.WriteLine($"IsSupported result: {supportResult}");
            
            if (supportResult != 1)
            {
                Console.WriteLine("❌ Not supported!");
                GetAndPrintLastError();
                return;
            }
            
            // Step 3: Test CreateSession with desktop window
            Console.WriteLine("Step 3: Calling BaketaCapture_CreateSession()...");
            IntPtr desktopWindow = GetDesktopWindow();
            Console.WriteLine($"Testing with desktop window handle: 0x{desktopWindow.ToInt64():X}");
            
            int sessionId;
            int createResult = BaketaCapture_CreateSession(desktopWindow, out sessionId);
            Console.WriteLine($"CreateSession result: {createResult} (0x{createResult:X8}), SessionId: {sessionId}");
            
            if (createResult != 0)
            {
                Console.WriteLine("❌ CreateSession failed! **DETAILED ERROR BELOW**");
                Console.WriteLine($"🔍 Numeric Error Code Analysis:");
                Console.WriteLine($"  - Decimal: {createResult}");
                Console.WriteLine($"  - Hexadecimal: 0x{createResult:X8}");
                Console.WriteLine($"  - As HRESULT: {(createResult < 0 ? "FAILED" : "SUCCESS")}");
                GetAndPrintLastError();
                return;
            }
            
            Console.WriteLine("✅ All tests passed!");
        }
        catch (DllNotFoundException ex)
        {
            Console.WriteLine($"❌ DLL not found: {ex.Message}");
        }
        catch (EntryPointNotFoundException ex)
        {
            Console.WriteLine($"❌ Entry point not found: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Exception: {ex.GetType().Name}: {ex.Message}");
        }
    }
    
    static void GetAndPrintLastError()
    {
        try
        {
            const int bufferSize = 1024;
            IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                int length = BaketaCapture_GetLastError(buffer, bufferSize);
                if (length > 0)
                {
                    string errorMsg = Marshal.PtrToStringAnsi(buffer, Math.Min(length, bufferSize - 1));
                    Console.WriteLine($"🔍 DETAILED ERROR: {errorMsg}");
                }
                else
                {
                    Console.WriteLine("No error message available");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get error message: {ex.Message}");
        }
    }
    
    [DllImport("user32.dll")]
    static extern IntPtr GetDesktopWindow();
}
