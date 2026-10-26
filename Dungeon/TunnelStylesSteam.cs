using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// スチームパンクのトンネル（TunnelStyle.Steampunk）。全部が銅でできた、巨大な機械の吹き抜け。
/// ・巨大な電気筒… 全階を貫くガラスの筒に、銅の節。青白い稲妻が、端から端まで走る。
/// ・歯車… 壁に、噛み合う大歯車の列。天井にも、水平に回る大歯車。曲がり角には、歯車の塔。
/// ・大きな橋… 各階の高さで、トンネルを渡る銅の橋（歩ける）。
/// ・飛行船… 階と階のあいだの空を、トンネルに沿って、ぐるっと周回する。
/// ・ガラスの円柱エレベーター（この造りだけ）… TunnelGimmicks が作る（外壁通路から、全階をつなぐ）。
/// </summary>
public partial class TunnelStyleBuilder
{
    GameObject Beam(Vector3 p, Vector3 q, float thick, Material mat, bool solid = false)
    {
        Vector3 d = q - p;
        float len = d.magnitude;
        if (len < 0.01f) return null;
        return Box((p + q) * 0.5f, new Vector3(thick, thick, len), mat, Quaternion.LookRotation(d / len), solid);
    }

    System.Collections.IEnumerator Steampunk()
    {
        var cu = Lit(SteamArt.Copper, 0.55f);
        var cuD = Lit(SteamArt.CopperDark, 0.45f);
        var brass = Lit(SteamArt.Brass, 0.65f);
        var elec = Lit(SteamArt.Electric, 0.5f, 3.4f);
        var glass = Ghost(new Color(0.55f, 0.85f, 1f, 0.15f));
        var deckMat = Lit(new Color(0.5f, 0.3f, 0.18f), 0.5f);
        Color blue = SteamArt.Electric, warm = new Color(1f, 0.65f, 0.35f);

        float minWd = float.MaxValue;
        foreach (var s in segs) minWd = Mathf.Min(minWd, s.Wd);
        float tubeR = Mathf.Clamp(minWd * 0.08f, 1.6f, 2.6f);
        float tubeT = tubeR + 2.6f;
        float full = topY - bottomY;

        // 昇降機（外壁通路ぎわ）の位置。そのまわりには、筒・橋・歯車を置かない
        var s0 = segs[0];
        int lo = s0.alongX ? s0.sh.Void.xMin : s0.sh.Void.yMin, hi = s0.alongX ? s0.sh.Void.xMax : s0.sh.Void.yMax;
        float elevU = (lo + (int)((hi - lo) * 0.3f)) * cs - (s0.alongX ? s0.x0 : s0.z0);
        bool NearElev(Seg s, float u, float margin) => s == s0 && Mathf.Abs(u - elevU) < margin;

        // ---------------- 巨大な電気筒と、橋の位置
        const float D = 38f;
        var tubeSlots = new List<(Seg s, float u, float step)>();
        var bridgeSlots = new List<(Seg s, float u)>();
        foreach (var s in segs)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(s.L / D));
            float step = s.L / count;
            for (int i = 0; i < count; i++) tubeSlots.Add((s, step * (i + 0.5f), step));
            for (int i = 1; i < count; i++) bridgeSlots.Add((s, step * i));
        }

        int tubeNo = 0;
        foreach (var (s, u, step) in tubeSlots)
        {
            if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
            for (int side = 0; side < 2; side++)
            {
                float t = side == 0 ? tubeT : s.Wd - tubeT;
                if (side == 0 && NearElev(s, u, 12f)) continue;
                var p = P(s, u, t);
                if (NearJunction(p, 10f)) continue;
                SteamArt.ElectricTube(moverRoot, new Vector3(p.x, bottomY, p.y), full, tubeR, glass, cu, brass, elec);
                if ((tubeNo++ & 1) == 0) Lamp(new Vector3(p.x, bottomY + full * R(0.25f, 0.75f), p.y), blue, 5.5f, 34f);
            }
        }

        // ---------------- 大きな橋（各階の高さ。歩ける）
        int bridgeNo = 0;
        foreach (var (s, u) in bridgeSlots)
        {
            Vector2 mid = P(s, u, s.Wd * 0.5f);
            if (NearJunction(mid, 16f) || NearElev(s, u, 13f)) continue;
            for (int f = 0; f < n - 1; f++)
            {
                if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                CopperBridge(s, u, g.FloorY(f), f == 0 ? topY : g.FloorY(f - 1) - 0.5f, cu, cuD, brass, elec, deckMat, bridgeNo++ % 3 == 0);
            }
        }

        // ---------------- トンネルの真ん中: 電気の尖塔（全階を貫く柱、階の高さで回る環、頂の稲妻）
        foreach (var (s, u, step) in tubeSlots)
        {
            if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
            var c = P(s, u, s.Wd * 0.5f);
            if (NearJunction(c, 12f) || NearElev(s, u, 10f)) continue;
            var root = new GameObject("SteamSpire").transform;
            root.SetParent(moverRoot, false);
            root.localPosition = new Vector3(c.x, bottomY, c.y);
            Prim(PrimitiveType.Cylinder, Vector3.up * (full * 0.5f), new Vector3(1.4f, full * 0.5f, 1.4f), cu, null, false, root);
            Prim(PrimitiveType.Cylinder, Vector3.up * (full * 0.5f), new Vector3(0.5f, full * 0.5f, 0.5f), elec, null, false, root);
            for (float y = 1f; y < full; y += 6f)
                Prim(PrimitiveType.Cylinder, Vector3.up * y, new Vector3(2f, 0.2f, 2f), brass, null, false, root);
            Prim(PrimitiveType.Cylinder, Vector3.up * 0.4f, new Vector3(3.6f, 0.4f, 3.6f), cuD, null, false, root);
            Prim(PrimitiveType.Sphere, Vector3.up * (full - 1f), Vector3.one * 2.2f, elec, null, false, root);
            ElectricArc.Create(root, Vector3.up * (full - 1f), Vector3.up * 1.5f + new Vector3(1.2f, 0f, 0.4f), 24, 0.7f, 0.2f, true);
            for (int f = 0; f < n; f++)
            {
                float ry = g.FloorY(f) - bottomY + 3.2f;
                if (ry > full - 3f) continue;
                var ring = new GameObject("SpireRing").transform;
                ring.SetParent(root, false);
                ring.localPosition = Vector3.up * ry;
                ring.localRotation = Quaternion.Euler(R(-12f, 12f), R(0f, 360f), R(-12f, 12f));
                ring.gameObject.AddComponent<TunnelSpin>().speed = new Vector3(0f, (f & 1) == 0 ? 24f : -24f, 0f);
                for (int k = 0; k < 10; k++)
                {
                    float a = k * Mathf.PI * 2f / 10f;
                    Prim(PrimitiveType.Sphere, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.5f, Vector3.one * (k % 3 == 0 ? 0.7f : 0.35f), k % 3 == 0 ? elec : brass, null, false, ring);
                }
                Box(Vector3.zero, new Vector3(5f, 0.12f, 0.12f), cu, null, false, ring);
                Box(Vector3.zero, new Vector3(0.12f, 0.12f, 5f), cu, null, false, ring);
            }
            Lamp(new Vector3(c.x, bottomY + full * R(0.3f, 0.7f), c.y), blue, 4.5f, 30f);
        }

        // ---------------- 壁の、噛み合う大歯車の列
        steamTrainNo = 0;
        foreach (var s in segs)
        {
            int cnt = Mathf.Max(1, Mathf.RoundToInt(s.L / 90f));
            for (int f = 0; f < n - 1; f++)
                for (int i = 0; i < cnt; i++)
                {
                    if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                    int side = (f + i) & 1;
                    float uc = s.L * ((i + 0.5f) / cnt) + ((f & 1) == 0 ? 0f : s.L * 0.18f / cnt);
                    GearTrain(s, uc, g.FloorY(f) - 10f + R(-1f, 1f), side, cu, cuD, brass, elec, NearElev);
                }
        }

        // ---------------- 天井: 梁と管、稲妻の導管、水平の大歯車
        foreach (var s in segs)
        {
            if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
            Vector3 ud = s.alongX ? Vector3.right : Vector3.forward;
            Vector3 across = s.alongX ? Vector3.forward : Vector3.right;
            for (float u = 7f; u < s.L - 4f; u += 14f)
            {
                var c = P(s, u, s.Wd * 0.5f);
                if (NearJunction(c, 12f)) continue;
                Box(new Vector3(c.x, topY - 0.9f, c.y), s.alongX ? new Vector3(0.8f, 1.1f, s.Wd) : new Vector3(s.Wd, 1.1f, 0.8f), cu);
                for (int k = 0; k < 2; k++)
                {
                    Vector2 pe = P(s, u, k == 0 ? 0.6f : s.Wd - 0.6f);
                    Prim(PrimitiveType.Cylinder, new Vector3(pe.x, topY - 1.6f, pe.y), new Vector3(0.9f, 1.4f, 0.9f), brass);
                }
            }
            for (int k = 0; k < 2; k++)
            {
                float tt = s.Wd * (k == 0 ? 0.3f : 0.7f);
                var a = P(s, 0f, tt); var b = P(s, s.L, tt);
                Vector3 mp = new Vector3((a.x + b.x) * 0.5f, topY - 3f, (a.y + b.y) * 0.5f);
                Prim(PrimitiveType.Cylinder, mp, new Vector3(1.6f, s.L * 0.5f, 1.6f), cu, Quaternion.FromToRotation(Vector3.up, ud));
                for (float u = 5f; u < s.L - 2f; u += 9f)
                {
                    var pp = P(s, u, tt);
                    Prim(PrimitiveType.Cylinder, new Vector3(pp.x, topY - 3f, pp.y), new Vector3(2.1f, 0.18f, 2.1f), brass, Quaternion.FromToRotation(Vector3.up, ud));
                }
            }
            {
                var a = P(s, 0f, s.Wd * 0.5f); var b = P(s, s.L, s.Wd * 0.5f);
                Vector3 mp = new Vector3((a.x + b.x) * 0.5f, topY - 1.9f, (a.y + b.y) * 0.5f);
                Prim(PrimitiveType.Cylinder, mp, new Vector3(0.35f, s.L * 0.5f, 0.35f), elec, Quaternion.FromToRotation(Vector3.up, ud));
            }
            if (s.Wd >= 18f)
            {
                var frame = Quaternion.LookRotation(Vector3.up, ud);
                for (float u = D * 0.5f; u < s.L - 12f; u += D * 2f)
                {
                    float rA = Mathf.Min(7f, s.Wd * 0.26f), rB = rA * 0.7f;
                    var ca = P(s, u, s.Wd * 0.5f);
                    if (NearJunction(ca, 14f)) continue;
                    Vector3 a3 = new Vector3(ca.x, topY - 1.2f, ca.y);
                    Vector3 b3 = a3 + ud * (rA + rB);
                    if (!InInterior(new Vector2(b3.x, b3.z), rB)) continue;
                    int nA = SteamArt.TeethFor(rA), nB = SteamArt.TeethFor(rB);
                    SteamArt.BuildGear(moverRoot, a3, frame, rA, 1f, nA, 8f, SteamArt.AngleToward(frame, ud), cu, cuD, brass, elec);
                    SteamArt.BuildGear(moverRoot, b3, frame, rB, 1f, nB, -8f * nA / nB, SteamArt.AngleToward(frame, -ud) + 180f / nB, cu, cuD, brass, elec);
                }
            }
        }

        // ---------------- 底: ボイラー（蒸気を噴く）
        int boilers = 0;
        foreach (var (s, u, step) in tubeSlots)
        {
            if (boilers >= 8 || !Chance(0.5f)) continue;
            var p = P(s, Mathf.Clamp(u + step * 0.25f, 5f, s.L - 5f), s.Wd * (Chance(0.5f) ? 0.22f : 0.78f));
            if (NearJunction(p, 12f) || NearCenterline(p, 6f)) continue;
            Quaternion along = Quaternion.LookRotation(s.alongX ? Vector3.right : Vector3.forward) * Quaternion.Euler(90f, 0f, 0f);
            Prim(PrimitiveType.Cylinder, new Vector3(p.x, bottomY + 1.8f, p.y), new Vector3(3.4f, 3.4f, 3.4f), cu, along);
            for (int k = -1; k <= 1; k++)
                Prim(PrimitiveType.Cylinder, new Vector3(p.x, bottomY + 1.8f, p.y) + (s.alongX ? Vector3.right : Vector3.forward) * (k * 2.4f), new Vector3(3.6f, 0.16f, 3.6f), brass, along);
            Prim(PrimitiveType.Cylinder, new Vector3(p.x, bottomY + 5f, p.y), new Vector3(0.9f, 2.6f, 0.9f), cuD);
            Prim(PrimitiveType.Sphere, new Vector3(p.x, bottomY + 2.6f, p.y) + (s.alongX ? Vector3.forward : Vector3.right) * 1.75f, Vector3.one * 0.7f, elec);
            boilers++;
            Fx.Play(new Fx.Spec
            {
                color = new Color(0.92f, 0.94f, 0.97f, 0.5f), color2 = new Color(0.7f, 0.75f, 0.8f, 0.4f), count = 0, rate = 9f, duration = 30f, loop = true, life = 5f,
                speedMin = 0.8f, speedMax = 1.6f, sizeMin = 1f, sizeMax = 2.2f, gravity = -0.04f, shape = ParticleSystemShapeType.Cone, radius = 0.5f, angle = 10f,
                grow = true, additive = false, shrink = false,
            }, new Vector3(p.x, bottomY + 6.4f, p.y), moverRoot, Quaternion.Euler(-90f, 0f, 0f));
        }

        // ---------------- 飛行船（階と階のあいだを、トンネルに沿って周回）
        float k0 = Mathf.Min(minWd * 0.5f - 5f, tubeR * 2f + 2.6f + 3.2f);
        k0 = Mathf.Max(k0, 6f);
        var loop = VoidLoop(Ledge + k0, 14f);
        if (loop.Count >= 4)
        {
            int ships = Mathf.Clamp(Mathf.RoundToInt(totalLen / 140f), 2, 5);
            for (int i = 0; i < ships; i++)
            {
                if (LoadBudget.Check()) { yield return null; LoadBudget.Reset(); }
                float y = g.FloorY(i % (n - 1)) - 9.5f;   // 甲板は、上の階の橋より、約3m下（飛び降りて乗れる）
                var path = new List<Vector3>();
                foreach (var pt in loop) path.Add(new Vector3(pt.x, y, pt.y));
                if ((i & 1) == 1) path.Reverse();
                BuildAirship(path, R(5.5f, 8f), (i + R(0f, 0.3f)) / ships, cu, cuD, brass, elec);
            }
        }

        SegParticles(new Fx.Spec
        {
            color = blue, color2 = warm, count = 0, duration = 30f, loop = true, life = 8f,
            speedMin = 0.15f, speedMax = 0.7f, sizeMin = 0.06f, sizeMax = 0.18f, gravity = -0.03f,
            shape = ParticleSystemShapeType.Box, radius = 1f,
        }, 1f / 110f);
    }

    // ---------------------------------------------------------------- 橋

    void CopperBridge(Seg s, float u, float y, float ceilY, Material cu, Material cuD, Material brass, Material elec, Material deckMat, bool withLamp)
    {
        Vector2 a2 = P(s, u, 0f), b2 = P(s, u, s.Wd);
        Vector3 a = new Vector3(a2.x, y, a2.y), b = new Vector3(b2.x, y, b2.y);
        Vector3 dir = (b - a).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, dir);
        float len = Vector3.Distance(a, b);
        const float W = 6.5f;
        var rot = Quaternion.LookRotation(dir);
        Vector3 mid = (a + b) * 0.5f;

        Slab(a, b, W, 0.45f, deckMat, true, 2.4f);
        for (float d = 2f; d < len - 1f; d += 3f)
            Box(a + dir * d + Vector3.up * 0.02f, new Vector3(W - 0.4f, 0.03f, 0.12f), cuD, rot);
        foreach (float sd in new[] { -1f, 1f })
        {
            Vector3 off = right * (sd * (W * 0.5f - 0.15f));
            // 手すりは、真ん中の広場（半径 6m）の所だけ、開ける
            float railLen = len * 0.5f - 6.2f;
            if (railLen > 1f)
            {
                Box(a + dir * (railLen * 0.5f) + off + Vector3.up * 0.6f, new Vector3(0.22f, 1.2f, railLen), cu, rot, true);
                Box(b - dir * (railLen * 0.5f) + off + Vector3.up * 0.6f, new Vector3(0.22f, 1.2f, railLen), cu, rot, true);
            }
            Box(mid + off * 0.9f + Vector3.up * 0.04f, new Vector3(0.14f, 0.05f, len - 1f), elec, rot);
            for (float d = 1f; d < len; d += 4f)
                if (Mathf.Abs(d - len * 0.5f) > 6.2f)
                    Prim(PrimitiveType.Cylinder, a + dir * d + off + Vector3.up * 0.9f, new Vector3(0.28f, 1f, 0.28f), brass);
            // 下の、トラス
            int m = Mathf.Max(3, Mathf.RoundToInt(len / 4f));
            Vector3 so = off * 0.7f;
            Beam(a + dir * 1f + so - Vector3.up * 1.7f, b - dir * 1f + so - Vector3.up * 1.7f, 0.3f, cuD);
            for (int k = 0; k < m; k++)
            {
                Vector3 p = a + dir * (len * k / m) + so - Vector3.up * 0.45f;
                Vector3 q = a + dir * (len * (k + 0.5f) / m) + so - Vector3.up * 1.7f;
                Vector3 r2 = a + dir * (len * (k + 1f) / m) + so - Vector3.up * 0.45f;
                Beam(p, q, 0.22f, cu);
                Beam(q, r2, 0.22f, cu);
            }
        }
        // 門型の梁（吊りランプ）
        foreach (float f in new[] { 0.28f, 0.72f })
        {
            Vector3 c = a + dir * (len * f);
            Vector3 l = c + right * (W * 0.5f - 0.15f), r3 = c - right * (W * 0.5f - 0.15f);
            Prim(PrimitiveType.Cylinder, l + Vector3.up * 2.6f, new Vector3(0.4f, 2.6f, 0.4f), cu);
            Prim(PrimitiveType.Cylinder, r3 + Vector3.up * 2.6f, new Vector3(0.4f, 2.6f, 0.4f), cu);
            Beam(l + Vector3.up * 5.2f, r3 + Vector3.up * 5.2f, 0.45f, cu);
            Beam(l + Vector3.up * 3.9f, l * 0.7f + c * 0.3f + Vector3.up * 5.2f, 0.18f, brass);
            Beam(r3 + Vector3.up * 3.9f, r3 * 0.7f + c * 0.3f + Vector3.up * 5.2f, 0.18f, brass);
            Prim(PrimitiveType.Sphere, c + Vector3.up * 4.5f, Vector3.one * 0.6f, elec);
        }
        // 真ん中の広場: 円い銅の舞台と、天井へ伸びる電気の柱、回る環、下で回る歯車
        Prim(PrimitiveType.Cylinder, mid + Vector3.down * 0.225f, new Vector3(11f, 0.225f, 11f), deckMat, null, true);
        Prim(PrimitiveType.Cylinder, mid + Vector3.up * 0.02f, new Vector3(9f, 0.02f, 9f), brass);
        Prim(PrimitiveType.Cylinder, mid + Vector3.up * 0.04f, new Vector3(7f, 0.02f, 7f), cuD);
        float pillarH = Mathf.Max(3f, ceilY - y);
        Prim(PrimitiveType.Cylinder, mid + Vector3.up * (pillarH * 0.5f), new Vector3(1.1f, pillarH * 0.5f, 1.1f), cu);
        Prim(PrimitiveType.Cylinder, mid + Vector3.up * (pillarH * 0.5f), new Vector3(0.4f, pillarH * 0.5f, 0.4f), elec);
        for (float hh = 0.5f; hh < pillarH; hh += 3.5f)
            Prim(PrimitiveType.Cylinder, mid + Vector3.up * hh, new Vector3(1.6f, 0.15f, 1.6f), brass);
        var ringT = Mover("PlazaRing", mid + Vector3.up * 3.2f, new Vector3(0f, 30f, 0f), 0f);
        for (int k = 0; k < 8; k++)
        {
            float ang = k * Mathf.PI * 2f / 8f;
            Prim(PrimitiveType.Sphere, new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 3.2f, Vector3.one * (k % 2 == 0 ? 0.6f : 0.3f), k % 2 == 0 ? elec : brass, null, false, ringT.transform);
        }
        var cogRoot = SteamArt.BuildGear(moverRoot, mid + Vector3.down * 1.6f, Quaternion.LookRotation(Vector3.up, Vector3.right), 2.1f, 0.6f, SteamArt.TeethFor(2.1f), (u > s.L * 0.5f ? 1f : -1f) * 14f, 0f, cu, cuD, brass, elec);
        ElectricArc.Create(moverRoot, mid + Vector3.up * (pillarH - 0.4f), mid + Vector3.up * 0.8f + dir * 2.6f, 14, 0.5f, 0.14f, true);
        if (withLamp) Lamp(mid + Vector3.up * 4.4f, SteamArt.Electric, 5f, 26f);
        Stand(new Vector3(mid.x, y + 0.3f, mid.z));
    }

    // ---------------------------------------------------------------- 歯車の列

    delegate bool NearElevFn(Seg s, float u, float margin);

    int steamTrainNo;

    void GearTrain(Seg s, float uc, float y, int side, Material cu, Material cuD, Material brass, Material elec, NearElevFn nearElev)
    {
        float[] rs = Chance(0.5f) ? new[] { R(6.5f, 8f), R(4.5f, 6f), R(6.5f, 8f) } : new[] { R(7f, 9f), R(5f, 6.5f) };
        float span = 0f;
        foreach (var r in rs) span += r * 2f;
        if (span > s.L - 6f) return;
        float start = Mathf.Clamp(uc - span * 0.5f, 3f, s.L - span - 3f);
        if (side == 0 && nearElev(s, start + span * 0.5f, span * 0.5f + 7f)) return;
        var pc0 = P(s, start + span * 0.5f, side == 0 ? 1.7f : s.Wd - 1.7f);
        if (NearJunction(pc0, span * 0.5f + 4f)) return;

        Vector3 ud = s.alongX ? Vector3.right : Vector3.forward;
        Vector3 acrossDir = s.alongX ? Vector3.forward : Vector3.right;
        float tPos = side == 0 ? 1.7f : s.Wd - 1.7f;
        Vector3 axis = side == 0 ? acrossDir : -acrossDir;   // Z+ が、トンネルの内側
        var frame = Quaternion.LookRotation(axis, Vector3.up);

        float cursor = start;
        Vector3 prev = default; float prevR = 0f, prevSpin = 0f; int prevN = 0;
        for (int i = 0; i < rs.Length; i++)
        {
            float r = rs[i];
            cursor = i == 0 ? start + r : cursor + prevR + r;
            var pc = P(s, cursor, tPos);
            Vector3 pos = new Vector3(pc.x, y, pc.y);
            int nT = SteamArt.TeethFor(r);
            float spin, phase;
            if (i == 0)
            {
                spin = R(11f, 16f) * ((steamTrainNo++ & 1) == 0 ? 1f : -1f);
                phase = SteamArt.AngleToward(frame, ud);
            }
            else
            {
                spin = -prevSpin * prevN / nT;
                phase = SteamArt.AngleToward(frame, prev - pos) + 180f / nT;
            }
            SteamArt.BuildGear(moverRoot, pos, frame, r, 1.6f, nT, spin, phase, cu, cuD, brass, elec);
            prev = pos; prevR = r; prevSpin = spin; prevN = nT;
        }
    }

    // ---------------------------------------------------------------- 曲がり角: 歯車の塔

    void SteamCore(Vector3 c, float maxR)
    {
        var cu = Lit(SteamArt.Copper, 0.55f);
        var cuD = Lit(SteamArt.CopperDark, 0.45f);
        var brass = Lit(SteamArt.Brass, 0.65f);
        var elec = Lit(SteamArt.Electric, 0.5f, 3.4f);
        var glass = Ghost(new Color(0.55f, 0.85f, 1f, 0.15f));
        float full = topY - bottomY;

        SteamArt.ElectricTube(moverRoot, new Vector3(c.x, bottomY, c.z), full, 3f, glass, cu, brass, elec);
        Lamp(new Vector3(c.x, (topY + bottomY) * 0.5f, c.z), SteamArt.Electric, 7f, 50f);

        float R1 = Mathf.Max(5f, maxR * 0.6f);
        int n1 = Mathf.Max(12, (Mathf.RoundToInt(R1 * 2.2f) / 6) * 6);
        int n2 = n1 / 2;
        float R2 = R1 * 0.5f;
        var frame = Quaternion.LookRotation(Vector3.up, Vector3.right);
        for (int f = 0; f < n - 1; f++)
        {
            float y = g.FloorY(f) - 10f;
            float sgn = (f & 1) == 0 ? 1f : -1f;
            float spin1 = 7f * sgn;
            float off = f * 37f;
            Vector3 center = new Vector3(c.x, y, c.z);
            Vector3 d0 = Quaternion.Euler(0f, off, 0f) * Vector3.right;
            SteamArt.BuildGear(moverRoot, center, frame, R1, 1.4f, n1, spin1, SteamArt.AngleToward(frame, d0), cu, cuD, brass, elec);
            for (int k = 0; k < 3; k++)
            {
                Vector3 dk = Quaternion.Euler(0f, off + k * 120f, 0f) * Vector3.right;
                Vector3 pos = center + dk * (R1 + R2);
                SteamArt.BuildGear(moverRoot, pos, frame, R2, 1.1f, n2, -spin1 * n1 / n2, SteamArt.AngleToward(frame, -dk) + 180f / n2, cu, cuD, brass, elec);
            }
        }
    }

    // ---------------------------------------------------------------- 飛行船

    void BuildAirship(List<Vector3> path, float speed, float startFrac, Material cu, Material cuD, Material brass, Material elec)
    {
        var root = new GameObject("Airship");
        root.transform.SetParent(moverRoot, false);
        var t = root.transform;
        var canvas = Lit(new Color(0.58f, 0.36f, 0.22f), 0.35f);
        var window = Lit(new Color(1f, 0.8f, 0.45f), 0.3f, 2.6f);

        // 気球（銅色）と、真鍮の帯
        Prim(PrimitiveType.Sphere, new Vector3(0f, 3.4f, 0f), new Vector3(5.4f, 4.6f, 15f), canvas, null, false, t);
        for (int i = -2; i <= 2; i++)
        {
            float z = i * 2.6f;
            float sc = Mathf.Sqrt(Mathf.Max(0.05f, 1f - (z / 7.5f) * (z / 7.5f))) * 1.01f;
            Prim(PrimitiveType.Cylinder, new Vector3(0f, 3.4f, z), new Vector3(5.4f * sc, 0.1f, 4.6f * sc), brass, Quaternion.Euler(90f, 0f, 0f), false, t);
        }
        // ゴンドラ
        Prim(PrimitiveType.Cube, new Vector3(0f, -0.4f, 0f), new Vector3(2.4f, 1.5f, 6f), cu, null, false, t);
        Prim(PrimitiveType.Cube, new Vector3(0f, 0.4f, 0f), new Vector3(2.7f, 0.14f, 6.3f), brass, null, false, t);
        for (int i = -2; i <= 2; i++)
            foreach (float sx in new[] { -1.21f, 1.21f })
                Prim(PrimitiveType.Cube, new Vector3(sx, -0.3f, i * 1.1f), new Vector3(0.05f, 0.5f, 0.5f), window, null, false, t);
        // つり索
        foreach (float sz in new[] { -2.4f, 2.4f })
            foreach (float sx in new[] { -1f, 1f })
                Beam2(t, new Vector3(sx * 1.1f, 0.4f, sz), new Vector3(sx * 1.7f, 2.4f, sz), 0.1f, brass);
        // 尾翼
        Prim(PrimitiveType.Cube, new Vector3(0f, 4.6f, -6.4f), new Vector3(0.18f, 3.2f, 3.2f), cuD, null, false, t);
        Prim(PrimitiveType.Cube, new Vector3(0f, 3.4f, -6.4f), new Vector3(5.2f, 0.18f, 3.0f), cuD, null, false, t);
        // プロペラ（後ろで回る）
        var prop = new GameObject("Propeller");
        prop.transform.SetParent(t, false);
        prop.transform.localPosition = new Vector3(0f, -0.4f, -3.3f);
        prop.AddComponent<TunnelSpin>().speed = new Vector3(0f, 0f, 540f);
        Prim(PrimitiveType.Cube, Vector3.zero, new Vector3(2.4f, 0.22f, 0.06f), brass, null, false, prop.transform);
        Prim(PrimitiveType.Cube, Vector3.zero, new Vector3(0.22f, 2.4f, 0.06f), brass, null, false, prop.transform);
        Prim(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.4f, cu, null, false, prop.transform);
        // 船首のコイル（稲妻）
        Prim(PrimitiveType.Cylinder, new Vector3(0f, 5.4f, 5.8f), new Vector3(0.25f, 0.7f, 0.25f), cu, null, false, t);
        Prim(PrimitiveType.Sphere, new Vector3(0f, 6.3f, 5.8f), Vector3.one * 0.6f, elec, null, false, t);
        ElectricArc.Create(t, new Vector3(0f, 6.3f, 5.8f), new Vector3(1.6f, 4.2f, 5.0f), 8, 0.25f, 0.1f, true);
        ElectricArc.Create(t, new Vector3(0f, 6.3f, 5.8f), new Vector3(-1.6f, 4.2f, 5.0f), 8, 0.25f, 0.1f, true);

        // 気球の上の甲板（乗れる）。柱で支え、まわりに低い手すり。後ろ側は、乗り降りのために開けてある
        var deck = Box(new Vector3(0f, 6.2f, 0f), new Vector3(4.6f, 0.3f, 9f), cu, null, true, t);
        deck.name = "AirshipDeck";
        foreach (float sx in new[] { -1.7f, 1.7f })
            foreach (float sz in new[] { -3.5f, 3.5f })
                Prim(PrimitiveType.Cylinder, new Vector3(sx, 5.5f, sz), new Vector3(0.3f, 0.7f, 0.3f), brass, null, false, t);
        foreach (float sx in new[] { -2.2f, 2.2f })
            Box(new Vector3(sx, 6.7f, 0f), new Vector3(0.14f, 0.7f, 9f), brass, null, true, t);
        Box(new Vector3(0f, 6.7f, 4.4f), new Vector3(4.6f, 0.7f, 0.14f), brass, null, true, t);
        Box(new Vector3(-1.5f, 6.7f, -4.4f), new Vector3(1.6f, 0.7f, 0.14f), brass, null, true, t);
        Box(new Vector3(1.5f, 6.7f, -4.4f), new Vector3(1.6f, 0.7f, 0.14f), brass, null, true, t);
        Box(new Vector3(0f, 6.36f, 0f), new Vector3(0.12f, 0.03f, 8.6f), elec, null, false, t);
        Prim(PrimitiveType.Cylinder, new Vector3(0f, 7.3f, 3.6f), new Vector3(0.18f, 0.9f, 0.18f), cuD, null, false, t);
        Prim(PrimitiveType.Sphere, new Vector3(0f, 8.3f, 3.6f), Vector3.one * 0.5f, window, null, false, t);

        var lg = new GameObject("ShipLight");
        lg.transform.SetParent(t, false);
        lg.transform.localPosition = new Vector3(0f, -1.6f, 0f);
        var l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.color = new Color(1f, 0.78f, 0.5f); l.intensity = 3.5f; l.range = 22f; l.shadows = LightShadows.None;

        var loopC = root.AddComponent<AirshipLoop>();
        loopC.SetDeck(deck.transform, new Vector2(2.3f, 4.5f));
        loopC.Setup(path, speed, startFrac);
    }

    void Beam2(Transform at, Vector3 p, Vector3 q, float thick, Material mat)
    {
        Vector3 d = q - p;
        float len = d.magnitude;
        if (len < 0.01f) return;
        Prim(PrimitiveType.Cube, (p + q) * 0.5f, new Vector3(thick, thick, len), mat, Quaternion.LookRotation(d / len), false, at);
    }
}
