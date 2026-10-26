using System.Collections;
using UnityEngine;

/// <summary>
/// アーケードマスターの「クラッシュ」。ボスの HP が 50% を切ると（今までどおり耐えて）無敵になり、絶対に生き残れないレベルの弾幕・魔法・衝撃波を、
/// プレイヤーが倒れるまで、放ち続ける。プレイヤーが倒れた瞬間に GAME OVER？ が出る（光る領域へ行けば、CONTINUE できる）。
/// クラッシュ中は、アイテムとスキルが使えない（通常攻撃・魔法・回避は使える）。
/// </summary>
public partial class ArcadeMasterAI
{
    bool crushActive, crushDeath, continued;
    System.Func<bool> prevPlayerGuard;
    bool guardInstalled;

    /// <summary>プレイヤーが致死ダメージを受けた瞬間に、倒れさせず（HP 2%）、GAME OVER？ の演出へ進める。</summary>
    void InstallPlayerGuard()
    {
        if (guardInstalled || playerHealth == null) return;
        guardInstalled = true;
        prevPlayerGuard = playerHealth.deathGuard;
        playerHealth.deathGuard = () =>
        {
            if (crushActive && !crushDeath)
            {
                crushDeath = true;
                playerHealth.SetRatio(0.02f);
                playerHealth.GrantInvulnerability(25f);
                return true;
            }
            return prevPlayerGuard != null && prevPlayerGuard();
        };
    }

    void RemovePlayerGuard()
    {
        if (!guardInstalled) return;
        guardInstalled = false;
        if (playerHealth != null) playerHealth.deathGuard = prevPlayerGuard;
        prevPlayerGuard = null;
    }

    IEnumerator CrushRoutine()
    {
        // 領域が開いていたら、強制的に閉じる
        StopDdr();
        if (DomainRealm.IsActive && DomainRealm.Active != null) DomainRealm.Active.End("領域が打ち切られた");
        if (ArcadeRealm.Active != null) ArcadeRealm.EndActive();
        { float w = 0f; while ((DomainRealm.IsActive || ArcadeRealm.Active != null) && w < 4f) { w += Time.deltaTime; yield return null; } }
        PixelBullets.ClearAll();
        DismissMinions(true);
        DismissClones();

        crushActive = true;
        crushDeath = false;
        health.untouchable = true;   // HP が半分を切った: 耐えて、無敵になる
        SetShield(true);
        face.Show(FaceKind.Angry, 99f);
        parts.liftTarget = 3.8f;
        parts.SetDance(true);
        SkillSystem.CrushLock = true; // アイテム・スキルは、使えない
        InstallPlayerGuard();
        AddCleanup(() =>
        {
            crushActive = false;
            SkillSystem.CrushLock = false;
            RemovePlayerGuard();
            if (!InMasterCode) health.untouchable = false;
            if (parts != null) { parts.SetDance(false); parts.liftTarget = 0f; }
        });
        AudioManager.PlaySfx("nesrpg03_11", 1f, 0.7f);
        AudioManager.PlaySfx("arcadeg01_16", 1f);
        CinematicDirector.Shake(0.6f, 1.0f);
        Toast("<color=#ff3030>C R A S H</color>  アーケードマスターは無敵！ アイテムとスキルは使えない");
        yield return new WaitForSeconds(1.2f);

        BuildSpellPool();
        GetArena(out var center, out float half);
        int n = 0;
        while (!crushDeath && !dead && playerHealth != null && !playerHealth.IsDead)
        {
            n++;
            Vector3 o = BulletOrigin(transform.position);
            PixelBullets.Ring(o, 36, n * 5.1f, 8f + n % 4, B(1.3f), DdrColors[n % DdrColors.Length], n % 2 == 0 ? 24f : -24f);
            PixelBullets.Ring(o, 36, n * 5.1f + 5f, 11f, B(1.3f), DdrColors[(n + 3) % DdrColors.Length], n % 2 == 0 ? -18f : 18f);
            float yaw = AimYaw(transform.position);
            for (int k = -6; k <= 6; k++)
            {
                float a = (yaw + k * 6f) * Mathf.Deg2Rad;
                PixelBullets.Spawn(o, new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (13f + Mathf.Abs(k) * 0.5f), B(1.5f), DdrColors[(n + k + 9) % DdrColors.Length], 5f, 0.45f, 0f, n % 3 == 0 ? 40f : 0f);
            }
            if (caster != null)
                for (int s = 0; s < 3; s++) caster.CastNow(spellPool[UnityEngine.Random.Range(0, spellPool.Length)]);
            if (n % 3 == 0)
            {
                Vector2 off = UnityEngine.Random.insideUnitCircle * 5f;
                Vector3 g = PlayerPos + new Vector3(off.x, 0f, off.y);
                g.y = center.y;
                ArcadeShock.Strike(this, g, NeonArt.PickF(), Mathf.Clamp(half * 0.25f, 4f, 8f), half * 2.1f, 12f, B(1.5f), gameObject);
            }
            if (n % 4 == 0) AudioManager.PlaySfxAt("arcadeg01_" + (1 + n / 4 % 12).ToString("00"), transform.position, 0.6f);
            CinematicDirector.Shake(0.05f, 0.2f);
            yield return new WaitForSeconds(0.17f);
        }

        // プレイヤーが倒れた瞬間: GAME OVER？
        if (dead) yield break;
        PixelBullets.ClearAll();
        ArcadeShock.CancelAll();
        yield return StartCoroutine(GameOverRoutine());
    }
}
