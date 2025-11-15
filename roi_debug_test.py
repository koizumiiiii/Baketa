#!/usr/bin/env python3
"""
ROI画像保存デバッグテスト
AdaptiveTileStrategyの設定状況を確認
"""
import sys
import json

def analyze_roi_settings():
    print("ROI画像保存デバッグテスト")
    print("=" * 50)
    
    # appsettings.json の設定確認
    settings_path = "E:/dev/Baketa/Baketa.UI/bin/Debug/net8.0-windows10.0.19041.0/appsettings.json"
    dev_settings_path = "E:/dev/Baketa/Baketa.UI/bin/Debug/net8.0-windows10.0.19041.0/appsettings.Development.json"
    
    print("1. 設定ファイル確認:")
    try:
        with open(settings_path, 'r', encoding='utf-8') as f:
            config = json.load(f)
        
        print(f"OK appsettings.json読み込み成功")
        
        # AdvancedSettings関連の検索
        def find_nested_key(obj, key, path=""):
            """ネストしたJSONから特定のキーを検索"""
            results = []
            if isinstance(obj, dict):
                for k, v in obj.items():
                    current_path = f"{path}.{k}" if path else k
                    if k == key:
                        results.append((current_path, v))
                    elif isinstance(v, (dict, list)):
                        results.extend(find_nested_key(v, key, current_path))
            elif isinstance(obj, list):
                for i, item in enumerate(obj):
                    current_path = f"{path}[{i}]"
                    results.extend(find_nested_key(item, key, current_path))
            return results
        
        # ROI関連設定の検索
        roi_keys = ["EnableRoiImageOutput", "RoiImageOutputPath", "RoiImageFormat"]
        advanced_keys = ["EnableAdvanced", "AdvancedSettings"]
        
        print("2. ROI関連設定:")
        for key in roi_keys + advanced_keys:
            results = find_nested_key(config, key)
            if results:
                for path, value in results:
                    print(f"   {path}: {value}")
            else:
                print(f"   {key}: 見つかりません")
        
        # Development設定も確認
        print("\n3. Development設定:")
        try:
            with open(dev_settings_path, 'r', encoding='utf-8') as f:
                dev_config = json.load(f)
            print(f"✅ appsettings.Development.json読み込み成功")
            
            for key in roi_keys + advanced_keys:
                results = find_nested_key(dev_config, key)
                if results:
                    for path, value in results:
                        print(f"   {path}: {value}")
                else:
                    print(f"   {key}: 見つかりません")
        except Exception as e:
            print(f"❌ Development設定読み込み失敗: {e}")
            
    except Exception as e:
        print(f"❌ 設定ファイル読み込み失敗: {e}")
    
    # デフォルト値の確認
    print("\n4. デフォルト値（AdvancedSettings.cs）:")
    print("   EnableRoiImageOutput: true (デフォルト)")
    print("   RoiImageOutputPath: Documents/Baketa/ROI (デフォルト)")
    print("   RoiImageFormat: PNG (デフォルト)")
    
    print("\n5. 予想される問題:")
    print("   - appsettings.jsonにAdvancedSettings設定がない")
    print("   - EnableRoiImageOutput設定が明示的にfalseになっている")  
    print("   - ImageDiagnosticsSaverの注入問題")
    print("   - AdaptiveTileStrategyが実際に実行されていない")

if __name__ == "__main__":
    analyze_roi_settings()