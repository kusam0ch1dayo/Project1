using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum GameAction { Forward, Back, Left, Right, Jump, Sprint, Dodge, Attack, Interact, Inventory, Map, Skills, SkillMode, SkillMenu, BuildMenu, PetCommand, RideSkill, RideSkillSwitch }

/// <summary>
/// キーコンフィグ。各操作にキーボードのキー、またはマウスのボタンを割り当てられる（PlayerPrefs に保存）。
/// ゲーム内の入力は、キーを直接読まずに IsPressed / WasPressed を使うこと。
/// </summary>
public static class KeyBindings
{
    public struct Binding
    {
        public bool mouse;   // true: マウスボタン (code = 0左 1右 2中 3進む 4戻る) / false: キー (code = (int)Key)
        public int code;
        public static Binding K(Key k) => new Binding { mouse = false, code = (int)k };
        public static Binding M(int b) => new Binding { mouse = true, code = b };
        public bool SameAs(Binding o) => mouse == o.mouse && code == o.code;
        public override string ToString() => (mouse ? "m:" : "k:") + (mouse ? code.ToString() : ((Key)code).ToString());
    }

    public static readonly GameAction[] All = (GameAction[])Enum.GetValues(typeof(GameAction));

    static Binding[] binds;

    static Binding Default(GameAction a)
    {
        switch (a)
        {
            case GameAction.Forward: return Binding.K(Key.W);
            case GameAction.Back: return Binding.K(Key.S);
            case GameAction.Left: return Binding.K(Key.A);
            case GameAction.Right: return Binding.K(Key.D);
            case GameAction.Jump: return Binding.K(Key.Space);
            case GameAction.Sprint: return Binding.K(Key.LeftShift);
            case GameAction.Dodge: return Binding.K(Key.LeftCtrl);
            case GameAction.Attack: return Binding.M(0);
            case GameAction.Interact: return Binding.K(Key.E);
            case GameAction.Inventory: return Binding.K(Key.Tab);
            case GameAction.Skills: return Binding.K(Key.T);
            case GameAction.SkillMode: return Binding.K(Key.R);
            case GameAction.SkillMenu: return Binding.K(Key.G);
            case GameAction.BuildMenu: return Binding.K(Key.H);
            case GameAction.PetCommand: return Binding.K(Key.F);
            case GameAction.RideSkill: return Binding.K(Key.V);
            case GameAction.RideSkillSwitch: return Binding.K(Key.B);
            case GameAction.Map: return Binding.K(Key.M);
            default: return Binding.K(Key.M);
        }
    }

    public static string Label(GameAction a)
    {
        switch (a)
        {
            case GameAction.Forward: return "前進";
            case GameAction.Back: return "後退";
            case GameAction.Left: return "左へ移動";
            case GameAction.Right: return "右へ移動";
            case GameAction.Jump: return "ジャンプ";
            case GameAction.Sprint: return "ダッシュ";
            case GameAction.Dodge: return "回避";
            case GameAction.Attack: return "攻撃";
            case GameAction.Interact: return "調べる・話す・開ける";
            case GameAction.Inventory: return "インベントリ";
            case GameAction.Skills: return "スキルツリー";
            case GameAction.SkillMode: return "スキルモード切り替え";
            case GameAction.SkillMenu: return "スキルメニュー（スロット設定）";
            case GameAction.BuildMenu: return "ビルド構成（装備・スキルの保存と切り替え）";
            case GameAction.PetCommand: return "召喚獣への命令（召喚獣を見ながら）・ライドから降りる";
            case GameAction.RideSkill: return "ライドスキルを使う";
            case GameAction.RideSkillSwitch: return "ライドスキルの切り替え";
            default: return "地図";
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => binds = null;

    static void EnsureLoaded()
    {
        if (binds != null) return;
        binds = new Binding[All.Length];
        foreach (var a in All)
        {
            var b = Default(a);
            string s = PlayerPrefs.GetString("bind_" + a, "");
            if (s.StartsWith("k:") && Enum.TryParse(s.Substring(2), out Key key)) b = Binding.K(key);
            else if (s.StartsWith("m:") && int.TryParse(s.Substring(2), out int mb)) b = Binding.M(mb);
            binds[(int)a] = b;
        }
    }

    public static Binding Get(GameAction a)
    {
        EnsureLoaded();
        return binds[(int)a];
    }

    static void Save(GameAction a) => PlayerPrefs.SetString("bind_" + a, binds[(int)a].ToString());

    /// <summary>割り当てを変える。他の操作が同じ入力を使っていたら、その操作に今までの入力を渡す（入れ替え）。</summary>
    public static void Set(GameAction a, Binding b)
    {
        EnsureLoaded();
        var old = binds[(int)a];
        foreach (var other in All)
        {
            if (other == a || !binds[(int)other].SameAs(b)) continue;
            binds[(int)other] = old;
            Save(other);
        }
        binds[(int)a] = b;
        Save(a);
    }

    public static void ResetAll()
    {
        EnsureLoaded();
        foreach (var a in All)
        {
            binds[(int)a] = Default(a);
            PlayerPrefs.DeleteKey("bind_" + a);
        }
    }

    // ---------------------------------------------------------------- 入力の問い合わせ

    static UnityEngine.InputSystem.Controls.ButtonControl Control(Binding b)
    {
        if (b.mouse)
        {
            var m = Mouse.current;
            if (m == null) return null;
            switch (b.code)
            {
                case 0: return m.leftButton;
                case 1: return m.rightButton;
                case 2: return m.middleButton;
                case 3: return m.forwardButton;
                default: return m.backButton;
            }
        }
        var kb = Keyboard.current;
        return kb != null ? kb[(Key)b.code] : null;
    }

    public static bool IsPressed(GameAction a)
    {
        if (ListToolbar.Typing) return false; // 絞り込み欄に入力中は、キー操作を受け付けない
        var c = Control(Get(a));
        var pb = PadKeys.For(a);
        return (c != null && c.isPressed) || (pb != null && pb.isPressed);
    }

    public static bool WasPressed(GameAction a)
    {
        if (ListToolbar.Typing) return false;
        var c = Control(Get(a));
        var pb = PadKeys.For(a);
        return (c != null && c.wasPressedThisFrame) || (pb != null && pb.wasPressedThisFrame);
    }

    // ---------------------------------------------------------------- 表示・割り当て待ち

    public static string DisplayName(Binding b)
    {
        if (b.mouse)
        {
            switch (b.code)
            {
                case 0: return "マウス 左ボタン";
                case 1: return "マウス 右ボタン";
                case 2: return "マウス 中ボタン";
                case 3: return "マウス 進むボタン";
                default: return "マウス 戻るボタン";
            }
        }

        var k = (Key)b.code;
        switch (k)
        {
            case Key.LeftCtrl: return "左Ctrl";
            case Key.RightCtrl: return "右Ctrl";
            case Key.LeftShift: return "左Shift";
            case Key.RightShift: return "右Shift";
            case Key.LeftAlt: return "左Alt";
            case Key.RightAlt: return "右Alt";
            case Key.UpArrow: return "↑";
            case Key.DownArrow: return "↓";
            case Key.LeftArrow: return "←";
            case Key.RightArrow: return "→";
            case Key.Enter: return "Enter";
            case Key.Backquote: return "`";
        }
        string s = k.ToString();
        if (s.StartsWith("Digit")) return s.Substring(5);
        return s;
    }

    public static string DisplayName(GameAction a) => DisplayName(Get(a));

    /// <summary>今フレームに押された入力を1つ返す（キー割り当て待ち用）。Esc は割り当てできない。</summary>
    public static bool TryCapture(out Binding result)
    {
        result = default;

        var kb = Keyboard.current;
        if (kb != null)
        {
            foreach (var key in kb.allKeys)
            {
                if (!key.wasPressedThisFrame) continue;
                if (key.keyCode == Key.Escape || key.keyCode == Key.None || key.keyCode == Key.IMESelected) continue;
                result = Binding.K(key.keyCode);
                return true;
            }
        }

        var m = Mouse.current;
        if (m != null)
        {
            if (m.leftButton.wasPressedThisFrame) { result = Binding.M(0); return true; }
            if (m.rightButton.wasPressedThisFrame) { result = Binding.M(1); return true; }
            if (m.middleButton.wasPressedThisFrame) { result = Binding.M(2); return true; }
            if (m.forwardButton.wasPressedThisFrame) { result = Binding.M(3); return true; }
            if (m.backButton.wasPressedThisFrame) { result = Binding.M(4); return true; }
        }
        return false;
    }
}
