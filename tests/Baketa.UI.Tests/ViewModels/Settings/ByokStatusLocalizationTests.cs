using System.Globalization;
using Baketa.UI.Resources;
using Xunit;

namespace Baketa.UI.Tests.ViewModels.Settings;

// Other resource tests mutate Strings.Culture; status assertions must not overlap them.
[CollectionDefinition("BYOK status localization", DisableParallelization = true)]
public sealed class ByokStatusLocalizationDefinition
{
}

[Collection("BYOK status localization")]
public sealed class ByokStatusLocalizationTests
{
    [Theory]
    [InlineData("en", "No key saved.", "✓ Key saved (connection verified).")]
    [InlineData("ja", "キー未設定。", "✓ キー保存済み（接続を確認しました）。")]
    [InlineData("de", "Kein Schlüssel gespeichert.", "✓ Schlüssel gespeichert (Verbindung bestätigt).")]
    [InlineData("es", "No hay ninguna clave guardada.", "✓ Clave guardada (conexión verificada).")]
    [InlineData("fr", "Aucune clé enregistrée.", "✓ Clé enregistrée (connexion vérifiée).")]
    [InlineData("it", "Nessuna chiave salvata.", "✓ Chiave salvata (connessione verificata).")]
    [InlineData("ko", "저장된 키가 없습니다.", "✓ 키가 저장되었습니다(연결 확인 완료).")]
    [InlineData("pt", "Nenhuma chave salva.", "✓ Chave salva (conexão verificada).")]
    [InlineData("zh-CN", "未保存密钥。", "✓ 密钥已保存（连接已验证）。")]
    [InlineData("zh-TW", "尚未儲存金鑰。", "✓ 金鑰已儲存（連線已驗證）。")]
    public void KeyStatus_AllLanguages_DescribesKeyWithoutSelectingMethod(
        string language, string noKey, string saved)
    {
        // Use an explicit culture without mutating shared localization state.
        var culture = CultureInfo.GetCultureInfo(language);
        var resources = Strings.ResourceManager.GetResourceSet(culture, true, true);
        Assert.NotNull(resources);
        foreach (var provider in new[] { "Google", "Gemini" })
        {
            Assert.Equal(noKey, resources.GetString($"{provider}_NoKey"));
            Assert.Equal(saved, resources.GetString($"{provider}_TestSuccess"));
        }
    }
}
