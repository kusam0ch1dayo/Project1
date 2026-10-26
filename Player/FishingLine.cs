using UnityEngine;

/// <summary>
/// 釣り竿の浮きと針（釣り糸つき）。放物線を描いて飛び、
///  - 敵に当たる → 少しダメージを与えて、針が刺さる。しばらくの間、プレイヤーの方へ引き寄せる（通常のボスまで。大ボスは引き寄せられない）。
///  - 床・壁に当たる → そこに留まる。水に落ちる → 水面に浮かぶ。
/// もう一度、左クリックを押すまで、戻ってこない（PlayerCombat.UpdateRod）。
/// </summary>
public class FishingLine : MonoBehaviour
{
    enum Phase { Flying, Resting, Hooked, Returning }

    PlayerCombat owner;
    Phase phase;
    Vector3 vel;
    float damage, flightT, pullT, bobT;
    bool floating;
    float surfaceY;
    Health hooked;
    Vector3 hookOffset;
    LineRenderer lr;
    Transform body;

    const float Gravity = 16f;
    const float PullSpeed = 11f;      // 引き寄せる速さ（m/秒）
    const float PullTime = 2.2f;      // 1回の針で、引き寄せ続ける時間
    const float PullStopDist = 2.6f;  // ここまで近づいたら、引き寄せをやめる
    const float SnapDist = 80f;       // これより離れると、糸が切れて戻る
    const float ReturnSpeed = 42f;
    const int Points = 14;

    public bool Returning => phase == Phase.Returning;

    // ---- 釣り（村の池に浮かんでいるとき）
    bool fishable, biting, reeling;
    float waitT, biteT, splashT;
    FishingCatch pending;
    TextMesh alert;

    /// <summary>魚が食いついている（3秒の間に左クリックすると、連打が始まる）。</summary>
    public bool Biting => biting;
    /// <summary>連打の最中。</summary>
    public bool Reeling => reeling;

    public static FishingLine Cast(PlayerCombat owner, Vector3 origin, Vector3 velocity, float damage, Color color)
    {
        var go = new GameObject("FishingBobber");
        go.transform.position = origin;
        var f = go.AddComponent<FishingLine>();
        f.owner = owner;
        f.vel = velocity;
        f.damage = damage;
        f.phase = Phase.Flying;
        f.Build(color);
        AudioManager.PlaySfxAt("whoosh", origin, 0.8f, 1.4f);
        return f;
    }

    void Build(Color color)
    {
        body = new GameObject("Bobber").transform;
        body.SetParent(transform, false);
        Part(PrimitiveType.Sphere, new Vector3(0f, 0.06f, 0f), new Vector3(0.2f, 0.14f, 0.2f), new Color(0.95f, 0.15f, 0.12f));
        Part(PrimitiveType.Sphere, new Vector3(0f, -0.03f, 0f), new Vector3(0.2f, 0.12f, 0.2f), new Color(0.96f, 0.96f, 0.94f));
        Part(PrimitiveType.Cylinder, new Vector3(0f, 0.17f, 0f), new Vector3(0.025f, 0.07f, 0.025f), new Color(0.15f, 0.15f, 0.15f));
        Part(PrimitiveType.Cube, new Vector3(0f, -0.2f, 0.02f), new Vector3(0.02f, 0.12f, 0.02f), Color.Lerp(color, Color.gray, 0.4f)); // 針

        var lgo = new GameObject("FishingLineRenderer");
        lgo.transform.SetParent(transform, false);
        lr = lgo.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = Points;
        lr.widthMultiplier = 0.012f;
        lr.numCapVertices = 0;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.sharedMaterial = EnemyJumpFx.UnlitShared(new Color(0.92f, 0.92f, 0.88f));
    }

    void Part(PrimitiveType type, Vector3 pos, Vector3 scale, Color c)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(body, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().material.color = c;
    }

    /// <summary>糸を巻き取る（浮きと針が、竿の先へ戻ってくる）。</summary>
    public void Retrieve()
    {
        if (phase == Phase.Returning || reeling) return;
        StopBite();
        phase = Phase.Returning;
        hooked = null;
        AudioManager.PlaySfxAt("whoosh", transform.position, 0.5f, 1.8f);
    }

    /// <summary>すぐに消す（武器を持ち替えたときなど）。</summary>
    public void Snap()
    {
        if (owner != null) owner.OnRodReturned(this);
        Destroy(gameObject);
    }

    void Update()
    {
        if (owner == null) { Destroy(gameObject); return; }
        float dt = Time.deltaTime;
        Vector3 tip = owner.RodTip;

        if (phase != Phase.Returning && (transform.position - owner.transform.position).sqrMagnitude > SnapDist * SnapDist) Retrieve();

        switch (phase)
        {
            case Phase.Flying: Fly(dt); break;
            case Phase.Resting: Rest(dt); break;
            case Phase.Hooked: Hold(dt); break;
            case Phase.Returning:
            {
                Vector3 to = tip - transform.position;
                float step = ReturnSpeed * dt;
                if (to.magnitude <= step + 0.3f) { AudioManager.PlaySfx("equip", 0.4f, 1.6f); Snap(); return; }
                transform.position += to.normalized * step;
                break;
            }
        }
        DrawLine(tip);
    }

    void Fly(float dt)
    {
        flightT += dt;
        vel.y -= Gravity * dt;
        Vector3 pos = transform.position, step = vel * dt;

        // 敵（少し大きめの判定）
        foreach (var col in Physics.OverlapSphere(pos + step, 0.6f, ~0, QueryTriggerInteraction.Ignore))
        {
            var h = col.GetComponentInParent<Health>();
            if (h == null || h.IsDead || h.transform.root == owner.transform.root || h.GetComponent<AllyAI>() != null) continue;
            HitEnemy(h, col.bounds.center);
            return;
        }

        // 床・壁
        if (Physics.Raycast(pos, step.normalized, out var hit, step.magnitude + 0.05f, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.transform.root != owner.transform.root)
        {
            var h = hit.collider.GetComponentInParent<Health>();
            if (h != null && !h.IsDead && h.GetComponent<AllyAI>() == null) { HitEnemy(h, hit.point); return; }
            transform.position = hit.point + hit.normal * 0.12f;
            Land(false);
            return;
        }
        transform.position = pos + step;

        // 水
        if (WaterZones.At(transform.position, out float sy) && transform.position.y <= sy)
        {
            surfaceY = sy;
            transform.position = new Vector3(transform.position.x, sy, transform.position.z);
            Land(true);
            return;
        }
        if (flightT > 5f) Land(false);
    }

    void Land(bool water)
    {
        phase = Phase.Resting;
        floating = water;
        vel = Vector3.zero;
        if (water) { SwimFx.Splash(transform.position, 0.5f); AudioManager.PlaySfxAt("land", transform.position, 0.5f, 1.6f); }
        else AudioManager.PlaySfxAt("land", transform.position, 0.35f, 1.8f);
        fishable = water && WaterZones.Fishable(transform.position);
        if (fishable) waitT = Random.Range(5f, 20f);   // 5〜20秒待つと、魚が食いつく
    }

    void Rest(float dt)
    {
        if (!floating) return;
        bobT += dt;
        float y = surfaceY + Mathf.Sin(bobT * 3f) * 0.04f;
        if (fishable) y = Fish(dt, y);
        transform.position = new Vector3(transform.position.x, y, transform.position.z);
    }

    /// <summary>釣り: 待つ → 食いつく（3秒、浮きが激しく浮き沈みする）→ 左クリックで連打（FishingReel）。</summary>
    float Fish(float dt, float y)
    {
        if (reeling)
            return surfaceY - 0.12f - Mathf.Abs(Mathf.Sin(bobT * 9f)) * 0.12f;   // 引っ張り合いの間は、沈みがち

        if (!biting)
        {
            waitT -= dt;
            if (waitT > 2.5f && Random.value < dt * 0.25f) return y - 0.06f;   // ときどき、つつかれて、ぴくっと動く
            if (waitT <= 0f) StartBite();
            return y;
        }

        biteT -= dt;
        splashT -= dt;
        if (splashT <= 0f)
        {
            splashT = 0.28f;
            SwimFx.Splash(new Vector3(transform.position.x, surfaceY, transform.position.z), 0.35f);
            AudioManager.PlaySfxAt("land", transform.position, 0.35f, 1.9f);
        }
        if (alert != null)
        {
            alert.transform.position = transform.position + Vector3.up * (0.9f + Mathf.Sin(bobT * 14f) * 0.06f);
            var cam = Camera.main;
            if (cam != null) alert.transform.rotation = cam.transform.rotation;
        }
        if (biteT <= 0f)
        {
            StopBite();
            Hz.Toast("<color=#aaaaaa>……魚に逃げられた。もう一度、待ってみよう</color>");
            waitT = Random.Range(5f, 20f);
        }
        // 激しく浮き沈みする
        return surfaceY - 0.05f - Mathf.Abs(Mathf.Sin(bobT * 15f)) * 0.28f;
    }

    void StartBite()
    {
        biting = true;
        biteT = 3f;
        splashT = 0f;
        var rod = owner.CurrentWeapon;
        pending = FishingCatch.Roll(rod != null ? rod.rarity : Rarity.Common);
        AudioManager.PlaySfx("enemy_telegraph", 0.8f, 1.3f);
        if (alert == null)
        {
            alert = Fisherman.MakeText(transform, "BiteAlert", "！", transform.position + Vector3.up * 0.9f, 0.09f, new Color(1f, 0.85f, 0.2f));
        }
        alert.gameObject.SetActive(true);
    }

    void StopBite()
    {
        biting = false;
        if (alert != null) alert.gameObject.SetActive(false);
    }

    /// <summary>食いついている間に、左クリックした: 連打が始まる。</summary>
    public void BeginReel()
    {
        if (!biting || reeling) return;
        StopBite();
        reeling = true;
        var catchNow = pending;
        StartCoroutine(FishingReel.Run(owner.damage, catchNow != null ? catchNow.rank : 0, win =>
        {
            reeling = false;
            if (win && catchNow != null)
            {
                catchNow.Grant(transform.position);
                Retrieve();
            }
            else waitT = Random.Range(5f, 20f);
        }));
    }

    void HitEnemy(Health h, Vector3 point)
    {
        owner.StrikeTarget(h, point, damage, true);
        Fx.Hit(point);
        AudioManager.PlaySfxAt("hit", point, 0.7f, 1.3f);
        hooked = h;
        hookOffset = point - h.transform.position;
        pullT = 0f;
        phase = Phase.Hooked;
        if (!CanPull(h)) Hz.Toast("<color=#ffcc66>重すぎて、引き寄せられない！</color>");
    }

    /// <summary>引き寄せられる相手か: ふつうの敵と、通常のボス（アーケードマスター・グランドプレデターなどの大ボスは無理）。</summary>
    static bool CanPull(Health h)
    {
        if (h.GetComponent<EnemyAI>() != null) return true;
        var boss = h.GetComponent<BossAI>();
        return boss != null && boss.Reelable;
    }

    void Hold(float dt)
    {
        if (hooked == null || hooked.IsDead || !hooked.gameObject.activeInHierarchy)
        {
            if (hooked != null && hooked.IsDead) Achievements.OnSpecial("reel_kill");   // 実績: 引き寄せている最中に倒した
            // 刺さっていた相手が倒れた: 針が外れて、落ちる
            hooked = null;
            phase = Phase.Flying;
            vel = Vector3.zero;
            flightT = 0f;
            return;
        }
        transform.position = hooked.transform.position + hookOffset;

        pullT += dt;
        if (pullT > PullTime) return;
        Vector3 to = owner.transform.position - hooked.transform.position;
        var enemy = hooked.GetComponent<EnemyAI>();
        if (enemy == null || !enemy.IsFlyer) to.y = 0f;
        if (to.magnitude <= PullStopDist) { pullT = PullTime + 1f; return; }

        Vector3 dir = to.normalized;
        if (enemy != null) enemy.Stun(0.25f, dir * PullSpeed);
        else
        {
            var boss = hooked.GetComponent<BossAI>();
            if (boss != null && boss.Reelable) boss.Reel(dir * PullSpeed * dt);
        }
    }

    void DrawLine(Vector3 tip)
    {
        if (lr == null) return;
        Vector3 end = transform.position + Vector3.up * 0.2f;
        float dist = Vector3.Distance(tip, end);
        // 引き寄せている間は、ぴんと張る。それ以外は、少したるむ
        bool taut = phase == Phase.Hooked && pullT <= PullTime || phase == Phase.Returning || biting || reeling;
        float sag = taut ? 0f : Mathf.Min(1.6f, dist * 0.07f);
        for (int i = 0; i < Points; i++)
        {
            float t = i / (float)(Points - 1);
            Vector3 p = Vector3.Lerp(tip, end, t);
            p.y -= sag * 4f * t * (1f - t);
            lr.SetPosition(i, p);
        }
    }

    void OnDestroy()
    {
        if (reeling) FishingReel.Abort();
        if (owner != null) owner.OnRodReturned(this);
    }
}
