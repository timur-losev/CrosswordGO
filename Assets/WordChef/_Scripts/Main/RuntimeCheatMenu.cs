using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class RuntimeCheatMenu : MonoBehaviour
{
    private struct CheatAction
    {
        public string Label;
        public Action Callback;
    }

    private static RuntimeCheatMenu instance;
    private static readonly List<CheatAction> actions = new List<CheatAction>();
    private static bool defaultsInitialized;

    private GameObject overlayRoot;
    private Text statusText;
    private Canvas targetCanvas;
    private Font menuFont;

    public static void ShowMenu()
    {
        ShowMenu(null);
    }

    public static void ShowMenu(Transform contextTransform)
    {
        EnsureDefaultActions();
        RuntimeCheatMenu menu = EnsureInstance();
        menu.SetTargetCanvasFromContext(contextTransform);
        menu.Show();
    }

    public static void RegisterAction(string label, Action callback)
    {
        EnsureDefaultActions();
        for (int i = 0; i < actions.Count; i++)
        {
            if (actions[i].Label == label)
            {
                actions[i] = new CheatAction { Label = label, Callback = callback };
                return;
            }
        }

        actions.Add(new CheatAction { Label = label, Callback = callback });
        if (instance != null)
        {
            instance.RebuildUi();
        }
    }

    private static RuntimeCheatMenu EnsureInstance()
    {
        if (instance != null)
        {
            return instance;
        }

        instance = FindObjectOfType<RuntimeCheatMenu>();
        if (instance != null)
        {
            return instance;
        }

        GameObject host = new GameObject("RuntimeCheatMenu");
        instance = host.AddComponent<RuntimeCheatMenu>();
        return instance;
    }

    private void Show()
    {
        EnsureUi();
        overlayRoot.SetActive(true);
        ForcePanelLayout();
    }

    private void Hide()
    {
        if (overlayRoot != null)
        {
            overlayRoot.SetActive(false);
        }
    }

    private void EnsureUi()
    {
        if (overlayRoot != null)
        {
            AttachToCanvas();
            ForcePanelLayout();
            return;
        }

        AttachToCanvas();
        ResolveMenuFont();

        overlayRoot = new GameObject("CheatMenuOverlay", typeof(RectTransform), typeof(Image), typeof(Button));
        overlayRoot.transform.SetParent(transform, false);

        RectTransform overlayRect = overlayRoot.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        Image overlayImage = overlayRoot.GetComponent<Image>();
        overlayImage.color = new Color(0f, 0f, 0f, 0.65f);

        Button overlayButton = overlayRoot.GetComponent<Button>();
        overlayButton.onClick.AddListener(Hide);

        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        panel.transform.SetParent(overlayRoot.transform, false);

        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.5f);
        panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(520f, 0f);

        Image panelImage = panel.GetComponent<Image>();
        panelImage.color = new Color(0.12f, 0.12f, 0.12f, 0.98f);

        VerticalLayoutGroup panelLayout = panel.GetComponent<VerticalLayoutGroup>();
        panelLayout.childAlignment = TextAnchor.UpperCenter;
        panelLayout.spacing = 16f;
        panelLayout.padding = new RectOffset(24, 24, 24, 24);
        panelLayout.childForceExpandWidth = true;
        panelLayout.childForceExpandHeight = false;

        ContentSizeFitter fitter = panel.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

        GameObject title = CreateText("Cheat Menu", 34, TextAnchor.MiddleCenter, Color.white);
        AddPreferredHeight(title, 58f);
        title.transform.SetParent(panel.transform, false);

        for (int i = 0; i < actions.Count; i++)
        {
            CheatAction action = actions[i];
            GameObject actionButton = CreateButton(action.Label, () => OnActionClicked(action));
            actionButton.transform.SetParent(panel.transform, false);
        }

        GameObject closeButton = CreateButton("Close", Hide);
        closeButton.transform.SetParent(panel.transform, false);

        GameObject status = CreateText(string.Empty, 24, TextAnchor.MiddleCenter, new Color(0.92f, 0.92f, 0.92f, 1f));
        AddPreferredHeight(status, 48f);
        status.transform.SetParent(panel.transform, false);
        statusText = status.GetComponent<Text>();

        overlayRoot.SetActive(false);
    }

    private void RebuildUi()
    {
        if (overlayRoot != null)
        {
            Destroy(overlayRoot);
            overlayRoot = null;
            statusText = null;
        }
    }

    private void AttachToCanvas()
    {
        Canvas canvas = targetCanvas != null ? targetCanvas : FindBestCanvas();
        if (canvas != null && transform.parent != canvas.transform)
        {
            transform.SetParent(canvas.transform, false);
            transform.SetAsLastSibling();
        }
    }

    private void SetTargetCanvasFromContext(Transform contextTransform)
    {
        if (contextTransform == null)
        {
            return;
        }

        Canvas contextCanvas = contextTransform.GetComponentInParent<Canvas>();
        if (contextCanvas != null)
        {
            targetCanvas = contextCanvas.rootCanvas;
        }
    }

    private void OnEraseSaveClicked()
    {
        string error;
        if (SaveDebugActions.TryEraseSave(out error))
        {
            statusText.text = "Save erased.";
            return;
        }

        statusText.text = "Erase failed. See Console.";
    }

    private static void EnsureDefaultActions()
    {
        if (defaultsInitialized)
        {
            return;
        }

        defaultsInitialized = true;
        actions.Clear();
        actions.Add(new CheatAction { Label = "Erase Save", Callback = DefaultEraseSave });
    }

    private static void DefaultEraseSave()
    {
        EnsureInstance().OnEraseSaveClicked();
    }

    private void OnActionClicked(CheatAction action)
    {
        if (action.Callback != null)
        {
            action.Callback.Invoke();
        }
    }

    private static GameObject CreateText(string text, int fontSize, TextAnchor alignment, Color color)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        Text label = go.GetComponent<Text>();
        RuntimeCheatMenu menu = EnsureInstance();
        label.text = text;
        label.font = menu.menuFont;
        label.fontSize = fontSize;
        label.alignment = alignment;
        label.color = color;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Overflow;

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0f, fontSize + 16f);
        return go;
    }

    private static GameObject CreateButton(string label, UnityEngine.Events.UnityAction onClick)
    {
        GameObject buttonGo = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        RectTransform rect = buttonGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(0f, 78f);

        Image image = buttonGo.GetComponent<Image>();
        image.color = new Color(0.23f, 0.23f, 0.23f, 1f);

        Button button = buttonGo.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.highlightedColor = new Color(0.32f, 0.32f, 0.32f, 1f);
        colors.pressedColor = new Color(0.16f, 0.16f, 0.16f, 1f);
        button.colors = colors;
        button.onClick.AddListener(onClick);

        LayoutElement layoutElement = buttonGo.GetComponent<LayoutElement>();
        layoutElement.preferredHeight = 78f;

        GameObject textGo = CreateText(label, 30, TextAnchor.MiddleCenter, Color.white);
        textGo.transform.SetParent(buttonGo.transform, false);

        RectTransform textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return buttonGo;
    }

    private static void AddPreferredHeight(GameObject go, float preferredHeight)
    {
        LayoutElement layout = go.GetComponent<LayoutElement>();
        if (layout == null)
        {
            layout = go.AddComponent<LayoutElement>();
        }

        layout.preferredHeight = preferredHeight;
    }

    private void ResolveMenuFont()
    {
        if (menuFont != null)
        {
            return;
        }

        Text sceneText = FindObjectOfType<Text>();
        if (sceneText != null && sceneText.font != null)
        {
            menuFont = sceneText.font;
            return;
        }

        menuFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    private Canvas FindBestCanvas()
    {
        Canvas[] canvases = FindObjectsOfType<Canvas>();
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (!canvas.isActiveAndEnabled)
            {
                continue;
            }

            if (canvas.rootCanvas != canvas)
            {
                continue;
            }

            if (canvas.renderMode == RenderMode.ScreenSpaceOverlay || canvas.renderMode == RenderMode.ScreenSpaceCamera)
            {
                return canvas;
            }
        }

        for (int i = 0; i < canvases.Length; i++)
        {
            if (canvases[i].isActiveAndEnabled && canvases[i].rootCanvas == canvases[i])
            {
                return canvases[i];
            }
        }

        return null;
    }

    private void ForcePanelLayout()
    {
        RectTransform panelRect = overlayRoot != null ? overlayRoot.transform.Find("Panel") as RectTransform : null;
        if (panelRect == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
    }
}
