using UnityEngine;
using System.Collections;
using UnityEngine.EventSystems;

public class ButtonGotoScene : MyButton, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler {

    public int sceneIndex;
    public bool useScreenFader;
    public bool useKeyCode;
    public KeyCode keyCode;
    public bool enableCheatMenuOnHold;
    public float cheatMenuHoldSeconds = 3f;

    private bool isPointerDown;
    private float pointerDownTime;
    private bool longPressTriggered;
    private bool suppressNextClick;

    public override void OnButtonClick()
    {
        Debug.Log("[CheatHold]OnButtonClick " + gameObject.name);

        if (suppressNextClick)
        {
            suppressNextClick = false;
            return;
        }

        base.OnButtonClick();
        CUtils.LoadScene(sceneIndex, useScreenFader);
    }

    private void Update()
    {
        if (enableCheatMenuOnHold && isPointerDown && !longPressTriggered)
        {
            if (Time.unscaledTime - pointerDownTime >= cheatMenuHoldSeconds)
            {
                longPressTriggered = true;
                suppressNextClick = true;
                RuntimeCheatMenu.ShowMenu(transform);
            }
        }

        if (useKeyCode && Input.GetKeyDown(keyCode) && !DialogController.instance.IsDialogShowing())
        {
            OnButtonClick();
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!enableCheatMenuOnHold)
        {
            return;
        }

        isPointerDown = true;
        pointerDownTime = Time.unscaledTime;
        longPressTriggered = false;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isPointerDown = false;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isPointerDown = false;
    }
}
