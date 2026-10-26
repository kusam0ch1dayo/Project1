using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>アーケードマスターの攻撃(1): インベーダー / ピクセルボール / グリッチワープ / ダンスダンスレクイエム。</summary>
public partial class ArcadeMasterAI
{
    // ================================================================ インベーダー

    /// <summary>ウイルスを無数に召喚する。1体だけ赤い個体（HPが高い）がいて、倒すと全ウイルスが消滅する。</summary>
    IEnumerator Invader()
    {
        lastInvader = Time.time;
        invaderActive = false; // 赤いウイルスが出てから、判定を始める
        redVirus = null;
        face.Show(FaceKind.Smug, 1.6f);
        parts.rig.enabled = false;
        AudioManager.PlaySfxAt("arcadeg01_13", transform.position, 1f);
        Toast("<color=#c36bff>インベーダー襲来！</color>  赤いウイルスを倒せ");
        int n = HpRatio < 0.5f ? 11 : 8;
        float yaw0 = UnityEngine.Random.value * 360f;
        int redIdx = UnityEngine.Random.Range(0, n);
        for (int i = 0; i < n; i++)
        {
            Vector3 p = RingPoint(5f, 11f, yaw0 + i * 360f / n);
            var go = SpawnMinion(i == redIdx ? ArcadeMasterData.RedVirus : ArcadeMasterData.MinionVirus, p, UnityEngine.Random.Range(1.8f, 4.5f));
            if (go != null && i == redIdx)
            {
                redVirus = go;
                // 赤い個体の目印（光の柱）
                var beacon = MagicFxExtra.Cylinder(new Color(1f, 0.15f, 0.2f), 3f);
                beacon.transform.SetParent(go.transform, false);
                beacon.transform.localPosition = Vector3.up * 6f;
                beacon.transform.localScale = new Vector3(0.18f, 6f, 0.18f);
            }
            AudioManager.PlaySfxAt("gbg01_" + (1 + i % 8).ToString("00"), p, 0.7f, 1f);
            yield return new WaitForSeconds(0.12f);
        }
        parts.rig.enabled = true;
        yield return new WaitForSeconds(0.8f);
        invaderActive = redVirus != null; // 赤い個体が出せなかったら、判定しない
    }

    /// <summary>赤いウイルスが倒れたら、すべてのウイルスを消す。ボスが強スタン・撃破のときにも消える。</summary>
    int redKills;

    void UpdateInvader()
    {
        if (!invaderActive) return;
        bool redDead = redVirus == null || (redVirus.GetComponent<Health>() != null && redVirus.GetComponent<Health>().IsDead);
        if (!redDead) return;
        AudioManager.PlaySfx("snesrpg04_14", 1f);
        AudioManager.PlaySfx("arcadeg01_17", 0.8f);
        Toast("<color=#7dffb0>赤いウイルスを倒した！</color>  すべてのウイルスが消滅した");
        CinematicDirector.Shake(0.2f, 0.4f);
        DismissMinions(true);
        redKills++;
        if (redKills % 2 == 0) { Toast("<color=#ffe566>2体目の赤いウイルスを倒した！</color>"); StrongStun(); } // 2回倒すたびに、強スタン
    }

    // ================================================================ ピクセルボール

    IEnumerator PixelBall()
    {
        int pat;
        do { pat = UnityEngine.Random.Range(0, 5); } while (pat == lastPattern);
        lastPattern = pat;
        float hpMul = HpRatio < 0.5f ? 1.3f : 1f;
        float spdMul = HpRatio < 0.5f ? 1.12f : 1f;
        Color ca = NeonArt.Pink, cb = NeonArt.Cyan, cc = NeonArt.Yellow;

        // 胸のモニターに弾幕のパターンが映る
        face.ShowPattern(pat, 1.35f);
        parts.rig.enabled = false;
        for (int i = 0; i < 6; i++)
        {
            AudioManager.PlaySfxAt("arcadeg01_" + (1 + i).ToString("00"), transform.position, 0.6f, 1f);
            if (parts.armL != null) { parts.armL.localRotation = Quaternion.Euler(-70f - i * 8f, 0f, -25f); parts.armR.localRotation = Quaternion.Euler(-70f - i * 8f, 0f, 25f); }
            yield return new WaitForSeconds(0.2f);
        }
        parts.rig.enabled = true;

        Vector3 o = BulletOrigin(transform.position);
        float dmg = B(0.5f);
        AudioManager.PlaySfxAt("snesrpg04_01", transform.position, 0.9f);
        switch (pat)
        {
            case 0: // 16方向 → 弾の隙間を埋めるように、回転しながら追加
                for (int rep = 0; rep < 2; rep++)
                {
                    PixelBullets.Ring(o, 16, 0f, 8f * spdMul, dmg, ca);
                    AudioManager.PlaySfxAt("arcadeg01_05", transform.position, 0.7f);
                    yield return new WaitForSeconds(0.4f);
                    PixelBullets.Ring(o, 16, 11.25f, 7f * spdMul, dmg, cb, 24f * (rep == 0 ? 1f : -1f));
                    AudioManager.PlaySfxAt("arcadeg01_07", transform.position, 0.7f);
                    yield return new WaitForSeconds(0.4f);
                    PixelBullets.Ring(o, 16, 5.6f, 6f * spdMul, dmg, cc, 0f);
                    yield return new WaitForSeconds(0.55f);
                }
                break;

            case 1: // 渦巻き（3本の腕）
            {
                int steps = Mathf.RoundToInt(46 * hpMul);
                for (int i = 0; i < steps; i++)
                {
                    float a0 = i * 13f;
                    for (int arm = 0; arm < 3; arm++)
                    {
                        float a = (a0 + arm * 120f) * Mathf.Deg2Rad;
                        PixelBullets.Spawn(o, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 8f * spdMul, dmg, arm == 0 ? ca : arm == 1 ? cb : cc);
                    }
                    if (i % 4 == 0) AudioManager.PlaySfxAt("gbg01_" + (1 + (i / 4) % 10).ToString("00"), transform.position, 0.4f, 1f);
                    yield return new WaitForSeconds(0.065f);
                }
                yield return new WaitForSeconds(0.6f);
                break;
            }

            case 2: // 弾の壁（すき間をくぐれ）
            {
                for (int row = 0; row < 4; row++)
                {
                    float yaw = AimYaw(transform.position);
                    Vector3 aim = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                    Vector3 side = Vector3.Cross(Vector3.up, aim);
                    Vector3 c0 = o + aim * 3.5f;
                    int gap = UnityEngine.Random.Range(-5, 6);
                    for (int k = -9; k <= 9; k++)
                    {
                        if (Mathf.Abs(k - gap) <= 1) continue;
                        PixelBullets.Spawn(c0 + side * (k * 1.6f), aim * 7f * spdMul, dmg, row % 2 == 0 ? ca : cb, 6f, 0.4f);
                    }
                    AudioManager.PlaySfxAt("arcadeg01_09", transform.position, 0.7f);
                    yield return new WaitForSeconds(0.95f);
                }
                break;
            }

            case 3: // 十字・斜めの流れ（回りながら伸びる）
            {
                int bullets = Mathf.RoundToInt(7 * hpMul);
                for (int wave = 0; wave < 2; wave++)
                {
                    for (int i = 0; i < bullets; i++)
                    {
                        for (int dIdx = 0; dIdx < 8; dIdx++)
                        {
                            float a = (dIdx * 45f + wave * 22.5f + i * 8f) * Mathf.Deg2Rad;
                            PixelBullets.Spawn(o, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 7.5f * spdMul, dmg, dIdx % 2 == 0 ? ca : cb, 5f, 0.34f, wave == 0 ? 14f : -14f);
                        }
                        AudioManager.PlaySfxAt("arcadeg01_" + (1 + i % 12).ToString("00"), transform.position, 0.35f, 1f);
                        yield return new WaitForSeconds(0.12f);
                    }
                    yield return new WaitForSeconds(0.5f);
                }
                break;
            }

            default: // 自機狙いの扇 ×5（ときどき、ばらまき）
            {
                for (int f = 0; f < 5; f++)
                {
                    float yaw = AimYaw(transform.position);
                    int count = Mathf.RoundToInt(7 * hpMul);
                    for (int i = 0; i < count; i++)
                    {
                        float a = (yaw + (i - (count - 1) * 0.5f) * 7f) * Mathf.Deg2Rad;
                        PixelBullets.Spawn(o, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (9f + (i % 2) * 2.5f) * spdMul, dmg, f % 2 == 0 ? ca : cc);
                    }
                    if (f % 2 == 1) PixelBullets.Ring(o, 12, UnityEngine.Random.value * 30f, 6f * spdMul, dmg, cb);
                    AudioManager.PlaySfxAt("arcadeg01_" + (3 + f).ToString("00"), transform.position, 0.7f);
                    yield return new WaitForSeconds(0.55f);
                }
                break;
            }
        }
        yield return new WaitForSeconds(0.4f);
    }

    // ================================================================ グリッチワープ

    IEnumerator GlitchWarp()
    {
        int n = HpRatio < 0.5f ? 3 : 2;
        for (int k = 0; k < n; k++)
        {
            face.Show(FaceKind.Warn, 1f);
            Vector3 pp = PlayerPos;
            // 行き先: プレイヤーの背後 / 頭上（真上から落ちてくる）
            bool overhead = UnityEngine.Random.value < 0.4f;
            Vector3 back = player != null ? -player.forward : -transform.forward;
            back.y = 0f;
            Vector3 want = overhead ? pp : pp + back.normalized * 3.4f;
            if (!NavMesh.SamplePosition(want, out var hit, 4f, NavMesh.AllAreas)) hit.position = transform.position;
            Vector3 dest = hit.position;

            // 分解されて消える
            AudioManager.PlaySfxAt("arcadeg01_17", transform.position, 1f);
            DigitalFx.Burst(transform.position + Vector3.up * 2.2f, 4.5f);
            parts.SetVisible(false);
            if (shieldGo != null) shieldGo.SetActive(false);

            // 出現地点に、ノイズの予兆
            Telegraph.Noise(dest, 5f, 0.9f);
            for (int i = 0; i < 4; i++)
            {
                AudioManager.PlaySfxAt("arcadeg01_" + (13 + i), dest, 0.6f, 1f);
                yield return new WaitForSeconds(0.2f);
            }
            if (player != null) { Vector3 toP = player.position - dest; toP.y = 0f; if (toP.sqrMagnitude > 0.1f) transform.rotation = Quaternion.LookRotation(toP); }
            agent.Warp(dest);

            // 現れる
            parts.SetVisible(true);
            if (shieldGo != null) shieldGo.SetActive(shieldUp);
            if (overhead)
            {
                parts.lift = 7f; parts.liftTarget = 0f;
                while (parts.lift > 0.05f) { parts.lift = Mathf.MoveTowards(parts.lift, 0f, 36f * Time.deltaTime); yield return null; }
            }
            AudioManager.PlaySfxAt("explosion2", dest, 1f, 1.1f);
            AudioManager.PlaySfxAt("nesg02_14", dest, 0.7f);
            Fx.Slam(dest, 5f);
            DigitalFx.Burst(dest + Vector3.up * 1.2f, 3f);
            CinematicDirector.Shake(0.35f, 0.4f);
            if (player != null)
            {
                Vector3 d = player.position - dest; d.y = 0f;
                if (d.magnitude < 5.2f && !PlayerAirborne(dest.y)) { Hurt(B(1.5f), dest); if (fpc != null) fpc.BossLaunch(d); }
            }
            yield return new WaitForSeconds(0.55f);
        }
    }

    // ================================================================ ダンスダンスレクイエム

    static readonly Color[] DdrColors =
    {
        new Color(1f, 0.2f, 0.5f), new Color(0.2f, 0.95f, 1f), new Color(1f, 0.85f, 0.2f), new Color(0.6f, 0.4f, 1f),
        new Color(0.3f, 1f, 0.5f), new Color(1f, 0.5f, 0.15f), new Color(0.4f, 0.6f, 1f),
    };

    static Material TileMat(Color c, float glow)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c * 0.5f);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c * 0.5f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * glow);
        return m;
    }

    /// <summary>ボス部屋全体に 16×16 のグリッドが広がる。光った順番を覚えて、その順に落ちる四角い光柱を避ける（3〜6回）。</summary>
    bool ddrActive;
    Coroutine ddr;
    GameObject ddrHolder;

    /// <summary>ダンスダンスレクイエムを始める（背景で進む。そのあいだも、アーケードマスターは、ほかの行動をする）。</summary>
    IEnumerator StartDdr()
    {
        if (!ddrActive)
        {
            ddrActive = true;
            ddr = StartCoroutine(DanceBody());
        }
        yield break;
    }

    public void StopDdr()
    {
        if (ddr != null) StopCoroutine(ddr);
        ddr = null;
        if (ddrHolder != null) Destroy(ddrHolder);
        ddrHolder = null;
        ddrActive = false;
    }

    /// <summary>
    /// ボス部屋全体に 16×16 のグリッドが広がる（部屋の広さに合わせて、タイルが大きくなる）。
    /// 20 か所が順番に光る → 覚えた 20 か所に、四角い光柱が同時に落ちる。これを 3〜6 回くりかえす。
    /// </summary>
    IEnumerator DanceBody()
    {
        GetArena(out var center, out float half);
        const int N = 16;
        float cell = half * 2f / N;
        float floorY = center.y + 0.05f;
        Vector3 origin = new Vector3(center.x - (N - 1) * 0.5f * cell, floorY, center.z - (N - 1) * 0.5f * cell);

        var holder = new GameObject("DanceGrid").transform;
        ddrHolder = holder.gameObject;
        var dark = TileMat(new Color(0.12f, 0.1f, 0.25f), 0.5f);
        var darkAlt = TileMat(new Color(0.08f, 0.07f, 0.18f), 0.4f);
        var lit = new Material[DdrColors.Length];
        for (int i = 0; i < lit.Length; i++) lit[i] = TileMat(DdrColors[i], 3.2f);
        var warnMat = TileMat(Color.white, 4.5f);
        var tiles = new Renderer[N, N];
        for (int x = 0; x < N; x++)
            for (int z = 0; z < N; z++)
            {
                var tl = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(tl.GetComponent<Collider>());
                tl.transform.SetParent(holder, false);
                tl.transform.position = origin + new Vector3(x * cell, 0f, z * cell);
                tl.transform.localScale = new Vector3(cell * 0.94f, 0.04f, cell * 0.94f);
                var rr = tl.GetComponent<Renderer>();
                rr.sharedMaterial = (x + z) % 2 == 0 ? dark : darkAlt;
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tiles[x, z] = rr;
            }
        Material Base(int x, int z) => (x + z) % 2 == 0 ? dark : darkAlt;

        AudioManager.PlaySfxAt("arcadeg01_14", transform.position, 1f);
        Toast("<color=#ffe566>ダンスダンスレクイエム</color>  光った 20 か所に、柱が同時に落ちる");
        yield return new WaitForSeconds(0.7f);

        int rounds = Mathf.Min(6, UnityEngine.Random.Range(3, 6) + (HpRatio < 0.5f ? 1 : 0));
        for (int r = 0; r < rounds; r++)
        {
            var seq = new List<Vector2Int>();
            for (int guard = 0; guard < 400 && seq.Count < 20; guard++)
            {
                var c = new Vector2Int(UnityEngine.Random.Range(0, N), UnityEngine.Random.Range(0, N));
                if (!seq.Contains(c)) seq.Add(c);
            }

            // ① 光る順番（20 か所）
            float step = Mathf.Max(0.07f, 0.12f - r * 0.008f);
            for (int i = 0; i < seq.Count; i++)
            {
                var c = seq[i];
                tiles[c.x, c.y].sharedMaterial = lit[i % lit.Length];
                if (i % 2 == 0) AudioManager.PlaySfxAt("gbg02_" + (1 + (i * 2) % 16).ToString("00"), tiles[c.x, c.y].transform.position, 0.6f, 1f);
                yield return new WaitForSeconds(step * 0.7f);
                tiles[c.x, c.y].sharedMaterial = Base(c.x, c.y);
                yield return new WaitForSeconds(step * 0.3f);
            }
            yield return new WaitForSeconds(0.5f);

            // ② 20 本が、同時に落ちる
            for (int i = 0; i < seq.Count; i++)
                StartCoroutine(Pillar(tiles[seq[i].x, seq[i].y], lit[i % lit.Length], warnMat, Base(seq[i].x, seq[i].y), cell, DdrColors[i % DdrColors.Length], i < 3));
            yield return new WaitForSeconds(1.6f);
        }
        Destroy(holder.gameObject);
        ddrHolder = null;
        ddrActive = false;
        ddr = null;
    }

    IEnumerator Pillar(Renderer tile, Material litMat, Material warnMat, Material baseMat, float cell, Color col, bool loud)
    {
        Vector3 pos = tile.transform.position;
        // 落ちる前の警告（点滅）
        for (int i = 0; i < 4; i++)
        {
            tile.sharedMaterial = i % 2 == 0 ? warnMat : litMat;
            yield return new WaitForSeconds(0.06f);
        }
        tile.sharedMaterial = litMat;
        yield return new WaitForSeconds(0.04f);
        // 光柱
        var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(p.GetComponent<Collider>());
        p.transform.position = pos + Vector3.up * 9f;
        p.transform.localScale = new Vector3(cell * 0.94f, 18f, cell * 0.94f); // 光柱は、地面のタイルと同じ太さ
        var m = TileMat(col, 4f);
        p.GetComponent<Renderer>().sharedMaterial = m;
        p.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (loud)
        {
            AudioManager.PlaySfxAt("pillar_fall", pos, 0.7f, 1.3f);
            AudioManager.PlaySfxAt("nesg02_07", pos, 0.5f);
            MagicFx.FlashLight(pos + Vector3.up * 2f, col, 14f, 10f, 0.25f);
        }
        Fx.Play(new Fx.Spec { color = col, color2 = Color.white, count = 24, speedMin = 2f, speedMax = 7f, sizeMin = 0.1f, sizeMax = 0.26f, life = 0.7f, radius = cell * 0.4f, gravity = 0.6f }, pos + Vector3.up * 0.2f);
        CinematicDirector.Shake(0.08f, 0.15f);
        float t = 0f;
        bool hurt = false;
        const float Life = 1.0f;     // 光柱が立っている時間（長め）
        while (t < Life)
        {
            t += Time.deltaTime;
            // 立っているあいだに入ると、当たる（1本につき1回）
            if (!hurt && player != null && playerHealth != null && !playerHealth.IsDead)
            {
                Vector3 d = player.position - pos;
                if (Mathf.Abs(d.x) <= cell * 0.5f + 0.2f && Mathf.Abs(d.z) <= cell * 0.5f + 0.2f && d.y < 3f) { hurt = true; Hurt(B(1.4f), pos); }
            }
            // 最後の 0.2 秒だけ、細くなって消える（それまでは、タイルと同じ太さのまま）
            float k = t < Life - 0.2f ? 1f : Mathf.Clamp01((Life - t) / 0.2f);
            if (p != null) p.transform.localScale = new Vector3(cell * 0.94f * k, 18f, cell * 0.94f * k);
            yield return null;
        }
        if (p != null) Destroy(p);
        if (tile != null) tile.sharedMaterial = baseMat;
    }
}
