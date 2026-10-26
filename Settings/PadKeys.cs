using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

/// <summary>パッド専用の操作（キーボードでは、マウスやホイール・数字キーで行うもの）。</summary>
public enum PadAct { Magic = 100, MagicNext = 101, SlotNext = 102, SlotUse = 103 }

/// <summary>
/// ゲームパッドのボタン割り当て（キーコンフィグのパッド版。PlayerPrefs に保存）。
/// 左スティック（移動）・右スティック（視点）・Start（Esc）は固定。
/// </summary>
public static class PadKeys
{
    public const int None = -1;

    /// <summary>割り当てられるボタン（Start は Esc 専用なので含めない）。</summary>
    public static readonly GamepadButton[] Buttons =
    {
        GamepadButton.South, GamepadButton.East, GamepadButton.West, GamepadButton.North,
        GamepadButton.LeftShoulder, GamepadButton.RightShoulder, GamepadButton.LeftTrigger, GamepadButton.RightTrigger,
        GamepadButton.LeftStick, GamepadButton.RightStick,
        GamepadButton.DpadUp, GamepadButton.DpadDown, GamepadButton.DpadLeft, GamepadButton.DpadRight,
        GamepadButton.Select,
    };

    static readonly System.Collections.Generic.Dictionary<int, int> binds = new System.Collections.Generic.Dictionary<int, int>();
    public static readonly PadAct[] Extras = (PadAct[])Enum.GetValues(typeof(PadAct));

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => binds.Clear();

    static int Default(PadAct a)
    {
        switch (a)
        {
            case PadAct.Magic: return (int)GamepadButton.RightTrigger;
            case PadAct.MagicNext: return (int)GamepadButton.RightShoulder;
            case PadAct.SlotNext: return (int)GamepadButton.West;
            case PadAct.SlotUse: return (int)GamepadButton.RightStick;
            default: return None;
        }
    }

    public static string Label(PadAct a)
    {
        switch (a)
        {
            case PadAct.Magic: return "魔法を使う（マウス右ボタン）";
            case PadAct.MagicNext: return "魔法の切り替え（ホイール）";
            case PadAct.SlotNext: return "スロットの番号切り替え（1→2→3）";
            default: return "選んだスロットを使う";
        }
    }

    static int Default(GameAction a)
    {
        switch (a)
        {
            case GameAction.Jump: return (int)GamepadButton.South;
            case GameAction.Sprint: return (int)GamepadButton.LeftStick;
            case GameAction.Dodge: return (int)GamepadButton.East;
            case GameAction.Attack: return (int)GamepadButton.LeftTrigger;
            case GameAction.Interact: return (int)GamepadButton.North;
            case GameAction.Inventory: return (int)GamepadButton.DpadUp;
            case GameAction.Skills: return (int)GamepadButton.DpadLeft;
            case GameAction.Map: return (int)GamepadButton.DpadDown;
            case GameAction.SkillMenu: return (int)GamepadButton.DpadRight;
            case GameAction.BuildMenu: return (int)GamepadButton.Select;
            case GameAction.SkillMode: return (int)GamepadButton.LeftShoulder;
            default: return None;   // 移動はスティック
        }
    }

    /// <summary>パッドで割り当て変更できる操作（移動はスティック固定なので除く）。</summary>
    public static bool Configurable(GameAction a)
        => a != GameAction.Forward && a != GameAction.Back && a != GameAction.Left && a != GameAction.Right;

    static int DefaultOf(int id) => id >= 100 ? Default((PadAct)id) : Default((GameAction)id);

    static IEnumerable<int> AllIds()
    {
        foreach (var a in KeyBindings.All) if (Configurable(a)) yield return (int)a;
        foreach (var e in Extras) yield return (int)e;
    }

    public static int GetId(int id)
    {
        if (!binds.TryGetValue(id, out int v)) { v = PlayerPrefs.GetInt("padbind_" + id, DefaultOf(id)); binds[id] = v; }
        return v;
    }

    public static int Get(GameAction a) => GetId((int)a);
    public static int Get(PadAct a) => GetId((int)a);

    /// <summary>割り当てを変える。ほかの操作が同じボタンなら、その操作に、今までのボタンを渡す（入れ替え）。</summary>
    public static void SetId(int id, int button)
    {
        int old = GetId(id);
        foreach (var o in AllIds())
        {
            if (o == id || GetId(o) != button) continue;
            binds[o] = old;
            PlayerPrefs.SetInt("padbind_" + o, old);
        }
        binds[id] = button;
        PlayerPrefs.SetInt("padbind_" + id, button);
    }

    public static void Set(GameAction a, int button) => SetId((int)a, button);

    public static void ResetAll()
    {
        foreach (var o in AllIds()) PlayerPrefs.DeleteKey("padbind_" + o);
        foreach (var a in KeyBindings.All) PlayerPrefs.DeleteKey("padbind_" + (int)a);
        binds.Clear();
    }

    public static string Name(int button)
    {
        if (button == None) return "（なし）";
        switch ((GamepadButton)button)
        {
            case GamepadButton.South: return "A（下）";
            case GamepadButton.East: return "B（右）";
            case GamepadButton.West: return "X（左）";
            case GamepadButton.North: return "Y（上）";
            case GamepadButton.LeftShoulder: return "LB";
            case GamepadButton.RightShoulder: return "RB";
            case GamepadButton.LeftTrigger: return "LT";
            case GamepadButton.RightTrigger: return "RT";
            case GamepadButton.LeftStick: return "左スティック押し込み";
            case GamepadButton.RightStick: return "右スティック押し込み";
            case GamepadButton.DpadUp: return "十字キー 上";
            case GamepadButton.DpadDown: return "十字キー 下";
            case GamepadButton.DpadLeft: return "十字キー 左";
            case GamepadButton.DpadRight: return "十字キー 右";
            case GamepadButton.Select: return "Back（Select）";
            case GamepadButton.Start: return "Start";
            default: return ((GamepadButton)button).ToString();
        }
    }

    public static string Name(GameAction a) => Name(Get(a));
    public static string Name(PadAct a) => Name(Get(a));

    /// <summary>今フレームに押されたボタンを返す（割り当て待ち用）。</summary>
    public static bool TryCapture(out int button)
    {
        button = None;
        var pad = Gamepad.current;
        if (pad == null) return false;
        foreach (var b in Buttons)
            if (pad[b].wasPressedThisFrame) { button = (int)b; return true; }
        return false;
    }

    // ---------------------------------------------------------------- 入力の問い合わせ

    /// <summary>操作に割り当てられたパッドのボタン（なければ null）。</summary>
    public static ButtonControl For(GameAction a) => ForId((int)a);
    public static ButtonControl For(PadAct a) => ForId((int)a);

    static ButtonControl ForId(int id)
    {
        var pad = Gamepad.current;
        if (pad == null) return null;
        int b = GetId(id);
        return b == None ? null : pad[(GamepadButton)b];
    }

    public static bool Was(GameAction a) { var c = For(a); return c != null && c.wasPressedThisFrame; }
    public static bool Held(GameAction a) { var c = For(a); return c != null && c.isPressed; }
    /// <summary>パッドで、このスロットが選ばれていて、「使う」ボタンが押され続けているか（長押しスキルの維持用）。</summary>
    public static bool SlotHeld(int slot) => QuickSlotUI.PadSelected == slot && Held(PadAct.SlotUse);

    public static bool Was(PadAct a) { var c = For(a); return c != null && c.wasPressedThisFrame; }
    public static bool Held(PadAct a) { var c = For(a); return c != null && c.isPressed; }

    /// <summary>Esc、または Start ボタン。</summary>
    public static bool Esc(Keyboard kb)
    {
        var pad = Gamepad.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame) return true;
        if (pad == null) return false;
        // Start、またはメニューの中での B（回避ボタン）で、閉じる
        return pad.startButton.wasPressedThisFrame || (FirstPersonController.MenuOpen && pad.buttonEast.wasPressedThisFrame);
    }
}
