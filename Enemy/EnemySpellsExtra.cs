using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 敵の追加の魔法: 魔法陣射撃・水流噴射・壁召喚・竜巻召喚・隕石。EnemyCaster から呼ばれる。
/// 数値は EnemySpell の項目を流用する（各 Cast の説明を参照）。
/// </summary>
public static class EnemySpellsExtra
{
    static readonly Color Red = new Color(1f, 0.15f, 0.1f);
    static readonly Quaternion Flat = Quaternion.Euler(-90f, 0f, 0f);

    public static GameObject FlatDisc(Color color, float diameter, float height = 0.06f, float glow = 1.5f, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(go.GetComponent<Collider>());
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(diameter, 0.01f, diameter);
        go.transform.localPosition = new Vector3(0f, height, 0f);
        var mat = go.GetComponent<Renderer>().material;
        mat.color = color;
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", color * glow);
        return go;
    }

    /// <summary>floor の高さを返す（足元の地面。見つからなければ fallbackY）。</summary>
    public static float GroundY(Vector3 pos, float fallbackY)
    {
        foreach (var h in Physics.RaycastAll(pos + Vector3.up * 2f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (EnemyMagicHit.IsPlayer(h.collider) || EnemyMagicHit.IsEnemyCollider(h.collider)) continue;
            return h.point.y;
        }
        return fallbackY;
    }

    // ---------------------------------------------------------------- 魔法陣射撃

    /// <summary>
    /// 魔法陣射撃: 敵の背後に、無数の魔法陣が次々と現れ、ビームを放つ。
    /// count = 魔法陣の数 / spread = 魔法陣が出る範囲（背後の半径）/ interval = 次の魔法陣までの間隔 / lifetime = 出現してから撃つまでの秒数 / radius = ビームの太さ
    /// </summary>
    public static IEnumerator MagicCircles(EnemySpell s, float damage, Transform owner, Health player, float castHeight)
    {
        int n = Mathf.Max(1, s.count);
        for (int i = 0; i < n; i++)
        {
            if (owner == null || player == null || player.IsDead) yield break;
            // 敵から見てプレイヤーと反対側（背後）の、扇形の範囲に、高さもばらばらに
            Vector3 away = owner.position - player.transform.position;
            away.y = 0f;
            away = away.sqrMagnitude > 0.01f ? away.normalized : -owner.forward;
            float ang = Random.Range(-75f, 75f);
            Vector3 dirOut = Quaternion.AngleAxis(ang, Vector3.up) * away;
            float dist = Random.Range(1.5f, Mathf.Max(2.5f, s.spread));
            float h = castHeight * Random.Range(0.3f, 1.5f) + Random.Range(0f, 1.5f);
            Vector3 pos = owner.position + dirOut * dist + Vector3.up * h;
            HostileMagicCircle.Spawn(pos, s, damage, owner, player);
            yield return new WaitForSeconds(Mathf.Max(0.03f, s.interval));
        }
    }

    // ---------------------------------------------------------------- 水流噴射

    /// <summary>
    /// 水流噴射: 地面にいくつか赤い警告円が出て、数秒後に水が噴き出し続ける。ダメージはなく、プレイヤーを上へ吹き飛ばす。
    /// count = 噴き出す場所の数 / aimCount = そのうちプレイヤーの足元を狙う数 / spread = 場所の範囲 / radius = 水柱の半径 /
    /// lifetime = 噴き出している秒数 / castTime（詠唱）のほかに、警告の秒数は 1.6 秒 / knockback = 打ち上げる速さ
    /// </summary>
    public static void WaterJets(Vector3 origin, EnemySpell s, Transform owner, Transform target, Health player)
    {
        int n = Mathf.Max(1, s.count);
        var placed = new List<Vector3>();
        float r = Mathf.Max(0.8f, s.radius);
        for (int i = 0; i < n; i++)
        {
            Vector3 p = origin;
            bool ok = false;
            for (int tries = 0; tries < 14 && !ok; tries++)
            {
                if (i < s.aimCount && target != null)
                {
                    Vector2 off = i == 0 ? Vector2.zero : Random.insideUnitCircle * r * 2.2f;
                    p = target.position + new Vector3(off.x, 0f, off.y);
                }
                else
                {
                    Vector2 c = Random.insideUnitCircle * Mathf.Max(3f, s.spread);
                    p = (target != null ? target.position : origin) + new Vector3(c.x, 0f, c.y);
                }
                if (!NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas)) continue;
                p = hit.position;
                ok = true;
                foreach (var q in placed) if ((q - p).sqrMagnitude < r * r * 2.2f) { ok = false; break; }
            }
            if (!ok) continue;
            placed.Add(p);
            HostileGeyser.Spawn(p, s, owner, player);
        }
        AudioManager.PlaySfxAt("pillar_cast", origin, 1f, 0.8f);
    }

    // ---------------------------------------------------------------- 壁召喚

    /// <summary>壁召喚: 敵の前（プレイヤー側）に、石の壁を出す。しばらくすると消える。wallHalfWidth = 壁の幅の半分 / wallLifetime = 残る秒数。</summary>
    public static void Wall(Transform owner, Transform target, EnemySpell s, float enemyScale)
    {
        if (owner == null) return;
        Vector3 to = target != null ? target.position - owner.position : owner.forward;
        to.y = 0f;
        Vector3 fwd = to.sqrMagnitude > 0.01f ? to.normalized : owner.forward;
        float width = Mathf.Max(3f, s.wallHalfWidth * 2f);
        float dist = 2.2f + enemyScale * 0.8f;
        Vector3 pos = owner.position + fwd * dist;
        pos.y = GroundY(pos, owner.position.y);

        // プレイヤーを巻き込んで閉じ込めない
        if (target != null && (target.position - pos).sqrMagnitude < (width * 0.5f + 1f) * (width * 0.5f + 1f)
            && Mathf.Abs(Vector3.Dot(target.position - pos, fwd)) < 1.8f) return;

        HostileWall.Spawn(pos, Quaternion.LookRotation(fwd), width, s.wallLifetime > 0f ? s.wallLifetime : 8f, s.element);
    }

    // ---------------------------------------------------------------- 竜巻召喚

    /// <summary>
    /// 竜巻召喚: 大きな竜巻を何本か発生させる。少しずつプレイヤーを引き寄せ、触れると連続ダメージと共に上へ吹き飛ばす。
    /// count = 本数 / radius = 竜巻の半径 / lifetime = 続く秒数 / speed = 引き寄せる強さ / interval = ダメージの間隔 / spread = 出る範囲
    /// </summary>
    public static void Tornadoes(Vector3 origin, EnemySpell s, float damage, Transform owner, Transform target, Health player)
    {
        int n = Mathf.Max(1, s.count);
        for (int i = 0; i < n; i++)
        {
            Vector3 center = target != null ? target.position : origin;
            Vector3 p = origin;
            bool ok = false;
            for (int tries = 0; tries < 14 && !ok; tries++)
            {
                float a = (i * 360f / n + Random.Range(-30f, 30f)) * Mathf.Deg2Rad;
                float d = Random.Range(Mathf.Max(5f, s.spread * 0.5f), Mathf.Max(7f, s.spread));
                p = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d;
                if (NavMesh.SamplePosition(p, out var hit, 2.5f, NavMesh.AllAreas)) { p = hit.position; ok = true; }
            }
            if (!ok) continue;
            HostileTornado.Spawn(p, s, damage, owner, player);
        }
        AudioManager.PlaySfxAt("whoosh", origin, 1f, 0.5f);
    }

    // ---------------------------------------------------------------- 隕石

    /// <summary>
    /// 隕石: 地面に警告の円が出て、空（天井の近く）に開いた裂け目から、炎の尾を引く岩が落ちてくる。着弾で爆発・衝撃波・破片・クレーター。
    /// count = 隕石の数 / aimCount = そのうちプレイヤーを狙う数 / spread = 落ちる範囲 / radius = 爆発の半径 / lifetime = 警告から着弾までの秒数
    /// </summary>
    public static void Meteors(Vector3 origin, EnemySpell s, float damage, Transform owner, Transform target, Health player, bool heavy)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt(s.count * 0.5f));            // 隕石の数は、半分
        int aim = Mathf.Min(n, Mathf.CeilToInt(s.aimCount * 0.5f));        // プレイヤーを狙う数も、半分
        var placed = new List<Vector3>();
        float r = Mathf.Max(1f, s.radius);
        int spawned = 0;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = origin;
            bool ok = false;
            for (int tries = 0; tries < 14 && !ok; tries++)
            {
                if (i < aim && target != null)
                {
                    Vector2 off = i == 0 ? Vector2.zero : Random.insideUnitCircle * r * 2.2f;
                    p = target.position + new Vector3(off.x, 0f, off.y);
                    if (NavMesh.SamplePosition(p, out var h0, 3f, NavMesh.AllAreas)) p = h0.position; else p.y = target.position.y;
                    ok = true;
                }
                else
                {
                    Vector2 c = Random.insideUnitCircle * Mathf.Max(3f, s.spread);
                    p = (target != null ? target.position : origin) + new Vector3(c.x, 0f, c.y);
                    if (!NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas)) continue;
                    p = hit.position;
                    ok = true;
                    foreach (var q in placed) if ((q - p).sqrMagnitude < r * r * 1.3f) { ok = false; break; }
                }
            }
            if (!ok) continue;
            placed.Add(p);
            // 順番に降ってくる（最初の数発は、プレイヤー狙いなので早め）
            float stagger = i < aim ? i * 0.35f : Random.Range(0.2f, Mathf.Max(0.6f, s.lifetime * 0.55f));
            HostileMeteor.Spawn(p, s, damage, owner, target, player, stagger, heavy);
            spawned++;
        }
        if (spawned > 0) AudioManager.PlaySfxAt("pillar_cast", origin, 1f, 0.6f);
    }
}

/// <summary>敵の魔法陣。出現して回り、狙いを定めてから、ビームを放つ。</summary>
public class HostileMagicCircle : MonoBehaviour
{
    EnemySpell spell;
    float damage, t, chargeTime, aimTime;
    Transform owner;
    Health player;
    GameObject circle;
    GameObject warn;
    bool fired;
    Vector3 lockedTarget;
    Color color;

    public static void Spawn(Vector3 pos, EnemySpell s, float damage, Transform owner, Health player)
    {
        var go = new GameObject("HostileMagicCircle");
        go.transform.position = pos;
        var c = go.AddComponent<HostileMagicCircle>();
        c.spell = s;
        c.damage = damage;
        c.owner = owner;
        c.player = player;
        c.color = MagicFx.Main(s.element);
        c.chargeTime = Mathf.Max(0.3f, s.lifetime);
        c.aimTime = 0.35f;

        c.circle = MagicFxExtra.MagicCircle(pos, Quaternion.identity, 0.9f, c.color, c.chargeTime + 1.2f);
        c.circle.transform.SetParent(go.transform, true);
        c.circle.transform.localScale = Vector3.one * 0.01f;
        MagicFx.FlashLight(pos, c.color, 6f, 6f, 0.25f);
        AudioManager.PlaySfxAt("beam_charge", pos, 0.35f, Random.Range(1.4f, 1.9f));
    }

    void Update()
    {
        t += Time.deltaTime;
        if (player == null || player.IsDead) { Destroy(gameObject); return; }

        Vector3 aimPoint = player.transform.position + Vector3.up * 1f;
        Vector3 toPlayer = aimPoint - transform.position;

        // 現れる: 小さい所から、ぱっと開く。魔法陣の面は、プレイヤーの方を向く
        float grow = Mathf.Clamp01(t / 0.25f);
        if (circle != null)
        {
            circle.transform.localScale = Vector3.one * Mathf.SmoothStep(0.01f, 1f, grow);
            if (toPlayer.sqrMagnitude > 0.01f && !fired)
                circle.transform.rotation = Quaternion.Slerp(circle.transform.rotation, Quaternion.LookRotation(toPlayer.normalized), 10f * Time.deltaTime);
        }

        if (!fired && t < chargeTime - aimTime) lockedTarget = aimPoint;

        // 狙いを定める予告の線（ここで狙いが固定される。動けば避けられる）
        if (!fired && t >= chargeTime - aimTime)
        {
            if (warn == null)
            {
                warn = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(warn.GetComponent<Collider>());
                var mat = warn.GetComponent<Renderer>().material;
                mat.color = new Color(1f, 0.2f, 0.15f);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(1f, 0.2f, 0.15f) * 2f);
            }
            Vector3 d = lockedTarget - transform.position;
            float len = Mathf.Min(spell.range, d.magnitude + 6f);
            warn.transform.position = transform.position + d.normalized * len * 0.5f;
            warn.transform.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            warn.transform.localScale = new Vector3(0.05f, len * 0.5f, 0.05f);
        }

        if (!fired && t >= chargeTime)
        {
            fired = true;
            if (warn != null) Destroy(warn);
            Vector3 dir = (lockedTarget - transform.position).normalized;
            HostileBeam.Fire(transform.position, dir, spell, damage, owner, player);
        }

        if (fired && t > chargeTime + 0.5f)
        {
            // 縮んで消える
            if (circle != null) circle.transform.localScale = Vector3.Lerp(circle.transform.localScale, Vector3.zero, 12f * Time.deltaTime);
            if (t > chargeTime + 0.9f) Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        if (warn != null) Destroy(warn);
    }
}

/// <summary>敵の水流。警告円 → 水が噴き出し続ける（ダメージなし。範囲内のプレイヤーを上へ打ち上げ続ける）。</summary>
public class HostileGeyser : MonoBehaviour
{
    float radius, life, lift, t;
    const float Warn = 1.6f;
    GameObject warnOuter, warnInner;
    GameObject jetFx;
    Light glow;
    Health player;
    float splashAt;

    public static void Spawn(Vector3 pos, EnemySpell s, Transform owner, Health player)
    {
        var go = new GameObject("HostileGeyser");
        go.transform.position = pos;
        var g = go.AddComponent<HostileGeyser>();
        g.radius = Mathf.Max(0.8f, s.radius);
        g.life = Mathf.Max(1f, s.lifetime);
        g.lift = Mathf.Max(8f, s.knockback);
        g.player = player;

        g.warnOuter = EnemySpellsExtra.FlatDisc(new Color(0.9f, 0.1f, 0.08f), g.radius * 2f, 0.05f, 1.2f, go.transform);
        g.warnInner = EnemySpellsExtra.FlatDisc(new Color(1f, 0.35f, 0.2f), 0.05f, 0.08f, 2f, go.transform);
    }

    void Update()
    {
        t += Time.deltaTime;
        if (t < Warn)
        {
            float k = t / Warn;
            float d = radius * 2f * k;
            warnInner.transform.localScale = new Vector3(d, 0.01f, d);
            // 地面から、水の気配（細かい泡）
            if (Time.frameCount % 6 == 0)
                Fx.Play(new Fx.Spec
                {
                    color = new Color(0.5f, 0.8f, 1f), color2 = Color.white, count = 4, speedMin = 0.5f, speedMax = 1.5f + 3f * k, sizeMin = 0.06f, sizeMax = 0.14f,
                    life = 0.5f, radius = radius * 0.5f, shape = ParticleSystemShapeType.Circle, gravity = 0.8f,
                }, transform.position + Vector3.up * 0.1f, null, Quaternion.Euler(-90f, 0f, 0f));
            return;
        }

        if (jetFx == null) StartJet();

        float left = Warn + life - t;
        if (left <= 0f)
        {
            if (glow != null) Destroy(glow.gameObject);
            if (jetFx != null) MagicFx.Detach(jetFx, 1.5f);
            Destroy(gameObject);
            return;
        }
        if (glow != null) glow.intensity = Mathf.Clamp01(left / 0.6f) * 3f;
        if (left < 0.8f && jetFx != null)
            foreach (var ps in jetFx.GetComponentsInChildren<ParticleSystem>()) { var e = ps.emission; e.rateOverTimeMultiplier *= 0.9f; }

        // 範囲内のプレイヤーを、上へ打ち上げ続ける（水柱の高さまで）
        if (player != null && !player.IsDead)
        {
            Vector3 d = player.transform.position - transform.position;
            float dy = d.y;
            d.y = 0f;
            if (d.magnitude <= radius + 0.3f && dy > -1f && dy < 12f)
            {
                var fpc = player.GetComponent<FirstPersonController>();
                if (fpc != null) fpc.Lift(lift);
                if (Time.time >= splashAt)
                {
                    splashAt = Time.time + 0.3f;
                    AudioManager.PlaySfxAt("whoosh", player.transform.position, 0.5f, 1.6f);
                }
            }
        }
    }

    void StartJet()
    {
        jetFx = new GameObject("WaterJetFx");
        jetFx.transform.SetParent(transform, false);
        Destroy(warnOuter);
        Destroy(warnInner);

        // 水柱本体（上へ勢いよく）
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.45f, 0.75f, 1f, 0.8f), color2 = new Color(0.8f, 0.95f, 1f, 0.8f), count = 0, rate = 260f, duration = 1f, loop = true, local = true,
            speedMin = 12f, speedMax = 20f, sizeMin = 0.35f, sizeMax = 0.8f, life = 0.7f, gravity = 0.5f, radius = radius * 0.55f,
            shape = ParticleSystemShapeType.Circle, shrink = false,
        }, Vector3.up * 0.1f, jetFx.transform, Quaternion.Euler(-90f, 0f, 0f));
        // しぶき
        Fx.Play(new Fx.Spec
        {
            color = Color.white, color2 = new Color(0.6f, 0.85f, 1f), count = 0, rate = 120f, duration = 1f, loop = true, local = true,
            speedMin = 5f, speedMax = 14f, sizeMin = 0.1f, sizeMax = 0.25f, life = 1.1f, gravity = 1.5f, radius = radius * 0.4f,
            shape = ParticleSystemShapeType.Cone, angle = 30f,
        }, Vector3.up * 0.3f, jetFx.transform, Quaternion.Euler(-90f, 0f, 0f));
        // 根元の水煙
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.8f, 0.92f, 1f, 0.4f), color2 = new Color(0.7f, 0.85f, 1f, 0.3f), count = 0, rate = 30f, duration = 1f, loop = true, local = true,
            speedMin = 0.5f, speedMax = 2f, sizeMin = 1f, sizeMax = 2f, life = 1f, radius = radius, shape = ParticleSystemShapeType.Circle, edge = true,
            additive = false, shrink = false, grow = true,
        }, Vector3.up * 0.15f, jetFx.transform, Quaternion.Euler(-90f, 0f, 0f));
        glow = MagicFx.AddLight(jetFx.transform, Vector3.up * 1.5f, new Color(0.5f, 0.8f, 1f), radius * 4f + 4f, 3f);
        MagicFx.Impact(Element.Water, transform.position + Vector3.up * 0.2f, radius * 1.4f);
        AudioManager.PlaySfxAt("explosion2", transform.position, 0.7f, 1.6f);
    }
}

/// <summary>敵が出す石の壁。地面からせり上がり、しばらく残って、崩れて消える。弾・光線は素通りする（敵自身の魔法は壁に邪魔されない）。</summary>
public class HostileWall : MonoBehaviour
{
    float life, t, height = 3.6f;
    Vector3 basePos;
    float sink;

    public static void Spawn(Vector3 pos, Quaternion rot, float width, float lifetime, Element element)
    {
        var go = new GameObject("HostileWall");
        go.transform.SetPositionAndRotation(pos, rot);
        var w = go.AddComponent<HostileWall>();
        w.life = lifetime;
        w.basePos = pos;

        var stone = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stone.name = "Stone";
        stone.transform.SetParent(go.transform, false);
        stone.transform.localPosition = new Vector3(0f, w.height * 0.5f, 0f);
        stone.transform.localScale = new Vector3(width, w.height, 1.1f);
        var mat = stone.GetComponent<Renderer>().material;
        mat.color = Color.Lerp(new Color(0.42f, 0.4f, 0.38f), MagicFx.Main(element), 0.2f);

        // 縁取りの岩（がたがたの見た目）
        for (int i = 0; i < 5; i++)
        {
            var chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(chunk.GetComponent<Collider>());
            chunk.transform.SetParent(go.transform, false);
            chunk.transform.localPosition = new Vector3(Random.Range(-width * 0.45f, width * 0.45f), w.height + Random.Range(-0.1f, 0.25f), Random.Range(-0.2f, 0.2f));
            chunk.transform.localRotation = Random.rotation;
            chunk.transform.localScale = Vector3.one * Random.Range(0.4f, 0.8f);
            chunk.GetComponent<Renderer>().material.color = mat.color * 0.9f;
        }
        // 敵自身の魔法は通す（EnemyMagicHit.IsEnemyCollider が、このマーカーを敵の体として扱う）
        stone.AddComponent<HostileWallMarker>();

        w.sink = -w.height;
        go.transform.position = pos + Vector3.up * w.sink;
        Fx.Dust(pos, 2.2f);
        AudioManager.PlaySfxAt("explosion2", pos, 0.8f, 0.7f);
        CinematicDirector.Shake(0.08f, 0.25f);
    }

    void Update()
    {
        t += Time.deltaTime;
        float rise = Mathf.Clamp01(t / 0.35f);
        float fall = life - t < 0.8f ? Mathf.Clamp01((life - t) / 0.8f) : 1f;
        // せり上がる → 立つ → 崩れて沈む
        float y = Mathf.Lerp(-height, 0f, Mathf.SmoothStep(0f, 1f, rise)) * 1f;
        if (fall < 1f) y = Mathf.Lerp(-height, 0f, fall);
        transform.position = basePos + Vector3.up * y;
        if (fall < 1f && Time.frameCount % 5 == 0) Fx.Dust(basePos, 1.2f);
        if (t >= life) Destroy(gameObject);
    }
}

/// <summary>敵の壁のしるし（敵の魔法・弾が、この壁を素通りするための目印）。</summary>
public class HostileWallMarker : MonoBehaviour { }

/// <summary>敵の竜巻。その場にとどまり（追いかけない）、範囲内のプレイヤーを引き寄せる。触れると連続ダメージ＋上へ吹き飛ばす。</summary>
public class HostileTornado : MonoBehaviour
{
    float radius, life, pull, tick, damage, t, nextHit;
    Element element;
    Transform owner;
    Health player;
    List<StatusApply> effects;
    GameObject fx;

    public static void Spawn(Vector3 pos, EnemySpell s, float damage, Transform owner, Health player)
    {
        var go = new GameObject("HostileTornado");
        go.transform.position = pos;
        var tw = go.AddComponent<HostileTornado>();
        tw.radius = Mathf.Max(1.5f, s.radius);
        tw.life = Mathf.Max(2f, s.lifetime);
        tw.pull = Mathf.Max(0f, s.speed);
        tw.tick = Mathf.Max(0.12f, s.interval);
        tw.damage = damage;
        tw.element = s.element;
        tw.owner = owner;
        tw.player = player;
        tw.effects = s.effects;

        float height = Mathf.Max(7f, tw.radius * 3.5f);
        tw.fx = MagicFx.Tornado(s.element == Element.None ? Element.Wind : s.element, tw.radius, height);
        tw.fx.transform.SetParent(go.transform, false);
        tw.fx.transform.localPosition = Vector3.zero;

        // 竜巻は、その場にとどまる（プレイヤーを追いかけない）
        Fx.Slam(pos, tw.radius * 1.5f);
        AudioManager.PlaySfxAt("whoosh", pos, 0.9f, 0.5f);
    }

    void Update()
    {
        t += Time.deltaTime;
        if (t >= life) { Dissolve(); return; }

        if (player == null || player.IsDead) return;
        Vector3 to = player.transform.position - transform.position;
        float dy = to.y;
        to.y = 0f;
        float dist = to.magnitude;

        if (FloorUtil.Different(transform, player.transform) || dy < -3f || dy > 14f) return;

        var fpc = player.GetComponent<FirstPersonController>();
        float pullRange = radius * 2.4f;   // 吸い込みの届く範囲を狭くした（以前は 3.5 倍）
        // 少しずつ引き寄せる（近いほど強い）
        if (dist < pullRange && dist > 0.3f && fpc != null)
        {
            float k = 1f - dist / pullRange;
            fpc.PushThisFrame(-to.normalized * pull * 0.4f * (0.1f + k * 0.8f)); // 吸い込む力を、約 1/3 に弱めた
        }

        // 触れたら、連続ダメージと打ち上げ
        if (dist <= radius)
        {
            if (fpc != null) fpc.Lift(11f);
            if (Time.time >= nextHit)
            {
                nextHit = Time.time + tick;
                EnemyMagicHit.Hit(player, damage, element, transform.position, owner, effects);
                AudioManager.PlaySfxAt("hit", player.transform.position, 0.5f, 1.6f);
            }
        }
    }

    void Dissolve()
    {
        if (fx != null) MagicFx.Detach(fx, 1.5f);
        Destroy(gameObject);
    }
}

/// <summary>敵の隕石。警告の円 → 空の裂け目から、炎の尾を引く岩が斜めに落ちる → 爆発・衝撃波・破片・クレーター・燃えさし。</summary>
public class HostileMeteor : MonoBehaviour
{
    float radius, damage, fallTime, startDelay, knockback;
    Element element;
    Transform owner, target;
    Health player;
    bool heavy;

    float t;
    Vector3 impact, startPoint;
    GameObject warnOuter, warnInner, rock, tail, riftFx;
    bool started, landed;
    float travel;
    float warnLead; // 警告が出てから、落下が始まるまで
    Light rockLight;

    const float FallSpeed = 34f;

    public static void Spawn(Vector3 impact, EnemySpell s, float damage, Transform owner, Transform target, Health player, float stagger, bool heavy)
    {
        var go = new GameObject("HostileMeteor");
        go.transform.position = impact;
        var m = go.AddComponent<HostileMeteor>();
        m.impact = impact;
        m.radius = Mathf.Max(1.5f, s.radius);
        m.damage = damage;
        m.element = s.element;
        m.knockback = s.knockback;
        m.owner = owner;
        m.target = target;
        m.player = player;
        m.heavy = heavy;
        m.startDelay = 0f; // 警告は、全員いっしょに出る（着弾の時刻だけ、ずれる）

        // 落ちてくる場所: 天井の手前までの高さ。斜め上から
        float clearance = 22f;
        if (Physics.Raycast(impact + Vector3.up * 0.5f, Vector3.up, out var up, 30f, ~0, QueryTriggerInteraction.Ignore)) clearance = Mathf.Max(3.2f, up.distance - 0.8f);
        Vector3 side = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Mathf.Min(clearance * 0.6f, 9f);
        m.startPoint = impact + Vector3.up * clearance + side;
        m.fallTime = Vector3.Distance(m.startPoint, impact) / FallSpeed;
        float total = Mathf.Max(s.lifetime, m.fallTime + 0.5f);
        m.warnLead = total - m.fallTime + stagger;

        m.BuildWarning();
    }

    void BuildWarning()
    {
        warnOuter = EnemySpellsExtra.FlatDisc(new Color(1f, 0.25f, 0.05f), radius * 2f, 0.05f, 1.3f, transform);
        warnOuter.transform.position = impact + Vector3.up * 0.05f;
        warnInner = EnemySpellsExtra.FlatDisc(new Color(1f, 0.8f, 0.25f), 0.05f, 0.07f, 2f, transform);
        warnInner.transform.position = impact + Vector3.up * 0.07f;
    }

    void Update()
    {
        t += Time.deltaTime;
        float local = t - startDelay;
        if (local < 0f) { SetWarningVisible(false); return; }
        SetWarningVisible(!landed);

        // 警告: 赤い円が脈打ち、中の円が広がる
        if (!started)
        {
            float k = Mathf.Clamp01(local / (warnLead + fallTime));
            float d = radius * 2f * k;
            warnInner.transform.localScale = new Vector3(d, 0.01f, d);
            float pulse = 0.6f + 0.4f * Mathf.Sin(local * 12f);
            warnOuter.GetComponent<Renderer>().material.SetColor("_EmissionColor", new Color(1f, 0.25f, 0.05f) * (0.8f + pulse));
            if (local >= warnLead) BeginFall();
            return;
        }

        if (landed) return;
        travel += FallSpeed * Time.deltaTime;
        float full = Vector3.Distance(startPoint, impact);
        float f = Mathf.Clamp01(travel / full);
        Vector3 pos = Vector3.Lerp(startPoint, impact + Vector3.up * 0.4f, f);
        rock.transform.position = pos;
        rock.transform.Rotate(240f * Time.deltaTime, 160f * Time.deltaTime, 90f * Time.deltaTime, Space.Self);
        // 炎の尾が、進行方向と逆へ伸びる
        tail.transform.position = pos;
        tail.transform.rotation = Quaternion.LookRotation((startPoint - impact).normalized);
        // 近づくほど、地面が明るくなる
        if (rockLight != null) rockLight.intensity = Mathf.Lerp(8f, 40f, f);
        // 風を切る音（落ち始めと着弾直前）
        if (f >= 1f) Land();
    }

    void SetWarningVisible(bool v)
    {
        if (warnOuter != null) warnOuter.SetActive(v);
        if (warnInner != null) warnInner.SetActive(v);
    }

    void BeginFall()
    {
        started = true;
        Color fire = new Color(1f, 0.45f, 0.1f);

        // 空の裂け目（閃光）
        MagicFx.FlashLight(startPoint, new Color(1f, 0.6f, 0.2f), 14f, 18f, 0.4f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.7f, 0.2f), color2 = new Color(1f, 1f, 0.8f), count = 24, speedMin = 2f, speedMax = 6f, sizeMin = 0.3f, sizeMax = 0.9f, life = 0.5f, radius = 0.6f,
        }, startPoint);
        AudioManager.PlaySfxAt("whoosh", impact, 0.9f, 0.5f);

        // 岩本体: ごつごつした、焦げた岩（大小の球を組み合わせ、割れ目が赤く光る）
        rock = new GameObject("MeteorRock");
        rock.transform.position = startPoint;
        float size = Mathf.Clamp(radius * 0.55f, 0.8f, 2.2f);
        var rockMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        rockMat.color = new Color(0.12f, 0.08f, 0.07f);
        rockMat.EnableKeyword("_EMISSION");
        rockMat.SetColor("_EmissionColor", fire * 1.2f);
        for (int i = 0; i < 6; i++)
        {
            var part = GameObject.CreatePrimitive(i == 0 ? PrimitiveType.Sphere : (i % 2 == 0 ? PrimitiveType.Cube : PrimitiveType.Sphere));
            Object.Destroy(part.GetComponent<Collider>());
            part.transform.SetParent(rock.transform, false);
            part.transform.localPosition = i == 0 ? Vector3.zero : Random.onUnitSphere * size * 0.45f;
            part.transform.localRotation = Random.rotation;
            part.transform.localScale = Vector3.one * size * (i == 0 ? 1f : Random.Range(0.45f, 0.75f));
            part.GetComponent<Renderer>().sharedMaterial = rockMat;
        }
        // 赤熱した芯
        var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(core.GetComponent<Collider>());
        core.transform.SetParent(rock.transform, false);
        core.transform.localScale = Vector3.one * size * 0.7f;
        var coreMat = core.GetComponent<Renderer>().material;
        coreMat.color = fire;
        coreMat.EnableKeyword("_EMISSION");
        coreMat.SetColor("_EmissionColor", fire * 4f);
        rockLight = MagicFx.AddLight(rock.transform, Vector3.zero, new Color(1f, 0.55f, 0.2f), 18f + radius * 2f, 8f);

        // 炎と煙の尾（進行方向の後ろ側へ流れる）
        tail = new GameObject("MeteorTail");
        var back = Quaternion.LookRotation((startPoint - impact).normalized);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.55f, 0.1f), color2 = new Color(1f, 0.9f, 0.4f), count = 0, rate = 240f, duration = 1f, loop = true, local = true,
            speedMin = 4f, speedMax = 9f, sizeMin = size * 0.6f, sizeMax = size * 1.3f, life = 0.45f, radius = size * 0.35f,
            shape = ParticleSystemShapeType.Cone, angle = 12f,
        }, Vector3.zero, tail.transform, Quaternion.identity);
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.2f, 0.17f, 0.16f, 0.7f), color2 = new Color(0.35f, 0.3f, 0.28f, 0.6f), count = 0, rate = 110f, duration = 1f, loop = true, local = true,
            speedMin = 2f, speedMax = 5f, sizeMin = size * 0.9f, sizeMax = size * 1.8f, life = 1.2f, radius = size * 0.3f,
            shape = ParticleSystemShapeType.Cone, angle = 15f, additive = false, shrink = false, grow = true,
        }, Vector3.zero, tail.transform, Quaternion.identity);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.85f, 0.4f), color2 = new Color(1f, 0.5f, 0.15f), count = 0, rate = 120f, duration = 1f, loop = true, local = true,
            speedMin = 3f, speedMax = 10f, sizeMin = 0.05f, sizeMax = 0.12f, life = 0.8f, radius = size * 0.5f,
            shape = ParticleSystemShapeType.Cone, angle = 25f, gravity = 0.5f,
        }, Vector3.zero, tail.transform, Quaternion.identity);
    }

    void Land()
    {
        landed = true;
        Color fire = new Color(1f, 0.45f, 0.1f);
        if (rock != null) Destroy(rock);
        if (tail != null) MagicFx.Detach(tail, 1.8f);
        Destroy(warnOuter);
        Destroy(warnInner);

        // ダメージ: 爆発の中心ほど強い。外側へ吹き飛ばす
        if (player != null && !player.IsDead && !FloorUtil.Different(transform, player.transform))
        {
            Vector3 to = player.transform.position - impact;
            float dy = Mathf.Abs(to.y);
            to.y = 0f;
            float d = to.magnitude;
            if (d <= radius + 0.3f && dy < 3.5f)
            {
                float falloff = 1f - 0.45f * Mathf.Clamp01(d / radius);
                float before = player.Current;
                EnemyMagicHit.Hit(player, damage * falloff, element, impact, owner);
                if (to.sqrMagnitude < 0.01f) to = Random.insideUnitSphere;
                if (heavy && player.Current < before)
                {
                    var fpc = player.GetComponent<FirstPersonController>();
                    if (fpc != null) fpc.BossLaunch(to);
                }
                else EnemyMagicHit.Knock(player, to, Mathf.Max(8f, knockback));
            }
        }

        Vector3 p = impact + Vector3.up * 0.2f;
        // 閃光・火の玉
        MagicFx.FlashLight(p + Vector3.up, new Color(1f, 0.7f, 0.3f), 22f + radius * 3f, 60f, 0.6f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.95f, 0.7f), color2 = new Color(1f, 0.7f, 0.2f), count = 8, speedMin = 0f, speedMax = 1f, sizeMin = radius * 2.2f, sizeMax = radius * 3f, life = 0.2f, radius = 0.3f,
        }, p + Vector3.up * 0.5f);
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.45f, 0.08f), color2 = new Color(1f, 0.85f, 0.3f), count = 90, speedMin = 3f, speedMax = 6f + radius * 2f, sizeMin = 0.6f, sizeMax = 1.4f + radius * 0.25f,
            life = 0.9f, radius = 0.4f, gravity = -0.4f,
        }, p);
        // 火の粉（高く舞い上がり、落ちてくる）
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.9f, 0.4f), color2 = new Color(1f, 0.4f, 0.1f), count = 120, speedMin = 6f, speedMax = 14f + radius * 2f, sizeMin = 0.05f, sizeMax = 0.16f,
            life = 1.6f, radius = 0.3f, gravity = 1.6f, shape = ParticleSystemShapeType.Cone, angle = 55f,
        }, p, null, Quaternion.Euler(-90f, 0f, 0f));
        // 岩の破片（重く、放物線を描いて飛び散る）
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.16f, 0.12f, 0.1f), color2 = new Color(0.4f, 0.3f, 0.25f), count = 40, speedMin = 5f, speedMax = 12f + radius, sizeMin = 0.12f, sizeMax = 0.35f,
            life = 1.5f, radius = 0.3f, gravity = 2.4f, additive = false, shrink = false, shape = ParticleSystemShapeType.Cone, angle = 50f,
        }, p, null, Quaternion.Euler(-90f, 0f, 0f));
        // 地面を走る衝撃波（砂煙の輪）
        Fx.Play(new Fx.Spec
        {
            color = new Color(0.5f, 0.42f, 0.34f, 0.7f), color2 = new Color(0.7f, 0.6f, 0.5f, 0.6f), count = 90, speedMin = radius * 2.5f, speedMax = radius * 4f, sizeMin = 0.8f, sizeMax = 1.6f,
            life = 0.8f, radius = 0.5f, shape = ParticleSystemShapeType.Circle, edge = true, additive = false, shrink = false, grow = true,
        }, impact + Vector3.up * 0.3f, null, Quaternion.Euler(-90f, 0f, 0f));
        // 広がる光のリング
        var ring = EnemySpellsExtra.FlatDisc(new Color(1f, 0.6f, 0.2f), 1f, 0.12f, 3f);
        ring.transform.position = impact + Vector3.up * 0.12f;
        ring.AddComponent<MeteorShockwave>().Init(radius * 2.4f, 0.45f);
        // クレーター（焦げ跡。しばらく残って、薄れる）
        var scorch = EnemySpellsExtra.FlatDisc(new Color(0.03f, 0.02f, 0.02f), radius * 1.5f, 0.04f, 0f);
        scorch.transform.position = impact + Vector3.up * 0.04f;
        var scorchMat = scorch.GetComponent<Renderer>().material;
        scorch.AddComponent<MeteorScorch>().Init(scorchMat, 7f);
        // 燃えさし（地面でしばらく燃える。ダメージはなし）
        Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.5f, 0.1f), color2 = new Color(1f, 0.85f, 0.3f), count = 0, rate = 18f, duration = 5f, speedMin = 0.5f, speedMax = 2f, sizeMin = 0.3f, sizeMax = 0.7f,
            life = 0.9f, radius = radius * 0.55f, shape = ParticleSystemShapeType.Circle, gravity = -0.3f,
        }, impact + Vector3.up * 0.1f, null, Quaternion.Euler(-90f, 0f, 0f));
        MagicFx.AddLight(scorch.transform, Vector3.up * 0.6f, new Color(1f, 0.5f, 0.15f), radius * 3f, 3f).gameObject.AddComponent<MeteorEmberLight>();

        AudioManager.PlaySfxAt("explosion1", impact, 1f, 0.7f);
        AudioManager.PlaySfxAt("explosion2", impact, 0.9f, 0.5f);
        // 近いほど、強く揺れる
        if (target != null)
        {
            float dist = Vector3.Distance(target.position, impact);
            if (dist < 30f) CinematicDirector.Shake(Mathf.Lerp(0.5f, 0.12f, dist / 30f), 0.5f);
        }
        Destroy(gameObject, 0.1f);
    }
}

/// <summary>隕石の衝撃波のリング（広がりながら薄れる）。</summary>
public class MeteorShockwave : MonoBehaviour
{
    float maxDiameter, duration, t;
    Material mat;
    Color baseColor;

    public void Init(float diameter, float seconds)
    {
        maxDiameter = diameter;
        duration = seconds;
        mat = GetComponent<Renderer>().material;
        baseColor = mat.color;
    }

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / duration);
        float d = Mathf.Lerp(0.5f, maxDiameter, 1f - (1f - k) * (1f - k));
        transform.localScale = new Vector3(d, 0.02f * (1f - k), d);
        mat.color = new Color(baseColor.r, baseColor.g, baseColor.b, 1f - k);
        mat.SetColor("_EmissionColor", baseColor * (3f * (1f - k)));
        if (t >= duration) Destroy(gameObject);
    }
}

/// <summary>焦げ跡（時間とともに薄れて消える）。</summary>
public class MeteorScorch : MonoBehaviour
{
    Material mat;
    float life, t;

    public void Init(Material m, float seconds)
    {
        mat = m;
        life = seconds;
    }

    void Update()
    {
        t += Time.deltaTime;
        if (t >= life) { Destroy(gameObject); return; }
        // 色は暗いまま、最後の 2 秒で縮んで消える
        float k = Mathf.Clamp01((life - t) / 2f);
        transform.localScale = new Vector3(transform.localScale.x, transform.localScale.y, transform.localScale.z);
        if (mat != null) mat.color = new Color(0.03f, 0.02f, 0.02f) * Mathf.Lerp(0.2f, 1f, k);
    }
}

/// <summary>燃えさしの灯り（だんだん暗くなる）。</summary>
public class MeteorEmberLight : MonoBehaviour
{
    Light l;
    float t;

    void Start() => l = GetComponent<Light>();

    void Update()
    {
        if (l == null) return;
        t += Time.deltaTime;
        l.intensity = Mathf.Max(0f, 3f - t * 0.5f) * (0.8f + 0.2f * Mathf.PerlinNoise(t * 9f, 0.3f));
        if (t > 6f) Destroy(gameObject);
    }
}
