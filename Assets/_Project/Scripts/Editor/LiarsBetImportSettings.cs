#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// Resources/Cards, Backgrounds, Buttons, Hud 안의 PNG를 가져올 때 자동으로 Sprite 설정을 적용합니다.
public class LiarsBetImportSettings : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        bool isCard = assetPath.Contains("/Resources/Cards/");
        bool isBackground = assetPath.Contains("/Resources/Backgrounds/");
        bool isButton = assetPath.Contains("/Resources/Buttons/");
        bool isHud = assetPath.Contains("/Resources/Hud/");
        bool isChoice = assetPath.Contains("/Resources/ChoiceButtons/");
        bool isResult = assetPath.Contains("/Resources/Result/");
        if (!isCard && !isBackground && !isButton && !isHud && !isChoice && !isResult) return;

        var imp = (TextureImporter)assetImporter;
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.alphaIsTransparency = true;   // 둥근 모서리 투명 처리
        imp.mipmapEnabled = false;
        imp.maxTextureSize = 2048;

        if (isButton)
        {
            // 9-slice: 리벳이 있는 좌우 끝은 늘어나지 않고 가운데만 늘어납니다.
            imp.spritePixelsPerUnit = 200;                       // 600x104 -> 캔버스에서 300x52
            imp.spriteBorder = new Vector4(56, 22, 56, 22);      // left, bottom, right, top
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
        }
        else if (isResult)
        {
            // 결과 팝업: 가죽 패널은 9-slice, 제목 이미지는 그대로
            bool panel = assetPath.Contains("result_panel");
            imp.spritePixelsPerUnit = 200;
            imp.spriteBorder = panel ? new Vector4(100, 100, 100, 100) : Vector4.zero;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
        }
        else if (isChoice)
        {
            // 선택 창 버튼/상자: 9-slice (모서리는 늘어나지 않고 가운데만 늘어남)
            bool panel = assetPath.Contains("choice_panel");
            imp.spritePixelsPerUnit = 200;
            imp.spriteBorder = panel ? new Vector4(40, 40, 40, 40) : new Vector4(26, 26, 26, 26);
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
        }
        else if (isHud)
        {
            // 모래시계(채우기 이미지)·코인: 압축 없이, 사각형 메시로 가져옵니다.
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            imp.SetTextureSettings(settings);
        }
        else
        {
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
#endif
