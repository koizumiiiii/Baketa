#!/usr/bin/env python3
"""
HWND 0x1000C のウィンドウキャプチャ失敗原因分析
Windows Graphics Capture API の制限要因を特定
"""

import ctypes
import ctypes.wintypes
import sys

# User32.dll関数
user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32

def get_window_info(hwnd):
    """指定されたHWNDのウィンドウ情報を取得"""
    if not user32.IsWindow(hwnd):
        return None
    
    # ウィンドウクラス名取得
    class_name = ctypes.create_unicode_buffer(256)
    user32.GetClassNameW(hwnd, class_name, 256)
    
    # ウィンドウタイトル取得
    window_title = ctypes.create_unicode_buffer(512)
    user32.GetWindowTextW(hwnd, window_title, 512)
    
    # プロセスID取得
    process_id = ctypes.wintypes.DWORD()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(process_id))
    
    # プロセス名取得
    process_handle = kernel32.OpenProcess(0x0410, False, process_id.value)
    process_name = 'Unknown'
    if process_handle:
        try:
            name_buffer = ctypes.create_unicode_buffer(256)
            size = ctypes.wintypes.DWORD(256)
            if kernel32.QueryFullProcessImageNameW(process_handle, 0, name_buffer, ctypes.byref(size)):
                process_name = name_buffer.value.split('\\')[-1]
        except:
            pass
        finally:
            kernel32.CloseHandle(process_handle)
    
    # ウィンドウ状態確認
    is_visible = user32.IsWindowVisible(hwnd)
    is_minimized = user32.IsIconic(hwnd)
    is_maximized = user32.IsZoomed(hwnd)
    
    # Extended Window Styles取得
    ex_style = user32.GetWindowLongW(hwnd, -20)  # GWL_EXSTYLE
    
    # Window Styles取得  
    style = user32.GetWindowLongW(hwnd, -16)  # GWL_STYLE
    
    return {
        'hwnd': f'0x{hwnd:08X}',
        'class_name': class_name.value,
        'title': window_title.value,
        'process_id': process_id.value,
        'process_name': process_name,
        'is_visible': is_visible,
        'is_minimized': is_minimized,
        'is_maximized': is_maximized,
        'ex_style': f'0x{ex_style:08X}',
        'style': f'0x{style:08X}',
        'has_layered': bool(ex_style & 0x80000),  # WS_EX_LAYERED
        'has_toolwindow': bool(ex_style & 0x80),  # WS_EX_TOOLWINDOW
        'has_topmost': bool(ex_style & 0x8),      # WS_EX_TOPMOST
        'has_transparent': bool(ex_style & 0x20), # WS_EX_TRANSPARENT
        'has_noredirectionbitmap': bool(ex_style & 0x200000),  # WS_EX_NOREDIRECTIONBITMAP
    }

def analyze_capture_restrictions(info):
    """Windows Graphics Capture API制限要因の分析"""
    restrictions = []
    
    if not info['is_visible']:
        restrictions.append("❌ ウィンドウが非表示 - キャプチャ不可")
    elif info['is_minimized']:
        restrictions.append("❌ ウィンドウが最小化 - キャプチャ不可")
    
    if info['has_layered']:
        restrictions.append("⚠️ レイヤードウィンドウ - キャプチャ制限あり")
    
    if info['has_transparent']:
        restrictions.append("⚠️ 透明ウィンドウ - キャプチャ制限あり")
    
    if info['has_toolwindow']:
        restrictions.append("⚠️ ツールウィンドウ - キャプチャ制限あり")
    
    if info['has_noredirectionbitmap']:
        restrictions.append("❌ WS_EX_NOREDIRECTIONBITMAP設定 - DWM合成対象外")
    
    # システムプロセス確認
    system_processes = ['dwm.exe', 'winlogon.exe', 'csrss.exe', 'lsass.exe', 'services.exe']
    if info['process_name'].lower() in system_processes:
        restrictions.append("❌ システムプロセス - セキュリティ制限によりキャプチャ不可")
    
    # セキュリティ関連ウィンドウ確認
    security_keywords = ['secure', 'uac', 'credential', 'authentication', 'login']
    title_lower = info['title'].lower()
    if any(keyword in title_lower for keyword in security_keywords):
        restrictions.append("❌ セキュリティ関連ウィンドウ - キャプチャ不可")
    
    # 特殊クラス名確認
    special_classes = ['#32770', 'Button', 'Static', 'Edit', 'ComboBox']  # ダイアログやコントロール
    if info['class_name'] in special_classes:
        restrictions.append("⚠️ システムダイアログ/コントロール - キャプチャ制限あり")
    
    return restrictions

def main():
    # 問題のHWND 0x1000Cを調査
    target_hwnd = 0x1000C
    print(f"分析対象: HWND {target_hwnd:#08X}")
    print("=" * 50)
    
    info = get_window_info(target_hwnd)
    
    if info:
        print("ウィンドウ情報:")
        for key, value in info.items():
            print(f"  {key}: {value}")
        
        print()
        print("Windows Graphics Capture API制限要因分析:")
        restrictions = analyze_capture_restrictions(info)
        
        if restrictions:
            for restriction in restrictions:
                print(f"  {restriction}")
        else:
            print("  ✅ 一般的な制限要因は見当たらず")
            print("  🔍 より詳細な調査が必要:")
            print("    - プロセス権限・整合性レベル")
            print("    - DPI設定・スケーリング")  
            print("    - ウィンドウ描画方式（GDI vs DirectX）")
            print("    - アンチチート/保護ソフトウェア")
            
        # 追加の技術情報
        print()
        print("技術情報:")
        if info['has_layered']:
            print("  - レイヤードウィンドウは透明効果や合成処理のためキャプチャが困難")
        if info['has_noredirectionbitmap']:
            print("  - WS_EX_NOREDIRECTIONBITMAP: DWM合成から除外、キャプチャ不可")
        if info['process_name'] == 'dwm.exe':
            print("  - Desktop Window Manager: システムコア、キャプチャ不可")
            
    else:
        print("❌ 指定されたHWNDは無効または存在しないウィンドウです")
        print("  - ウィンドウが既に閉じられた可能性")
        print("  - 一時的なウィンドウ（メニュー、ポップアップ等）")

if __name__ == "__main__":
    main()