# 3Dダンジョン（Unity 6 / URP）コード一式

作成日: 2026-10-26

## 中身
- Assets/Scripts/ : ゲーム本体の C#
- Assets/Editor/  : エディタ拡張（Dungeon メニューの MenuItem・各種 Creator ウィンドウ）
- Assets/unityroom/ : unityroom ランキング API クライアント（ゲームが参照）
- Assets/Plugins/WebGL/ : WebGL 用 jslib
- Packages/manifest.json, packages-lock.json, ProjectSettings/ProjectVersion.txt : Unity バージョン・パッケージ
- MENU_ITEMS.txt : すべての [MenuItem] のパスと定義場所（ファイル:行）。36 件

アセット（シーン・プレハブ・ScriptableObject・画像・音声）と Library は含めていません。

## メニューの定義
MenuItem は `[MenuItem("Dungeon/...")]` 属性で定義されています。
新しいメニューを探すときは、`MenuItem(` を grep してください。

## 伏せている値
次の値は、公開しないように `REDACTED` に置き換えています（ビルドは通りますが、その機能は動きません）。
- Assets/Scripts/Core/UnityroomRanking.cs の HmacKey（unityroom の HMAC 認証キー）
- Assets/Scripts/Core/GiftCode.cs の Secret（ギフトコードの署名鍵）
