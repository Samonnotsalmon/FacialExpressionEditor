# NSL_Facial Expression Editor

VRChatアバターの表情・ジェスチャー設定を行うUnityエディタ拡張です（自分専用）。

- ベース顔ごとに独立した表情設定と編集用クリップを作成できる
- ジェスチャー・固定メニュー・顔パーツ・既存モーションとの連携を設定できる
- Modular Avatar / NDMFで、ビルド時に非破壊で適用する

現在のバージョンは **0.4.0**。変更内容は [CHANGELOG.md](CHANGELOG.md)、仕様は [docs/spec.md](docs/spec.md) を参照。

## VCC で入れる
1. VCC の Settings → Packages → Add Repository に次の URL を入れる（各PCで1回だけ）。
   ```
   https://samonnotsalmon.github.io/FacialExpressionEditor/index.json
   ```
   上の URL のページを開いて「VCC に追加する」を押してもよい。
2. プロジェクトの Manage Project で「NSL_Facial Expression Editor」を追加・削除する。バージョンもここで選べる。

## 使い方
1. メニューの Tools → NotSalmon → NSL_Facial Expression Editor を開く。
2. 「新しく始める」に、アバターのプレハブ（Hierarchy のアバターでも）をドロップして始める。
3. できた表情設定のプレハブ（`アバター名_FEE.prefab`）を、使うアバターの中に入れる。

## リリースの手順
1. `package.json` の `version` を上げてコミットする。
2. 同じバージョンのタグ（`v0.4.0` など）を付けて送る。
   ```
   git tag v0.4.0
   git push origin main v0.4.0
   ```
3. GitHub Actions が、リリース（zip）と VCC 用の一覧（GitHub Pages）を作り直す。

最初の1回だけ、GitHub のリポジトリの Settings → Pages の Source を「GitHub Actions」にしておく。

## 開発
開発用のUnityプロジェクトでは、VCC ではなく `Packages/manifest.json` の `dependencies` でこのフォルダを直接参照する（編集がすぐ反映される）。

```json
"jp.samon.facial-expression-editor": "file:<このフォルダのパス>"
```

## 依存
- VRChat Avatars SDK
- Modular Avatar（NDMFを含む）
