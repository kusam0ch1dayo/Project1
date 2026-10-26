using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ネオンのトンネル（TunnelStyle.Neon）。地底都市（Metropolis）の摩天楼に、ネオンの装飾を重ねた街。
/// ・誰かのゲームプレイ画面が、無数に宙に浮かぶ。
/// ・敵が乗ったリフトが、街の上を縦横無尽に飛び回る。プレイヤーも乗れる（乗ると一緒に運ばれる）。乗っている敵は、近づくと転移して襲ってくる。
/// ・街の底から、サーチライトが夜空（天井）を照らす。
/// </summary>
public partial class TunnelStyleBuilder
{
    List<Tower> neonTowers;

    /// <summary>空中の点 p が、建物の中・天井や地面のすぐそば・トンネルの外か。</summary>
    bool NeonBlocked(Vector3 p)
    {
        if (p.y < bottomY + 5f || p.y > topY - 3f) return true;
        if (!InInterior(new Vector2(p.x, p.z), 3f)) return true;
        if (neonTowers != null)
            foreach (var tw in neonTowers)
                if (Mathf.Abs(p.x - tw.c.x) < tw.w * 0.5f + 3.2f && Mathf.Abs(p.z - tw.c.y) < tw.d * 0.5f + 3.2f && p.y < tw.topY + 4f) return true;
        return false;
    }

    bool NeonSegmentFree(Vector3 a, Vector3 b)
    {
        float len = Vector3.Distance(a, b);
        int steps = Mathf.Max(2, Mathf.CeilToInt(len / 3f));
        for (int i = 0; i <= steps; i++)
            if (NeonBlocked(Vector3.Lerp(a, b, i / (float)steps))) return false;
        return true;
    }

    Vector3? NeonRandomPoint()
    {
        for (int t = 0; t < 12; t++)
        {
            var s = segs[rng.Next(segs.Count)];
            var p2 = P(s, R(2f, s.L - 2f), R(2f, s.Wd - 2f));
            var pos = new Vector3(p2.x, R(bottomY + 8f, topY - 5f), p2.y);
            if (!NeonBlocked(pos)) return pos;
        }
        return null;
    }

    System.Collections.IEnumerator NeonCity()
    {
        // 土台: 地底都市（摩天楼・空中回廊・街灯）
        var city = Metropolis();
        while (city.MoveNext()) yield return null;

        // ---------------- 宙に浮かぶ、無数のゲームプレイ画面
        int screens = Mathf.Clamp(Mathf.RoundToInt(totalArea / 260f), 16, 80);
        int placed = 0;
        for (int t = 0; t < screens * 6 && placed < screens; t++)
        {
            if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
            var pos = NeonRandomPoint();
            if (!pos.HasValue) continue;
            var s = segs[rng.Next(segs.Count)];
            Vector3 face = new Vector3(s.cx - pos.Value.x, 0f, s.cz - pos.Value.z);
            if (face.sqrMagnitude < 4f) face = Quaternion.Euler(0f, R(0f, 360f), 0f) * Vector3.forward;
            NeonArt.Screen(moverRoot, pos.Value, face, R(3f, 9f), rng.Next(NeonArt.Kinds), NeonArt.Pick(rng), 0.7f, R(-5f, 5f));
            placed++;
        }

        // ---------------- サーチライト（街の底から、斜めに伸びて回る光の筒）
        float full = topY - bottomY;
        for (int i = 0; i < 6; i++)
        {
            var s = segs[rng.Next(segs.Count)];
            var p2 = P(s, R(4f, s.L - 4f), R(4f, s.Wd - 4f));
            if (NearJunction(p2, 3f)) continue;
            var gp = new GameObject("Searchlight");
            gp.transform.SetParent(moverRoot, false);
            gp.transform.localPosition = new Vector3(p2.x, bottomY + 0.3f, p2.y);
            gp.AddComponent<TunnelSpin>().speed = new Vector3(0f, R(12f, 28f) * (Chance(0.5f) ? 1f : -1f), 0f);
            var tilt = new GameObject("Tilt").transform;
            tilt.SetParent(gp.transform, false);
            tilt.localRotation = Quaternion.Euler(R(10f, 22f), 0f, 0f);
            float len = Mathf.Min(full - 4f, 46f);
            Color c = NeonArt.Pick(rng);
            Prim(PrimitiveType.Cylinder, new Vector3(0f, len * 0.5f, 0f), new Vector3(1.6f, len * 0.5f, 1.6f), Ghost(new Color(c.r, c.g, c.b, 0.1f)), null, false, tilt);
            Prim(PrimitiveType.Cylinder, Vector3.zero, new Vector3(1.6f, 0.2f, 1.6f), Lit(c, 0.3f, 3f), null, false, gp.transform);
        }

        // ---------------- リフト
        int lifts = Mathf.Clamp(Mathf.RoundToInt(totalLen / 55f), 3, 9);
        var perim = VoidLoop(Ledge + 3.5f, 12f);
        for (int i = 0; i < lifts; i++)
        {
            if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
            List<Vector3> path = null;
            if (perim.Count >= 4 && i % 2 == 0) path = NeonPerimeterPath(perim, i);
            if (path == null) path = NeonFreePath();
            if (path == null) continue;
            BuildLift(path, R(4.5f, 8f), (i + R(0f, 0.3f)) / lifts, NeonArt.Pick(rng), i % 3 != 2);
        }

        SegParticles(new Fx.Spec
        {
            color = NeonArt.Pink, color2 = NeonArt.Cyan, count = 0, duration = 30f, loop = true, life = 9f,
            speedMin = 0.3f, speedMax = 1.2f, sizeMin = 0.08f, sizeMax = 0.2f, shape = ParticleSystemShapeType.Box, radius = 1f,
        }, 1f / 140f);
    }

    /// <summary>トンネルの縁をなぞる、上下に波打つ周回路（縁の通路から、飛び移って乗れる）。</summary>
    List<Vector3> NeonPerimeterPath(List<Vector2> perim, int i)
    {
        var path = new List<Vector3>();
        float mid = (bottomY + topY) * 0.5f, amp = Mathf.Min((topY - bottomY) * 0.32f, 40f);
        float cycles = 1 + (i / 2) % 2, phase = i * 1.7f;
        for (int k = 0; k < perim.Count; k++)
        {
            float t = (float)k / perim.Count;
            path.Add(new Vector3(perim[k].x, mid + amp * Mathf.Sin(t * cycles * Mathf.PI * 2f + phase), perim[k].y));
        }
        if ((i & 2) != 0) path.Reverse();
        return path;
    }

    /// <summary>街の中を、建物を避けて、縦横無尽に飛び回る周回路。</summary>
    List<Vector3> NeonFreePath()
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var start = NeonRandomPoint();
            if (!start.HasValue) continue;
            var pts = new List<Vector3> { start.Value };
            Vector3 cur = start.Value;
            for (int k = 0; k < 7; k++)
            {
                for (int tries = 0; tries < 8; tries++)
                {
                    var q = cur + new Vector3(R(-55f, 55f), R(-20f, 20f), R(-55f, 55f));
                    q.y = Mathf.Clamp(q.y, bottomY + 8f, topY - 5f);
                    if (Vector3.Distance(q, cur) < 14f || NeonBlocked(q) || !NeonSegmentFree(cur, q)) continue;
                    pts.Add(q); cur = q;
                    break;
                }
            }
            while (pts.Count >= 3 && !NeonSegmentFree(pts[pts.Count - 1], pts[0])) pts.RemoveAt(pts.Count - 1);
            if (pts.Count >= 3) return pts;
        }
        return null;
    }

    void BuildLift(List<Vector3> path, float speed, float startFrac, Color neon, bool withRider)
    {
        var root = new GameObject("NeonLift");
        root.transform.SetParent(moverRoot, false);
        var t = root.transform;
        var body = Lit(new Color(0.14f, 0.12f, 0.24f), 0.5f);
        var dark = Lit(new Color(0.07f, 0.06f, 0.12f), 0.4f);
        var glow = Lit(neon, 0.3f, 3f);

        var deck = Box(new Vector3(0f, -0.2f, 0f), new Vector3(4.6f, 0.4f, 4.6f), body, null, true, t);
        deck.name = "LiftDeck";
        // 縁のネオン
        Box(new Vector3(0f, 0.02f, 2.27f), new Vector3(4.6f, 0.05f, 0.1f), glow, null, false, t);
        Box(new Vector3(0f, 0.02f, -2.27f), new Vector3(4.6f, 0.05f, 0.1f), glow, null, false, t);
        Box(new Vector3(2.27f, 0.02f, 0f), new Vector3(0.1f, 0.05f, 4.6f), glow, null, false, t);
        Box(new Vector3(-2.27f, 0.02f, 0f), new Vector3(0.1f, 0.05f, 4.6f), glow, null, false, t);
        // 低い手すり（左右。前後は開けてある）
        foreach (float sx in new[] { -2.2f, 2.2f })
        {
            Box(new Vector3(sx, 0.95f, 0f), new Vector3(0.08f, 0.08f, 4.4f), glow, null, false, t);
            foreach (float sz in new[] { -2.1f, 0f, 2.1f })
                Prim(PrimitiveType.Cylinder, new Vector3(sx, 0.5f, sz), new Vector3(0.1f, 0.5f, 0.1f), dark, null, false, t);
        }
        // 下: 推進の光
        Prim(PrimitiveType.Cylinder, new Vector3(0f, -0.5f, 0f), new Vector3(2.2f, 0.06f, 2.2f), glow, null, false, t);
        Prim(PrimitiveType.Cylinder, new Vector3(0f, -2.6f, 0f), new Vector3(2.0f, 2.0f, 2.0f), Ghost(new Color(neon.r, neon.g, neon.b, 0.14f)), null, false, t);
        // 後ろの操作柱と画面
        Prim(PrimitiveType.Cylinder, new Vector3(0f, 0.65f, -1.8f), new Vector3(0.3f, 0.65f, 0.3f), dark, null, false, t);
        NeonArt.Screen(t, new Vector3(0f, 1.7f, -1.8f), Vector3.forward, 1.3f, rng.Next(NeonArt.Kinds), neon);
        // 前の矢印（進む方向）
        for (int k = 0; k < 3; k++)
            Box(new Vector3(0f, 0.03f, 0.6f + k * 0.45f), new Vector3(0.9f - k * 0.25f, 0.03f, 0.08f), glow, null, false, t);

        var lg = new GameObject("LiftLight");
        lg.transform.SetParent(t, false);
        lg.transform.localPosition = new Vector3(0f, -1.2f, 0f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.color = neon; l.intensity = 4f; l.range = 20f; l.shadows = LightShadows.None;

        var loop = root.AddComponent<AirshipLoop>();
        loop.SetDeck(deck.transform, new Vector2(2.3f, 2.3f));
        loop.Setup(path, speed, startFrac);
        if (withRider) root.AddComponent<NeonLiftRider>().deck = deck.transform;
    }
}
