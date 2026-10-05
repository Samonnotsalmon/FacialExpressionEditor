# Facial Expression Editor

VRChatアバターの表情・ジェスチャー設定を行うUnityエディタ拡張です（自分専用）。

- 1つの表情セットを、顔バリアント（ジト目・ツリ目・タレ目など）のアバター間で共有できる
- バリアントごとに、ベース顔の扱いと表情の差し替えを設定できる
- Modular Avatar / NDMFで、ビルド時に非破壊で適用する

仕様は [docs/spec.md](docs/spec.md) を参照。

## 導入（開発中）
開発用のUnityプロジェクトで、`Packages/manifest.json` の `dependencies` に次を追加する。

```json
"jp.samon.facial-expression-editor": "file:C:/Claude/FacialExpressionEditor"
```

## 依存
- VRChat Avatars SDK
- Modular Avatar（NDMFを含む）
