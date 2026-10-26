using UnityEngine;

/// <summary>ダメージ数字（クリティカルは大きく赤く）や、敵の頭上の属性ラベルなど、ワールド空間の文字。</summary>
public class WorldText : MonoBehaviour
{
    float life, age;
    Vector3 velocity;
    TextMesh mesh;
    Color baseColor;
    bool billboardOnly;

    static Font numberFont;

    static Font NumberFont
    {
        get
        {
            return UIKit.NumberFont;
        }
    }

    static TextMesh Make(Vector3 pos, string text, Color color, float size, Font font)
    {
        var go = new GameObject("WorldText");
        go.transform.position = pos;
        var tm = go.AddComponent<TextMesh>();
        tm.font = font;
        tm.text = text;
        tm.fontSize = 64;
        tm.characterSize = 0.04f * size;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        return tm;
    }

    /// <summary>上へ浮かびながら消える文字（ダメージ数字・MISS など）。</summary>
    public static void Popup(Vector3 pos, string text, Color color, float size = 1f)
    {
        if (!Application.isPlaying) return;
        pos += new Vector3(Random.Range(-0.25f, 0.25f), 0f, Random.Range(-0.25f, 0.25f));
        var tm = Make(pos, text, color, size, NumberFont);
        var w = tm.gameObject.AddComponent<WorldText>();
        w.mesh = tm;
        w.baseColor = color;
        w.life = 0.9f;
        w.velocity = new Vector3(0f, 1.6f, 0f);
    }

    public static void Damage(Vector3 pos, float amount, bool crit)
    {
        string text = Mathf.RoundToInt(amount).ToString();
        if (crit) Popup(pos, text + "!", new Color(1f, 0.25f, 0.2f), 2.1f);
        else Popup(pos, text, Color.white, 1f);
    }

    /// <summary>頭上の属性・レベルの文字を、書き換える（召喚獣のレベルが上がったとき）。</summary>
    public static void SetElementLabel(Transform parent, Element element, int level)
    {
        if (parent == null) return;
        foreach (var w in parent.GetComponentsInChildren<WorldText>(true))
        {
            if (!w.billboardOnly || w.owner != parent) continue;
            var tm = w.GetComponent<TextMesh>();
            if (tm != null) tm.text = level > 0 ? $"Lv{level}  {ElementUtil.Name(element)}" : ElementUtil.Name(element);
        }
    }

    /// <summary>敵の頭上に常に表示する、属性の文字（カメラの方を向く）。</summary>
    public static void AttachElementLabel(Transform parent, Element element, float height, int level = 0)
    {
        if (!Application.isPlaying) return;
        string label = level > 0 ? $"Lv{level}  {ElementUtil.Name(element)}" : ElementUtil.Name(element);
        var tm = Make(parent.position + Vector3.up * height, label, ElementUtil.Color(element), 1.4f, UIKit.Font);
        tm.transform.SetParent(parent, true);
        tm.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;
        var w = tm.gameObject.AddComponent<WorldText>();
        w.billboardOnly = true;
        w.owner = parent;
        w.rend = tm.GetComponent<MeshRenderer>();
        w.rend.enabled = false; // 見えるかどうかは、最初の判定で決まる
        w.checkTimer = Random.value * 0.1f;
    }

    // 属性ラベル用: 壁越しには見えないようにする
    Transform owner;
    MeshRenderer rend;
    float checkTimer;
    const float LabelMaxDistance = 28f;

    bool IsLabelVisible(Camera cam)
    {
        if (cam == null || owner == null) return false;

        var hp = owner.GetComponent<Health>();
        if (hp != null && hp.IsDead) return false;

        Vector3 from = cam.transform.position;
        if ((transform.position - from).sqrMagnitude > LabelMaxDistance * LabelMaxDistance) return false;

        if (!DarkDungeon.IsLit(owner.position + Vector3.up * 1f)) return false; // 暗闇の向こうは、見えない

        // カメラとの間に壁などがあれば隠す（持ち主自身のコライダーは無視）
        if (Physics.Linecast(from, transform.position, out var hit, ~0, QueryTriggerInteraction.Ignore))
            return hit.collider.transform.root == owner.root;
        return true;
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam != null) transform.rotation = cam.transform.rotation;

        if (billboardOnly)
        {
            checkTimer -= Time.unscaledDeltaTime;
            if (checkTimer <= 0f)
            {
                checkTimer = 0.1f;
                if (rend != null) rend.enabled = IsLabelVisible(cam);
            }
            return;
        }

        age += Time.deltaTime;
        transform.position += velocity * Time.deltaTime;
        velocity.y = Mathf.MoveTowards(velocity.y, 0f, 2.2f * Time.deltaTime);

        float a = Mathf.Clamp01(1f - Mathf.Max(0f, age - life * 0.5f) / (life * 0.5f));
        var c = baseColor;
        c.a = a;
        mesh.color = c;
        if (age >= life) Destroy(gameObject);
    }
}
