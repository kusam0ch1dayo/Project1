using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// クエストの報告先の案内。拠点で、受けているクエストを達成したとき、報告先の村人の方角と距離を画面の上に出す
/// （村人には、遠くからでも見える光の柱も立つ）。
/// </summary>
public class QuestGuide : MonoBehaviour
{
    static QuestGuide inst;
    GameObject canvasGo;
    Text text;

    static readonly string[] Arrows = { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => inst = null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (inst != null) return;
        var go = new GameObject("QuestGuide");
        DontDestroyOnLoad(go);
        inst = go.AddComponent<QuestGuide>();
    }

    void Update()
    {
        bool show = false;
        string msg = "";
        var q = QuestSystem.Active;
        if (q != null && QuestSystem.CanTurnIn(q) && !GameSession.RunActive && SceneManager.GetActiveScene().name == SceneNames.Home
            && FirstPersonController.CursorLocked && !QuestUI.IsOpen)
        {
            var npc = VillageNpc.Find(q.npc);
            var cam = Camera.main;
            if (npc != null && cam != null)
            {
                Vector3 to = npc.transform.position - cam.transform.position;
                to.y = 0f;
                Vector3 fwd = cam.transform.forward;
                fwd.y = 0f;
                if (to.sqrMagnitude > 0.01f && fwd.sqrMagnitude > 0.01f)
                {
                    float ang = Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up); // 正 = 右
                    int idx = Mathf.RoundToInt(((ang % 360f) + 360f) % 360f / 45f) % 8;
                    msg = $"<color=#7CFC9A>クエスト報告</color>   {QuestSystem.NpcName(q.npc)}  <color=#ffe066>{Arrows[idx]}</color>  {Mathf.RoundToInt(to.magnitude)}m";
                    show = true;
                }
            }
        }

        if (show && canvasGo == null) Build();
        if (canvasGo != null && canvasGo.activeSelf != show) canvasGo.SetActive(show);
        if (show && text != null) text.text = msg;
    }

    void Build()
    {
        canvasGo = new GameObject("QuestGuideCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        text = UIKit.Label(canvasGo.transform, "Guide", "", 34, Color.white, TextAnchor.MiddleCenter, new Vector2(0.28f, 0.86f), new Vector2(0.72f, 0.92f), true);
    }
}
