using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

/// <summary>
/// macOS 用の音声認識ヘルパー（Tools/MacSpeech/SpeechHelper.app）の、ビルドと同梱。
/// メニューからビルドし、macOS 向けにゲームを書き出すと、.app の Contents/Resources に自動でコピーされる。
/// </summary>
public static class MacSpeechHelperBuild
{
    static string ToolDir => Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "..", "Tools", "MacSpeech"));
    public static string HelperApp => Path.Combine(ToolDir, "SpeechHelper.app");

    [MenuItem("Tools/Mac 音声認識/ヘルパーをビルド")]
    public static void Build()
    {
        if (Application_IsWindows()) { Debug.LogWarning("macOS 専用です。"); return; }
        var psi = new ProcessStartInfo("/bin/bash", "\"" + Path.Combine(ToolDir, "build.sh") + "\"")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = ToolDir,
        };
        using (var p = Process.Start(psi))
        {
            string o = p.StandardOutput.ReadToEnd(), e = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode == 0) Debug.Log("[MacSpeech] " + o.Trim());
            else Debug.LogError("[MacSpeech] ビルド失敗:\n" + e + o);
        }
    }

    static bool Application_IsWindows() => UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor;

    class Post : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneOSX) return;
            if (!Directory.Exists(HelperApp))
            {
                Debug.LogWarning("[MacSpeech] SpeechHelper.app が無いため同梱しません（音声認識は V キー代用になります）。先に メニュー Tools > Mac 音声認識 > ヘルパーをビルド を実行してください。");
                return;
            }
            string dest = Path.Combine(report.summary.outputPath, "Contents", "Resources", "SpeechHelper.app");
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            // 実行権限と署名を保つため cp -R を使う
            var psi = new ProcessStartInfo("/bin/cp", "-R \"" + HelperApp + "\" \"" + dest + "\"") { UseShellExecute = false };
            using (var p = Process.Start(psi)) p.WaitForExit();
            Debug.Log("[MacSpeech] SpeechHelper.app を同梱しました: " + dest);
        }
    }
}
