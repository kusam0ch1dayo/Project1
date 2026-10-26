using UnityEditor;
using UnityEngine;

/// <summary>
/// Resources/Audio の取り込み設定を自動で最適化する。
/// BGM（長い曲）はストリーミング再生でメモリを節約し、効果音は読み込み時に展開して即再生できるようにする。
/// </summary>
public class AudioImportSettings : AssetPostprocessor
{
    [MenuItem("Dungeon/Reimport Audio (apply import settings)")]
    static void ReimportAll()
    {
        AssetDatabase.ImportAsset("Assets/Resources/Audio", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        Debug.Log("Resources/Audio を再インポートしました。");
    }

    void OnPreprocessAudio()
    {
        if (!assetPath.Contains("Resources/Audio/")) return;

        var importer = (AudioImporter)assetImporter;
        var settings = importer.defaultSampleSettings;

        if (assetPath.Contains("/BGM/"))
        {
            settings.loadType = AudioClipLoadType.Streaming;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.6f;
        }
        else
        {
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.8f;
        }
        importer.defaultSampleSettings = settings;
    }
}
