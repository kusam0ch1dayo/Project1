using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 画面の上部のコンパス。「ボスコンパス」を持っていると、ボス部屋の方角を、「トレジャーレーダー」を持っていると、未開封の宝箱（ミミックも）の方角を示す。
/// 高さは分からない。トレジャーレーダーは、同じ階に宝箱が無いときだけ、別の階の宝箱を示す。
/// 「ペットコンパス」を持っていると、ダンジョンにいる召喚獣（倒れている子・置き去りの子も。倒れている子は赤）の方角を示す。
/// </summary>
public class NavCompass : MonoBehaviour
{
    const float Half = 360f;

    Canvas canvas;
    RectTransform strip;
    Text north, east, south, west, bossMark, chestMark;
    readonly Text[] petMarks = new Text[3];
    bool built;
    float fightCheckAt;
    bool fight;
    float scan;
    int lastBossM = -1;
    DungeonGenerator gen;
    Chest nearest;
    bool nearestOtherFloor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var go = new GameObject("NavCompass");
        DontDestroyOnLoad(go);
        go.AddComponent<NavCompass>();
    }

    void Build()
    {
        built = true;
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 8;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        strip = UIKit.Panel(transform, "Strip", new Color(0.05f, 0.06f, 0.1f, 0.55f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
        strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 1f);
        strip.pivot = new Vector2(0.5f, 1f);
        strip.sizeDelta = new Vector2(Half * 2f, 44f);
        strip.anchoredPosition = new Vector2(0f, -18f);
        strip.GetComponent<Graphic>().raycastTarget = false;

        north = Mark("N", new Color(1f, 0.55f, 0.5f), 26);
        east = Mark("E", Color.white, 24);
        south = Mark("S", Color.white, 24);
        west = Mark("W", Color.white, 24);
        bossMark = Mark("ボス", new Color(1f, 0.3f, 0.25f), 24);
        chestMark = Mark("宝", new Color(1f, 0.85f, 0.3f), 24);
        for (int i = 0; i < petMarks.Length; i++) petMarks[i] = Mark("ペット" + i, new Color(0.5f, 1f, 0.6f), 22);
        // 中央の目印
        var tick = UIKit.Label(strip, "Center", "▼", 18, new Color(1f, 1f, 1f, 0.7f), TextAnchor.UpperCenter, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), true);
        tick.rectTransform.sizeDelta = new Vector2(40f, 20f);
        tick.rectTransform.anchoredPosition = new Vector2(0f, 12f);
        strip.gameObject.SetActive(false);
    }

    Text Mark(string text, Color color, int size)
    {
        var t = UIKit.Label(strip, "Mark_" + text, text, size, color, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), true);
        t.rectTransform.sizeDelta = new Vector2(120f, 44f);
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static bool Has(string id)
    {
        foreach (var it in GameSession.Bag) if (it != null && it.id == id) return true;
        return false;
    }

    /// <summary>世界の向き（度。北 = +Z が 0、東 = +X が 90）と、カメラの向きの差を、コンパス上の横位置にする。範囲外は端に寄せる。</summary>
    static float X(float relDeg, out bool clamped)
    {
        float r = Mathf.DeltaAngle(0f, relDeg);
        clamped = Mathf.Abs(r) > 90f;
        return Mathf.Clamp(r / 90f, -1f, 1f) * (Half - 30f);
    }

    void Update()
    {
        if (!built) Build();
        var player = Hz.Player;
        var cam = Camera.main;
        bool on = GameSession.RunActive && player != null && cam != null && Hz.PlayerAlive;
        bool boss = on && Has(ExtraItems.CompassId);
        bool treasure = on && Has(ExtraItems.RadarId);
        bool pets = on && Has(ExtraItems.PetCompassId);
        if (Time.unscaledTime >= fightCheckAt)
        {
            fightCheckAt = Time.unscaledTime + 0.4f;
            fight = false;
            foreach (var b in BossAI.Registry) if (b != null && b.InFight) { fight = true; break; }
        }
        bool show = (boss || treasure || pets) && !PauseMenu.IsOpen && !fight; // ボス戦では、コンパスを隠す
        if (strip.gameObject.activeSelf != show) strip.gameObject.SetActive(show);
        if (!show) return;

        float yaw = cam.transform.eulerAngles.y;
        PlaceCardinal(north, 0f - yaw); PlaceCardinal(east, 90f - yaw); PlaceCardinal(south, 180f - yaw); PlaceCardinal(west, 270f - yaw);

        // ボス部屋
        bossMark.gameObject.SetActive(boss);
        if (boss)
        {
            if (gen == null) gen = FindFirstObjectByType<DungeonGenerator>();
            if (gen != null)
            {
                Vector3 to = gen.BossPoint() - player.transform.position;
                float ang = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - yaw;
                float x = X(ang, out bool cl);
                bossMark.rectTransform.anchoredPosition = new Vector2(x, 0f);
                bossMark.color = new Color(1f, 0.3f, 0.25f, cl ? 0.55f : 1f);
                int bm = Mathf.RoundToInt(new Vector2(to.x, to.z).magnitude);
                if (bm != lastBossM) { lastBossM = bm; bossMark.text = $"ボス\n<size=16>{bm}m</size>"; }
            }
            else bossMark.gameObject.SetActive(false);
        }

        // 召喚獣
        PlacePets(pets, player.transform.position, yaw);

        // 宝箱
        chestMark.gameObject.SetActive(false);
        if (treasure)
        {
            scan -= Time.unscaledDeltaTime;
            if (scan <= 0f || (nearest != null && (nearest.Opened || !nearest.gameObject.activeInHierarchy)))
            {
                scan = 0.5f;
                FindNearestChest(player.transform.position);
            }
            if (nearest != null)
            {
                Vector3 to = nearest.transform.position - player.transform.position;
                float ang = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - yaw;
                float x = X(ang, out bool cl);
                chestMark.gameObject.SetActive(true);
                chestMark.rectTransform.anchoredPosition = new Vector2(x, 0f);
                chestMark.color = new Color(1f, 0.85f, 0.3f, cl ? 0.55f : 1f);
                chestMark.text = $"宝{(nearestOtherFloor ? "（別の階）" : "")}\n<size=16>{new Vector2(to.x, to.z).magnitude:0}m</size>";
            }
        }
    }

    /// <summary>召喚獣の方角（倒れている子を先に、最大3匹）。</summary>
    void PlacePets(bool on, Vector3 from, float yaw)
    {
        int used = 0;
        if (on)
        {
            var list = new System.Collections.Generic.List<PetRuntime>();
            foreach (var p in PetSystem.Spawned) if (p != null && p.inst != null && !p.Ridden) list.Add(p);
            list.Sort((a, b) => a.Downed != b.Downed ? (a.Downed ? -1 : 1) : (a.transform.position - from).sqrMagnitude.CompareTo((b.transform.position - from).sqrMagnitude));
            foreach (var p in list)
            {
                if (used >= petMarks.Length) break;
                Vector3 to = p.transform.position - from;
                float ang = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - yaw;
                float x = X(ang, out bool cl);
                var t = petMarks[used++];
                t.gameObject.SetActive(true);
                t.rectTransform.anchoredPosition = new Vector2(x, 0f);
                Color c = p.Downed ? new Color(1f, 0.45f, 0.4f) : new Color(0.5f, 1f, 0.6f);
                t.color = new Color(c.r, c.g, c.b, cl ? 0.55f : 1f);
                bool other = FloorUtil.Different(from, p.transform.position);
                t.text = $"{p.inst.Name}{(p.Downed ? "（倒れている）" : "")}{(other ? "（別の階）" : "")}\n<size=16>{new Vector2(to.x, to.z).magnitude:0}m</size>";
            }
        }
        for (int i = used; i < petMarks.Length; i++) if (petMarks[i].gameObject.activeSelf) petMarks[i].gameObject.SetActive(false);
    }

    void PlaceCardinal(Text t, float rel)
    {
        float x = X(rel, out bool cl);
        t.gameObject.SetActive(!cl);
        t.rectTransform.anchoredPosition = new Vector2(x, 0f);
    }

    /// <summary>同じ階の、未開封の宝箱のうち、いちばん近いもの。無ければ、別の階のいちばん近いもの。</summary>
    void FindNearestChest(Vector3 from)
    {
        Chest best = null, bestOther = null;
        float bd = float.MaxValue, bod = float.MaxValue;
        foreach (var c in Chest.All)
        {
            if (c == null || c.Opened || !c.gameObject.activeInHierarchy) continue;
            float d = Vector3.Distance(new Vector3(from.x, 0f, from.z), new Vector3(c.transform.position.x, 0f, c.transform.position.z));
            if (!FloorUtil.Different(from, c.transform.position)) { if (d < bd) { bd = d; best = c; } }
            else if (d < bod) { bod = d; bestOther = c; }
        }
        nearest = best != null ? best : bestOther;
        nearestOtherFloor = best == null && bestOther != null;
    }
}
