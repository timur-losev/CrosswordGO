using UnityEngine;
using UnityEngine.EventSystems;

public class DragCorrector : MonoBehaviour
{
    void Start()
    {
        int defaultValue = EventSystem.current.pixelDragThreshold;
        EventSystem.current.pixelDragThreshold =
                Mathf.Max(defaultValue, (int)(defaultValue * Screen.dpi / 160f));
    }
}