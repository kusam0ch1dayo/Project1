using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 村を歩き回る村人。道（ノードをつないだ道筋）をぶらぶら歩く。
/// 依頼を持っている村人（QuestSystem の依頼主）には、頭の上に印が出て、Eで話しかけられる。話している間は立ち止まる。
/// </summary>
public class VillageNpc : MonoBehaviour, IInteractable
{
    // ---------------------------------------------------------------- 道筋（ノード）

    static readonly Vector3[] Nodes =
    {
        new Vector3(0f, 0f, -4f),     // 0 広場の南（十字路）
        new Vector3(-12f, 0f, -4f),   // 1
        new Vector3(-37f, 0f, -4f),   // 2
        new Vector3(-68f, 0f, -4f),   // 3 西の集落の奥
        new Vector3(12f, 0f, -6f),    // 4
        new Vector3(37f, 0f, -6f),    // 5 東の端
        new Vector3(0f, 0f, -24f),    // 6
        new Vector3(0f, 0f, -42f),    // 7
        new Vector3(0f, 0f, -60f),    // 8 南の十字路
        new Vector3(-40f, 0f, -60f),  // 9
        new Vector3(40f, 0f, -60f),   // 10
        new Vector3(-8f, 0f, 2f),     // 11 広場
        new Vector3(0f, 0f, 8f),      // 12
        new Vector3(8f, 0f, 2f),      // 13
        new Vector3(-52f, 0f, -4f),   // 14 西の集落の中ほど
        new Vector3(26f, 0f, -20f),   // 15 畑の入口
    };

    static readonly int[][] Links =
    {
        new[] { 1, 4, 6, 11, 13 },        // 0
        new[] { 0, 2 },                    // 1
        new[] { 1, 14 },                   // 2
        new[] { 14 },                      // 3
        new[] { 0, 5, 15 },                // 4
        new[] { 4 },                       // 5
        new[] { 0, 7 },                    // 6
        new[] { 6, 8 },                    // 7
        new[] { 7, 9, 10 },                // 8
        new[] { 8 },                       // 9
        new[] { 8 },                       // 10
        new[] { 0, 12 },                   // 11
        new[] { 11, 13 },                  // 12
        new[] { 12, 0 },                   // 13
        new[] { 2, 3 },                    // 14
        new[] { 4 },                       // 15
    };

    // ---------------------------------------------------------------- 村人の見た目

    struct Look
    {
        public Color shirt, pants, hair;
        public int style; // 0 短髪 / 1 長髪 / 2 麦わら帽子 / 3 ひげ
        public bool skirt, apron;
    }

    static readonly Look[] Looks =
    {
        new Look { shirt = new Color(0.55f, 0.35f, 0.25f), pants = new Color(0.3f, 0.3f, 0.35f), hair = new Color(0.25f, 0.15f, 0.08f), style = 3 },
        new Look { shirt = new Color(0.8f, 0.45f, 0.5f), pants = new Color(0.6f, 0.25f, 0.3f), hair = new Color(0.55f, 0.3f, 0.12f), style = 1, skirt = true, apron = true },
        new Look { shirt = new Color(0.3f, 0.5f, 0.7f), pants = new Color(0.35f, 0.3f, 0.25f), hair = new Color(0.1f, 0.1f, 0.1f), style = 0 },
        new Look { shirt = new Color(0.9f, 0.85f, 0.5f), pants = new Color(0.35f, 0.45f, 0.3f), hair = new Color(0.85f, 0.65f, 0.25f), style = 1, skirt = true },
        new Look { shirt = new Color(0.45f, 0.6f, 0.35f), pants = new Color(0.4f, 0.3f, 0.2f), hair = new Color(0.4f, 0.25f, 0.1f), style = 2 },
        new Look { shirt = new Color(0.7f, 0.5f, 0.8f), pants = new Color(0.4f, 0.3f, 0.5f), hair = new Color(0.2f, 0.12f, 0.1f), style = 1, skirt = true, apron = true },
        new Look { shirt = new Color(0.6f, 0.6f, 0.62f), pants = new Color(0.25f, 0.25f, 0.28f), hair = new Color(0.75f, 0.75f, 0.75f), style = 3 },
        new Look { shirt = new Color(0.9f, 0.6f, 0.3f), pants = new Color(0.5f, 0.3f, 0.2f), hair = new Color(0.7f, 0.2f, 0.12f), style = 1, skirt = true },
        new Look { shirt = new Color(0.5f, 0.3f, 0.25f), pants = new Color(0.25f, 0.2f, 0.18f), hair = new Color(0.15f, 0.1f, 0.06f), style = 2, apron = true },
        new Look { shirt = new Color(0.4f, 0.7f, 0.7f), pants = new Color(0.3f, 0.35f, 0.4f), hair = new Color(0.9f, 0.8f, 0.5f), style = 0, skirt = true },
    };

    static readonly Color Skin = new Color(0.92f, 0.76f, 0.62f);

    // ---------------------------------------------------------------- 状態

    public int index;
    int from, to;
    float wait, lateral;
    bool talking;
    Transform player;
    TextMesh label, mark;
    EnemyRig rig;
    GameObject beacon;
    float refreshAt;

    /// <summary>村にいる村人（クエストの報告先の案内に使う）。</summary>
    public static readonly List<VillageNpc> All = new List<VillageNpc>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    public static VillageNpc Find(int index)
    {
        for (int i = All.Count - 1; i >= 0; i--)
        {
            if (All[i] == null) { All.RemoveAt(i); continue; }
            if (All[i].index == index) return All[i];
        }
        return null;
    }

    public string Prompt => QuestSystem.HasQuest(index) ? $"E : {QuestSystem.NpcName(index)}に話しかける（依頼）" : "";
    public bool CanInteract => QuestSystem.HasQuest(index) && !QuestUI.IsOpen;

    public void Interact(GameObject interactor)
    {
        if (!QuestSystem.HasQuest(index) || QuestUI.IsOpen) return;
        var ui = interactor.GetComponent<QuestUI>();
        if (ui == null) ui = interactor.AddComponent<QuestUI>();
        talking = true;
        player = interactor.transform;
        ui.Open(index);
        if (!QuestUI.IsOpen) talking = false;
    }

    public static void SpawnAll(Transform parent)
    {
        var rng = new System.Random(4321);
        for (int i = 0; i < QuestSystem.NpcCount; i++)
        {
            var go = Build(i);
            go.transform.SetParent(parent, false);
            int a = rng.Next(Nodes.Length);
            var n = go.GetComponent<VillageNpc>();
            n.from = a;
            n.to = Next(a, -1, rng);
            float t = (float)rng.NextDouble();
            n.lateral = ((float)rng.NextDouble() - 0.5f) * 1.6f;
            go.transform.position = Vector3.Lerp(Nodes[a], Nodes[n.to], t) + Offset(a, n.to, n.lateral);
        }
    }

    static int Next(int at, int prev, System.Random rng)
    {
        var l = Links[at];
        if (l.Length == 1) return l[0];
        int pick;
        int guard = 0;
        do { pick = l[rng.Next(l.Length)]; } while (pick == prev && ++guard < 8);
        return pick;
    }

    /// <summary>道の左右に少しずらす（村人どうしが重ならないように）。</summary>
    static Vector3 Offset(int a, int b, float lateral)
    {
        Vector3 d = Nodes[b] - Nodes[a];
        d.y = 0f;
        if (d.sqrMagnitude < 0.01f) return Vector3.zero;
        return Vector3.Cross(Vector3.up, d.normalized) * lateral;
    }

    void OnEnable() { QuestSystem.Changed += RefreshMark; if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { QuestSystem.Changed -= RefreshMark; All.Remove(this); }

    void Start() => RefreshMark();

    void RefreshMark()
    {
        if (mark == null) return;
        var q = QuestSystem.ForNpc(index);
        bool show = q != null && q.state != 2;
        mark.gameObject.SetActive(show);
        if (show)
        {
            mark.text = q.state == 1 ? (QuestSystem.CanTurnIn(q) ? "?" : "…") : "!";
            mark.color = q.state == 1 ? (QuestSystem.CanTurnIn(q) ? new Color(0.5f, 1f, 0.6f) : new Color(0.6f, 0.8f, 1f)) : new Color(1f, 0.85f, 0.2f);
        }
        if (label != null) label.color = q != null ? new Color(1f, 0.92f, 0.6f) : new Color(1f, 1f, 1f, 0.85f);

        // 受けている依頼の相手は、遠くからでも見える光の柱が立つ（報告できるときは緑、まだのときは水色）
        bool mine = q != null && q.state == 1;
        if (mine)
        {
            bool ready = QuestSystem.CanTurnIn(q);
            Color c = ready ? new Color(0.45f, 1f, 0.55f) : new Color(0.5f, 0.8f, 1f);
            if (beacon == null)
            {
                beacon = MagicFxExtra.Cylinder(c, 3f);
                beacon.name = "QuestBeacon";
                beacon.transform.SetParent(transform, false);
                beacon.transform.localPosition = new Vector3(0f, 26f, 0f);
                beacon.transform.localScale = new Vector3(0.28f, 26f, 0.28f);
            }
            var m = beacon.GetComponent<Renderer>().material;
            m.color = c;
            m.SetColor("_EmissionColor", c * (ready ? 3.5f : 1.6f));
            beacon.SetActive(true);
        }
        else if (beacon != null) beacon.SetActive(false);
        if (label != null && mine) label.text = QuestSystem.NpcName(index) + (QuestSystem.CanTurnIn(q) ? "  ◀報告先" : "  ◀依頼主");
        else if (label != null) label.text = QuestSystem.NpcName(index);
    }

    void Update()
    {
        // 納品の品を揃えたなど、持ち物の変化で報告できるようになったら、印を更新する
        refreshAt -= Time.deltaTime;
        if (refreshAt <= 0f) { refreshAt = 1f; RefreshMark(); }

        if (player == null)
        {
            var p = FindFirstObjectByType<FirstPersonController>();
            if (p != null) player = p.transform;
        }

        // 話している間は、立ち止まって相手の方を向く
        if (talking)
        {
            if (!QuestUI.IsOpen) talking = false;
            else if (player != null) FaceTo(player.position - transform.position, 10f);
            if (talking) return;
        }

        if (wait > 0f)
        {
            wait -= Time.deltaTime;
            return;
        }

        Vector3 target = Nodes[to] + Offset(from, to, lateral);
        Vector3 d = target - transform.position;
        d.y = 0f;
        if (d.magnitude < 0.4f)
        {
            int prev = from;
            from = to;
            to = Next(to, prev, new System.Random(Random.Range(0, int.MaxValue)));
            lateral = (Random.value - 0.5f) * 1.6f;
            if (Random.value < 0.4f) wait = Random.Range(1.5f, 6f);
            return;
        }

        FaceTo(d, 6f);
        transform.position += d.normalized * 1.4f * Time.deltaTime;
    }

    void FaceTo(Vector3 dir, float speed)
    {
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * speed);
    }

    void LateUpdate()
    {
        // 名前と印は、いつも画面の方を向く
        var cam = Camera.main;
        if (cam == null) return;
        if (label != null) label.transform.rotation = cam.transform.rotation;
        if (mark != null && mark.gameObject.activeSelf)
        {
            mark.transform.rotation = cam.transform.rotation;
            mark.transform.localPosition = new Vector3(0f, 2.55f + Mathf.Sin(Time.time * 3f) * 0.08f, 0f);
        }
    }

    // ---------------------------------------------------------------- 見た目

    static GameObject Build(int i)
    {
        var look = Looks[i % Looks.Length];
        var root = new GameObject("Villager_" + QuestSystem.NpcName(i));
        var npc = root.AddComponent<VillageNpc>();
        npc.index = i;

        var col = root.AddComponent<CapsuleCollider>();
        col.height = 1.8f;
        col.radius = 0.35f;
        col.center = new Vector3(0f, 0.9f, 0f);

        var rig = root.AddComponent<EnemyRig>();
        rig.move = EnemyRig.Move.Biped;
        rig.stride = 28f;
        npc.rig = rig;

        rig.legL = Limb(root.transform, "LegL", new Vector3(0.12f, 0.8f, 0f), 0.8f, 0.18f, look.pants);
        rig.legR = Limb(root.transform, "LegR", new Vector3(-0.12f, 0.8f, 0f), 0.8f, 0.18f, look.pants);
        Part(rig.legL, PrimitiveType.Cube, "Shoe", new Vector3(0f, -0.78f, 0.05f), new Vector3(0.2f, 0.1f, 0.3f), new Color(0.2f, 0.14f, 0.1f));
        Part(rig.legR, PrimitiveType.Cube, "Shoe", new Vector3(0f, -0.78f, 0.05f), new Vector3(0.2f, 0.1f, 0.3f), new Color(0.2f, 0.14f, 0.1f));

        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        body.localPosition = new Vector3(0f, 0.8f, 0f);
        rig.body = body;
        Part(body, PrimitiveType.Cube, "Torso", new Vector3(0f, 0.38f, 0f), new Vector3(0.46f, 0.62f, 0.28f), look.shirt);
        if (look.skirt) Part(root.transform, PrimitiveType.Cylinder, "Skirt", new Vector3(0f, 0.72f, 0f), new Vector3(0.58f, 0.22f, 0.5f), Color.Lerp(look.shirt, look.pants, 0.5f));
        if (look.apron) Part(body, PrimitiveType.Cube, "Apron", new Vector3(0f, 0.2f, 0.16f), new Vector3(0.38f, 0.5f, 0.03f), new Color(0.95f, 0.93f, 0.88f));

        rig.armL = Limb(body, "ArmL", new Vector3(0.3f, 0.65f, 0f), 0.55f, 0.12f, Skin);
        rig.armR = Limb(body, "ArmR", new Vector3(-0.3f, 0.65f, 0f), 0.55f, 0.12f, Skin);
        Part(rig.armL, PrimitiveType.Cube, "Sleeve", new Vector3(0f, -0.14f, 0f), new Vector3(0.15f, 0.3f, 0.15f), look.shirt);
        Part(rig.armR, PrimitiveType.Cube, "Sleeve", new Vector3(0f, -0.14f, 0f), new Vector3(0.15f, 0.3f, 0.15f), look.shirt);

        var head = new GameObject("Head").transform;
        head.SetParent(body, false);
        head.localPosition = new Vector3(0f, 0.98f, 0f);
        rig.head = head;
        Part(head, PrimitiveType.Sphere, "Face", Vector3.zero, Vector3.one * 0.34f, Skin);
        foreach (float s in new[] { 1f, -1f })
            Part(head, PrimitiveType.Sphere, "Eye", new Vector3(s * 0.07f, 0.02f, 0.15f), Vector3.one * 0.04f, Color.black);
        Part(head, PrimitiveType.Cube, "Mouth", new Vector3(0f, -0.07f, 0.16f), new Vector3(0.07f, 0.012f, 0.01f), new Color(0.6f, 0.3f, 0.3f));

        switch (look.style)
        {
            case 0: // 短髪
                Part(head, PrimitiveType.Sphere, "Hair", new Vector3(0f, 0.07f, -0.02f), new Vector3(0.37f, 0.3f, 0.37f), look.hair);
                break;
            case 1: // 長髪
                Part(head, PrimitiveType.Sphere, "Hair", new Vector3(0f, 0.06f, -0.03f), new Vector3(0.38f, 0.32f, 0.38f), look.hair);
                Part(head, PrimitiveType.Cube, "HairBack", new Vector3(0f, -0.15f, -0.13f), new Vector3(0.3f, 0.4f, 0.1f), look.hair);
                break;
            case 2: // 麦わら帽子
                Part(head, PrimitiveType.Cylinder, "HatBrim", new Vector3(0f, 0.13f, 0f), new Vector3(0.62f, 0.015f, 0.62f), new Color(0.88f, 0.75f, 0.35f));
                Part(head, PrimitiveType.Cylinder, "HatTop", new Vector3(0f, 0.2f, 0f), new Vector3(0.32f, 0.08f, 0.32f), new Color(0.88f, 0.75f, 0.35f));
                Part(head, PrimitiveType.Cylinder, "HatBand", new Vector3(0f, 0.15f, 0f), new Vector3(0.33f, 0.015f, 0.33f), new Color(0.6f, 0.25f, 0.2f));
                break;
            default: // ひげ
                Part(head, PrimitiveType.Sphere, "Hair", new Vector3(0f, 0.07f, -0.02f), new Vector3(0.37f, 0.28f, 0.37f), look.hair);
                Part(head, PrimitiveType.Cube, "Beard", new Vector3(0f, -0.14f, 0.1f), new Vector3(0.24f, 0.16f, 0.1f), look.hair);
                break;
        }

        // 名前と、依頼の印
        npc.label = MakeText(root.transform, "Name", QuestSystem.NpcName(i), new Vector3(0f, 2.15f, 0f), 0.035f, new Color(1f, 1f, 1f, 0.85f));
        npc.mark = MakeText(root.transform, "Mark", "!", new Vector3(0f, 2.55f, 0f), 0.09f, new Color(1f, 0.85f, 0.2f));
        npc.mark.gameObject.SetActive(false);
        return root;
    }

    static TextMesh MakeText(Transform parent, string name, string text, Vector3 pos, float size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var tm = go.AddComponent<TextMesh>();
        tm.font = UIKit.Font;
        tm.text = text;
        tm.fontSize = 64;
        tm.characterSize = size;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        go.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;
        return tm;
    }

    static Transform Limb(Transform parent, string name, Vector3 pivot, float len, float thick, Color color)
    {
        var n = new GameObject(name).transform;
        n.SetParent(parent, false);
        n.localPosition = pivot;
        Part(n, PrimitiveType.Cube, "Mesh", new Vector3(0f, -len * 0.5f, 0f), new Vector3(thick, len, thick), color);
        return n;
    }

    static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        SharedMats.Apply(go.GetComponent<Renderer>(), color);
        return go;
    }
}
