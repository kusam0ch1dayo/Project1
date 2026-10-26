using UnityEngine;

public enum PortalKind
{
    /// <summary>拠点にある、ダンジョンへの入口</summary>
    DungeonEntrance,
    /// <summary>ダンジョン内にある、拠点への帰還ポータル（戦利品を持ち帰れる）</summary>
    ReturnHome,
    /// <summary>帰還ポータルそっくりの偽物。入ると罠部屋へ転送される。</summary>
    FakeExit,
    /// <summary>無限ダンジョンの、ボスを倒したあとのゲート（休憩エリアへ）。</summary>
    InfiniteGate,
    /// <summary>無限ダンジョンの、休憩エリアにある、次のステージへのポータル。</summary>
    InfiniteNext,
    /// <summary>真・無限ダンジョンの、ボスを倒したあとのゲート（すぐに次の階へ）。</summary>
    TrueInfiniteGate,
}

/// <summary>シーンを移動するゲート。見た目は Start でコード生成する。</summary>
public class Portal : MonoBehaviour, IInteractable
{
    public PortalKind kind = PortalKind.DungeonEntrance;

    bool used;

    float warnUntil;

    /// <summary>ボスを倒したのに、報酬の宝箱をまだ開けていない。</summary>
    static bool RewardUnopened => BossAI.RewardChest != null && !BossAI.RewardChest.Opened;

    public string Prompt => kind == PortalKind.TrueInfiniteGate ? $"E : ゲートをくぐって、第{TrueInfinite.Floor + 1}階へ"
        : kind == PortalKind.ReturnHome && TrueInfinite.Active ? $"E : 挑戦をやめて帰る（挑戦終了。第{TrueInfinite.Floor}階 → +{TrueInfinite.Reward} G。持ち物は、挑戦前に戻る）"
        : kind == PortalKind.InfiniteGate ? "E : ゲートをくぐって、休憩エリアへ"
        : kind == PortalKind.InfiniteNext ? $"E : 次のステージを確認して進む（第{Infinite.Floor}階）"
        : kind == PortalKind.ReturnHome && Infinite.Active ? $"E : 無限ダンジョンから脱出する（入手品を持ち帰り、{Infinite.DG} DG → {Infinite.DG} G）"
        : kind == PortalKind.DungeonEntrance
        ? $"E : ダンジョンを選ぶ（探索 {GameSession.Runs + 1} 回目）"
        : kind == PortalKind.ReturnHome && RewardUnopened
            ? "<color=#ffb050>E : 拠点へ帰還する（ボスの宝箱が、まだ開いていません！）</color>"
            : "E : 拠点へ帰還する（戦利品を持ち帰る）";

    public bool CanInteract => !used;

    public void Interact(GameObject interactor)
    {
        if (used) return;

        // 真・無限ダンジョン: すぐに次の階へ
        if (kind == PortalKind.TrueInfiniteGate)
        {
            used = true;
            TrueInfinite.GoNext(transform);
            return;
        }

        // 無限ダンジョン
        if (kind == PortalKind.InfiniteGate)
        {
            used = true;
            Infinite.GoRest(transform);
            return;
        }
        if (kind == PortalKind.InfiniteNext)
        {
            var nui = interactor.GetComponent<InfiniteNextUI>();
            if (nui == null) nui = interactor.AddComponent<InfiniteNextUI>();
            nui.Open(() => { if (used) return; used = true; Infinite.GoNext(transform); });
            return;
        }

        // 偽の出口: 罠部屋へ転送される
        if (kind == PortalKind.FakeExit)
        {
            if (TrapRoomLink.TeleportToTrapRoom(interactor)) used = true;
            return;
        }

        // チュートリアルの途中では、まだ帰れない（中断するなら、Esc のメニューから）
        if (kind == PortalKind.ReturnHome && TutorialSession.Active && !TutorialSession.Completed)
        {
            var thud = PlayerHUD.Instance;
            if (thud != null) thud.ShowToast("チュートリアルを最後まで進めましょう（中断するなら Esc のメニューから）");
            return;
        }

        string target = kind == PortalKind.DungeonEntrance ? SceneNames.Dungeon : SceneNames.Home;
        if (!SceneTransition.CanLoad(target))
        {
            var hud = PlayerHUD.Instance;
            if (hud != null) hud.ShowToast($"シーン '{target}' が Build Settings にありません");
            return;
        }

        // ダンジョンの入口: ダンジョン選択画面を開く（挑戦するを押すと入る）
        if (kind == PortalKind.DungeonEntrance)
        {
            var ui = interactor.GetComponent<DungeonSelectUI>();
            if (ui == null) ui = interactor.AddComponent<DungeonSelectUI>();
            ui.Open(dungeon => Enter(dungeon, target));
            return;
        }

        // ボスの宝箱を開けずに帰ろうとしたら、忠告する（もう一度 E で、そのまま帰還する）
        if (kind == PortalKind.ReturnHome && RewardUnopened && Time.time > warnUntil)
        {
            warnUntil = Time.time + 8f;
            var whud = PlayerHUD.Instance;
            if (whud != null) whud.ShowToast("<color=#ffb050>ボスの宝箱を、まだ開けていません！</color>\n<size=22>このまま帰還すると、報酬を受け取れません。それでも帰るなら、もう一度 E</size>");
            AudioManager.PlaySfx("cancel", 0.8f, 0.8f);
            return;
        }

        used = true;
        CinematicDirector.ExitThroughPortal(transform, new Color(0.3f, 0.85f, 1f));
    }

    /// <summary>選んだダンジョンに入る。ポータルに吸い込まれる演出のあと、シーンを移動する。</summary>
    void Enter(DungeonData dungeon, string target)
    {
        if (used) return;
        if (!GameSession.CanEnterDungeon(out var reason))
        {
            var hud = PlayerHUD.Instance;
            if (hud != null) hud.ShowToast($"<color=#ff9a6a>{reason}</color>");
            return;
        }
        used = true;
        DungeonSession.Current = dungeon;
        GameSession.BeginRun();
        if (dungeon.isInfinite) Infinite.Begin(); // 持ち物・装備・スキルを没収し、最初の階を作る
        if (dungeon.isTrueInfinite) TrueInfinite.Begin(); // 真・無限ダンジョン: 最初の階を作る
        CinematicDirector.EnterDungeon(transform, new Color(0.7f, 0.3f, 1f), target);
    }

    void Start()
    {
        Build();
    }

    void Build()
    {
        bool entrance = kind == PortalKind.DungeonEntrance;
        var stone = new Color(0.3f, 0.3f, 0.34f);
        var glow = entrance ? new Color(0.7f, 0.3f, 1f)
                 : kind == PortalKind.InfiniteGate ? new Color(1f, 0.85f, 0.3f)
                 : kind == PortalKind.InfiniteNext ? new Color(0.4f, 1f, 0.6f)
                 : kind == PortalKind.TrueInfiniteGate ? new Color(0.7f, 0.6f, 1f)
                 : new Color(0.3f, 0.85f, 1f);

        Part("PillarL", new Vector3(-1.4f, 1.6f, 0f), new Vector3(0.5f, 3.2f, 0.5f), stone, false);
        Part("PillarR", new Vector3(1.4f, 1.6f, 0f), new Vector3(0.5f, 3.2f, 0.5f), stone, false);
        Part("Lintel", new Vector3(0f, 3.35f, 0f), new Vector3(3.3f, 0.5f, 0.6f), stone, false);
        Part("Gate", new Vector3(0f, 1.5f, 0f), new Vector3(2.3f, 3f, 0.12f), glow, true);

        Fx.PortalGlow(transform, glow);

        var col = gameObject.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 1.6f, 0f);
        col.size = new Vector3(3.3f, 3.2f, 0.7f);

        var lightGo = new GameObject("PortalLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.5f, -1f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = glow;
        light.range = 10f;
        light.intensity = 12f;
    }

    void Part(string name, Vector3 pos, Vector3 scale, Color color, bool emissive)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;

        var mat = go.GetComponent<Renderer>().material;
        mat.color = color;
        if (emissive)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * 2f);
        }
    }
}
