using UnityEngine;

/// <summary>
/// 拠点の店主＋カウンター。Eで話しかけるとショップ画面（ShopUI）が開く。見た目は Start でコード生成する。
/// このオブジェクトの forward がお客さん側。店主はカウンターの奥（-forward 側）に立つ。
/// </summary>
public class Shopkeeper : MonoBehaviour, IInteractable
{
    public ShopKind kind = ShopKind.Equipment;

    public string Prompt => kind == ShopKind.Equipment
        ? "E : 店主に話しかける（装備ショップ）"
        : "E : 店主に話しかける（アイテムショップ）";
    public bool CanInteract => true;

    public void Interact(GameObject interactor)
    {
        var ui = interactor.GetComponent<ShopUI>();
        if (ui == null) ui = interactor.AddComponent<ShopUI>();
        ui.Open(kind);
    }

    void Start()
    {
        bool equip = kind == ShopKind.Equipment;
        var wood = new Color(0.4f, 0.26f, 0.12f);
        var top = new Color(0.55f, 0.38f, 0.2f);
        var cloth = equip ? new Color(0.35f, 0.2f, 0.45f) : new Color(0.15f, 0.4f, 0.3f);

        // カウンター
        Part(PrimitiveType.Cube, "Counter", new Vector3(0f, 0.5f, 0f), new Vector3(3.2f, 1f, 0.8f), wood, true);
        Part(PrimitiveType.Cube, "CounterTop", new Vector3(0f, 1.03f, 0f), new Vector3(3.4f, 0.08f, 1.0f), top, false);

        // 商品の飾り
        if (equip)
        {
            Part(PrimitiveType.Cube, "Sword", new Vector3(-0.9f, 1.12f, 0f), new Vector3(0.12f, 0.05f, 0.8f), new Color(0.8f, 0.85f, 0.9f), false);
            Part(PrimitiveType.Cube, "Armor", new Vector3(0f, 1.25f, 0f), new Vector3(0.5f, 0.3f, 0.4f), new Color(0.5f, 0.5f, 0.55f), false);
            Part(PrimitiveType.Sphere, "Helm", new Vector3(0.9f, 1.22f, 0f), new Vector3(0.3f, 0.3f, 0.3f), new Color(0.6f, 0.6f, 0.65f), false);
        }
        else
        {
            Part(PrimitiveType.Sphere, "PotionRed", new Vector3(-0.8f, 1.2f, 0f), new Vector3(0.25f, 0.25f, 0.25f), new Color(0.9f, 0.2f, 0.2f), false);
            Part(PrimitiveType.Sphere, "PotionGreen", new Vector3(0f, 1.2f, 0f), new Vector3(0.25f, 0.25f, 0.25f), new Color(0.3f, 0.85f, 0.35f), false);
            Part(PrimitiveType.Sphere, "PotionBlue", new Vector3(0.8f, 1.2f, 0f), new Vector3(0.25f, 0.25f, 0.25f), new Color(0.3f, 0.5f, 0.95f), false);
        }

        // 店主（体・頭・帽子）
        Part(PrimitiveType.Capsule, "Body", new Vector3(0f, 0.95f, -0.9f), new Vector3(0.7f, 0.95f, 0.7f), cloth, false);
        Part(PrimitiveType.Sphere, "Head", new Vector3(0f, 2.0f, -0.9f), new Vector3(0.45f, 0.45f, 0.45f), new Color(0.9f, 0.75f, 0.6f), false);
        Part(PrimitiveType.Cylinder, "Hat", new Vector3(0f, 2.28f, -0.9f), new Vector3(0.5f, 0.06f, 0.5f), cloth * 0.8f, false);
        Part(PrimitiveType.Cylinder, "HatTop", new Vector3(0f, 2.4f, -0.9f), new Vector3(0.3f, 0.12f, 0.3f), cloth * 0.8f, false);

        // 看板
        Part(PrimitiveType.Cube, "Sign", new Vector3(0f, 3.0f, -1.0f), new Vector3(2.6f, 0.6f, 0.08f), wood, false);
        MakeSignText(equip ? "装備ショップ" : "アイテムショップ");

        var lightGo = new GameObject("ShopLight");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 2.8f, 0.6f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.85f, 0.6f);
        light.range = 7f;
        light.intensity = 8f;
    }

    void MakeSignText(string text)
    {
        var go = new GameObject("SignText");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 3.0f, -0.94f);
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // TextMesh は -Z 側から読める
        var tm = go.AddComponent<TextMesh>();
        tm.font = UIKit.Font;
        tm.text = text;
        tm.fontSize = 64;
        tm.characterSize = 0.05f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = new Color(1f, 0.9f, 0.6f);
        go.GetComponent<MeshRenderer>().sharedMaterial = UIKit.Font.material;
    }

    void Part(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Color color, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (!keepCollider) Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        SharedMats.Apply(go.GetComponent<Renderer>(), color);
    }
}
