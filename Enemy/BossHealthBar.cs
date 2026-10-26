using UnityEngine;
using UnityEngine.UI;

/// <summary>画面上部のボス体力バー。ボスの子オブジェクトとして生成され、ボスと一緒に消える。</summary>
public class BossHealthBar : MonoBehaviour
{
    Health health;
    RectTransform fill;
    GameObject root;

    public static BossHealthBar Create(Transform parent, string bossName, Health health)
    {
        var go = new GameObject("BossHealthBar");
        go.transform.SetParent(parent, false);
        var bar = go.AddComponent<BossHealthBar>();
        bar.health = health;
        bar.Build(bossName);
        return bar;
    }

    public void SetVisible(bool visible)
    {
        if (root != null) root.SetActive(visible);
    }

    void Build(string bossName)
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        var rootRt = UIKit.Panel(transform, "Root", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one);
        rootRt.GetComponent<Image>().raycastTarget = false;
        root = rootRt.gameObject;

        var bg = UIKit.Panel(root.transform, "Bg", new Color(0f, 0f, 0f, 0.75f),
                             new Vector2(0.25f, 0.90f), new Vector2(0.75f, 0.93f));
        fill = UIKit.Panel(bg, "Fill", new Color(0.7f, 0.1f, 0.15f), Vector2.zero, Vector2.one);
        fill.offsetMin = new Vector2(3f, 3f);
        fill.offsetMax = new Vector2(-3f, -3f);

        UIKit.Label(root.transform, "Name", bossName, 30, Color.white, TextAnchor.LowerCenter,
                    new Vector2(0.25f, 0.93f), new Vector2(0.75f, 0.97f), true);

        root.SetActive(false);
    }

    void Update()
    {
        if (health != null && fill != null)
            fill.anchorMax = new Vector2(Mathf.Clamp01(health.Current / health.maxHealth), 1f);
    }
}
