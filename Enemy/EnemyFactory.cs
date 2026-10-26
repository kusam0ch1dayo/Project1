using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// EnemyData とレベルから、敵・ボスを1体生成する。
/// 見た目はデータのプレハブ（無ければプリミティブの標準モデル）。当たり判定・AI・HPなどはここで付ける。
/// </summary>
public static class EnemyFactory
{
    static EnemyData defaultEnemy, defaultBoss;

    public static EnemyData DefaultEnemy => defaultEnemy != null ? defaultEnemy : (defaultEnemy = EnemyData.CreateDefault(false));
    public static EnemyData DefaultBoss => defaultBoss != null ? defaultBoss : (defaultBoss = EnemyData.CreateDefault(true));

    /// <summary>敵（data.isBoss ならボス）を pos に生成する。roomCenter は、ボスの撃破報酬が出る位置の中心。</summary>
    public static GameObject Spawn(EnemyData data, int level, Vector3 pos, Transform parent, Vector3 roomCenter = default, bool allowWater = false)
    {
        if (data == null) data = DefaultEnemy;
        ExtraSpellAssign.Apply(data); // 追加した魔法を、対応する敵に持たせる
        level = Mathf.Max(1, level);
        float scale = Mathf.Max(0.1f, data.scale);
        Element element = data.RollElement();

        var root = new GameObject((data.isBoss ? "Boss_" : "Enemy_") + data.displayName);
        root.transform.SetParent(parent, false);
        root.transform.position = pos;

        var col = root.AddComponent<CapsuleCollider>();
        col.height = data.colliderHeight * scale;
        col.radius = data.colliderRadius * scale;
        col.center = new Vector3(0f, col.height * 0.5f, 0f);

        // HP はレベルで増え、個体ごとに少しばらつく
        var health = root.AddComponent<Health>();
        health.SetMax(data.maxHealth * LevelScale.Hp(level) * Random.Range(0.92f, 1.08f) * Difficulty.EnemyHp);
        health.defense = data.DefenseAt(level) * Difficulty.EnemyDef; // 防御力もレベルで上がる

        var renderers = BuildVisual(root.transform, data, element, scale);

        // 空を飛ぶ敵は、ナビメッシュを使わず、自由に上下へ飛ぶ（EnemyAI が動かす）
        if (!data.IsFlyer)
        {
            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = Mathf.Min(col.radius, 1.2f);
            agent.height = col.height;
            if (data.aquatic == AquaticMode.None && !allowWater) agent.areaMask = NavMesh.AllAreas & ~(1 << DungeonGenerator.WaterArea); // 水に沈んだ所は通らない
            // 置いた所より高い所（天井の上のナビメッシュ）へ、引き上げられないように
            if (NavMesh.SamplePosition(pos, out var sp, 2f, agent.areaMask) && sp.position.y <= pos.y + 1f) agent.Warp(sp.position);
            else if (NavMesh.SamplePosition(pos, out sp, 2f, NavMesh.AllAreas) && sp.position.y <= pos.y + 1f) { agent.areaMask = NavMesh.AllAreas; agent.Warp(sp.position); }
        }

        if (data.isBoss)
        {
            var ai = root.AddComponent<BossAI>();
            ai.Configure(data, level, element);
            ai.renderers = renderers;
            ai.rewardCenter = roomCenter != default ? roomCenter : pos;
            if (data.id == ArcadeMasterData.Id) { var brain = root.AddComponent<ArcadeMasterAI>(); brain.Setup(ai); ai.brain = brain; } // アーケードマスター
            if (data.id == GrandPredatorData.Id) { var gp = root.AddComponent<GrandPredatorAI>(); gp.Setup(ai); ai.predator = gp; } // グランドプレデター
        }
        else
        {
            var ai = root.AddComponent<EnemyAI>();
            ai.Configure(data, level, element);
            ai.renderers = renderers;
            root.AddComponent<EnemyDecoyHunter>(); // プレイヤーを追っていないとき、タレット・蘇生した味方を狙う
            if (data.special != EnemySpecial.None) root.AddComponent<EnemySpecialAbility>().Setup(data); // 特殊能力
        }

        // デバフを付ける敵・味方にバフを配る敵
        if ((data.hitEffects != null && data.hitEffects.Count > 0) || (data.supportBuffs != null && data.supportBuffs.Count > 0))
            root.AddComponent<EnemyStatusSource>().Setup(data);

        // 魔法を使う敵（ボスの多くは、いろいろな魔法を使う）
        if (data.spells != null && data.spells.Count > 0)
            root.AddComponent<EnemyCaster>().Setup(data, level);

        Corruption.OnSpawn(root, data.isBoss); // 侵食ダンジョンの敵・ハードモードのボス
        WorldText.AttachElementLabel(root.transform, element, data.labelHeight * scale, level);
        AttachAura(root.transform, element, col);
        return root;
    }

    /// <summary>
    /// アライズで蘇生した味方の敵を作る。見た目・当たり判定・HP は敵と同じで、AI は AllyAI（プレイヤーに付き従い、敵を攻撃する）。
    /// </summary>
    public static GameObject SpawnAlly(EnemyData data, int level, Element element, Vector3 pos)
    {
        if (data == null) return null;
        level = Mathf.Max(1, level);
        float scale = Mathf.Max(0.1f, data.scale);

        var root = new GameObject("Ally_" + data.displayName);
        root.transform.position = pos;

        var col = root.AddComponent<CapsuleCollider>();
        col.height = data.colliderHeight * scale;
        col.radius = data.colliderRadius * scale;
        col.center = new Vector3(0f, col.height * 0.5f, 0f);

        var health = root.AddComponent<Health>();
        health.SetMax(data.maxHealth * LevelScale.Hp(level));
        health.defense = data.DefenseAt(level);

        var renderers = BuildVisual(root.transform, data, element, scale);

        if (!data.IsFlyer)
        {
            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = Mathf.Min(col.radius, 1.2f);
            agent.height = col.height;
            if (NavMesh.SamplePosition(pos, out var hit, 3f, NavMesh.AllAreas)) agent.Warp(hit.position);
        }

        var ai = root.AddComponent<AllyAI>();
        ai.Setup(data, level, element, renderers);
        WorldText.AttachElementLabel(root.transform, element, data.labelHeight * scale, level);
        AttachAura(root.transform, element, col);
        return root;
    }

    /// <summary>召喚獣（ペット）を作る。見た目・動きは、蘇生した味方と同じ（AllyAI）。ステータスは PetRuntime が決める。</summary>
    public static GameObject SpawnPet(PetInstance p, Vector3 pos)
    {
        if (p == null || p.data == null) return null;
        var data = p.data.ToEnemyData();
        float scale = Mathf.Max(0.1f, data.scale);

        var root = new GameObject("Pet_" + p.data.displayName);
        root.transform.position = pos;
        var col = root.AddComponent<CapsuleCollider>();
        col.height = data.colliderHeight * scale;
        col.radius = data.colliderRadius * scale;
        col.center = new Vector3(0f, col.height * 0.5f, 0f);
        var health = root.AddComponent<Health>();
        health.SetMax(p.MaxHp);

        var renderers = BuildVisual(root.transform, data, p.element, scale);
        if (!data.IsFlyer)
        {
            var agent = root.AddComponent<NavMeshAgent>();
            agent.radius = Mathf.Min(col.radius, 1.2f);
            agent.height = col.height;
            if (NavMesh.SamplePosition(pos, out var hit, 3f, NavMesh.AllAreas)) agent.Warp(hit.position);
        }

        var rt = root.AddComponent<PetRuntime>();
        rt.Init(p);
        var ai = root.AddComponent<AllyAI>();
        ai.Setup(data, p.level, p.element, renderers);
        WorldText.AttachElementLabel(root.transform, p.element, data.labelHeight * scale, p.level);
        AttachAura(root.transform, p.element, col);
        if (Hz.Player != null) ai.SetOwner(Hz.Player.transform);
        return root;
    }

    /// <summary>雷・岩・氷の敵は、体のまわりに、属性のオーラを纏う。</summary>
    static void AttachAura(Transform root, Element element, CapsuleCollider col)
    {
        if (!ElementAura.Has(element) || col == null) return;
        float r = Mathf.Clamp(col.radius, 0.3f, 4f), h = Mathf.Clamp(col.height, 0.6f, 8f);
        ElementAura.Attach(root, element, new Vector3(0f, h * 0.5f, 0f), new Vector3(r * 2.2f, h * 1.05f, r * 2.2f));
    }

    // ---------------------------------------------------------------- 見た目

    /// <summary>図鑑用: 敵の見た目だけを作る（AI・当たり判定なし）。</summary>
    public static void BuildPreview(Transform root, EnemyData data)
    {
        if (data == null) return;
        BuildVisual(root, data, data.randomElement ? Element.None : data.element, Mathf.Max(0.1f, data.scale));
    }

    static Renderer[] BuildVisual(Transform root, EnemyData data, Element element, float scale)
    {
        var visual = new GameObject("Visual").transform;
        visual.SetParent(root, false);
        visual.localScale = Vector3.one * scale;

        if (data.prefab != null)
        {
            var inst = Object.Instantiate(data.prefab, visual);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;

            // 見た目専用: 物理判定・ナビ・他のスクリプトは使わない
            foreach (var c in inst.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            foreach (var rb in inst.GetComponentsInChildren<Rigidbody>()) Object.Destroy(rb);
            foreach (var a in inst.GetComponentsInChildren<NavMeshAgent>()) Object.Destroy(a);

            var list = new List<Renderer>();
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var mat = r.sharedMaterial;
                if (mat != null && (mat.HasProperty("_BaseColor") || mat.HasProperty("_Color"))) list.Add(r);
            }
            return list.ToArray();
        }

        // 標準の見た目（属性の色が少し混ざる）
        Color color = data.color;
        if (element != Element.None) color = Color.Lerp(color, ElementUtil.Color(element), data.isBoss ? 0.45f : 0.55f);

        // 種類ごとの見た目（アニメーションつき）
        var modeled = EnemyModels.Build(visual, data, color);
        if (modeled != null && modeled.Length > 0) return modeled;

        if (!data.isBoss)
        {
            var body = Part(visual, PrimitiveType.Capsule, "Body", new Vector3(0f, 1f, 0f), Vector3.one, color);
            Part(visual, PrimitiveType.Cube, "Eye", new Vector3(0f, 1.5f, 0.4f), new Vector3(0.5f, 0.12f, 0.2f), Color.black);
            return new[] { body.GetComponent<Renderer>() };
        }

        var bBody = Part(visual, PrimitiveType.Capsule, "Body", new Vector3(0f, 1.8f, 0f), new Vector3(1.8f, 1.8f, 1.8f), color);
        var hornL = Part(visual, PrimitiveType.Cube, "HornL", new Vector3(-0.55f, 3.5f, 0.1f), new Vector3(0.22f, 0.8f, 0.22f), color);
        var hornR = Part(visual, PrimitiveType.Cube, "HornR", new Vector3(0.55f, 3.5f, 0.1f), new Vector3(0.22f, 0.8f, 0.22f), color);
        hornL.transform.localRotation = Quaternion.Euler(0f, 0f, 20f);
        hornR.transform.localRotation = Quaternion.Euler(0f, 0f, -20f);
        Part(visual, PrimitiveType.Cube, "Eyes", new Vector3(0f, 2.7f, 0.8f), new Vector3(0.9f, 0.18f, 0.3f), new Color(1f, 0.85f, 0.2f));
        return new[] { bBody.GetComponent<Renderer>(), hornL.GetComponent<Renderer>(), hornR.GetComponent<Renderer>() };
    }

    static GameObject Part(Transform parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().material.color = color;
        return go;
    }
}
