using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 敵の見た目（プリミティブの組み立て）。コウモリ・ゴーレム・ゴブリン・魔術師・スケルトン・蜘蛛・精霊・亡霊・騎士・祈祷師・巨人・竜・鷹・死神・悪魔・ミミック。
/// すべて +Z 向き・足元が y=0・高さ約2m で作り、ボスは 1.8 倍になる。動きは EnemyRig が付ける。
/// どの見た目になるかは EnemyData の model（Auto なら、名前から決まる）。
/// </summary>
public static class EnemyModels
{
    // ---------------------------------------------------------------- 見た目の選択

    public static EnemyModel Resolve(EnemyData d)
    {
        if (d == null) return EnemyModel.Default;
        if (d.model != EnemyModel.Auto) return d.model;

        string n = ((d.name ?? "") + " " + (d.id ?? "")).ToLowerInvariant();
        bool Has(string s) => n.Contains(s);

        if (Has("mimic")) return EnemyModel.Mimic;
        if (Has("aqua_fish") || Has("aquafish")) return EnemyModel.Fish;
        if (Has("bone_shark") || Has("boneshark")) return EnemyModel.Shark;
        if (Has("merman")) return EnemyModel.Merman;
        if (Has("tutorial")) return EnemyModel.Goblin;
        if (Has("crystalguardian") || Has("golem") || Has("guardian")) return EnemyModel.Golem;
        if (Has("slime")) return EnemyModel.Slime;
        if (Has("bat")) return EnemyModel.Bat;
        if (Has("goblin")) return EnemyModel.Goblin;
        if (Has("shaman")) return EnemyModel.Shaman;
        if (Has("mage") || Has("hexer")) return EnemyModel.Mage;
        if (Has("skeleton")) return EnemyModel.Skeleton;
        if (Has("spider")) return EnemyModel.Spider;
        if (Has("wisp") || Has("sprite")) return EnemyModel.Wisp;
        if (Has("wraith")) return EnemyModel.Wraith;
        if (Has("knight")) return EnemyModel.Knight;
        if (Has("hawk")) return EnemyModel.Bird;
        if (Has("drake") || Has("dragon")) return EnemyModel.Drake;
        if (Has("reaper")) return EnemyModel.Reaper;
        if (Has("cavelord") || Has("flamewarden") || Has("abyssking")) return EnemyModel.Demon;
        if (Has("swampking") || Has("giant")) return EnemyModel.Giant;
        if (Has("defaultboss")) return EnemyModel.Demon;
        if (Has("defaultenemy")) return EnemyModel.Goblin;
        return d.isBoss ? EnemyModel.Demon : EnemyModel.Default;
    }

    /// <summary>見た目を作る。色を変える（ダメージの光・予備動作の赤）対象の Renderer を返す。Default のときは null。</summary>
    public static Renderer[] Build(Transform visual, EnemyData d, Color color)
    {
        var model = Resolve(d);
        if (model == EnemyModel.Default) return null;

        var root = new GameObject("Model").transform;
        root.SetParent(visual, false);
        if (d.isBoss) root.localScale = Vector3.one * 1.8f;

        var c = new Ctx
        {
            root = root,
            main = color,
            key = ((d.name ?? "") + " " + (d.id ?? "")).ToLowerInvariant(),
            rig = root.gameObject.AddComponent<EnemyRig>(),
        };

        switch (model)
        {
            case EnemyModel.Slime: Slime(c); break;
            case EnemyModel.Bat: Bat(c); break;
            case EnemyModel.Golem: Golem(c); break;
            case EnemyModel.Goblin: Goblin(c); break;
            case EnemyModel.Mage: Mage(c); break;
            case EnemyModel.Skeleton: Skeleton(c); break;
            case EnemyModel.Spider: SpiderModel(c); break;
            case EnemyModel.Wisp: Wisp(c); break;
            case EnemyModel.Wraith: Wraith(c); break;
            case EnemyModel.Knight: Knight(c); break;
            case EnemyModel.Shaman: Shaman(c); break;
            case EnemyModel.Giant: Giant(c); break;
            case EnemyModel.Drake: Drake(c); break;
            case EnemyModel.Bird: Bird(c); break;
            case EnemyModel.Reaper: Reaper(c); break;
            case EnemyModel.Demon: Demon(c); break;
            case EnemyModel.Mimic: MimicModel(c); break;
            case EnemyModel.Fish: FishModel(c); break;
            case EnemyModel.Shark: SharkModel(c); break;
            case EnemyModel.Merman: MermanModel(c); break;
            case EnemyModel.Virus: VirusModel(c); break;
            case EnemyModel.PixelKnight: PixelKnightModel(c); break;
            case EnemyModel.ArcadeMaster: ArcadeMasterModel(c); break;
            case EnemyModel.Bear: BearModel(c); break;
            case EnemyModel.Fairy: FairyModel(c); break;
        }
        Extras(c);
        return c.tint.ToArray();
    }

    // ---------------------------------------------------------------- 組み立ての道具

    class Ctx
    {
        public Transform root;
        public EnemyRig rig;
        public Color main;
        public string key;
        public readonly List<Renderer> tint = new List<Renderer>();

        public bool Has(string s) => key.Contains(s);

        public Transform Node(Transform parent, string name, Vector3 pos, Vector3 euler = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(euler);
            return t;
        }

        /// <summary>パーツ。tinted = true なら、ダメージ・予備動作の色変えの対象になる。</summary>
        public GameObject P(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, bool tinted = false, Vector3 euler = default)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.material.color = color;
            if (tinted) tint.Add(r);
            return go;
        }

        /// <summary>光るパーツ（目・魔法の玉など）。</summary>
        public GameObject Glow(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, float intensity = 3f, Vector3 euler = default)
        {
            var go = P(parent, type, name, pos, scale, color, false, euler);
            var mat = go.GetComponent<Renderer>().material;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * intensity);
            return go;
        }

        /// <summary>肩・腰から垂れ下がる手足（pivot を中心に回る。長さ len の箱が下に伸びる）。</summary>
        public Transform Limb(Transform parent, string name, Vector3 pivot, float len, float thick, Color color, bool tinted = true)
        {
            var n = Node(parent, name, pivot);
            P(n, PrimitiveType.Cube, "Mesh", new Vector3(0f, -len * 0.5f, 0f), new Vector3(thick, len, thick), color, tinted);
            return n;
        }
    }

    static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
    static Color Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);
    static Color Light(Color c, float k) => Color.Lerp(c, Color.white, k);
    static readonly Color Skin = new Color(0.9f, 0.74f, 0.6f);
    static readonly Color Wood = new Color(0.4f, 0.27f, 0.14f);
    static readonly Color Steel = new Color(0.72f, 0.74f, 0.8f);
    static readonly Color Bone = new Color(0.92f, 0.9f, 0.82f);
    static readonly Color Gold = new Color(0.95f, 0.78f, 0.3f);
    static readonly float[] Sides = { 1f, -1f };

    // ---------------------------------------------------------------- 追加の敵の飾り（つらら・フード・導火線・雷・岩）

    static void Extras(Ctx c)
    {
        var rig = c.rig;
        Transform body = rig.body != null ? rig.body : c.root;
        var ice = new Color(0.78f, 0.95f, 1f);

        // つらら（フロストゴーレム・アイスゴブリン）
        if (c.Has("frostgolem") || c.Has("icegoblin"))
        {
            bool golem = c.Has("golem");
            float y = golem ? 1.2f : 0.58f, w = golem ? 1.1f : 0.45f, z = golem ? 0.42f : 0.18f;
            int n = golem ? 10 : 6;
            for (int i = 0; i < n; i++)
            {
                float x = Random.Range(-w * 0.5f, w * 0.5f);
                float len = Random.Range(0.18f, golem ? 0.5f : 0.28f);
                bool front = i % 2 == 0;
                c.Glow(body, PrimitiveType.Cube, "Icicle", V(x, y - len * 0.5f, front ? z : -z), V(len * 0.22f, len, len * 0.22f), ice, 0.8f, V(Random.Range(-8f, 8f), 45f, Random.Range(-8f, 8f)));
            }
            if (rig.head != null)
                for (int i = 0; i < 3; i++)
                    c.Glow(rig.head, PrimitiveType.Cube, "Frost", V(Random.Range(-0.15f, 0.15f), golem ? 0.3f : 0.22f, Random.Range(-0.1f, 0.1f)), V(0.06f, Random.Range(0.12f, 0.25f), 0.06f), ice, 0.8f, V(Random.Range(-25f, 25f), 45f, Random.Range(-25f, 25f)));
            // 体に貼りついた氷
            c.P(body, PrimitiveType.Cube, "IceCrust", V(golem ? 0.3f : 0.1f, golem ? 0.9f : 0.35f, golem ? 0.4f : 0.15f), golem ? V(0.5f, 0.4f, 0.08f) : V(0.22f, 0.18f, 0.05f), new Color(0.85f, 0.97f, 1f), false, V(0f, 0f, 15f));
        }

        // シャドウアサシン: フード・覆面・光る紫の目・二本の短剣
        if (c.Has("assassin") && rig.head != null)
        {
            var cloth = new Color(0.08f, 0.07f, 0.11f);
            c.P(rig.head, PrimitiveType.Sphere, "Hood", V(0f, 0.04f, -0.03f), V(0.58f, 0.55f, 0.55f), cloth, true);
            c.P(rig.head, PrimitiveType.Cube, "Mask", V(0f, -0.08f, 0.2f), V(0.36f, 0.16f, 0.08f), new Color(0.15f, 0.13f, 0.18f));
            foreach (float s in Sides) c.Glow(rig.head, PrimitiveType.Sphere, "ShadowEye", V(s * 0.11f, 0.06f, 0.25f), Vector3.one * 0.07f, new Color(0.75f, 0.35f, 1f), 4f);
            c.P(body, PrimitiveType.Cube, "Scarf", V(0f, 0.62f, -0.12f), V(0.22f, 0.5f, 0.04f), new Color(0.35f, 0.08f, 0.15f), false, V(20f, 0f, 0f));
            if (rig.armL != null)
            {
                c.P(rig.armL, PrimitiveType.Cube, "Handle", V(0f, -0.52f, 0.03f), V(0.06f, 0.06f, 0.16f), Wood);
                c.Glow(rig.armL, PrimitiveType.Cube, "Blade", V(0f, -0.52f, 0.3f), V(0.04f, 0.07f, 0.45f), new Color(0.6f, 0.4f, 0.9f), 1.2f);
            }
        }

        // ボムスライム: 導火線と、火花
        if (c.Has("bombslime"))
        {
            c.P(body, PrimitiveType.Cylinder, "Fuse", V(0f, 1.2f, 0f), V(0.08f, 0.15f, 0.08f), new Color(0.25f, 0.2f, 0.15f), false, V(10f, 0f, 0f));
            c.Glow(body, PrimitiveType.Sphere, "Spark", V(0f, 1.38f, 0.04f), Vector3.one * 0.14f, new Color(1f, 0.7f, 0.2f), 5f);
            c.P(body, PrimitiveType.Cube, "Mark", V(0f, 0.75f, 0.74f), V(0.3f, 0.06f, 0.02f), Shade(c.main, 0.35f), false, V(0f, 0f, 45f));
            c.P(body, PrimitiveType.Cube, "Mark", V(0f, 0.75f, 0.74f), V(0.3f, 0.06f, 0.02f), Shade(c.main, 0.35f), false, V(0f, 0f, -45f));
        }

        // エレキバット: 光る稲妻の模様
        if (c.Has("electricbat"))
        {
            var y = new Color(1f, 0.95f, 0.35f);
            c.Glow(body, PrimitiveType.Cube, "Bolt", V(0f, 0.05f, 0.27f), V(0.06f, 0.3f, 0.02f), y, 3f, V(0f, 0f, 25f));
            c.Glow(body, PrimitiveType.Cube, "Bolt", V(0.04f, -0.12f, 0.27f), V(0.05f, 0.2f, 0.02f), y, 3f, V(0f, 0f, -30f));
            if (rig.wingL != null) c.Glow(rig.wingL, PrimitiveType.Cube, "Vein", V(0.55f, 0.02f, 0f), V(0.9f, 0.03f, 0.04f), y, 2f, V(0f, 20f, 0f));
            if (rig.wingR != null) c.Glow(rig.wingR, PrimitiveType.Cube, "Vein", V(-0.55f, 0.02f, 0f), V(0.9f, 0.03f, 0.04f), y, 2f, V(0f, -20f, 0f));
        }

        // 雷の魔道士: 帽子に稲妻
        if (c.Has("mage_thunder") && rig.head != null)
        {
            var y = new Color(1f, 0.92f, 0.3f);
            c.Glow(rig.head, PrimitiveType.Cube, "Bolt", V(0.12f, 0.35f, 0.2f), V(0.05f, 0.22f, 0.02f), y, 3f, V(0f, 0f, 25f));
            c.Glow(rig.head, PrimitiveType.Cube, "Bolt", V(0.14f, 0.2f, 0.22f), V(0.05f, 0.15f, 0.02f), y, 3f, V(0f, 0f, -30f));
        }

        // グランドゴーレム: 背中の岩の山と、苔むした肩
        if (c.Has("grandgolem"))
        {
            var rock = Shade(c.main, 0.8f);
            for (int i = 0; i < 6; i++)
                c.P(body, PrimitiveType.Cube, "BackRock", V(Random.Range(-0.5f, 0.5f), Random.Range(1.1f, 1.6f), Random.Range(-0.55f, -0.35f)), Vector3.one * Random.Range(0.3f, 0.55f), rock, true, V(Random.Range(0f, 45f), Random.Range(0f, 45f), Random.Range(0f, 45f)));
            foreach (float s in Sides)
                c.P(body, PrimitiveType.Cube, "Spike", V(s * 0.9f, 1.45f, 0f), V(0.18f, 0.55f, 0.18f), Shade(c.main, 0.6f), true, V(0f, 45f, s * -25f));
        }

        // ホノトリ: 炎をまとう尾と冠
        if (c.Has("honotori"))
        {
            var fire = new Color(1f, 0.55f, 0.12f);
            if (rig.head != null) for (int i = 0; i < 3; i++) c.Glow(rig.head, PrimitiveType.Cube, "Crest", V(0f, 0.18f + i * 0.04f, -0.08f - i * 0.08f), V(0.05f, 0.18f, 0.05f), fire, 3f, V(-30f - i * 10f, 0f, 0f));
            if (rig.tail != null) for (int i = 0; i < 4; i++) c.Glow(rig.tail, PrimitiveType.Cube, "FlameTail", V((i - 1.5f) * 0.08f, 0f, -0.3f - i * 0.05f), V(0.06f, 0.03f, 0.6f), fire, 3f, V(0f, (i - 1.5f) * 12f, 0f));
        }

        // ロックブレイカー: 兜から、目だけが光る
        if (c.Has("rockbreaker") && rig.head != null)
        {
            c.P(rig.head, PrimitiveType.Cube, "Visor", V(0f, 0.02f, 0.235f), V(0.36f, 0.12f, 0.02f), new Color(0.05f, 0.05f, 0.06f));
            foreach (float s in Sides) c.Glow(rig.head, PrimitiveType.Sphere, "GlowEye", V(s * 0.08f, 0.02f, 0.25f), Vector3.one * 0.06f, new Color(1f, 0.55f, 0.15f), 5f);
        }

        // ロックナイト: 岩の鎧
        if (c.Has("rockknight"))
        {
            var glow = new Color(1f, 0.55f, 0.18f);
            c.P(body, PrimitiveType.Cube, "RockPlate", V(0f, 1.05f, 0.25f), V(0.55f, 0.45f, 0.1f), Shade(c.main, 0.75f), true, V(0f, 0f, 8f));
            c.Glow(body, PrimitiveType.Cube, "Vein", V(0.05f, 1.05f, 0.31f), V(0.04f, 0.38f, 0.02f), glow, 2f, V(0f, 0f, 20f));
        }
    }

    // ---------------------------------------------------------------- スライム

    static void Slime(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Slime;
        var body = c.Node(c.root, "Body", Vector3.zero);
        rig.body = body;

        c.P(body, PrimitiveType.Sphere, "Gel", V(0f, 0.55f, 0f), V(1.5f, 1.1f, 1.5f), c.main, true);
        c.P(body, PrimitiveType.Sphere, "Drop", V(0.3f, 1.1f, -0.1f), V(0.35f, 0.3f, 0.35f), Light(c.main, 0.15f), true);
        c.P(body, PrimitiveType.Sphere, "Shine", V(-0.4f, 0.95f, 0.4f), V(0.3f, 0.18f, 0.2f), Color.white, false, V(0f, 0f, 30f));
        foreach (float s in Sides)
        {
            c.P(body, PrimitiveType.Sphere, "Eye", V(s * 0.25f, 0.65f, 0.62f), Vector3.one * 0.3f, Color.white);
            c.P(body, PrimitiveType.Sphere, "Pupil", V(s * 0.25f, 0.63f, 0.76f), Vector3.one * 0.14f, Color.black);
        }
        c.P(body, PrimitiveType.Cube, "Mouth", V(0f, 0.4f, 0.72f), V(0.22f, 0.04f, 0.05f), Shade(c.main, 0.3f));
    }

    // ---------------------------------------------------------------- コウモリ

    static void Bat(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Flyer;
        rig.flapSpeed = 18f;
        rig.wingAmp = 55f;
        var dark = Shade(c.main, 0.7f);

        var body = c.Node(c.root, "Body", V(0f, 1.2f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Sphere, "Torso", V(0f, 0f, 0f), V(0.55f, 0.7f, 0.5f), c.main, true);
        c.P(body, PrimitiveType.Sphere, "Fur", V(0f, 0.05f, 0.12f), V(0.4f, 0.4f, 0.3f), Light(c.main, 0.2f), true);

        var head = c.Node(body, "Head", V(0f, 0.4f, 0.15f));
        rig.head = head;
        c.P(head, PrimitiveType.Sphere, "Skull", Vector3.zero, Vector3.one * 0.45f, c.main, true);
        c.P(head, PrimitiveType.Cube, "Nose", V(0f, -0.06f, 0.2f), V(0.12f, 0.08f, 0.08f), Shade(c.main, 0.4f));
        foreach (float s in Sides)
        {
            c.P(head, PrimitiveType.Cube, "Ear", V(s * 0.15f, 0.3f, -0.02f), V(0.1f, 0.3f, 0.06f), dark, true, V(0f, 0f, -s * 15f));
            c.Glow(head, PrimitiveType.Sphere, "Eye", V(s * 0.1f, 0.05f, 0.19f), Vector3.one * 0.08f, new Color(1f, 0.15f, 0.1f));
            c.P(head, PrimitiveType.Cube, "Fang", V(s * 0.05f, -0.15f, 0.19f), V(0.035f, 0.1f, 0.035f), Color.white);
        }

        var wings = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            float s = Sides[i];
            var w = c.Node(body, "Wing", V(s * 0.2f, 0.15f, 0f));
            c.P(w, PrimitiveType.Cube, "Membrane", V(s * 0.55f, -0.05f, -0.1f), V(1.1f, 0.03f, 0.75f), dark, true);
            c.P(w, PrimitiveType.Cube, "Bone", V(s * 0.55f, 0.02f, 0.25f), V(1.1f, 0.05f, 0.05f), Shade(c.main, 0.5f));
            c.P(w, PrimitiveType.Cube, "Tip", V(s * 1.05f, -0.05f, -0.3f), V(0.4f, 0.03f, 0.5f), Shade(c.main, 0.55f), true, V(0f, s * 20f, 0f));
            wings[i] = w;
        }
        rig.wingL = wings[0];
        rig.wingR = wings[1];

        var legs = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            var l = c.Node(body, "Foot", V(Sides[i] * 0.12f, -0.3f, 0f));
            c.P(l, PrimitiveType.Cube, "Claw", V(0f, -0.12f, 0.02f), V(0.05f, 0.25f, 0.05f), Shade(c.main, 0.4f));
            legs[i] = l;
        }
        rig.legL = legs[0];
        rig.legR = legs[1];
    }

    // ---------------------------------------------------------------- ゴーレム（岩・溶岩・水晶・守護者）

    static void Golem(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Biped;
        rig.stride = 22f;
        rig.bobAmp = 0.02f;
        bool lava = c.Has("lava");
        bool crystal = c.Has("crystal");
        var rock = c.main;
        var rockDark = Shade(rock, 0.7f);
        var eyeColor = lava ? new Color(1f, 0.5f, 0.1f) : crystal ? new Color(0.5f, 0.9f, 1f) : new Color(1f, 0.7f, 0.25f);

        var legL = c.Limb(c.root, "LegL", V(0.35f, 0.8f, 0f), 0.8f, 0.5f, rockDark);
        var legR = c.Limb(c.root, "LegR", V(-0.35f, 0.8f, 0f), 0.8f, 0.5f, rockDark);
        c.P(legL, PrimitiveType.Cube, "Foot", V(0f, -0.75f, 0.1f), V(0.6f, 0.25f, 0.75f), Shade(rock, 0.55f), true);
        c.P(legR, PrimitiveType.Cube, "Foot", V(0f, -0.75f, 0.1f), V(0.6f, 0.25f, 0.75f), Shade(rock, 0.55f), true);
        rig.legL = legL;
        rig.legR = legR;

        var body = c.Node(c.root, "Body", V(0f, 0.8f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Cube, "Torso", V(0f, 0.6f, 0f), V(1.3f, 1.1f, 0.85f), rock, true);
        c.P(body, PrimitiveType.Cube, "Chest", V(0f, 0.7f, 0.36f), V(0.85f, 0.7f, 0.3f), Light(rock, 0.08f), true, V(0f, 0f, 4f));
        c.P(body, PrimitiveType.Cube, "Waist", V(0f, 0.05f, 0f), V(0.9f, 0.3f, 0.7f), rockDark, true);

        foreach (float s in Sides)
        {
            c.P(body, PrimitiveType.Cube, "Shoulder", V(s * 0.85f, 1.1f, 0f), V(0.6f, 0.5f, 0.6f), Shade(rock, 0.85f), true, V(10f, 0f, s * 15f));
            c.P(body, PrimitiveType.Cube, "Rock", V(s * 0.6f, 1.32f, -0.1f), V(0.25f, 0.2f, 0.25f), Shade(rock, 0.6f), true, V(20f, 30f, 10f));
        }

        var armL = c.Limb(body, "ArmL", V(0.85f, 1.05f, 0f), 1.0f, 0.38f, rock);
        var armR = c.Limb(body, "ArmR", V(-0.85f, 1.05f, 0f), 1.0f, 0.38f, rock);
        c.P(armL, PrimitiveType.Cube, "Fist", V(0f, -1.05f, 0f), Vector3.one * 0.6f, rockDark, true);
        c.P(armR, PrimitiveType.Cube, "Fist", V(0f, -1.05f, 0f), Vector3.one * 0.6f, rockDark, true);
        rig.armL = armL;
        rig.armR = armR;

        var head = c.Node(body, "Head", V(0f, 1.4f, 0.08f));
        rig.head = head;
        c.P(head, PrimitiveType.Cube, "Skull", Vector3.zero, V(0.55f, 0.5f, 0.5f), rock, true);
        c.P(head, PrimitiveType.Cube, "Brow", V(0f, 0.12f, 0.2f), V(0.6f, 0.1f, 0.15f), rockDark, true);
        foreach (float s in Sides)
            c.Glow(head, PrimitiveType.Cube, "Eye", V(s * 0.13f, 0.02f, 0.26f), V(0.12f, 0.07f, 0.04f), eyeColor);

        if (lava)
        {
            var glow = new Color(1f, 0.45f, 0.08f);
            c.Glow(body, PrimitiveType.Cube, "Crack", V(0f, 0.7f, 0.43f), V(0.06f, 0.75f, 0.02f), glow, 3f, V(0f, 0f, 10f));
            c.Glow(body, PrimitiveType.Cube, "Crack", V(0.3f, 0.5f, 0.43f), V(0.05f, 0.4f, 0.02f), glow, 3f, V(0f, 0f, -35f));
            c.Glow(body, PrimitiveType.Cube, "Crack", V(-0.3f, 0.85f, 0.43f), V(0.05f, 0.4f, 0.02f), glow, 3f, V(0f, 0f, 40f));
            c.Glow(armL, PrimitiveType.Cube, "Crack", V(0f, -0.4f, 0.2f), V(0.05f, 0.5f, 0.02f), glow, 3f);
            c.Glow(armR, PrimitiveType.Cube, "Crack", V(0f, -0.4f, 0.2f), V(0.05f, 0.5f, 0.02f), glow, 3f);
        }
        if (crystal)
        {
            var cr = new Color(0.55f, 0.85f, 1f);
            foreach (float s in Sides)
            {
                c.Glow(body, PrimitiveType.Cube, "Crystal", V(s * 0.75f, 1.55f, -0.1f), V(0.22f, 0.75f, 0.22f), cr, 1.6f, V(10f, 20f, s * 20f));
                c.Glow(body, PrimitiveType.Cube, "Crystal", V(s * 0.35f, 1.3f, -0.5f), V(0.2f, 0.6f, 0.2f), cr, 1.6f, V(-25f, 0f, s * 15f));
            }
            c.Glow(body, PrimitiveType.Cube, "Crystal", V(0f, 1.5f, -0.45f), V(0.25f, 0.85f, 0.25f), cr, 1.6f, V(-20f, 0f, 0f));
        }
        else if (!lava)
        {
            // 苔
            c.P(body, PrimitiveType.Cube, "Moss", V(-0.35f, 1.18f, 0.2f), V(0.5f, 0.08f, 0.35f), new Color(0.3f, 0.45f, 0.2f), false, V(0f, 20f, 8f));
        }
    }

    // ---------------------------------------------------------------- ゴブリン

    static void Goblin(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Biped;
        rig.stride = 38f;
        var skin = c.main;
        var cloth = new Color(0.45f, 0.3f, 0.15f);

        rig.legL = c.Limb(c.root, "LegL", V(0.15f, 0.55f, 0f), 0.55f, 0.16f, Shade(skin, 0.85f));
        rig.legR = c.Limb(c.root, "LegR", V(-0.15f, 0.55f, 0f), 0.55f, 0.16f, Shade(skin, 0.85f));
        c.P(rig.legL, PrimitiveType.Cube, "Foot", V(0f, -0.52f, 0.06f), V(0.2f, 0.1f, 0.32f), Shade(skin, 0.6f), true);
        c.P(rig.legR, PrimitiveType.Cube, "Foot", V(0f, -0.52f, 0.06f), V(0.2f, 0.1f, 0.32f), Shade(skin, 0.6f), true);

        var body = c.Node(c.root, "Body", V(0f, 0.55f, 0f), V(8f, 0f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Cube, "Torso", V(0f, 0.3f, 0f), V(0.5f, 0.55f, 0.32f), skin, true);
        c.P(body, PrimitiveType.Cube, "Cloth", V(0f, 0.02f, 0.02f), V(0.55f, 0.28f, 0.36f), cloth);
        c.P(body, PrimitiveType.Cube, "Strap", V(0f, 0.35f, 0.17f), V(0.12f, 0.6f, 0.03f), Wood, false, V(0f, 0f, 25f));

        var armL = c.Limb(body, "ArmL", V(0.32f, 0.5f, 0f), 0.5f, 0.13f, skin);
        var armR = c.Limb(body, "ArmR", V(-0.32f, 0.5f, 0f), 0.5f, 0.13f, skin);
        rig.armL = armL;
        rig.armR = armR;
        // 短剣
        c.P(armR, PrimitiveType.Cube, "Handle", V(0f, -0.52f, 0.03f), V(0.06f, 0.06f, 0.16f), Wood);
        c.P(armR, PrimitiveType.Cube, "Blade", V(0f, -0.52f, 0.3f), V(0.05f, 0.08f, 0.45f), Steel);

        var head = c.Node(body, "Head", V(0f, 0.75f, 0.05f));
        rig.head = head;
        c.P(head, PrimitiveType.Sphere, "Skull", Vector3.zero, V(0.5f, 0.45f, 0.45f), skin, true);
        c.P(head, PrimitiveType.Cube, "Nose", V(0f, -0.03f, 0.24f), V(0.1f, 0.12f, 0.14f), Shade(skin, 0.9f), true);
        foreach (float s in Sides)
        {
            c.P(head, PrimitiveType.Cube, "Ear", V(s * 0.32f, 0.03f, -0.05f), V(0.32f, 0.07f, 0.06f), skin, true, V(0f, 0f, s * 20f));
            c.Glow(head, PrimitiveType.Sphere, "Eye", V(s * 0.12f, 0.06f, 0.2f), Vector3.one * 0.08f, new Color(1f, 0.85f, 0.2f), 2f);
            c.P(head, PrimitiveType.Cube, "Tooth", V(s * 0.07f, -0.14f, 0.2f), V(0.04f, 0.08f, 0.03f), Color.white);
        }
    }

    // ---------------------------------------------------------------- 魔術師（火・水・風・光・闇・呪術師）

    static void Mage(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Floater;
        rig.hover = 0.1f;
        var robe = c.main;
        var trim = Gold;
        var orb = Light(c.main, 0.45f);

        var body = c.Node(c.root, "Body", Vector3.zero);
        rig.body = body;
        c.P(body, PrimitiveType.Cylinder, "RobeLower", V(0f, 0.6f, 0f), V(0.95f, 0.6f, 0.95f), robe, true);
        c.P(body, PrimitiveType.Cylinder, "RobeHem", V(0f, 0.04f, 0f), V(1.0f, 0.04f, 1.0f), trim);
        c.P(body, PrimitiveType.Cylinder, "Chest", V(0f, 1.4f, 0f), V(0.7f, 0.3f, 0.55f), Shade(robe, 0.9f), true);
        c.P(body, PrimitiveType.Cylinder, "Belt", V(0f, 1.1f, 0f), V(0.72f, 0.04f, 0.6f), trim);
        c.P(body, PrimitiveType.Sphere, "Gem", V(0f, 1.1f, 0.3f), Vector3.one * 0.1f, orb);
        c.P(body, PrimitiveType.Cube, "Cape", V(0f, 1.15f, -0.3f), V(0.6f, 0.9f, 0.05f), Shade(robe, 0.7f), true, V(6f, 0f, 0f));

        var head = c.Node(body, "Head", V(0f, 1.8f, 0f));
        rig.head = head;
        c.P(head, PrimitiveType.Sphere, "Face", Vector3.zero, Vector3.one * 0.38f, Skin);
        foreach (float s in Sides) c.P(head, PrimitiveType.Sphere, "Eye", V(s * 0.08f, 0.02f, 0.16f), Vector3.one * 0.05f, Color.black);
        c.P(head, PrimitiveType.Cube, "Beard", V(0f, -0.17f, 0.12f), V(0.2f, 0.18f, 0.08f), new Color(0.85f, 0.85f, 0.85f));
        c.P(head, PrimitiveType.Cylinder, "Brim", V(0f, 0.17f, 0f), V(0.85f, 0.025f, 0.85f), Shade(robe, 0.8f), true);
        c.P(head, PrimitiveType.Cylinder, "Hat1", V(0f, 0.3f, 0f), V(0.5f, 0.12f, 0.5f), Shade(robe, 0.85f), true);
        c.P(head, PrimitiveType.Cylinder, "Hat2", V(0f, 0.45f, -0.01f), V(0.36f, 0.1f, 0.36f), Shade(robe, 0.85f), true);
        c.P(head, PrimitiveType.Cylinder, "Hat3", V(0f, 0.58f, -0.04f), V(0.2f, 0.1f, 0.2f), Shade(robe, 0.85f), true, V(-12f, 0f, 0f));
        c.P(head, PrimitiveType.Cylinder, "HatBand", V(0f, 0.2f, 0f), V(0.52f, 0.025f, 0.52f), trim);

        var armL = c.Limb(body, "ArmL", V(0.38f, 1.55f, 0f), 0.6f, 0.14f, robe);
        var armR = c.Limb(body, "ArmR", V(-0.38f, 1.55f, 0f), 0.6f, 0.14f, robe);
        c.P(armL, PrimitiveType.Sphere, "Hand", V(0f, -0.62f, 0f), Vector3.one * 0.15f, Skin);
        c.P(armR, PrimitiveType.Sphere, "Hand", V(0f, -0.62f, 0f), Vector3.one * 0.15f, Skin);
        rig.armL = armL;
        rig.armR = armR;

        // 杖（先に属性の色の玉）
        c.P(armR, PrimitiveType.Cylinder, "Staff", V(0f, -0.55f, 0.12f), V(0.05f, 0.95f, 0.05f), Wood);
        c.Glow(armR, PrimitiveType.Sphere, "Orb", V(0f, 0.42f, 0.12f), Vector3.one * 0.24f, orb, 2.5f);
        c.P(armR, PrimitiveType.Cube, "Claw", V(0f, 0.32f, 0.12f), V(0.1f, 0.05f, 0.1f), trim);
    }

    // ---------------------------------------------------------------- スケルトン

    static void Skeleton(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Biped;
        rig.stride = 30f;
        var bone = Color.Lerp(Bone, c.main, 0.2f);

        rig.legL = c.Limb(c.root, "LegL", V(0.15f, 0.95f, 0f), 0.95f, 0.1f, bone);
        rig.legR = c.Limb(c.root, "LegR", V(-0.15f, 0.95f, 0f), 0.95f, 0.1f, bone);
        c.P(rig.legL, PrimitiveType.Cube, "Foot", V(0f, -0.92f, 0.07f), V(0.12f, 0.06f, 0.28f), bone, true);
        c.P(rig.legR, PrimitiveType.Cube, "Foot", V(0f, -0.92f, 0.07f), V(0.12f, 0.06f, 0.28f), bone, true);
        c.P(rig.legL, PrimitiveType.Sphere, "Knee", V(0f, -0.48f, 0f), Vector3.one * 0.13f, bone, true);
        c.P(rig.legR, PrimitiveType.Sphere, "Knee", V(0f, -0.48f, 0f), Vector3.one * 0.13f, bone, true);

        var body = c.Node(c.root, "Body", V(0f, 0.95f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Cube, "Pelvis", V(0f, 0.05f, 0f), V(0.38f, 0.14f, 0.2f), bone, true);
        c.P(body, PrimitiveType.Cylinder, "Spine", V(0f, 0.4f, -0.03f), V(0.06f, 0.3f, 0.06f), bone, true);
        for (int i = 0; i < 3; i++)
            c.P(body, PrimitiveType.Cube, "Rib", V(0f, 0.36f + i * 0.14f, 0.03f), V(0.44f - i * 0.05f, 0.05f, 0.24f), bone, true);
        c.P(body, PrimitiveType.Cube, "Collar", V(0f, 0.8f, 0f), V(0.52f, 0.05f, 0.12f), bone, true);

        var head = c.Node(body, "Head", V(0f, 1.0f, 0f));
        rig.head = head;
        c.P(head, PrimitiveType.Sphere, "Skull", V(0f, 0.1f, 0f), V(0.34f, 0.38f, 0.36f), bone, true);
        c.P(head, PrimitiveType.Cube, "Jaw", V(0f, -0.06f, 0.08f), V(0.2f, 0.08f, 0.2f), bone, true);
        foreach (float s in Sides)
        {
            c.P(head, PrimitiveType.Cube, "Socket", V(s * 0.08f, 0.12f, 0.15f), V(0.09f, 0.1f, 0.05f), Color.black);
            c.Glow(head, PrimitiveType.Sphere, "Eye", V(s * 0.08f, 0.12f, 0.18f), Vector3.one * 0.045f, new Color(1f, 0.3f, 0.2f));
        }

        var armL = c.Limb(body, "ArmL", V(0.3f, 0.85f, 0f), 0.75f, 0.07f, bone);
        var armR = c.Limb(body, "ArmR", V(-0.3f, 0.85f, 0f), 0.75f, 0.07f, bone);
        rig.armL = armL;
        rig.armR = armR;
        // 剣
        c.P(armR, PrimitiveType.Cube, "Guard", V(0f, -0.78f, 0f), V(0.3f, 0.04f, 0.07f), Wood);
        c.P(armR, PrimitiveType.Cube, "Blade", V(0f, -1.3f, 0f), V(0.07f, 1.0f, 0.025f), Steel);
        // 欠けた盾
        c.P(armL, PrimitiveType.Cube, "Shield", V(0.12f, -0.5f, 0.1f), V(0.05f, 0.5f, 0.4f), Wood, false, V(0f, 0f, 0f));
    }

    // ---------------------------------------------------------------- 蜘蛛

    static void SpiderModel(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Spider;
        rig.bobAmp = 0.02f;
        var dark = Shade(c.main, 0.7f);

        var body = c.Node(c.root, "Body", V(0f, 0.75f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Sphere, "Abdomen", V(0f, 0.15f, -0.8f), V(1.0f, 0.9f, 1.25f), c.main, true);
        c.Glow(body, PrimitiveType.Cube, "Mark", V(0f, 0.62f, -0.8f), V(0.16f, 0.03f, 0.32f), new Color(1f, 0.2f, 0.1f), 2f);
        c.P(body, PrimitiveType.Sphere, "Thorax", V(0f, 0.1f, 0.05f), V(0.7f, 0.55f, 0.7f), dark, true);
        c.P(body, PrimitiveType.Sphere, "Head", V(0f, 0.12f, 0.5f), Vector3.one * 0.45f, dark, true);
        foreach (float s in Sides)
        {
            c.Glow(body, PrimitiveType.Sphere, "Eye", V(s * 0.1f, 0.24f, 0.7f), Vector3.one * 0.09f, new Color(1f, 0.15f, 0.1f));
            c.Glow(body, PrimitiveType.Sphere, "Eye", V(s * 0.17f, 0.15f, 0.71f), Vector3.one * 0.07f, new Color(1f, 0.15f, 0.1f));
            c.P(body, PrimitiveType.Cube, "Fang", V(s * 0.08f, -0.08f, 0.72f), V(0.06f, 0.26f, 0.06f), Bone, false, V(-15f, 0f, 0f));
        }

        // 脚 8本（片側4本。外側が +x になるように回す）
        float[] yaw = { -42f, -14f, 14f, 42f };
        var legs = new List<Transform>();
        for (int i = 0; i < 4; i++)
        {
            foreach (float s in Sides)
            {
                float baseYaw = s > 0f ? -yaw[i] : 180f + yaw[i];
                var pivot = c.Node(c.root, "Leg", V(s * 0.28f, 0.8f, 0.4f - i * 0.28f), V(0f, baseYaw, 0f));
                c.P(pivot, PrimitiveType.Cube, "Upper", V(0.3f, 0.13f, 0f), V(0.7f, 0.09f, 0.09f), dark, true, V(0f, 0f, 35f));
                c.P(pivot, PrimitiveType.Sphere, "Knee", V(0.55f, 0.32f, 0f), Vector3.one * 0.12f, c.main, true);
                c.P(pivot, PrimitiveType.Cube, "Lower", V(0.82f, -0.1f, 0f), V(0.85f, 0.07f, 0.07f), dark, true, V(0f, 0f, -52f));
                legs.Add(pivot);
            }
        }
        rig.legs = legs.ToArray();
    }

    // ---------------------------------------------------------------- 精霊（光の精・風の精霊）

    static void Wisp(Ctx c)
    {
        var rig = c.rig;
        bool wind = c.Has("wind") || c.Has("sprite");
        rig.move = wind ? EnemyRig.Move.Flyer : EnemyRig.Move.Floater;
        rig.flapSpeed = 20f;
        rig.wingAmp = 35f;
        var glow = Light(c.main, 0.4f);

        var body = c.Node(c.root, "Body", V(0f, 1f, 0f));
        rig.body = body;
        c.Glow(body, PrimitiveType.Sphere, "Core", Vector3.zero, Vector3.one * 0.55f, glow, 1.2f);
        c.tint.Add(body.GetChild(0).GetComponent<Renderer>());
        c.Glow(body, PrimitiveType.Sphere, "Heart", Vector3.zero, Vector3.one * 0.3f, Color.white, 2f);
        foreach (float s in Sides)
        {
            c.P(body, PrimitiveType.Sphere, "Eye", V(s * 0.12f, 0.05f, 0.23f), Vector3.one * 0.08f, Color.black);
        }

        var lightGo = new GameObject("Light");
        lightGo.transform.SetParent(body, false);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = glow;
        l.range = 5f;
        l.intensity = 3f;

        var orbit = c.Node(body, "Orbit", Vector3.zero);
        rig.orbit = orbit;
        for (int i = 0; i < 3; i++)
        {
            float a = i * 120f * Mathf.Deg2Rad;
            c.Glow(orbit, PrimitiveType.Sphere, "Mote", V(Mathf.Cos(a) * 0.5f, (i - 1) * 0.15f, Mathf.Sin(a) * 0.5f), Vector3.one * 0.14f, glow, 2f);
        }

        var tail = c.Node(body, "Tail", V(0f, -0.2f, -0.25f));
        rig.tail = tail;
        for (int i = 0; i < 3; i++)
            c.Glow(tail, PrimitiveType.Sphere, "Trail", V(0f, -0.1f * (i + 1), -0.15f * (i + 1)), Vector3.one * (0.22f - i * 0.06f), glow, 1.2f);

        if (wind)
        {
            var wings = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                float s = Sides[i];
                var w = c.Node(body, "Wing", V(s * 0.2f, 0.05f, -0.05f));
                var wing = c.Glow(w, PrimitiveType.Cube, "Feather", V(s * 0.4f, 0.05f, -0.05f), V(0.8f, 0.03f, 0.45f), new Color(0.6f, 1f, 0.8f), 1f, V(0f, s * 12f, 0f));
                c.tint.Add(wing.GetComponent<Renderer>());
                c.Glow(w, PrimitiveType.Cube, "Feather", V(s * 0.65f, 0.1f, -0.25f), V(0.5f, 0.03f, 0.3f), new Color(0.6f, 1f, 0.8f), 1f, V(0f, s * 25f, 0f));
                wings[i] = w;
            }
            rig.wingL = wings[0];
            rig.wingR = wings[1];
        }
    }

    // ---------------------------------------------------------------- 亡霊

    static void Wraith(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Floater;
        rig.hover = 0.05f;
        var cloak = c.main;

        var body = c.Node(c.root, "Body", Vector3.zero);
        rig.body = body;
        c.P(body, PrimitiveType.Cylinder, "Cloak1", V(0f, 1.2f, 0f), V(0.9f, 0.35f, 0.75f), cloak, true);
        c.P(body, PrimitiveType.Cylinder, "Cloak2", V(0f, 0.8f, 0f), V(0.78f, 0.3f, 0.65f), Shade(cloak, 0.85f), true);
        c.P(body, PrimitiveType.Cylinder, "Cloak3", V(0f, 0.45f, 0f), V(0.5f, 0.25f, 0.42f), Shade(cloak, 0.7f), true);

        var tail = c.Node(body, "Tatters", V(0f, 0.35f, 0f));
        rig.tail = tail;
        for (int i = 0; i < 5; i++)
        {
            float a = i * 72f * Mathf.Deg2Rad;
            c.P(tail, PrimitiveType.Cube, "Strip", V(Mathf.Cos(a) * 0.22f, -0.15f, Mathf.Sin(a) * 0.18f), V(0.12f, 0.5f, 0.03f), Shade(cloak, 0.6f), true, V(Mathf.Sin(a) * 12f, a * 57f, 0f));
        }

        var head = c.Node(body, "Head", V(0f, 1.7f, 0.05f));
        rig.head = head;
        c.P(head, PrimitiveType.Sphere, "Hood", Vector3.zero, V(0.55f, 0.6f, 0.6f), Shade(cloak, 0.75f), true);
        c.P(head, PrimitiveType.Sphere, "Void", V(0f, 0f, 0.18f), V(0.36f, 0.4f, 0.3f), Color.black);
        foreach (float s in Sides)
            c.Glow(head, PrimitiveType.Cube, "Eye", V(s * 0.09f, 0.04f, 0.3f), V(0.1f, 0.05f, 0.04f), new Color(0.4f, 0.95f, 1f), 3f);

        var armL = c.Limb(body, "ArmL", V(0.45f, 1.45f, 0.05f), 0.9f, 0.1f, Shade(cloak, 0.8f));
        var armR = c.Limb(body, "ArmR", V(-0.45f, 1.45f, 0.05f), 0.9f, 0.1f, Shade(cloak, 0.8f));
        rig.armL = armL;
        rig.armR = armR;
        foreach (var a in new[] { armL, armR })
            for (int i = 0; i < 3; i++)
                c.P(a, PrimitiveType.Cube, "Claw", V((i - 1) * 0.06f, -0.98f, 0.02f), V(0.03f, 0.2f, 0.03f), new Color(0.75f, 0.8f, 0.85f), false, V(-10f, 0f, (i - 1) * 12f));
    }

    // ---------------------------------------------------------------- 騎士

    static void Knight(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Biped;
        rig.stride = 22f;
        var metal = Color.Lerp(Steel, c.main, 0.45f);
        var cloth = Color.Lerp(c.main, new Color(0.8f, 0.15f, 0.15f), 0.4f);

        rig.legL = c.Limb(c.root, "LegL", V(0.17f, 0.9f, 0f), 0.9f, 0.22f, Shade(metal, 0.85f));
        rig.legR = c.Limb(c.root, "LegR", V(-0.17f, 0.9f, 0f), 0.9f, 0.22f, Shade(metal, 0.85f));
        c.P(rig.legL, PrimitiveType.Cube, "Boot", V(0f, -0.85f, 0.06f), V(0.26f, 0.2f, 0.4f), Shade(metal, 0.55f), true);
        c.P(rig.legR, PrimitiveType.Cube, "Boot", V(0f, -0.85f, 0.06f), V(0.26f, 0.2f, 0.4f), Shade(metal, 0.55f), true);

        var body = c.Node(c.root, "Body", V(0f, 0.9f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Cube, "Torso", V(0f, 0.45f, 0f), V(0.7f, 0.85f, 0.42f), metal, true);
        c.P(body, PrimitiveType.Cube, "Plate", V(0f, 0.55f, 0.2f), V(0.62f, 0.5f, 0.1f), Light(metal, 0.2f), true);
        c.P(body, PrimitiveType.Cube, "Belt", V(0f, 0.05f, 0f), V(0.74f, 0.1f, 0.46f), Gold);
        c.P(body, PrimitiveType.Cube, "Tabard", V(0f, -0.1f, 0.24f), V(0.34f, 0.6f, 0.04f), cloth);
        foreach (float s in Sides) c.P(body, PrimitiveType.Sphere, "Pauldron", V(s * 0.46f, 0.85f, 0f), Vector3.one * 0.32f, Shade(metal, 1.1f), true);

        var head = c.Node(body, "Head", V(0f, 1.45f, 0f));
        rig.head = head;
        c.P(head, PrimitiveType.Cube, "Helm", Vector3.zero, V(0.42f, 0.45f, 0.45f), metal, true);
        c.Glow(head, PrimitiveType.Cube, "Visor", V(0f, 0.02f, 0.23f), V(0.3f, 0.05f, 0.02f), new Color(1f, 0.85f, 0.4f), 2f);
        c.P(head, PrimitiveType.Cube, "Crest", V(0f, 0.3f, -0.02f), V(0.06f, 0.2f, 0.4f), Shade(metal, 0.7f), true);
        c.P(head, PrimitiveType.Cube, "Plume", V(0f, 0.38f, -0.2f), V(0.08f, 0.2f, 0.35f), new Color(0.85f, 0.15f, 0.15f), false, V(25f, 0f, 0f));

        var armL = c.Limb(body, "ArmL", V(0.46f, 1.3f, 0f), 0.8f, 0.2f, Shade(metal, 0.9f));
        var armR = c.Limb(body, "ArmR", V(-0.46f, 1.3f, 0f), 0.8f, 0.2f, Shade(metal, 0.9f));
        rig.armL = armL;
        rig.armR = armR;
        // 剣と盾
        c.P(armR, PrimitiveType.Cube, "Guard", V(0f, -0.85f, 0f), V(0.32f, 0.05f, 0.08f), Gold);
        c.P(armR, PrimitiveType.Cube, "Blade", V(0f, -1.45f, 0f), V(0.09f, 1.1f, 0.03f), new Color(0.88f, 0.9f, 0.95f));
        c.P(armL, PrimitiveType.Cube, "Shield", V(0.2f, -0.5f, 0.12f), V(0.08f, 0.85f, 0.6f), metal, true);
        c.P(armL, PrimitiveType.Cube, "Emblem", V(0.25f, -0.5f, 0.12f), V(0.04f, 0.35f, 0.22f), Gold);
    }

    // ---------------------------------------------------------------- 祈祷師

    static void Shaman(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Floater;
        rig.hover = 0.05f;
        var robe = c.main;
        var feather = new[] { new Color(0.85f, 0.2f, 0.2f), new Color(0.2f, 0.7f, 0.65f), new Color(0.95f, 0.8f, 0.25f), new Color(0.2f, 0.7f, 0.65f), new Color(0.85f, 0.2f, 0.2f) };

        var body = c.Node(c.root, "Body", Vector3.zero);
        rig.body = body;
        c.P(body, PrimitiveType.Cylinder, "Robe", V(0f, 0.6f, 0f), V(0.85f, 0.6f, 0.85f), robe, true);
        c.P(body, PrimitiveType.Cylinder, "Chest", V(0f, 1.3f, 0f), V(0.66f, 0.28f, 0.5f), Skin);
        c.P(body, PrimitiveType.Cylinder, "Sash", V(0f, 1.05f, 0f), V(0.7f, 0.06f, 0.56f), feather[0]);
        c.P(body, PrimitiveType.Sphere, "Ruff", V(0f, 1.6f, 0f), V(0.75f, 0.2f, 0.65f), feather[1], true);
        for (int i = 0; i < 5; i++)
        {
            float a = (-50f + i * 25f) * Mathf.Deg2Rad;
            c.P(body, PrimitiveType.Sphere, "Bead", V(Mathf.Sin(a) * 0.3f, 1.33f - Mathf.Cos(a) * 0.05f, 0.28f), Vector3.one * 0.07f, Bone);
        }

        var head = c.Node(body, "Head", V(0f, 1.85f, 0f));
        rig.head = head;
        c.P(head, PrimitiveType.Cube, "Mask", V(0f, 0.02f, 0.05f), V(0.5f, 0.56f, 0.3f), new Color(0.7f, 0.5f, 0.25f), true);
        foreach (float s in Sides)
        {
            c.P(head, PrimitiveType.Cube, "Hole", V(s * 0.12f, 0.1f, 0.21f), V(0.12f, 0.1f, 0.03f), Color.black);
            c.Glow(head, PrimitiveType.Sphere, "Eye", V(s * 0.12f, 0.1f, 0.22f), Vector3.one * 0.05f, new Color(0.5f, 1f, 0.6f), 3f);
            c.P(head, PrimitiveType.Cube, "Stripe", V(s * 0.18f, -0.1f, 0.21f), V(0.04f, 0.2f, 0.02f), feather[0]);
        }
        c.P(head, PrimitiveType.Cube, "Mouth", V(0f, -0.14f, 0.21f), V(0.2f, 0.04f, 0.03f), Color.black);
        for (int i = 0; i < 5; i++)
        {
            float ang = -50f + i * 25f;
            c.P(head, PrimitiveType.Cube, "Feather", V(Mathf.Sin(ang * Mathf.Deg2Rad) * 0.3f, 0.42f, -0.05f), V(0.07f, 0.55f - Mathf.Abs(i - 2) * 0.08f, 0.03f), feather[i], false, V(0f, 0f, -ang));
        }

        var armL = c.Limb(body, "ArmL", V(0.34f, 1.45f, 0f), 0.65f, 0.12f, Skin, false);
        var armR = c.Limb(body, "ArmR", V(-0.34f, 1.45f, 0f), 0.65f, 0.12f, Skin, false);
        rig.armL = armL;
        rig.armR = armR;
        c.P(armR, PrimitiveType.Cylinder, "Staff", V(0f, -0.55f, 0.12f), V(0.05f, 1.0f, 0.05f), Wood);
        c.P(armR, PrimitiveType.Sphere, "Skull", V(0f, 0.5f, 0.12f), V(0.22f, 0.24f, 0.22f), Bone);
        c.Glow(armR, PrimitiveType.Sphere, "Eye", V(0.05f, 0.52f, 0.22f), Vector3.one * 0.05f, new Color(0.5f, 1f, 0.6f), 3f);
        c.Glow(armR, PrimitiveType.Sphere, "Eye", V(-0.05f, 0.52f, 0.22f), Vector3.one * 0.05f, new Color(0.5f, 1f, 0.6f), 3f);
        for (int i = 0; i < 3; i++)
            c.P(armR, PrimitiveType.Cube, "Feather", V((i - 1) * 0.1f, 0.35f, 0.04f), V(0.04f, 0.3f, 0.02f), feather[i], false, V(0f, 0f, (i - 1) * -20f));
    }

    // ---------------------------------------------------------------- 巨人（オーガ）

    static void Giant(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Biped;
        rig.stride = 20f;
        rig.bobAmp = 0.03f;
        var skin = c.main;
        var dark = Shade(skin, 0.75f);

        rig.legL = c.Limb(c.root, "LegL", V(0.38f, 0.85f, 0f), 0.85f, 0.42f, dark);
        rig.legR = c.Limb(c.root, "LegR", V(-0.38f, 0.85f, 0f), 0.85f, 0.42f, dark);
        c.P(rig.legL, PrimitiveType.Cube, "Foot", V(0f, -0.8f, 0.12f), V(0.5f, 0.25f, 0.68f), Shade(skin, 0.6f), true);
        c.P(rig.legR, PrimitiveType.Cube, "Foot", V(0f, -0.8f, 0.12f), V(0.5f, 0.25f, 0.68f), Shade(skin, 0.6f), true);

        var body = c.Node(c.root, "Body", V(0f, 0.85f, 0f), V(6f, 0f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Sphere, "Belly", V(0f, 0.6f, 0.08f), V(1.5f, 1.3f, 1.15f), skin, true);
        c.P(body, PrimitiveType.Cube, "Chest", V(0f, 1.2f, -0.05f), V(1.4f, 0.7f, 0.8f), skin, true);
        c.P(body, PrimitiveType.Cube, "Belt", V(0f, 0.3f, 0.1f), V(1.2f, 0.14f, 0.95f), Wood);
        c.P(body, PrimitiveType.Cube, "Cloth", V(0f, 0.1f, 0.55f), V(0.7f, 0.55f, 0.08f), new Color(0.35f, 0.25f, 0.15f));
        c.P(body, PrimitiveType.Cube, "Skull", V(0.4f, 0.33f, 0.58f), V(0.14f, 0.14f, 0.1f), Bone);

        var head = c.Node(body, "Head", V(0f, 1.7f, 0.2f));
        rig.head = head;
        c.P(head, PrimitiveType.Sphere, "Skull", Vector3.zero, V(0.65f, 0.55f, 0.6f), skin, true);
        c.P(head, PrimitiveType.Cube, "Brow", V(0f, 0.1f, 0.28f), V(0.6f, 0.1f, 0.2f), dark, true);
        c.P(head, PrimitiveType.Cube, "Jaw", V(0f, -0.2f, 0.18f), V(0.5f, 0.15f, 0.3f), skin, true);
        foreach (float s in Sides)
        {
            c.Glow(head, PrimitiveType.Sphere, "Eye", V(s * 0.17f, 0.03f, 0.3f), Vector3.one * 0.09f, new Color(1f, 0.85f, 0.2f), 2.5f);
            c.P(head, PrimitiveType.Cube, "Tusk", V(s * 0.2f, -0.2f, 0.3f), V(0.08f, 0.3f, 0.08f), Bone, false, V(-20f, 0f, s * -8f));
            c.P(head, PrimitiveType.Sphere, "Ear", V(s * 0.36f, 0.03f, -0.02f), V(0.12f, 0.25f, 0.18f), skin, true);
        }

        var armL = c.Limb(body, "ArmL", V(0.85f, 1.5f, 0f), 1.2f, 0.45f, skin);
        var armR = c.Limb(body, "ArmR", V(-0.85f, 1.5f, 0f), 1.2f, 0.45f, skin);
        c.P(armL, PrimitiveType.Sphere, "Fist", V(0f, -1.25f, 0f), Vector3.one * 0.6f, dark, true);
        c.P(armR, PrimitiveType.Sphere, "Fist", V(0f, -1.25f, 0f), Vector3.one * 0.6f, dark, true);
        rig.armL = armL;
        rig.armR = armR;
        // 棍棒（トゲつき）
        c.P(armR, PrimitiveType.Cylinder, "ClubHandle", V(0f, -1.45f, 0.05f), V(0.14f, 0.6f, 0.14f), Wood);
        c.P(armR, PrimitiveType.Cylinder, "ClubHead", V(0f, -2.15f, 0.05f), V(0.5f, 0.45f, 0.5f), Shade(Wood, 0.8f));
        for (int i = 0; i < 4; i++)
        {
            float a = i * 90f * Mathf.Deg2Rad;
            c.P(armR, PrimitiveType.Cube, "Spike", V(Mathf.Cos(a) * 0.27f, -2.15f, 0.05f + Mathf.Sin(a) * 0.27f), Vector3.one * 0.1f, Steel, false, V(45f, 45f, 0f));
        }
    }

    // ---------------------------------------------------------------- 竜

    static void Drake(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Quad;
        rig.stride = 20f;
        rig.bobAmp = 0.03f;
        var scale = c.main;
        var dark = Shade(scale, 0.7f);
        var belly = Light(Color.Lerp(scale, new Color(0.9f, 0.7f, 0.4f), 0.5f), 0.1f);

        var body = c.Node(c.root, "Body", V(0f, 1.1f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Sphere, "Torso", Vector3.zero, V(0.95f, 0.85f, 1.9f), scale, true);
        c.P(body, PrimitiveType.Sphere, "Belly", V(0f, -0.2f, 0.1f), V(0.7f, 0.5f, 1.6f), belly);
        for (int i = 0; i < 5; i++)
            c.P(body, PrimitiveType.Cube, "Spine", V(0f, 0.45f - Mathf.Abs(i - 2) * 0.03f, 0.6f - i * 0.3f), V(0.12f, 0.2f, 0.12f), dark, true, V(0f, 45f, 0f));
        c.P(body, PrimitiveType.Cube, "Neck", V(0f, 0.4f, 0.95f), V(0.38f, 0.36f, 0.8f), scale, true, V(-35f, 0f, 0f));

        var head = c.Node(body, "Head", V(0f, 0.78f, 1.5f));
        rig.head = head;
        c.P(head, PrimitiveType.Cube, "Skull", Vector3.zero, V(0.46f, 0.4f, 0.7f), scale, true);
        c.P(head, PrimitiveType.Cube, "Snout", V(0f, -0.06f, 0.5f), V(0.34f, 0.24f, 0.45f), dark, true);
        c.P(head, PrimitiveType.Cube, "Jaw", V(0f, -0.2f, 0.4f), V(0.3f, 0.08f, 0.55f), dark, true);
        c.Glow(head, PrimitiveType.Cube, "Flame", V(0f, -0.14f, 0.55f), V(0.16f, 0.06f, 0.3f), new Color(1f, 0.5f, 0.1f), 2.5f);
        foreach (float s in Sides)
        {
            c.Glow(head, PrimitiveType.Sphere, "Eye", V(s * 0.2f, 0.12f, 0.15f), Vector3.one * 0.1f, new Color(1f, 0.6f, 0.1f), 3f);
            c.P(head, PrimitiveType.Cube, "Horn", V(s * 0.18f, 0.25f, -0.2f), V(0.08f, 0.08f, 0.55f), Bone, false, V(30f, s * 8f, 0f));
            c.P(head, PrimitiveType.Cube, "Fang", V(s * 0.1f, -0.15f, 0.65f), V(0.04f, 0.14f, 0.04f), Color.white);
        }

        var wings = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            float s = Sides[i];
            var w = c.Node(body, "Wing", V(s * 0.4f, 0.45f, 0.1f));
            c.P(w, PrimitiveType.Cube, "Arm", V(s * 0.7f, 0.1f, 0f), V(1.4f, 0.08f, 0.1f), dark, true, V(0f, 0f, s * 12f));
            c.P(w, PrimitiveType.Cube, "Membrane", V(s * 0.7f, 0.05f, -0.4f), V(1.3f, 0.03f, 0.9f), Shade(scale, 0.55f), true);
            c.P(w, PrimitiveType.Cube, "Claw", V(s * 1.38f, 0.18f, 0f), V(0.05f, 0.14f, 0.05f), Bone);
            wings[i] = w;
        }
        rig.wingL = wings[0];
        rig.wingR = wings[1];

        var tail = c.Node(body, "Tail", V(0f, -0.1f, -0.9f));
        rig.tail = tail;
        c.P(tail, PrimitiveType.Cube, "Seg1", V(0f, 0f, -0.35f), V(0.4f, 0.35f, 0.8f), scale, true);
        c.P(tail, PrimitiveType.Cube, "Seg2", V(0f, 0f, -1.0f), V(0.28f, 0.25f, 0.7f), dark, true);
        c.P(tail, PrimitiveType.Cube, "Tip", V(0f, 0f, -1.5f), V(0.14f, 0.18f, 0.45f), Bone, false, V(0f, 45f, 0f));

        // 脚: 0 前左 / 1 前右 / 2 後左 / 3 後右
        var legs = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            float s = (i % 2 == 0) ? 1f : -1f;
            float z = i < 2 ? 0.6f : -0.6f;
            var l = c.Limb(c.root, "Leg", V(s * 0.45f, 0.95f, z), 0.95f, 0.26f, dark);
            c.P(l, PrimitiveType.Cube, "Claw", V(0f, -0.9f, 0.1f), V(0.3f, 0.12f, 0.4f), Shade(scale, 0.45f), true);
            legs[i] = l;
        }
        rig.legs = legs;
    }

    // ---------------------------------------------------------------- 熊（召喚獣: スノーベアー）

    static void BearModel(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Quad;
        rig.stride = 24f;
        rig.bobAmp = 0.03f;
        var fur = c.main;
        var dark = Shade(fur, 0.82f);
        var ice = new Color(0.7f, 0.92f, 1f);

        var body = c.Node(c.root, "Body", V(0f, 0.95f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Sphere, "Torso", Vector3.zero, V(1.1f, 0.95f, 1.7f), fur, true);
        c.P(body, PrimitiveType.Sphere, "Hump", V(0f, 0.3f, 0.35f), V(0.9f, 0.6f, 0.8f), fur, true);
        c.P(body, PrimitiveType.Sphere, "Rump", V(0f, 0.05f, -0.6f), V(0.95f, 0.8f, 0.7f), dark, true);

        var head = c.Node(body, "Head", V(0f, 0.35f, 0.95f));
        rig.head = head;
        c.P(head, PrimitiveType.Sphere, "Skull", Vector3.zero, V(0.65f, 0.58f, 0.62f), fur, true);
        c.P(head, PrimitiveType.Sphere, "Muzzle", V(0f, -0.1f, 0.3f), V(0.34f, 0.26f, 0.34f), Light(fur, 0.1f), true);
        c.P(head, PrimitiveType.Sphere, "Nose", V(0f, -0.04f, 0.47f), Vector3.one * 0.11f, new Color(0.15f, 0.15f, 0.2f));
        foreach (float s in Sides)
        {
            c.P(head, PrimitiveType.Sphere, "Ear", V(s * 0.22f, 0.27f, -0.05f), V(0.18f, 0.16f, 0.1f), dark, true);
            c.Glow(head, PrimitiveType.Sphere, "Eye", V(s * 0.14f, 0.08f, 0.27f), Vector3.one * 0.07f, ice, 2.5f);
        }
        // 背中の氷
        for (int i = 0; i < 5; i++)
            c.Glow(body, PrimitiveType.Cube, "IceSpike", V((i % 2 == 0 ? 0.15f : -0.15f), 0.6f, 0.5f - i * 0.25f), V(0.12f, 0.35f, 0.12f), ice, 0.9f, V(-20f, 45f, i % 2 == 0 ? -12f : 12f));

        var tail = c.Node(body, "Tail", V(0f, 0.1f, -0.9f));
        rig.tail = tail;
        c.P(tail, PrimitiveType.Sphere, "Tuft", Vector3.zero, Vector3.one * 0.22f, fur, true);

        var legs = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            float s = (i % 2 == 0) ? 1f : -1f;
            float z = i < 2 ? 0.55f : -0.55f;
            var l = c.Limb(c.root, "Leg", V(s * 0.38f, 0.8f, z), 0.8f, 0.32f, dark);
            c.P(l, PrimitiveType.Cube, "Paw", V(0f, -0.78f, 0.08f), V(0.36f, 0.12f, 0.42f), Shade(fur, 0.7f), true);
            legs[i] = l;
        }
        rig.legs = legs;
    }

    // ---------------------------------------------------------------- 妖精（召喚獣: クイーンフェアリー）

    static void FairyModel(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Floater;
        rig.hover = 0.25f;
        var dress = c.main;
        var glow = Light(dress, 0.5f);

        var body = c.Node(c.root, "Body", V(0f, 0.9f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Cylinder, "Skirt", V(0f, 0.05f, 0f), V(0.55f, 0.3f, 0.55f), dress, true);
        c.P(body, PrimitiveType.Cylinder, "Waist", V(0f, 0.38f, 0f), V(0.28f, 0.12f, 0.24f), Shade(dress, 0.85f), true);
        c.P(body, PrimitiveType.Sphere, "Chest", V(0f, 0.58f, 0f), V(0.3f, 0.32f, 0.24f), dress, true);

        var head = c.Node(body, "Head", V(0f, 0.92f, 0f));
        rig.head = head;
        c.P(head, PrimitiveType.Sphere, "Face", Vector3.zero, Vector3.one * 0.3f, Skin);
        c.P(head, PrimitiveType.Sphere, "Hair", V(0f, 0.06f, -0.04f), V(0.34f, 0.3f, 0.34f), new Color(0.95f, 0.85f, 0.5f));
        foreach (float s in Sides) c.P(head, PrimitiveType.Sphere, "Eye", V(s * 0.06f, 0.0f, 0.13f), Vector3.one * 0.045f, new Color(0.2f, 0.45f, 0.35f));
        // 王冠
        c.Glow(head, PrimitiveType.Cylinder, "Crown", V(0f, 0.2f, 0f), V(0.2f, 0.04f, 0.2f), Gold, 1.5f);
        for (int i = 0; i < 5; i++)
        {
            float a = i * 72f * Mathf.Deg2Rad;
            c.Glow(head, PrimitiveType.Cube, "CrownTip", V(Mathf.Cos(a) * 0.09f, 0.26f, Mathf.Sin(a) * 0.09f), V(0.03f, 0.08f, 0.03f), Gold, 1.5f);
        }

        var armL = c.Limb(body, "ArmL", V(0.17f, 0.68f, 0f), 0.35f, 0.07f, Skin, false);
        var armR = c.Limb(body, "ArmR", V(-0.17f, 0.68f, 0f), 0.35f, 0.07f, Skin, false);
        rig.armL = armL; rig.armR = armR;
        c.Glow(armR, PrimitiveType.Sphere, "Wand", V(0f, -0.4f, 0.1f), Vector3.one * 0.1f, glow, 3f);

        // 羽（4枚、半透明に光る）
        var wings = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            float s = Sides[i];
            var w = c.Node(body, "Wing", V(s * 0.08f, 0.62f, -0.14f));
            c.Glow(w, PrimitiveType.Sphere, "Upper", V(s * 0.32f, 0.18f, -0.05f), V(0.55f, 0.4f, 0.03f), glow, 1.3f, V(0f, s * 20f, s * -20f));
            c.Glow(w, PrimitiveType.Sphere, "Lower", V(s * 0.24f, -0.12f, -0.05f), V(0.38f, 0.28f, 0.03f), glow, 1.3f, V(0f, s * 20f, s * 25f));
            wings[i] = w;
        }
        rig.wingL = wings[0]; rig.wingR = wings[1];

        var lightGo = new GameObject("Light");
        lightGo.transform.SetParent(body, false);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point; l.color = glow; l.range = 5f; l.intensity = 2f;
    }

    // ---------------------------------------------------------------- 鷹

    static void Bird(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Flyer;
        rig.flapSpeed = 9f;
        rig.wingAmp = 40f;
        var feather = c.main;
        var dark = Shade(feather, 0.65f);
        var beak = new Color(0.95f, 0.8f, 0.25f);

        var body = c.Node(c.root, "Body", V(0f, 1.4f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Sphere, "Torso", Vector3.zero, V(0.65f, 0.6f, 1.0f), feather, true);
        c.P(body, PrimitiveType.Sphere, "Breast", V(0f, -0.1f, 0.3f), V(0.5f, 0.45f, 0.55f), Light(feather, 0.55f));

        var head = c.Node(body, "Head", V(0f, 0.3f, 0.55f));
        rig.head = head;
        c.P(head, PrimitiveType.Sphere, "Skull", Vector3.zero, Vector3.one * 0.4f, feather, true);
        c.P(head, PrimitiveType.Cube, "Beak", V(0f, -0.05f, 0.28f), V(0.12f, 0.12f, 0.3f), beak, false, V(12f, 0f, 0f));
        c.P(head, PrimitiveType.Cube, "Hook", V(0f, -0.14f, 0.42f), V(0.1f, 0.12f, 0.1f), beak);
        foreach (float s in Sides)
        {
            c.Glow(head, PrimitiveType.Sphere, "Eye", V(s * 0.14f, 0.06f, 0.12f), Vector3.one * 0.09f, new Color(1f, 0.85f, 0.2f), 2f);
            c.P(head, PrimitiveType.Cube, "Brow", V(s * 0.13f, 0.14f, 0.13f), V(0.14f, 0.04f, 0.06f), dark, true, V(0f, 0f, -s * 25f));
        }

        var wings = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            float s = Sides[i];
            var w = c.Node(body, "Wing", V(s * 0.3f, 0.15f, 0.1f));
            c.P(w, PrimitiveType.Cube, "Inner", V(s * 0.55f, 0f, 0f), V(1.1f, 0.05f, 0.65f), feather, true);
            c.P(w, PrimitiveType.Cube, "Mid", V(s * 1.35f, 0f, -0.12f), V(0.8f, 0.04f, 0.5f), dark, true);
            c.P(w, PrimitiveType.Cube, "Tip", V(s * 1.95f, 0f, -0.25f), V(0.5f, 0.03f, 0.3f), Shade(feather, 0.45f), true);
            wings[i] = w;
        }
        rig.wingL = wings[0];
        rig.wingR = wings[1];

        var tail = c.Node(body, "Tail", V(0f, 0f, -0.65f));
        rig.tail = tail;
        c.P(tail, PrimitiveType.Cube, "Fan", V(0f, 0f, -0.3f), V(0.4f, 0.04f, 0.6f), dark, true);
        foreach (float s in Sides) c.P(tail, PrimitiveType.Cube, "Fan", V(s * 0.18f, 0f, -0.3f), V(0.3f, 0.04f, 0.55f), dark, true, V(0f, s * -15f, 0f));

        var legs = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            var l = c.Limb(body, "Leg", V(Sides[i] * 0.18f, -0.35f, 0.15f), 0.4f, 0.07f, beak, false);
            c.P(l, PrimitiveType.Cube, "Talon", V(0f, -0.42f, 0.08f), V(0.1f, 0.05f, 0.2f), new Color(0.2f, 0.18f, 0.16f));
            legs[i] = l;
        }
        rig.legL = legs[0];
        rig.legR = legs[1];
    }

    // ---------------------------------------------------------------- 死神

    static void Reaper(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Floater;
        rig.hover = 0.1f;
        var cloak = c.main;

        var body = c.Node(c.root, "Body", Vector3.zero);
        rig.body = body;
        c.P(body, PrimitiveType.Cylinder, "Cloak1", V(0f, 1.15f, 0f), V(0.9f, 0.55f, 0.7f), cloak, true);
        c.P(body, PrimitiveType.Cylinder, "Cloak2", V(0f, 0.55f, 0f), V(0.7f, 0.4f, 0.55f), Shade(cloak, 0.85f), true);
        c.P(body, PrimitiveType.Cylinder, "Cloak3", V(0f, 0.15f, 0f), V(0.5f, 0.3f, 0.4f), Shade(cloak, 0.7f), true);
        c.P(body, PrimitiveType.Sphere, "Cape", V(0f, 1.75f, -0.05f), V(0.95f, 0.4f, 0.65f), Shade(cloak, 0.9f), true);

        var tail = c.Node(body, "Tatters", V(0f, 0.25f, 0f));
        rig.tail = tail;
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f * Mathf.Deg2Rad;
            c.P(tail, PrimitiveType.Cube, "Strip", V(Mathf.Cos(a) * 0.25f, -0.1f, Mathf.Sin(a) * 0.2f), V(0.1f, 0.6f, 0.03f), Shade(cloak, 0.55f), true, V(Mathf.Sin(a) * 10f, a * 57f, 0f));
        }

        var head = c.Node(body, "Head", V(0f, 1.95f, 0.05f));
        rig.head = head;
        c.P(head, PrimitiveType.Sphere, "Hood", Vector3.zero, V(0.62f, 0.66f, 0.66f), Shade(cloak, 0.8f), true);
        c.P(head, PrimitiveType.Sphere, "Face", V(0f, -0.02f, 0.12f), V(0.34f, 0.38f, 0.34f), Bone);
        c.P(head, PrimitiveType.Cube, "Jaw", V(0f, -0.14f, 0.22f), V(0.14f, 0.06f, 0.1f), Bone);
        foreach (float s in Sides)
        {
            c.P(head, PrimitiveType.Cube, "Socket", V(s * 0.07f, 0.02f, 0.27f), V(0.09f, 0.1f, 0.03f), Color.black);
            c.Glow(head, PrimitiveType.Sphere, "Eye", V(s * 0.07f, 0.02f, 0.29f), Vector3.one * 0.05f, new Color(1f, 0.15f, 0.2f), 3f);
        }

        var armL = c.Limb(body, "ArmL", V(0.5f, 1.6f, 0f), 0.9f, 0.09f, Bone, false);
        var armR = c.Limb(body, "ArmR", V(-0.5f, 1.6f, 0f), 0.9f, 0.09f, Bone, false);
        c.P(armL, PrimitiveType.Cube, "Sleeve", V(0f, -0.25f, 0f), V(0.22f, 0.5f, 0.22f), Shade(cloak, 0.85f), true);
        c.P(armR, PrimitiveType.Cube, "Sleeve", V(0f, -0.25f, 0f), V(0.22f, 0.5f, 0.22f), Shade(cloak, 0.85f), true);
        rig.armL = armL;
        rig.armR = armR;

        // 大鎌
        c.P(armR, PrimitiveType.Cylinder, "Staff", V(0f, -0.7f, 0.15f), V(0.05f, 1.6f, 0.05f), new Color(0.2f, 0.15f, 0.12f));
        var blade = new Color(0.8f, 0.85f, 0.95f);
        c.Glow(armR, PrimitiveType.Cube, "Blade", V(0f, 0.9f, 0.5f), V(0.05f, 0.12f, 0.75f), blade, 0.8f, V(-8f, 0f, 0f));
        c.Glow(armR, PrimitiveType.Cube, "Blade", V(0f, 0.7f, 1.0f), V(0.04f, 0.1f, 0.5f), blade, 0.8f, V(30f, 0f, 0f));
        c.Glow(armR, PrimitiveType.Cube, "Tip", V(0f, 0.42f, 1.25f), V(0.03f, 0.08f, 0.3f), blade, 0.8f, V(55f, 0f, 0f));
    }

    // ---------------------------------------------------------------- 悪魔

    static void Demon(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Biped;
        rig.stride = 24f;
        rig.bobAmp = 0.03f;
        var skin = c.main;
        var dark = Shade(skin, 0.65f);

        rig.legL = c.Limb(c.root, "LegL", V(0.3f, 0.95f, 0f), 0.95f, 0.3f, dark);
        rig.legR = c.Limb(c.root, "LegR", V(-0.3f, 0.95f, 0f), 0.95f, 0.3f, dark);
        c.P(rig.legL, PrimitiveType.Cube, "Hoof", V(0f, -0.9f, 0.07f), V(0.34f, 0.16f, 0.42f), Shade(skin, 0.3f), true);
        c.P(rig.legR, PrimitiveType.Cube, "Hoof", V(0f, -0.9f, 0.07f), V(0.34f, 0.16f, 0.42f), Shade(skin, 0.3f), true);

        var body = c.Node(c.root, "Body", V(0f, 0.95f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Cube, "Chest", V(0f, 0.7f, 0f), V(1.1f, 0.9f, 0.6f), skin, true);
        c.P(body, PrimitiveType.Cube, "Abs", V(0f, 0.1f, 0.04f), V(0.7f, 0.45f, 0.45f), dark, true);
        foreach (float s in Sides)
        {
            c.P(body, PrimitiveType.Cube, "Spike", V(s * 0.65f, 1.15f, -0.05f), V(0.15f, 0.5f, 0.15f), Bone, false, V(0f, 0f, s * -30f));
            c.P(body, PrimitiveType.Cube, "Pec", V(s * 0.25f, 0.85f, 0.28f), V(0.4f, 0.3f, 0.12f), Light(skin, 0.1f), true);
        }

        var head = c.Node(body, "Head", V(0f, 1.6f, 0.05f));
        rig.head = head;
        c.P(head, PrimitiveType.Cube, "Skull", Vector3.zero, V(0.55f, 0.5f, 0.5f), skin, true);
        c.P(head, PrimitiveType.Cube, "Jaw", V(0f, -0.2f, 0.12f), V(0.42f, 0.12f, 0.34f), dark, true);
        foreach (float s in Sides)
        {
            c.P(head, PrimitiveType.Cube, "Horn", V(s * 0.25f, 0.32f, -0.05f), V(0.1f, 0.5f, 0.1f), Bone, false, V(0f, 0f, -s * 25f));
            c.P(head, PrimitiveType.Cube, "HornTip", V(s * 0.4f, 0.64f, -0.05f), V(0.08f, 0.3f, 0.08f), Bone, false, V(0f, 0f, -s * 55f));
            c.Glow(head, PrimitiveType.Cube, "Eye", V(s * 0.14f, 0.05f, 0.26f), V(0.14f, 0.06f, 0.03f), new Color(1f, 0.85f, 0.2f), 3f, V(0f, 0f, s * 15f));
            c.P(head, PrimitiveType.Cube, "Fang", V(s * 0.12f, -0.2f, 0.3f), V(0.05f, 0.14f, 0.04f), Color.white);
        }

        var armL = c.Limb(body, "ArmL", V(0.75f, 1.4f, 0f), 0.95f, 0.3f, skin);
        var armR = c.Limb(body, "ArmR", V(-0.75f, 1.4f, 0f), 0.95f, 0.3f, skin);
        c.P(armL, PrimitiveType.Cube, "Fist", V(0f, -1.0f, 0f), Vector3.one * 0.4f, dark, true);
        c.P(armR, PrimitiveType.Cube, "Fist", V(0f, -1.0f, 0f), Vector3.one * 0.4f, dark, true);
        foreach (var a in new[] { armL, armR })
            for (int i = 0; i < 3; i++)
                c.P(a, PrimitiveType.Cube, "Claw", V((i - 1) * 0.12f, -1.25f, 0.05f), V(0.05f, 0.2f, 0.05f), Bone, false, V(-10f, 0f, 0f));
        rig.armL = armL;
        rig.armR = armR;

        var wings = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            float s = Sides[i];
            var w = c.Node(body, "Wing", V(s * 0.3f, 1.35f, -0.3f));
            c.P(w, PrimitiveType.Cube, "Bone", V(s * 0.7f, 0.3f, 0f), V(1.4f, 0.07f, 0.07f), dark, true, V(0f, 0f, s * 20f));
            c.P(w, PrimitiveType.Cube, "Membrane", V(s * 0.75f, 0.1f, -0.25f), V(1.2f, 0.03f, 0.85f), Shade(skin, 0.5f), true, V(0f, 0f, s * 10f));
            wings[i] = w;
        }
        rig.wingL = wings[0];
        rig.wingR = wings[1];

        var tail = c.Node(body, "Tail", V(0f, 0.1f, -0.35f));
        rig.tail = tail;
        c.P(tail, PrimitiveType.Cube, "Seg", V(0f, -0.1f, -0.5f), V(0.12f, 0.12f, 1.0f), dark, true, V(15f, 0f, 0f));
        c.P(tail, PrimitiveType.Cube, "Spade", V(0f, -0.25f, -1.05f), Vector3.one * 0.22f, Shade(skin, 0.4f), true, V(45f, 45f, 0f));
    }

    // ---------------------------------------------------------------- アクアフィッシュ・ボーンシャーク・マーマン

    static void FishModel(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Swimmer;
        var body = c.Node(c.root, "Body", V(0f, 0.45f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Sphere, "Torso", Vector3.zero, V(0.55f, 0.62f, 1.25f), c.main, true);
        c.P(body, PrimitiveType.Sphere, "Belly", V(0f, -0.12f, 0.05f), V(0.42f, 0.4f, 1.05f), Light(c.main, 0.55f), true);
        c.P(body, PrimitiveType.Cube, "Dorsal", V(0f, 0.36f, -0.05f), V(0.05f, 0.32f, 0.55f), Shade(c.main, 0.7f), true, V(-20f, 0f, 0f));
        for (int i = 0; i < 3; i++)
            c.P(body, PrimitiveType.Cube, "Stripe", V(0f, 0.06f, 0.25f - i * 0.28f), V(0.58f, 0.5f, 0.05f), Shade(c.main, 0.75f), true);

        var head = c.Node(body, "Head", V(0f, 0f, 0.55f));
        rig.head = head;
        c.P(head, PrimitiveType.Sphere, "Snout", V(0f, -0.02f, 0.1f), V(0.38f, 0.36f, 0.42f), c.main, true);
        foreach (float s in Sides)
        {
            c.P(head, PrimitiveType.Sphere, "Eye", V(s * 0.17f, 0.09f, 0.04f), Vector3.one * 0.14f, Color.white);
            c.P(head, PrimitiveType.Sphere, "Pupil", V(s * 0.2f, 0.09f, 0.07f), Vector3.one * 0.07f, Color.black);
            c.P(head, PrimitiveType.Cube, "Fang", V(s * 0.07f, -0.13f, 0.27f), V(0.03f, 0.12f, 0.03f), Color.white);
        }

        var tail = c.Node(body, "Tail", V(0f, 0f, -0.6f));
        rig.tail = tail;
        c.P(tail, PrimitiveType.Cube, "Stalk", V(0f, 0f, -0.15f), V(0.12f, 0.2f, 0.35f), Shade(c.main, 0.85f), true);
        c.P(tail, PrimitiveType.Cube, "FinU", V(0f, 0.14f, -0.42f), V(0.05f, 0.36f, 0.26f), Shade(c.main, 0.7f), true, V(30f, 0f, 0f));
        c.P(tail, PrimitiveType.Cube, "FinD", V(0f, -0.14f, -0.42f), V(0.05f, 0.36f, 0.26f), Shade(c.main, 0.7f), true, V(-30f, 0f, 0f));

        var fins = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            float s = Sides[i];
            var w = c.Node(body, "Fin", V(s * 0.26f, -0.1f, 0.15f));
            c.P(w, PrimitiveType.Cube, "FinMesh", V(s * 0.2f, 0f, -0.05f), V(0.4f, 0.03f, 0.22f), Shade(c.main, 0.75f), true, V(0f, s * -15f, 0f));
            fins[i] = w;
        }
        rig.wingL = fins[0];
        rig.wingR = fins[1];
    }

    static void SharkModel(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Swimmer;
        var bone = c.main;
        var dark = Shade(bone, 0.55f);
        var green = new Color(0.35f, 1f, 0.7f);

        var body = c.Node(c.root, "Body", V(0f, 0.5f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Sphere, "Torso", Vector3.zero, V(0.62f, 0.55f, 1.9f), Shade(bone, 0.9f), true);
        c.Glow(body, PrimitiveType.Sphere, "Core", V(0f, 0f, -0.05f), V(0.34f, 0.3f, 1.3f), green, 1.6f);
        for (int i = 0; i < 7; i++)
        {
            float z = -0.7f + i * 0.24f;
            float w = 0.74f - Mathf.Abs(z) * 0.18f;
            c.P(body, PrimitiveType.Cube, "Rib", V(0f, 0.02f, z), V(w, 0.5f, 0.07f), bone, true);
            c.P(body, PrimitiveType.Cube, "Spine", V(0f, 0.3f, z), V(0.08f, 0.1f, 0.18f), dark, true);
        }
        c.P(body, PrimitiveType.Cube, "Dorsal", V(0f, 0.55f, -0.1f), V(0.06f, 0.75f, 0.8f), bone, true, V(-25f, 0f, 0f));

        var head = c.Node(body, "Head", V(0f, 0.02f, 0.95f));
        rig.head = head;
        c.P(head, PrimitiveType.Sphere, "Skull", V(0f, 0.1f, 0.1f), V(0.62f, 0.5f, 0.85f), bone, true);
        c.P(head, PrimitiveType.Cube, "UpperJaw", V(0f, -0.1f, 0.38f), V(0.5f, 0.1f, 0.6f), bone, true);
        for (int i = 0; i < 6; i++)
            foreach (float s in Sides)
                c.P(head, PrimitiveType.Cube, "Tooth", V(s * 0.2f, -0.2f, 0.12f + i * 0.1f), V(0.04f, 0.12f, 0.04f), Color.white, false, V(0f, 0f, s * 8f));
        foreach (float s in Sides)
            c.Glow(head, PrimitiveType.Sphere, "Eye", V(s * 0.27f, 0.18f, 0.28f), Vector3.one * 0.14f, green, 3f);

        var jaw = c.Node(head, "Jaw", V(0f, -0.18f, 0.0f));
        rig.lid = jaw;
        c.P(jaw, PrimitiveType.Cube, "LowerJaw", V(0f, -0.04f, 0.38f), V(0.46f, 0.09f, 0.62f), bone, true);
        for (int i = 0; i < 6; i++)
            foreach (float s in Sides)
                c.P(jaw, PrimitiveType.Cube, "Tooth", V(s * 0.19f, 0.06f, 0.14f + i * 0.1f), V(0.04f, 0.12f, 0.04f), Color.white, false, V(0f, 0f, s * -8f));

        var tail = c.Node(body, "Tail", V(0f, 0f, -0.95f));
        rig.tail = tail;
        c.P(tail, PrimitiveType.Cube, "Stalk", V(0f, 0f, -0.25f), V(0.18f, 0.25f, 0.6f), bone, true);
        c.P(tail, PrimitiveType.Cube, "FinU", V(0f, 0.28f, -0.7f), V(0.06f, 0.65f, 0.4f), bone, true, V(25f, 0f, 0f));
        c.P(tail, PrimitiveType.Cube, "FinD", V(0f, -0.22f, -0.65f), V(0.06f, 0.45f, 0.35f), bone, true, V(-25f, 0f, 0f));

        var fins = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            float s = Sides[i];
            var w = c.Node(body, "Fin", V(s * 0.3f, -0.15f, 0.3f));
            c.P(w, PrimitiveType.Cube, "FinMesh", V(s * 0.35f, 0f, -0.1f), V(0.7f, 0.04f, 0.3f), bone, true, V(0f, s * -20f, 0f));
            fins[i] = w;
        }
        rig.wingL = fins[0];
        rig.wingR = fins[1];
    }

    static void MermanModel(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Biped;
        rig.stride = 30f;
        var skin = c.main;
        var fin = Light(skin, 0.25f);

        rig.legL = c.Limb(c.root, "LegL", V(0.14f, 0.85f, 0f), 0.85f, 0.2f, Shade(skin, 0.85f));
        rig.legR = c.Limb(c.root, "LegR", V(-0.14f, 0.85f, 0f), 0.85f, 0.2f, Shade(skin, 0.85f));
        foreach (var leg in new[] { rig.legL, rig.legR })
        {
            c.P(leg, PrimitiveType.Cube, "Webbed", V(0f, -0.83f, 0.14f), V(0.28f, 0.05f, 0.42f), fin, true);
            c.P(leg, PrimitiveType.Cube, "ShinFin", V(0f, -0.5f, -0.12f), V(0.04f, 0.4f, 0.14f), fin, true);
        }

        var body = c.Node(c.root, "Body", V(0f, 0.85f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Cube, "Torso", V(0f, 0.38f, 0f), V(0.6f, 0.78f, 0.36f), skin, true);
        c.P(body, PrimitiveType.Cube, "Belly", V(0f, 0.3f, 0.12f), V(0.42f, 0.55f, 0.1f), Light(skin, 0.45f), true);
        for (int i = 0; i < 3; i++)
            c.P(body, PrimitiveType.Cube, "Gill", V(0.3f, 0.5f - i * 0.1f, 0.05f), V(0.02f, 0.06f, 0.2f), Shade(skin, 0.5f));
        c.P(body, PrimitiveType.Cube, "BackFin", V(0f, 0.5f, -0.28f), V(0.05f, 0.6f, 0.2f), fin, true, V(-18f, 0f, 0f));

        var armL = c.Limb(body, "ArmL", V(0.38f, 0.7f, 0f), 0.6f, 0.15f, skin);
        var armR = c.Limb(body, "ArmR", V(-0.38f, 0.7f, 0f), 0.6f, 0.15f, skin);
        rig.armL = armL;
        rig.armR = armR;
        // 銛（トライデント）
        c.P(armR, PrimitiveType.Cylinder, "Shaft", V(0f, -0.55f, 0.35f), V(0.05f, 0.9f, 0.05f), Wood, false, V(90f, 0f, 0f));
        foreach (float x in new[] { -0.1f, 0f, 0.1f })
            c.P(armR, PrimitiveType.Cube, "Prong", V(x, -0.55f, 0.95f), V(0.03f, 0.04f, x == 0f ? 0.3f : 0.22f), Steel);
        c.P(armR, PrimitiveType.Cube, "Bar", V(0f, -0.55f, 0.82f), V(0.24f, 0.04f, 0.04f), Steel);

        var head = c.Node(body, "Head", V(0f, 0.95f, 0.02f));
        rig.head = head;
        c.P(head, PrimitiveType.Sphere, "Skull", Vector3.zero, V(0.46f, 0.5f, 0.5f), skin, true);
        c.P(head, PrimitiveType.Cube, "Crest", V(0f, 0.28f, -0.02f), V(0.04f, 0.32f, 0.4f), fin, true, V(-12f, 0f, 0f));
        c.P(head, PrimitiveType.Cube, "Mouth", V(0f, -0.14f, 0.22f), V(0.2f, 0.04f, 0.06f), Shade(skin, 0.4f));
        foreach (float s in Sides)
        {
            c.Glow(head, PrimitiveType.Sphere, "Eye", V(s * 0.14f, 0.05f, 0.2f), Vector3.one * 0.1f, new Color(1f, 0.85f, 0.2f), 2.5f);
            c.P(head, PrimitiveType.Cube, "EarFin", V(s * 0.28f, 0.03f, -0.02f), V(0.22f, 0.2f, 0.03f), fin, true, V(0f, s * -25f, 0f));
        }
    }

    // ---------------------------------------------------------------- ミミック

    /// <summary>アーケードマスター: ネオンに光る多色の機械生物。体の真ん中のドットモニターに、顔文字が映る。手足が生えている。</summary>
    static void ArcadeMasterModel(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Biped;
        rig.stride = 20f;
        c.root.localScale *= 0.72f;
        bool clone = c.Has("clone");
        var parts = c.root.gameObject.AddComponent<ArcadeParts>();
        parts.rig = rig;
        parts.root = c.root;
        if (clone)
        {
            parts.rainbowSpeed = 0f;
            parts.solidGlow = c.Has("red") ? new Color(1f, 0.2f, 0.2f) : c.Has("blue") ? new Color(0.3f, 0.5f, 1f) : new Color(1f, 0.9f, 0.2f);
        }
        var metal = new Color(0.2f, 0.19f, 0.32f);
        Color[] neon = { NeonArt.Pink, NeonArt.Cyan, NeonArt.Yellow, NeonArt.Green, NeonArt.Violet };
        void G(GameObject go) => parts.glow.Add(go.GetComponent<Renderer>());

        rig.legL = c.Limb(c.root, "LegL", V(0.34f, 0.85f, 0f), 0.85f, 0.34f, Shade(metal, 0.85f));
        rig.legR = c.Limb(c.root, "LegR", V(-0.34f, 0.85f, 0f), 0.85f, 0.34f, Shade(metal, 0.85f));
        parts.legL = rig.legL; parts.legR = rig.legR;
        foreach (var leg in new[] { rig.legL, rig.legR })
        {
            c.P(leg, PrimitiveType.Cube, "Foot", V(0f, -0.82f, 0.12f), V(0.46f, 0.2f, 0.66f), Shade(metal, 0.6f), true);
            G(c.Glow(leg, PrimitiveType.Cube, "Ring1", V(0f, -0.3f, 0f), V(0.4f, 0.07f, 0.4f), neon[0], 2.5f));
            G(c.Glow(leg, PrimitiveType.Cube, "Ring2", V(0f, -0.6f, 0f), V(0.38f, 0.06f, 0.38f), neon[1], 2.5f));
        }

        var body = c.Node(c.root, "Body", V(0f, 0.85f, 0f));
        rig.body = body; parts.body = body;
        c.P(body, PrimitiveType.Cube, "Torso", V(0f, 0.7f, 0f), V(1.45f, 1.35f, 0.9f), metal, true);
        G(c.Glow(body, PrimitiveType.Cube, "PanelL", V(0.74f, 0.7f, 0f), V(0.06f, 1.1f, 0.7f), neon[0], 2.2f));
        G(c.Glow(body, PrimitiveType.Cube, "PanelR", V(-0.74f, 0.7f, 0f), V(0.06f, 1.1f, 0.7f), neon[1], 2.2f));
        G(c.Glow(body, PrimitiveType.Cube, "TopBar", V(0f, 1.4f, 0f), V(1.3f, 0.07f, 0.8f), neon[2], 2.4f));

        // 胸のドットモニター（顔文字が映る）
        c.P(body, PrimitiveType.Cube, "MonitorFrame", V(0f, 0.82f, 0.46f), V(1.08f, 0.8f, 0.06f), Shade(metal, 0.5f));
        G(c.Glow(body, PrimitiveType.Cube, "MonitorTop", V(0f, 1.24f, 0.48f), V(1.1f, 0.05f, 0.05f), neon[3], 3f));
        G(c.Glow(body, PrimitiveType.Cube, "MonitorBottom", V(0f, 0.4f, 0.48f), V(1.1f, 0.05f, 0.05f), neon[4], 3f));
        ArcadeFace.Create(body, V(0f, 0.82f, 0.495f), V(0.98f, 0.62f, 1f));

        // アーケードの操作盤: ボタン・スティック・コイン投入口
        c.P(body, PrimitiveType.Cube, "Deck", V(0f, 0.22f, 0.5f), V(1.2f, 0.12f, 0.4f), Shade(metal, 0.7f));
        c.Glow(body, PrimitiveType.Sphere, "BtnRed", V(-0.38f, 0.3f, 0.58f), Vector3.one * 0.15f, new Color(1f, 0.15f, 0.15f), 3f);
        c.Glow(body, PrimitiveType.Sphere, "BtnYellow", V(-0.14f, 0.3f, 0.58f), Vector3.one * 0.15f, new Color(1f, 0.9f, 0.2f), 3f);
        c.P(body, PrimitiveType.Cylinder, "StickBase", V(0.32f, 0.3f, 0.58f), V(0.07f, 0.1f, 0.07f), new Color(0.05f, 0.05f, 0.07f));
        c.Glow(body, PrimitiveType.Sphere, "StickBall", V(0.32f, 0.44f, 0.58f), Vector3.one * 0.14f, new Color(1f, 0.2f, 0.4f), 2.5f);
        c.Glow(body, PrimitiveType.Cube, "CoinSlot", V(0f, 0.12f, 0.47f), V(0.2f, 0.07f, 0.03f), Color.white, 2f);

        // 肩と腕
        foreach (float s in Sides)
        {
            c.P(body, PrimitiveType.Sphere, "Shoulder", V(s * 0.95f, 1.2f, 0f), Vector3.one * 0.5f, Shade(metal, 1.1f), true);
            G(c.Glow(body, PrimitiveType.Sphere, "ShoulderLight", V(s * 0.95f, 1.46f, 0f), Vector3.one * 0.14f, neon[0], 3f));
        }
        var armL = c.Limb(body, "ArmL", V(0.95f, 1.2f, 0f), 0.95f, 0.3f, Shade(metal, 0.9f));
        var armR = c.Limb(body, "ArmR", V(-0.95f, 1.2f, 0f), 0.95f, 0.3f, Shade(metal, 0.9f));
        rig.armL = armL; rig.armR = armR; parts.armL = armL; parts.armR = armR;
        int ai = 2;
        foreach (var arm in new[] { armL, armR })
        {
            c.P(arm, PrimitiveType.Cube, "Fist", V(0f, -1.0f, 0f), V(0.46f, 0.4f, 0.46f), Shade(metal, 0.7f), true);
            G(c.Glow(arm, PrimitiveType.Cube, "Band", V(0f, -0.4f, 0f), V(0.34f, 0.08f, 0.34f), neon[ai++ % 5], 2.5f));
        }

        // 頭（ドーム）とアンテナ
        var head = c.Node(body, "Head", V(0f, 1.62f, 0f));
        rig.head = head; parts.head = head;
        c.P(head, PrimitiveType.Cube, "Dome", Vector3.zero, V(0.7f, 0.3f, 0.5f), Shade(metal, 1.1f), true);
        G(c.Glow(head, PrimitiveType.Sphere, "Lamp", V(0f, 0.26f, 0f), Vector3.one * 0.22f, neon[1], 3f));
        foreach (float s in Sides)
        {
            c.P(head, PrimitiveType.Cylinder, "Antenna", V(s * 0.25f, 0.33f, 0f), V(0.04f, 0.3f, 0.04f), Shade(metal, 0.5f));
            G(c.Glow(head, PrimitiveType.Sphere, "AntennaTip", V(s * 0.25f, 0.66f, 0f), Vector3.one * 0.12f, neon[3], 3f));
        }

        // 背中: 通気口のネオン
        c.P(body, PrimitiveType.Cube, "Back", V(0f, 0.7f, -0.5f), V(1.2f, 1.1f, 0.2f), Shade(metal, 0.6f));
        for (int k = 0; k < 3; k++) G(c.Glow(body, PrimitiveType.Cube, "Vent", V(0f, 0.95f - k * 0.25f, -0.62f), V(1.0f, 0.05f, 0.03f), neon[k], 2.5f));
    }

    static void VirusModel(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Floater;
        rig.hover = 0.12f;
        var spike = Shade(c.main, 0.75f);
        var neon = new Color(1f, 0.35f, 1f);

        var body = c.Node(c.root, "Body", V(0f, 1.3f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Sphere, "Core", Vector3.zero, Vector3.one * 0.75f, c.main, true);
        c.Glow(body, PrimitiveType.Sphere, "Nucleus", Vector3.zero, Vector3.one * 0.42f, neon, 1.6f);
        for (int i = 0; i < 14; i++)
        {
            float y = 1f - 2f * (i + 0.5f) / 14f, rr = Mathf.Sqrt(1f - y * y), a = i * 2.39996f;
            var dir = new Vector3(Mathf.Cos(a) * rr, y, Mathf.Sin(a) * rr);
            c.P(body, PrimitiveType.Cube, "Spike", dir * 0.42f, V(0.1f, 0.1f, 0.34f), spike, true, Quaternion.LookRotation(dir).eulerAngles);
            c.Glow(body, PrimitiveType.Sphere, "Tip", dir * 0.66f, Vector3.one * 0.13f, i % 2 == 0 ? neon : NeonArt.Cyan, 2.5f);
        }
        foreach (float s in Sides) c.Glow(body, PrimitiveType.Cube, "Eye", V(s * 0.16f, 0.1f, 0.37f), V(0.14f, 0.14f, 0.05f), Color.white, 3f);
        c.Glow(body, PrimitiveType.Cube, "Mouth", V(0f, -0.1f, 0.38f), V(0.3f, 0.06f, 0.04f), Color.white, 3f);

        // 周りを回る、ノイズの破片
        var orbit = c.Node(body, "Orbit", Vector3.zero);
        rig.orbit = orbit;
        Color[] cols = { NeonArt.Pink, NeonArt.Cyan, NeonArt.Yellow, NeonArt.Green };
        for (int i = 0; i < 6; i++)
        {
            float a = i * 60f * Mathf.Deg2Rad;
            c.Glow(orbit, PrimitiveType.Cube, "Glitch", V(Mathf.Cos(a) * 0.85f, (i % 3 - 1) * 0.2f, Mathf.Sin(a) * 0.85f), V(0.18f, 0.1f, 0.1f), cols[i % 4], 2.5f, V(0f, i * 40f, 0f));
        }

        var lightGo = new GameObject("Light");
        lightGo.transform.SetParent(body, false);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = neon;
        l.range = 5f;
        l.intensity = 2.5f;
    }

    static void PixelKnightModel(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Biped;
        rig.stride = 24f;
        var metal = c.main;
        var plate = Light(metal, 0.12f);
        Color cyan = NeonArt.Cyan, pink = NeonArt.Pink;

        rig.legL = c.Limb(c.root, "LegL", V(0.18f, 0.9f, 0f), 0.9f, 0.28f, Shade(metal, 0.85f));
        rig.legR = c.Limb(c.root, "LegR", V(-0.18f, 0.9f, 0f), 0.9f, 0.28f, Shade(metal, 0.85f));
        foreach (var leg in new[] { rig.legL, rig.legR })
        {
            c.P(leg, PrimitiveType.Cube, "Boot", V(0f, -0.85f, 0.06f), V(0.3f, 0.2f, 0.42f), Shade(metal, 0.6f), true);
            c.Glow(leg, PrimitiveType.Cube, "Trim", V(0f, -0.45f, 0.15f), V(0.3f, 0.04f, 0.02f), cyan, 2.5f);
        }

        var body = c.Node(c.root, "Body", V(0f, 0.9f, 0f));
        rig.body = body;
        c.P(body, PrimitiveType.Cube, "Torso", V(0f, 0.45f, 0f), V(0.8f, 0.9f, 0.52f), metal, true);
        c.P(body, PrimitiveType.Cube, "Plate", V(0f, 0.55f, 0.27f), V(0.64f, 0.5f, 0.08f), plate, true);
        c.Glow(body, PrimitiveType.Cube, "Core", V(0f, 0.55f, 0.32f), V(0.2f, 0.2f, 0.04f), cyan, 3f);
        c.Glow(body, PrimitiveType.Cube, "Belt", V(0f, 0.04f, 0f), V(0.84f, 0.08f, 0.56f), pink, 2.2f);
        foreach (float s in Sides) c.P(body, PrimitiveType.Cube, "Pauldron", V(s * 0.55f, 0.88f, 0f), V(0.34f, 0.34f, 0.4f), Shade(metal, 1.1f), true);

        var head = c.Node(body, "Head", V(0f, 1.45f, 0f));
        rig.head = head;
        c.P(head, PrimitiveType.Cube, "Helm", Vector3.zero, V(0.5f, 0.5f, 0.5f), metal, true);
        c.Glow(head, PrimitiveType.Cube, "Visor", V(0f, 0.02f, 0.26f), V(0.36f, 0.08f, 0.02f), pink, 3f);
        for (int k = 0; k < 3; k++)
            c.P(head, PrimitiveType.Cube, "Crest", V(0f, 0.32f + 0.1f * k, -0.04f - 0.07f * k), V(0.1f, 0.12f, 0.4f - 0.1f * k), Shade(metal, 0.7f), true);

        var armL = c.Limb(body, "ArmL", V(0.55f, 1.3f, 0f), 0.8f, 0.22f, Shade(metal, 0.9f));
        var armR = c.Limb(body, "ArmR", V(-0.55f, 1.3f, 0f), 0.8f, 0.22f, Shade(metal, 0.9f));
        rig.armL = armL;
        rig.armR = armR;
        c.P(armR, PrimitiveType.Cube, "Guard", V(0f, -0.85f, 0.1f), V(0.4f, 0.08f, 0.1f), Gold);
        c.Glow(armR, PrimitiveType.Cube, "Blade", V(0f, -1.45f, 0.1f), V(0.12f, 1.1f, 0.05f), cyan, 2.5f);
        c.P(armL, PrimitiveType.Cube, "Shield", V(0.22f, -0.5f, 0.12f), V(0.08f, 0.8f, 0.6f), Shade(metal, 0.9f), true);
        c.Glow(armL, PrimitiveType.Cube, "Emblem", V(0.27f, -0.5f, 0.12f), V(0.03f, 0.3f, 0.3f), pink, 2.5f);

        // 周りに浮かぶ、ピクセル
        var orbit = c.Node(body, "Orbit", V(0f, 0.9f, 0f));
        rig.orbit = orbit;
        Color[] cols = { NeonArt.Pink, NeonArt.Cyan, NeonArt.Yellow, NeonArt.Green, NeonArt.Violet };
        for (int i = 0; i < 5; i++)
        {
            float a = i * 72f * Mathf.Deg2Rad;
            c.Glow(orbit, PrimitiveType.Cube, "Pixel", V(Mathf.Cos(a) * 0.95f, (i % 2) * 0.3f - 0.1f, Mathf.Sin(a) * 0.95f), Vector3.one * 0.12f, cols[i], 2.5f, V(0f, i * 30f, 0f));
        }
    }

    static void MimicModel(Ctx c)
    {
        var rig = c.rig;
        rig.move = EnemyRig.Move.Mimic;
        var wood = c.main;
        var metal = new Color(0.55f, 0.5f, 0.4f);

        var body = c.Node(c.root, "Body", Vector3.zero);
        rig.body = body;
        c.P(body, PrimitiveType.Cube, "Box", V(0f, 0.4f, 0f), V(1.0f, 0.6f, 0.7f), wood, true);
        c.P(body, PrimitiveType.Cube, "Band", V(0.3f, 0.4f, 0f), V(0.08f, 0.62f, 0.72f), metal);
        c.P(body, PrimitiveType.Cube, "Band", V(-0.3f, 0.4f, 0f), V(0.08f, 0.62f, 0.72f), metal);
        c.P(body, PrimitiveType.Cube, "Inside", V(0f, 0.69f, 0f), V(0.9f, 0.04f, 0.6f), new Color(0.35f, 0.05f, 0.08f));
        foreach (float s in new[] { 1f, -1f })
            foreach (float z in new[] { 1f, -1f })
                c.P(body, PrimitiveType.Cube, "Foot", V(s * 0.4f, 0.08f, z * 0.26f), V(0.2f, 0.16f, 0.2f), Shade(wood, 0.5f));
        for (int i = 0; i < 5; i++)
            c.P(body, PrimitiveType.Cube, "Tooth", V(-0.4f + i * 0.2f, 0.76f, 0.3f), V(0.1f, 0.16f, 0.06f), Color.white, false, V(-10f, 0f, 0f));

        var lid = c.Node(body, "Lid", V(0f, 0.7f, -0.35f));
        rig.lid = lid;
        c.P(lid, PrimitiveType.Cube, "Top", V(0f, 0.17f, 0.35f), V(1.04f, 0.3f, 0.74f), wood, true);
        c.P(lid, PrimitiveType.Cube, "Band", V(0.3f, 0.17f, 0.35f), V(0.08f, 0.32f, 0.76f), metal);
        c.P(lid, PrimitiveType.Cube, "Band", V(-0.3f, 0.17f, 0.35f), V(0.08f, 0.32f, 0.76f), metal);
        for (int i = 0; i < 5; i++)
            c.P(lid, PrimitiveType.Cube, "Tooth", V(-0.4f + i * 0.2f, 0.0f, 0.66f), V(0.1f, 0.16f, 0.06f), Color.white, false, V(10f, 0f, 0f));
        foreach (float s in Sides)
            c.Glow(lid, PrimitiveType.Sphere, "Eye", V(s * 0.25f, 0.28f, 0.72f), Vector3.one * 0.12f, new Color(1f, 0.2f, 0.15f), 3f);

        var tongue = c.Node(body, "Tongue", V(0f, 0.68f, 0.2f));
        rig.tail = tongue;
        c.P(tongue, PrimitiveType.Cube, "Tongue", V(0f, 0f, 0.3f), V(0.3f, 0.04f, 0.6f), new Color(0.85f, 0.2f, 0.3f));
    }
}
