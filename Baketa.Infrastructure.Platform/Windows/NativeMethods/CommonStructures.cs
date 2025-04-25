using System.Runtime.InteropServices;

namespace Baketa.Infrastructure.Platform.Windows.NativeMethods
{
    /// <summary>
    /// RECT構造体
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct RECT
    {
        public readonly int left;
        public readonly int top;
        public readonly int right;
        public readonly int bottom;
        
        // コンストラクタを追加してreadonlyメンバーを初期化できるようにする
        public RECT(int left, int top, int right, int bottom)
        {
            this.left = left;
            this.top = top;
            this.right = right;
            this.bottom = bottom;
        }
        
        public int Width => right - left;
        public int Height => bottom - top;
        
        public override string ToString() => $"[{left}, {top}, {right}, {bottom}]";
    }
    
    /// <summary>
    /// POINT構造体
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct POINT
    {
        public readonly int X;
        public readonly int Y;
        
        public POINT(int x, int y)
        {
            X = x;
            Y = y;
        }
        
        public override string ToString() => $"({X}, {Y})";
    }
    
    /// <summary>
    /// SIZE構造体
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct SIZE
    {
        public readonly int Width;
        public readonly int Height;
        
        public SIZE(int width, int height)
        {
            Width = width;
            Height = height;
        }
        
        public override string ToString() => $"{Width}x{Height}";
    }
}
