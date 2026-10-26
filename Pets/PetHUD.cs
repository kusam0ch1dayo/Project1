using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>ダンジョンにいる召喚獣の HP・MP（画面の左）。倒れているときは「ダウン」。</summary>
public class PetHUD : MonoBehaviour
{
    class RowUi { public RectTransform root; public Text label; public RectTransform hp, mp; public PetRuntime pet; }

    GameObject canvasGo;
    Canvas canvas;
    RectTransform panel;
    readonly List<Text> marks = new List<Text>();
    static readonly RaycastHit[] hits = new RaycastHit[16];
    readonly List<RowUi> rows = new List<RowUi>();
    float refreshAt;

    void Update()
    {
        if (PetSystem.Spawned.Count == 0) { if (canvasGo != null && canvasGo.activeSelf) canvasGo.SetActive(false); return; }
        if (canvasGo == null) Build();
        if (!canvasGo.activeSelf) canvasGo.SetActive(true);
        UpdateMarks();

        if (Time.unscaledTime >= refreshAt)
        {
            refreshAt = Time.unscaledTime + 0.5f;
            Rebuild();
        }
        foreach (var r in rows)
        {
            if (r.pet == null) continue;
            var h = r.pet.Health;
            float hp = h != null && h.maxHealth > 0f && !r.pet.Downed ? h.Current / h.maxHealth : 0f;
            float mp = r.pet.MpMax > 0f ? r.pet.Mp / r.pet.MpMax : 0f;
            r.hp.anchorMax = new Vector2(Mathf.Clamp01(hp), 1f);
            r.mp.anchorMax = new Vector2(Mathf.Clamp01(mp), 1f);
            string state = r.pet.Downed ? "  <color=#ff8a7a>ダウン</color>" : r.pet.Ridden ? "  <color=#ffd75e>ライド中</color>" : r.pet.order == PetRuntime.Order.Move ? "  <color=#9fd8ff>待機</color>" : "";
            r.label.text = $"<color=#9fffb0>{r.pet.inst.Name}</color>  <size=15>Lv{r.pet.inst.level}</size>{state}";
        }
    }

    void Rebuild()
    {
        var want = new List<PetRuntime>();
        foreach (var p in PetSystem.Spawned) if (p != null && want.Count < 4) want.Add(p);
        bool same = want.Count == rows.Count;
        for (int i = 0; same && i < want.Count; i++) if (rows[i].pet != want[i]) same = false;
        if (same) return;
        foreach (var r in rows) if (r.root != null) Destroy(r.root.gameObject);
        rows.Clear();
        for (int i = 0; i < want.Count; i++)
        {
            float y1 = 1f - i * 0.25f, y0 = y1 - 0.23f;
            var root = UIKit.Panel(panel, "Pet", new Color(0f, 0f, 0f, 0.45f), new Vector2(0f, y0), new Vector2(1f, y1));
            root.GetComponent<Image>().raycastTarget = false;
            var label = UIKit.Label(root, "Name", "", 18, Color.white, TextAnchor.MiddleLeft, new Vector2(0.03f, 0.5f), new Vector2(0.97f, 1f), true);
            var hpBg = UIKit.Panel(root, "HpBg", new Color(0.15f, 0.05f, 0.05f, 0.9f), new Vector2(0.03f, 0.28f), new Vector2(0.97f, 0.46f));
            var hp = UIKit.Panel(hpBg, "Hp", new Color(0.4f, 0.9f, 0.45f), Vector2.zero, Vector2.one);
            var mpBg = UIKit.Panel(root, "MpBg", new Color(0.05f, 0.07f, 0.15f, 0.9f), new Vector2(0.03f, 0.08f), new Vector2(0.97f, 0.2f));
            var mp = UIKit.Panel(mpBg, "Mp", new Color(0.35f, 0.6f, 1f), Vector2.zero, Vector2.one);
            foreach (var img in root.GetComponentsInChildren<Image>()) img.raycastTarget = false;
            rows.Add(new RowUi { root = root, label = label, hp = hp, mp = mp, pet = want[i] });
        }
    }

    // ---------------------------------------------------------------- 壁越しの位置

    /// <summary>
    /// 召喚獣の位置を、壁越しに示す（壁に隠れているときだけ。見えているときは、出さない）。
    /// ダウンしていても示す。ただし、置き去りで救助を待っている召喚獣は、示さない（ペットコンパスで探す）。
    /// 画面の外にいるときは、画面の端に出す。
    /// </summary>
    void UpdateMarks()
    {
        var cam = Camera.main;
        bool show = cam != null && cam.enabled && !CinematicDirector.IsPlaying && !PetWindowBase.AnyOpen && !PauseMenu.IsOpen;
        int used = 0;
        if (show)
        {
            Vector3 cp = cam.transform.position;
            foreach (var p in PetSystem.Spawned)
            {
                if (p == null || p.inst == null || !p.gameObject.activeInHierarchy) continue;
                if (p.Downed && p.inst.IsLost) continue;   // 救助待ち（置き去り）は、見えない
                if (p.Ridden) continue;
                Vector3 wp = p.Center + Vector3.up * 0.6f;
                float dist = Vector3.Distance(cp, wp);
                if (!Hidden(cp, wp, p.transform)) continue;   // 見えているときは、出さない

                Vector3 sp = cam.WorldToScreenPoint(wp);
                Vector2 half = new Vector2(Screen.width, Screen.height) * 0.5f;
                Vector2 v = (Vector2)sp - half;
                if (sp.z < 0f) v = -v;   // 後ろ側
                // 画面の外（後ろを含む）: 画面の端に寄せる
                Vector2 lim = half - new Vector2(70f, 60f) * canvas.scaleFactor;
                bool off = sp.z < 0f || Mathf.Abs(v.x) > lim.x || Mathf.Abs(v.y) > lim.y;
                if (off)
                {
                    if (sp.z < 0f && v.sqrMagnitude < 1f) v = new Vector2(0f, -1f);
                    float s = Mathf.Min(lim.x / Mathf.Max(0.01f, Mathf.Abs(v.x)), lim.y / Mathf.Max(0.01f, Mathf.Abs(v.y)));
                    if (sp.z < 0f || s < 1f) v *= s;
                }

                var t = Mark(used++);
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.unscaledTime * (p.Downed ? 8f : 4f) + used);
                if (p.Downed) t.text = $"<color=#ff7a6a><size={Mathf.RoundToInt(30 * pulse)}>◆</size></color>\n<size=17><color=#ffb0a0>{p.inst.Name}  ダウン</color>\n{dist:0}m</size>";
                else t.text = $"<color=#7dff9a><size={Mathf.RoundToInt(26 * pulse)}>◆</size></color>\n<size=16><color=#c8ffd2>{p.inst.Name}</color>\n{dist:0}m</size>";
                t.rectTransform.anchoredPosition = v / canvas.scaleFactor;
                if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            }
        }
        for (int i = used; i < marks.Count; i++) if (marks[i].gameObject.activeSelf) marks[i].gameObject.SetActive(false);
    }

    /// <summary>カメラから見て、壁などに隠れているか（召喚獣自身・プレイヤー・敵や味方の体は、さえぎらない）。</summary>
    static bool Hidden(Vector3 from, Vector3 to, Transform pet)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.5f) return false;
        int n = Physics.RaycastNonAlloc(from, d / len, hits, len - 0.3f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var c = hits[i].collider;
            if (c == null) continue;
            if (c.transform.IsChildOf(pet)) continue;
            if (c.GetComponentInParent<Health>() != null) continue;   // プレイヤー・敵・味方の体
            return true;
        }
        return false;
    }

    Text Mark(int i)
    {
        while (marks.Count <= i)
        {
            var t = UIKit.Label(canvasGo.transform, "PetMark" + marks.Count, "", 18, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), true);
            t.rectTransform.sizeDelta = new Vector2(220f, 90f);
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.85f);
            t.gameObject.SetActive(false);
            marks.Add(t);
        }
        return marks[i];
    }

    void Build()
    {
        canvasGo = new GameObject("PetHUDCanvas");
        canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        panel = UIKit.Panel(canvasGo.transform, "Pets", new Color(0f, 0f, 0f, 0f), new Vector2(0.008f, 0.32f), new Vector2(0.16f, 0.5f));
        panel.GetComponent<Image>().raycastTarget = false;
    }

    void OnDestroy() { if (canvasGo != null) Destroy(canvasGo); }
}
