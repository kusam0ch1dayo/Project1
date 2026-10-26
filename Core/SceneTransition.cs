using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>フェードアウト → 非同期ロード → フェードイン でシーンを切り替える。</summary>
public class SceneTransition : MonoBehaviour
{
    const float FadeTime = 0.35f;

    static SceneTransition instance;

    Image fade;
    bool busy;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => instance = null;

    /// <summary>Build Settings に登録されていてロード可能か。</summary>
    public static bool CanLoad(string sceneName) => Application.CanStreamedLevelBeLoaded(sceneName);

    /// <summary>ロードを開始する。シーンが存在しなければ false。</summary>
    public static bool Load(string sceneName)
    {
        if (!CanLoad(sceneName))
        {
            Debug.LogError($"シーン '{sceneName}' が Build Settings にありません。メニュー Dungeon > Create All Scenes を実行してください。");
            return false;
        }

        if (instance == null)
        {
            var go = new GameObject("SceneTransition");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SceneTransition>();
            instance.BuildOverlay();
        }

        if (!instance.busy) instance.StartCoroutine(instance.Run(sceneName));
        return true;
    }

    void BuildOverlay()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var go = new GameObject("Fade", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(transform, false);
        fade = go.GetComponent<Image>();
        fade.color = new Color(0f, 0f, 0f, 0f);
        fade.raycastTarget = false;
        var rt = fade.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    IEnumerator Run(string sceneName)
    {
        busy = true;
        Time.timeScale = 1f;
        fade.raycastTarget = true; // フェード中の操作を遮断
        AudioManager.PlaySfx("scene_change", 0.8f);

        yield return Fade(0f, 1f);

        var op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone) yield return null;

        yield return Fade(1f, 0f);

        fade.raycastTarget = false;
        busy = false;
    }

    IEnumerator Fade(float from, float to)
    {
        for (float t = 0f; t < FadeTime; t += Time.unscaledDeltaTime)
        {
            fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t / FadeTime));
            yield return null;
        }
        fade.color = new Color(0f, 0f, 0f, to);
    }
}
