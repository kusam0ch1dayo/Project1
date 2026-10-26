using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>1つの「音のイベント」への割り当て。clips が複数ならランダムに選ばれる。空ならその場面は無音。</summary>
[Serializable]
public class SoundEntry
{
    [Tooltip("コードから呼ぶ名前。変更しないこと")] public string key;
    public string label;
    [TextArea] public string description;
    public bool isMusic;
    public AudioClip[] clips = new AudioClip[0];
    [Range(0f, 2f)] public float volume = 1f;
    [Range(0.5f, 2f)] public float pitch = 1f;

    public AudioClip Pick()
    {
        if (clips == null || clips.Length == 0) return null;
        int start = UnityEngine.Random.Range(0, clips.Length);
        for (int i = 0; i < clips.Length; i++)
        {
            var c = clips[(start + i) % clips.Length];
            if (c != null) return c;
        }
        return null;
    }
}

/// <summary>
/// 音の割り当て表（Resources/AudioBank.asset）。Unity のメニュー Dungeon > Audio Assignments で編集する。
/// 無い場合は、Resources/Audio の既定のファイルが使われる。
/// </summary>
public class AudioBank : ScriptableObject
{
    public List<SoundEntry> entries = new List<SoundEntry>();

    static AudioBank instance;

    public static AudioBank Instance
    {
        get
        {
            if (instance == null) instance = Resources.Load<AudioBank>("AudioBank");
            return instance;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => instance = null;

    public SoundEntry Find(string key)
    {
        foreach (var e in entries)
            if (e != null && e.key == key) return e;
        return null;
    }
}
