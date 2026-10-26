using UnityEngine;

/// <summary>持ちきれなかったアイテムが床に落ちたもの。Eで拾える（持てるようになっていれば）。</summary>
public class DroppedItem : MonoBehaviour, IInteractable
{
    ItemData item;
    ItemInstance instance; // 捨てたもの（強化・覚醒などの状態を保つ）
    Transform visual;
    float baseY;

    /// <summary>位置を動かしたとき（領域から戻すなど）: 浮き沈みの基準の高さも、いっしょに動かす。</summary>
    public void ShiftBase(float dy) => baseY += dy;

    public string Prompt => item == null ? "" : $"E : {item.displayName} を拾う";
    public bool CanInteract => item != null;

    public void Interact(GameObject interactor)
    {
        if (item == null) return;
        // 持てないときは、理由を通知して、その場に残る
        bool ok = instance != null ? GameSession.AddExisting(instance) : GameSession.Add(item) != null;
        if (!ok) return;

        var hud = PlayerHUD.Instance;
        if (hud != null) hud.ShowToast(ToastKind.Item, $"入手: {item.ColoredName}  [{item.RarityName}]");
        AudioManager.PlaySfxAt("equip", transform.position, 0.8f, 1.2f);
        item = null;
        Destroy(gameObject);
    }

    void Update()
    {
        if (visual == null) return;
        visual.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
        var p = visual.position;
        p.y = baseY + Mathf.Sin(Time.time * 2.5f) * 0.05f;
        visual.position = p;
    }

    /// <summary>所持品のアイテム（状態を保ったまま）を、origin の近くの床に捨てる。</summary>
    public static DroppedItem DropInstance(ItemInstance inst, Vector3 origin)
    {
        if (inst == null || inst.data == null) return null;
        var d = Drop(inst.data, origin);
        if (d != null) d.instance = inst;
        return d;
    }

    /// <summary>origin の近くの床に、アイテムを落とす。</summary>
    public static DroppedItem Drop(ItemData item, Vector3 origin)
    {
        if (item == null) return null;

        var off = Random.insideUnitCircle.normalized * Random.Range(0.8f, 1.3f);
        var pos = origin + new Vector3(off.x, 0f, off.y);
        // 床の高さに合わせる
        if (Physics.Raycast(pos + Vector3.up * 2f, Vector3.down, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            pos = hit.point;
        else pos = origin;

        var root = new GameObject("Drop_" + item.displayName);
        root.transform.position = pos;
        var drop = root.AddComponent<DroppedItem>();
        drop.item = item;

        // 当たり判定（Eで調べるための、小さな非トリガー）
        var col = root.AddComponent<SphereCollider>();
        col.radius = 0.28f;
        col.center = new Vector3(0f, 0.3f, 0f);

        var color = item.RarityColor;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(go.GetComponent<Collider>());
        go.name = "Visual";
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = new Vector3(0f, 0.4f, 0f);
        go.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
        go.transform.rotation = Quaternion.Euler(30f, 45f, 30f);
        go.GetComponent<Renderer>().material.color = color;
        drop.visual = go.transform;
        drop.baseY = go.transform.position.y;

        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = 2.5f;
        light.intensity = 1.5f;
        RarityDecor.DropFx(root.transform, item.rarity, 0f);   // エピック以上: 光の柱・回る輪・粒（ミシックは黒い結晶も）
        return drop;
    }
}
