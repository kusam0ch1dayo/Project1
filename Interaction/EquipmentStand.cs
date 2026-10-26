using UnityEngine;

/// <summary>拠点の装備台。Eでインベントリ（装備変更）画面を開く。見た目は Start でコード生成する。</summary>
public class EquipmentStand : MonoBehaviour, IInteractable
{
    public string Prompt => "E : 装備を変更する";
    public bool CanInteract => true;

    public void Interact(GameObject interactor)
    {
        var ui = interactor.GetComponent<InventoryUI>();
        if (ui != null) ui.Open();
    }

    void Start()
    {
        var wood = new Color(0.4f, 0.26f, 0.12f);

        Part("Table", new Vector3(0f, 0.5f, 0f), new Vector3(2.2f, 1f, 0.9f), wood, true);
        // 台の上の飾り（剣・鎧・指輪のつもり）
        Part("Sword", new Vector3(-0.6f, 1.05f, 0f), new Vector3(0.12f, 0.05f, 0.8f), new Color(0.8f, 0.85f, 0.9f), false);
        Part("Armor", new Vector3(0f, 1.15f, 0f), new Vector3(0.5f, 0.3f, 0.4f), new Color(0.5f, 0.5f, 0.55f), false);
        Part("Ring", new Vector3(0.6f, 1.05f, 0f), new Vector3(0.15f, 0.1f, 0.15f), new Color(0.9f, 0.75f, 0.2f), false);
    }

    void Part(string name, Vector3 pos, Vector3 scale, Color color, bool keepCollider)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        if (!keepCollider) Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().material.color = color;
    }
}
