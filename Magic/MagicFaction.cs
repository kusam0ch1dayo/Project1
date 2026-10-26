using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 敵がプレイヤーの魔法を使うための「向き」の切り替え。
/// 敵対モード（Hostile）の間は、プレイヤーの魔法の部品（弾・竜巻・レイピアなど）が、敵ではなく、プレイヤーを狙い・プレイヤーに当たる。
///  - MagicHit.IsEnemy / Damage / Push と MagicTargets.Alive が、このモードを見て、相手を入れ替える。
///  - 敵対モードの間に作られた魔法の部品には HostileTag が付き、その部品が動くとき（Update）も、敵対モードになる。
/// </summary>
public static class MagicFaction
{
    public static Health HostileTarget { get; private set; }
    public static Transform HostileOwner { get; private set; }
    public static bool Hostile => HostileTarget != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { HostileTarget = null; HostileOwner = null; }

    public struct Scope : IDisposable
    {
        readonly Health prevTarget;
        readonly Transform prevOwner;
        readonly bool active;

        public Scope(Health target, Transform owner)
        {
            prevTarget = HostileTarget;
            prevOwner = HostileOwner;
            active = true;
            HostileTarget = target;
            HostileOwner = owner;
        }

        public void Dispose()
        {
            if (!active) return;
            HostileTarget = prevTarget;
            HostileOwner = prevOwner;
        }
    }

    /// <summary>敵対モードに入る（using で使う）。target = 狙われる相手（プレイヤー）、owner = 唱えた敵。</summary>
    public static Scope Enter(Health target, Transform owner) => new Scope(target, owner);

    /// <summary>敵対モードの間に作られた魔法の部品に、印を付ける（あとで動くときも、敵対モードになる）。</summary>
    public static void TagIfHostile(GameObject go)
    {
        if (!Hostile || go == null || go.GetComponent<HostileTag>() != null) return;
        var t = go.AddComponent<HostileTag>();
        t.target = HostileTarget;
        t.owner = HostileOwner;
    }

    /// <summary>コルーチンの1歩ごとに、敵対モードにする。</summary>
    public static IEnumerator Wrap(IEnumerator inner, Health target, Transform owner)
    {
        while (true)
        {
            object cur;
            using (Enter(target, owner))
            {
                if (!inner.MoveNext()) yield break;
                cur = inner.Current;
            }
            yield return cur;
        }
    }

    /// <summary>魔法の部品の Update の中身を、印に合わせて動かす（印が無ければ、ふつうに）。印の相手がいなくなったら、部品を消す。</summary>
    public static void Run(MonoBehaviour mb, ref HostileTag tag, ref bool checkedTag, Action core)
    {
        if (!checkedTag) { tag = mb.GetComponent<HostileTag>(); checkedTag = true; }
        if (tag == null) { core(); return; }
        if (tag.target == null || tag.target.IsDead) { UnityEngine.Object.Destroy(mb.gameObject); return; }
        using (Enter(tag.target, tag.owner)) core();
    }
}

/// <summary>敵が唱えた魔法の部品の印。狙う相手（プレイヤー）と、唱えた敵。</summary>
public class HostileTag : MonoBehaviour
{
    public Health target;
    public Transform owner;
}
