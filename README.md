# NSL_Facial Expression Editor

VRChatアバターの表情・ジェスチャー設定を行うUnityエディタ拡張です（自分専用）。

- ベース顔ごとに独立した表情設定と編集用クリップを作成できる
- ジェスチャー・固定メニュー・顔パーツ・既存モーションとの連携を設定できる
- 「反応」タブで、元FXの条件・遷移を残したままコンタクト・PhysBoneのクリップを編集・差し替えできる
- Modular Avatar / NDMFで、ビルド時に非破壊で適用する

現在のバージョンは **0.5.0**。変更内容は [CHANGELOG.md](CHANGELOG.md)、仕様は [docs/spec.md](docs/spec.md) を参照。

## VCC で入れる
1. VCC の Settings → Packages → Add Repository に次の URL を入れる（各PCで1回だけ）。
   ```
   https://samonnotsalmon.github.io/FacialExpressionEditor/index.json
   ```
   上の URL のページを開いて「VCC に追加する」を押してもよい。
2. プロジェクトの Manage Project で「NSL_Facial Expression Editor」を追加・削除する。バージョンもここで選べる。

## 使い方
1. メニューの Tools → NotSalmon → NSL_Facial Expression Editor を開く。先にProjectのアバターPrefab、またはHierarchyのアバターを選択しておくと、ドロップ済みの状態で開く。既存の表情設定が1つでベース顔が変わっていなければ、その編集画面へ直接進む。
2. 「新しく始める」に、アバターのプレハブ（Hierarchy のアバターでも）をドロップして始める。
3. できた表情設定のプレハブ（`アバター名_FEE.prefab`）を、使うアバターの中に入れる。

## サムネイルの更新

編集対象アバターの髪や服のGameObject、またはRendererの表示を切り替えて「表示を更新」を押すと、一覧のサムネイルにも反映する。Hierarchyの目アイコンによるSceneビューだけの非表示は対象外。

ビルド時のメニュー画像は、ビルド対象アバターの外見に各表情を適用して作成する。同じ外見の画像があればキャッシュを再利用する。編集のために隠した前髪も撮影状態に含まれるため、メニュー画像に写す場合はビルド前に表示を戻す。撮影はMAの処理前で、ビルド中の最終加工やゲーム内の衣装切り替えは撮影対象外。

## リリースの手順
1. `package.json` の `version` を上げてコミットする。
2. 同じバージョンのタグ（`v0.5.0` など）を付けて送る。
   ```
   git tag v0.5.0
   git push origin main v0.5.0
   ```
3. GitHub Actions が、リリース（zip）と VCC 用の一覧（GitHub Pages）を作り直す。

最初の1回だけ、GitHub のリポジトリの Settings → Pages の Source を「GitHub Actions」にしておく。

## 開発
表情の取り込み・作成・生成を変更する前に、[仕様書](docs/spec.md)の「アニメーションの記録範囲と未指定値の扱い」を確認する。未指定と明示的な0を区別し、Empty・部分表情の記録範囲を維持する。実装済みの動作と今後の修正候補も同節で管理する。

開発用のUnityプロジェクトでは、VCC ではなく `Packages/manifest.json` の `dependencies` でこのフォルダを直接参照する（編集がすぐ反映される）。

```json
"jp.samon.facial-expression-editor": "file:<このフォルダのパス>"
```

## 依存
- VRChat Avatars SDK
- Modular Avatar（NDMFを含む）
