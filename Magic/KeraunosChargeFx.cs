using UnityEngine;

/// <summary>
/// ケラウノスカイザーのチャージの演出。溜めるほど激しくなる。
///  足元の電気の魔法陣 / 周りから手元へ走る稲妻 / 手元で膨らむプラズマの球 / 浮き上がる小石と土ぼこり /
///  空気の歪み（揺らぐもや・画面の揺れ・視野のゆらぎ・ちらつき）/ 放電の音（パチパチ・ゴロゴロ・うなり）。
/// </summary>
public class KeraunosChargeFx : MonoBehaviour
{
    Transform player, hand;
    float progress, t, arcT, crackT, rumbleT, warpT, flickerT;
    bool fullDone;
    GameObject sigil, orb;
    Light orbLight;
    ElementBolts orbBolts;
    ParticleSystem hazePs, debrisPs, sparkPs;

    public static KeraunosChargeFx Create(Transform player, Transform hand)
    {
        var go = new GameObject("KeraunosChargeFx");
        var f = go.AddComponent<KeraunosChargeFx>();
        f.player = player;
        f.hand = hand;
        go.transform.position = player.position;

        // 足元の、電気の魔法陣
        f.sigil = MagicFxExtra.Sigil(ThunderSpells.Yellow, 2.6f);
        f.sigil.transform.SetParent(go.transform, false);

        // 手元のプラズマの球（稲妻をまとう）
        f.orb = MagicFx.Orb(Color.Lerp(ThunderSpells.Yellow, Color.white, 0.6f), 0.05f);
        f.orb.transform.SetParent(hand, false);
        f.orb.transform.localPosition = new Vector3(0.28f, -0.22f, 1.1f);
        f.orbBolts = f.orb.AddComponent<ElementBolts>();
        f.orbBolts.size = Vector3.one * 2.5f;
        f.orbLight = MagicFx.AddLight(f.orb.transform, Vector3.zero, ThunderSpells.Yellow, 4f, 1f);

        // 空気の歪み（揺らぐ、薄いもや）
        f.hazePs = Fx.Play(new Fx.Spec
        {
            color = new Color(1f, 0.97f, 0.85f, 0.10f), color2 = new Color(0.9f, 0.95f, 1f, 0.06f), count = 0, rate = 6f, duration = 1f, loop = true, local = true,
            speedMin = 0.2f, speedMax = 0.8f, sizeMin = 1.2f, sizeMax = 2.6f, life = 1.2f, radius = 2.2f, additive = false, shrink = false, grow = true, gravity = -0.15f,
            shape = ParticleSystemShapeType.Circle, edge = true,
        }, Vector3.up * 0.6f, go.transform, Quaternion.Euler(-90f, 0f, 0f));
        if (f.hazePs != null) MagicFx.Orbit(f.hazePs, 2.5f);

        // 浮き上がる小石と土ぼこり
        f.debrisPs = Fx.Play(new Fx.Spec
        {
            color = new Color(0.45f, 0.38f, 0.3f), color2 = new Color(0.65f, 0.58f, 0.45f), count = 0, rate = 4f, duration = 1f, loop = true, local = true,
            speedMin = 0.3f, speedMax = 1.2f, sizeMin = 0.05f, sizeMax = 0.14f, life = 1.6f, radius = 3f, additive = false, gravity = -0.35f,
            shape = ParticleSystemShapeType.Circle,
        }, Vector3.up * 0.05f, go.transform, Quaternion.Euler(-90f, 0f, 0f));

        // 体のまわりに散る火花
        f.sparkPs = Fx.Play(new Fx.Spec
        {
            color = ThunderSpells.Yellow, color2 = Color.white, count = 0, rate = 10f, duration = 1f, loop = true, local = true,
            speedMin = 1f, speedMax = 4f, sizeMin = 0.03f, sizeMax = 0.08f, life = 0.3f, radius = 1.2f,
        }, Vector3.up * 1.1f, go.transform);

        AudioManager.PlaySfx("beam_charge", 0.9f, 0.55f);   // 低いうなり
        AudioManager.PlaySfx("beam", 0.5f, 2.3f);
        return f;
    }

    public void SetProgress(float p) => progress = Mathf.Clamp01(p);

    void Update()
    {
        if (player == null) { Destroy(gameObject); return; }
        float dt = Time.deltaTime;
        t += dt;
        float k = progress;
        transform.position = player.position;

        // 魔法陣と、まわりの演出が、溜めるほど激しくなる
        if (sigil != null) sigil.transform.localScale = Vector3.one * (0.6f + 0.8f * k);
        SetRate(hazePs, 6f + 30f * k);
        SetRate(debrisPs, 4f + 40f * k);
        SetRate(sparkPs, 10f + 120f * k);

        // 手元の球
        if (orb != null)
        {
            float pulse = 1f + Mathf.Sin(t * (10f + 20f * k)) * 0.08f;
            orb.transform.localScale = Vector3.one * (0.05f + 0.33f * k) * pulse;
            if (orbBolts != null) orbBolts.size = Vector3.one * (0.6f + 2.2f * k);
            if (orbLight != null) { orbLight.intensity = 1f + 9f * k + Random.value * 3f * k; orbLight.range = 4f + 10f * k; }
        }

        Vector3 handPos = orb != null ? orb.transform.position : player.position + Vector3.up * 1.4f;

        // まわりから、手元へ走る稲妻（溜めるほど、多く・太く）
        arcT -= dt;
        if (arcT <= 0f)
        {
            arcT = Mathf.Lerp(0.3f, 0.04f, k);
            int n = 1 + Mathf.RoundToInt(3f * k);
            for (int i = 0; i < n; i++)
            {
                Vector3 from = player.position + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized * Random.Range(1.5f, 2.5f + 2.5f * k) + Vector3.up * Random.Range(0f, 2.5f);
                ThunderFx.Lightning(from, handPos, ThunderSpells.Yellow, 0.025f + 0.05f * k, 0.08f + 0.06f * k);
            }
            if (k > 0.3f && Random.value < 0.35f)   // 地面へ落ちる放電
            {
                Vector3 g = player.position + new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f)).normalized * Random.Range(2f, 5f);
                ThunderFx.Lightning(g + Vector3.up * Random.Range(4f, 7f), g, Color.Lerp(ThunderSpells.Yellow, Color.white, 0.4f), 0.06f + 0.06f * k, 0.12f);
                ThunderFx.Spark(g, ThunderSpells.Yellow, 0.6f + k);
            }
        }

        // 放電の音: パチパチ（間隔が短くなっていく）と、ゴロゴロ
        crackT -= dt;
        if (crackT <= 0f)
        {
            crackT = Mathf.Lerp(0.35f, 0.06f, k) * Random.Range(0.7f, 1.3f);
            AudioManager.PlaySfx("beam", 0.18f + 0.3f * k, Random.Range(2.0f, 2.8f));
        }
        rumbleT -= dt;
        if (rumbleT <= 0f && k > 0.25f)
        {
            rumbleT = Mathf.Lerp(1.6f, 0.6f, k) * Random.Range(0.8f, 1.2f);
            AudioManager.PlaySfx("explosion2", 0.15f + 0.35f * k, Random.Range(0.35f, 0.55f));
        }

        // 空気の歪み: 細かい揺れ・視野のゆらぎ・画面のちらつき
        warpT -= dt;
        if (warpT <= 0f)
        {
            warpT = Mathf.Lerp(0.45f, 0.12f, k);
            CinematicDirector.Shake(0.01f + 0.05f * k, warpT + 0.05f);
            CinematicDirector.FovPunch(Random.Range(-1f, 1f) * (0.6f + 3.4f * k), warpT * 1.4f);
        }
        flickerT -= dt;
        if (flickerT <= 0f && k > 0.4f)
        {
            flickerT = Random.Range(0.25f, 0.9f) * Mathf.Lerp(1.5f, 0.6f, k);
            ScreenFlash.Play(new Color(1f, 0.95f, 0.7f), 0.02f, 0.08f, 0.08f + 0.17f * k);
        }

        if (k >= 1f && !fullDone)
        {
            fullDone = true;
            ThunderFx.Burst(handPos, ThunderSpells.Yellow, 2.5f);
            ThunderFx.GroundRing(player.position, ThunderSpells.Yellow, 6f);
            ScreenFlash.Play(new Color(1f, 0.95f, 0.7f), 0.03f, 0.25f, 0.35f);
            AudioManager.PlaySfx("pillar_cast", 0.8f, 1.5f);
            AudioManager.PlaySfx("explosion2", 0.6f, 0.45f);
        }
    }

    static void SetRate(ParticleSystem ps, float rate)
    {
        if (ps == null) return;
        var em = ps.emission;
        em.rateOverTime = rate;
    }

    /// <summary>チャージをやめた・放った: 片づける。</summary>
    public void Finish()
    {
        if (orb != null) Destroy(orb);
        foreach (var ps in GetComponentsInChildren<ParticleSystem>()) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        if (sigil != null) MagicFx.Detach(sigil, 0.5f);
        enabled = false;
        Destroy(gameObject, 1.5f);
    }

    void OnDestroy()
    {
        if (orb != null) Destroy(orb);
    }
}
