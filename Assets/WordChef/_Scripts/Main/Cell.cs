using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class Cell : MonoBehaviour
{
    public Text letterText;
    public string letter;
    public bool isShown;
    public Color highlightColor = new Color(0.7f, 0.7f, 0.7f, 1f);

    private Vector3 originLetterScale;
    private Color originTextColor;
    private bool isHighlighted;

    private void Awake()
    {
        if (letterText != null)
        {
            originTextColor = letterText.color;
        }
    }

    public void Animate()
    {
        Vector3 beginPosition = TextPreview.instance.transform.position;
        originLetterScale = letterText.transform.localScale;
        Vector3 middlePoint = CUtils.GetMiddlePoint(beginPosition, transform.position, -0.3f);
        Vector3[] waypoint = { beginPosition, middlePoint, transform.position };

        ShowText();
        letterText.transform.position = beginPosition;
        letterText.transform.localScale = TextPreview.instance.text.transform.localScale;
        letterText.transform.SetParent(MonoUtils.instance.textFlyTransform);
        iTween.MoveTo(letterText.gameObject, iTween.Hash("path", waypoint, "time", 0.2f, "oncomplete", "OnMoveToComplete", "oncompletetarget", gameObject));
        iTween.ScaleTo(letterText.gameObject, iTween.Hash("scale", originLetterScale, "time", 0.2f));
    }

    private void OnMoveToComplete()
    {
        letterText.transform.SetParent(transform);
        iTween.ScaleTo(letterText.gameObject, iTween.Hash("scale", originLetterScale * 1.3f, "time", 0.15f, "oncomplete", "OnScaleUpComplete", "oncompletetarget", gameObject));
    }

    private void OnScaleUpComplete()
    {
        iTween.ScaleTo(letterText.gameObject, iTween.Hash("scale", originLetterScale, "time", 0.15f));
    }

    public void ShowHint()
    {
        isShown = true;
        originLetterScale = letterText.transform.localScale;
        ShowText();
        OnMoveToComplete();
    }

    public void ShowText()
    {
        letterText.text = letter;
        if (letterText != null)
        {
            letterText.color = originTextColor;
        }
    }

    public void SetHighlighted(bool highlighted)
    {
        if (isHighlighted == highlighted) return;
        isHighlighted = highlighted;

        if (letterText == null)
        {
            return;
        }

        if (isShown)
        {
            letterText.text = letter;
            letterText.color = originTextColor;
            return;
        }

        if (highlighted)
        {
            letterText.text = letter;
            letterText.color = highlightColor;
        }
        else
        {
            letterText.color = originTextColor;
            letterText.text = string.Empty;
        }
    }
}
