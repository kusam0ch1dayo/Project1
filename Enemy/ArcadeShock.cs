using System.Collections;
using UnityEngine;

/// <summary>
/// 多色の四角いラインが空から落ちてきて、地面に触れると、領域の端まで地面を走る衝撃波になる（ジャンプで躱せる）。
/// 電界・電磁域とマスターコードで使う（マスターコードは、より大きい）。
/// </summary>
public static class ArcadeShock
{
    static Mesh tube;
    static Material waveMat;
    static int epoch;
    static MonoBehaviour runner;

    /// <summary>落下中・走行中の衝撃波を、すべて消す（領域が終わったとき）。</summary>
    public static void CancelAll() { epoch++; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { tube = null; waveMat = null; epoch = 0; runner = null; }

    /// <summary>衝撃波の見た目: 縦長の円筒の壁（両面）。XZ を半径、Y を高さに拡大して使う。</summary>
    static Mesh Tube()
    {
        if (tube != null) return tube;
        const int N = 64;
        var v = new System.Collections.Generic.List<Vector3>();
        var t = new System.Collections.Generic.List<int>();
        for (int i = 0; i <= N; i++)
        {
            float a = i * Mathf.PI * 2f / N;
            v.Add(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            v.Add(new Vector3(Mathf.Cos(a), 1f, Mathf.Sin(a)));
        }
        for (int i = 0; i < N; i++)
        {
            int b = i * 2;
            t.AddRange(new[] { b, b + 1, b + 2, b + 1, b + 3, b + 2 });
            t.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 }); // 裏からも見える
        }
        tube = new Mesh { name = "ArcadeShockTube" };
        tube.SetVertices(v);
        tube.SetTriangles(t, 0);
        tube.RecalculateNormals();
        tube.RecalculateBounds();
        return tube;
    }

    /// <summary>光らせすぎない、単色の材質（Unlit。両面）。白飛びや、陰の黒つぶれを防ぐ。</summary>
    static Material Glow(Color c, float k)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
        Color col = Color.Lerp(c, Color.white, 0.12f);
        m.color = col;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        return m;
    }

    /// <summary>
    /// ground に、size 四方の大きなラインを落とす。maxRadius まで、speed の速さで衝撃波が走る。
    /// 衝撃波は、高さ約 0.7m の壁。ジャンプしていれば当たらない。
    /// </summary>
    public static void Strike(MonoBehaviour host, Vector3 ground, Color color, float size, float maxRadius, float speed, float damage, GameObject owner)
    {
        if (runner == null) runner = new GameObject("ArcadeShockRunner").AddComponent<ShockRunner>();
        runner.StartCoroutine(Run(epoch, ground, color, size, maxRadius, speed, damage, owner));
    }

    /// <summary>プレイヤーの領域魔法用: 衝撃波が、プレイヤーではなく、敵に当たる（ジャンプしている敵には当たらない）。</summary>
    public static void StrikeEnemies(Vector3 ground, Color color, float size, float maxRadius, float speed, float damage)
    {
        if (runner == null) runner = new GameObject("ArcadeShockRunner").AddComponent<ShockRunner>();
        runner.StartCoroutine(Run(epoch, ground, color, size, maxRadius, speed, damage, null, true));
    }

    static IEnumerator Run(int myEpoch, Vector3 ground, Color color, float size, float maxRadius, float speed, float damage, GameObject owner, bool vsEnemies = false)
    {
        // 落ちる場所の、四角い予兆
        var mark = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(mark.GetComponent<Collider>());
        mark.transform.position = ground + Vector3.up * 0.06f;
        mark.transform.localScale = new Vector3(size, 0.03f, size);
        mark.GetComponent<Renderer>().sharedMaterial = Glow(color, 2.2f);
        mark.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 大きな四角いラインが落ちてくる
        float len = size * 6f;
        var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(line.GetComponent<Collider>());
        line.transform.localScale = new Vector3(size, len, size);
        line.GetComponent<Renderer>().sharedMaterial = Glow(color, 3.5f);
        line.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        float y = ground.y + 80f;
        AudioManager.PlaySfxAt("arcadeg01_16", ground, 0.5f, 1.4f);
        while (y > ground.y + len * 0.5f)
        {
            if (epoch != myEpoch) { Object.Destroy(line); Object.Destroy(mark); yield break; }
            y -= 62f * Time.deltaTime;
            if (line != null) line.transform.position = new Vector3(ground.x, y, ground.z);
            yield return null;
        }
        Object.Destroy(line);
        Object.Destroy(mark);

        // 着弾
        var enemyHit = vsEnemies ? new System.Collections.Generic.HashSet<Health>() : null;
        if (vsEnemies)
            foreach (var h in new System.Collections.Generic.List<Health>(MagicTargets.Alive))
            {
                if (h == null || h.IsDead || !MagicHit.IsEnemy(h)) continue;
                Vector3 dd = h.transform.position - ground; dd.y = 0f;
                if (dd.magnitude > size * 0.9f || Mathf.Abs(h.transform.position.y - ground.y) > 4f) continue;
                enemyHit.Add(h);
                MagicHit.Damage(h, damage * 1.6f, Element.None, ground + Vector3.up * 30f, MagicTargets.Center(h)); // 直撃は、衝撃波より強い
            }
        AudioManager.PlaySfxAt("explosion1", ground, 0.7f, 1.3f);
        AudioManager.PlaySfxAt("nesg02_07", ground, 0.6f);
        CinematicDirector.Shake(0.3f, 0.35f);
        MagicFx.FlashLight(ground + Vector3.up * 1.5f, color, 22f, 14f, 0.3f);
        Fx.Play(new Fx.Spec { color = color, color2 = Color.white, count = 70, speedMin = 3f, speedMax = 12f, sizeMin = 0.15f, sizeMax = 0.4f, life = 0.9f, radius = size * 0.4f, gravity = 0.8f }, ground + Vector3.up * 0.3f);

        // 衝撃波: 地面を、領域の端まで走る
        var wave = new GameObject("ArcadeShockWave");
        wave.AddComponent<MeshFilter>().sharedMesh = Tube();
        var mr = wave.AddComponent<MeshRenderer>();
        mr.sharedMaterial = Glow(color, 3f);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var lightGo = new GameObject("WaveLight");
        lightGo.transform.SetParent(wave.transform, false);
        var wl = lightGo.AddComponent<Light>();
        wl.type = LightType.Point; wl.color = color; wl.range = 10f; wl.intensity = 6f; wl.shadows = LightShadows.None;

        var player = Hz.Player;
        var ph = player != null ? player.GetComponent<Health>() : null;
        float r = size * 0.5f;
        bool hit = false;
        const float Height = 0.7f;
        float baseY = ground.y;
        while (r < maxRadius && wave != null)
        {
            if (epoch != myEpoch) break;
            r += speed * Time.deltaTime;
            wave.transform.position = new Vector3(ground.x, baseY + 0.02f, ground.z);
            wave.transform.localScale = new Vector3(r, Height, r);
            lightGo.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            if (vsEnemies)
            {
                foreach (var h in new System.Collections.Generic.List<Health>(MagicTargets.Alive))
                {
                    if (h == null || h.IsDead || enemyHit.Contains(h) || !MagicHit.IsEnemy(h)) continue;
                    Vector3 d = h.transform.position - ground; d.y = 0f;
                    float feet = h.transform.position.y - baseY;
                    if (Mathf.Abs(d.magnitude - r) < 1.4f && feet < Height + 0.1f && feet > -4f)
                    {
                        enemyHit.Add(h);
                        MagicHit.Damage(h, damage, Element.None, ground, MagicTargets.Center(h));
                    }
                }
            }
            else if (!hit && player != null && ph != null && !ph.IsDead)
            {
                Vector3 d = player.transform.position - ground; d.y = 0f;
                // 同じ高さにいて、ジャンプしていなければ当たる
                if (Mathf.Abs(d.magnitude - r) < 1.0f && player.transform.position.y - baseY < Height + 0.05f && Mathf.Abs(player.transform.position.y - baseY) < 4f)
                {
                    hit = true;
                    EnemyStrike.Melee(ph, damage, ground, Element.Light, owner);
                }
            }
            yield return null;
        }
        if (wave != null) Object.Destroy(wave);
    }
}

/// <summary>衝撃波の実行役（領域などが消えても、止まらないように、独立した部品で動かす）。</summary>
public class ShockRunner : MonoBehaviour { }
