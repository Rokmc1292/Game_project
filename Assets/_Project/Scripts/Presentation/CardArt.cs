using UnityEngine;

/// 카드/배경 스프라이트를 Resources에서 불러오는 헬퍼.
/// uGUI(Image.sprite)와 UI Toolkit(style.backgroundImage) 모두에서 쓸 수 있습니다.
///
/// 카드 스킨(보상)을 장착하면 Resources/Cosmetics/<스킨 id>/card_N 그림을 대신 씁니다.
/// 그림이 없는 스킨은 Tint 색조로만 표현합니다.
public static class CardArt
{
    // The equipped card skin's tint, used only when that skin has no art of its own. Every view
    // that shows a number card multiplies its card image by this.
    public static Color Tint = Color.white;

    static string _skinFolder;   // "Cosmetics/<skin id>" when the equipped skin has art, else null

    public static void SetSkin(string skinId, Color fallbackTint)
    {
        _skinFolder = null;
        Tint = fallbackTint;
        if (string.IsNullOrEmpty(skinId)) return;
        string folder = $"Cosmetics/{skinId}";
        if (Resources.Load<Sprite>($"{folder}/card_0") == null) return;
        _skinFolder = folder;
        Tint = Color.white;
    }

    public static Sprite Digit(int n)
    {
        if (n < 0 || n > 9) { Debug.LogWarning($"CardArt: 0~9 범위가 아닙니다 ({n})"); return null; }
        return Card($"card_{n}");
    }
    public static Sprite Back()       => Card("card_back");
    public static Sprite Unknown()    => Card("card_unknown");
    public static Sprite Background() => Load("Backgrounds/background_underground");

    // A background skin's art (Resources/Cosmetics/<id>), or null when it has none.
    public static Sprite BackgroundSkin(string skinId) =>
        string.IsNullOrEmpty(skinId) ? null : Resources.Load<Sprite>($"Cosmetics/{skinId}");

    static Sprite Card(string name)
    {
        if (_skinFolder != null)
        {
            var skinned = Resources.Load<Sprite>($"{_skinFolder}/{name}");
            if (skinned != null) return skinned;
        }
        return Load($"Cards/{name}");
    }

    static Sprite Load(string path)
    {
        var s = Resources.Load<Sprite>(path);
        if (s == null) Debug.LogWarning("CardArt: 스프라이트를 찾을 수 없음 Resources/" + path);
        return s;
    }
}
