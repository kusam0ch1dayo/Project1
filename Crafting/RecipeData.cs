using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 合成のレシピ（大鍋で使う）。Dungeon > Recipe Creator で作る。Assets/Resources/Recipes に置く。
/// 材料（素材・アイテム）と少しの G で、新しいアイテム・装備・素材ができる。
/// </summary>
/// <summary>アイテム・素材のほかに、レシピで作れるもの。</summary>
public enum RecipeOutputExtra { None = 0, Gold, Crystal, Mako, Stone, MythicScroll }   // MythicScroll: まだ解放していない、ミシックランクのレシピの巻き物を1つ

[CreateAssetMenu(menuName = "Dungeon/Recipe", fileName = "NewRecipe")]
public class RecipeData : ScriptableObject
{
    public string id;
    public string displayName = "新しいレシピ";
    [TextArea] public string description;
    [Tooltip("一覧の並び順（小さいほど上）")] public int order;
    [Tooltip("オンなら、最初から解放されている。オフなら、レシピの巻き物を、鑑定屋で解放するまで、大鍋に出ない")] public bool startUnlocked;
    public List<RecipeIngredient> inputs = new List<RecipeIngredient>();
    [Tooltip("合成に使う G")] [Min(0)] public int gold;
    [Tooltip("true: 素材ができる / false: アイテム・装備ができる")] public bool outputIsMaterial;
    public string outputId;
    [Min(1)] public int outputCount = 1;
    [Tooltip("アイテム・素材の代わりに、G・魔水晶・魔煌珠・魔法石ができる（個数が、その量になる）。None なら、上の「できるもの」")]
    public RecipeOutputExtra outputExtra = RecipeOutputExtra.None;
    [Tooltip("魔法石のとき、できる属性")] public Element stoneElement = Element.Fire;

    public string Id => string.IsNullOrEmpty(id) ? name : id;
}
