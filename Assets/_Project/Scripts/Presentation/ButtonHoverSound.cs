using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiarsBatting.Presentation
{
    public class ButtonHoverSound : MonoBehaviour, IPointerEnterHandler
    {
        public string ClipName = "SFX_UI_Hover";

        public void OnPointerEnter(PointerEventData eventData)
        {
            var button = GetComponent<Button>();
            if (button != null && button.IsInteractable())
                GameAudio.Play(ClipName, volume: 0.4f);
        }
    }
}
