using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 「パーツ」タブ。ゲーム中にメニューから表情へ重ねて出すパーツ（汗・涙・頬染めなど）。
    /// - 「＋ 新しいパーツ」で空のパーツを作り、表情の編集ウィンドウで編集する。クリップのドロップでも追加できる
    /// - 元FXのパーツらしいレイヤー（メニューのトグルで顔のシェイプキーを少しだけ動かすもの）を取り込める
    /// </summary>
    internal partial class ExpressionEditorWindow
    {
        private const float MenuCellWidth = 86f;
        private const float MenuCellHeight = 102f;

        // 元FXから取り込むレイヤーのチェック。元FXを調べ直したら、パーツらしいものにチェックを付け直す。
        private HashSet<string> _partImportChecked = new HashSet<string>();
        private OriginalFxAnalysis _partImportFor;

        private void DrawPartsTab(ExpressionSet set)
        {
            EditorGUILayout.HelpBox(
                "ゲーム中にメニューから表情へ重ねて出すパーツです（汗・涙・頬染めなど）。「＋ 新しいパーツ」で作るか、クリップをドロップして追加します。\n" +
                "動かすプロパティの細かい選択は、表情データのインスペクタで行います。", MessageType.None);

            // まだ取り込んでいないパーツらしいレイヤーが元FXにあれば、目に付くよう先頭に出す。
            var pending = FxAnalysis.PartCandidates.Count > 0;
            if (pending)
            {
                DrawPartImport(set);
                EditorGUILayout.Space(12);
            }

            var rects = GridRects(set.parts.Count + 1, MenuCellWidth, MenuCellHeight, CenterWidth);
            for (var i = 0; i < set.parts.Count; i++)
            {
                var part = set.parts[i];
                var rect = rects[i];
                var group = string.IsNullOrEmpty(part.exclusiveGroup) ? null : part.exclusiveGroup;
                if (DrawCell(rect, PartThumbnail(part), part.name, IsSelected(SelectionKind.Part, part.id), group,
                        new Color(0.3f, 0.45f, 0.75f, 0.85f)))
                {
                    Select(SelectionKind.Part, part.id, null);
                }

                if (GUI.Button(new Rect(rect.xMax - 18, rect.y + 2, 16, 16), "×", EditorStyles.miniButton) &&
                    EditorUtility.DisplayDialog("パーツを削除", $"「{part.name}」を削除しますか？", "削除", "キャンセル"))
                {
                    Modify(set, "パーツを削除", () => set.parts.Remove(part));
                    GUIUtility.ExitGUI();
                }
            }

            var addRect = rects[set.parts.Count];
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(addRect, new Color(0, 0, 0, 0.12f));
            if (GUI.Button(new Rect(addRect.x + 4, addRect.y + 14, addRect.width - 8, 38), "＋ 新しい\nパーツ"))
            {
                CreatePart(set);
                GUIUtility.ExitGUI();
            }
            GUI.Label(new Rect(addRect.x, addRect.y + 52, addRect.width, 40), "クリップを\nドロップ", DropHint);
            var dropped = AcceptClipDrop(addRect);
            if (dropped != null)
            {
                var part = new FacialPart
                {
                    name = dropped.name,
                    clip = dropped,
                    properties = FxImporter.DefaultPartProperties(dropped),
                };
                Modify(set, "パーツを追加", () => set.parts.Add(part));
                ExpressionSetUtility.MakePartClipEditable(set, part);
                Select(SelectionKind.Part, part.id, null);
                MarkLibraryDirty();
                GUIUtility.ExitGUI();
            }

            if (!pending)
            {
                EditorGUILayout.Space(12);
                DrawPartImport(set);
            }
        }

        /// <summary>
        /// 空のパーツを作り、表情の編集ウィンドウで開く。
        /// </summary>
        private void CreatePart(ExpressionSet set)
        {
            var name = "新しいパーツ";
            for (var i = 2; set.parts.Any(p => p.name == name); i++) name = $"新しいパーツ {i}";

            var part = ExpressionSetUtility.CreatePart(set, name);
            Select(SelectionKind.Part, part.id, null);
            ExpressionClipEditorWindow.OpenPart(_avatar, part);
        }

        /// <summary>
        /// 元FXのレイヤーをパーツとして取り込む。顔を動かしている元FXのレイヤー（AFK以外）を並べ、パーツらしいものにチェックを付けておく。
        /// 取り込んだレイヤーは、ビルド時に生成したパーツ（とそのメニュー）に置き換える。
        /// </summary>
        private void DrawPartImport(ExpressionSet set)
        {
            var analysis = FxAnalysis;
            if (analysis.FaceLayers.Count == 0) return;
            if (_partImportFor != analysis)
            {
                _partImportFor = analysis;
                _partImportChecked = new HashSet<string>(analysis.PartCandidates);
            }

            EditorGUILayout.LabelField("元FXから取り込む", EditorStyles.boldLabel);
            if (analysis.PartCandidates.Count > 0)
            {
                EditorGUILayout.HelpBox($"元FXに、まだ取り込んでいないパーツが {analysis.PartCandidates.Count} 個あります。" +
                                        "取り込むと、ここに並んでプレビューで確かめられます。", MessageType.Info);
            }
            EditorGUILayout.LabelField("顔を動かしている元FXのレイヤーです。取り込んだレイヤーは、ビルド時に生成したパーツとそのメニューに置き換えます" +
                                       "（元のメニューのトグルは外れます）。パーツらしいものにチェックを付けています。",
                EditorStyles.wordWrappedMiniLabel);

            foreach (var layer in analysis.FaceLayers)
            {
                var clips = analysis.FaceLayerClips.TryGetValue(layer, out var list) ? list : new List<AnimationClip>();
                var on = _partImportChecked.Contains(layer);
                var next = EditorGUILayout.ToggleLeft($"{layer}（{string.Join("、", clips.Select(c => c.name))}）", on);
                if (next == on) continue;
                if (next) _partImportChecked.Add(layer);
                else _partImportChecked.Remove(layer);
            }

            var selected = analysis.FaceLayers.Where(_partImportChecked.Contains).ToList();
            using (new EditorGUI.DisabledScope(selected.Count == 0))
            {
                if (GUILayout.Button($"チェックしたレイヤーをパーツとして取り込む（{selected.Count}）", GUILayout.Width(300)))
                {
                    var added = 0;
                    Modify(set, "元FXからパーツを取り込み", () => added = selected.Sum(l => FxImporter.ImportPartsFromLayer(Descriptor, set, l)));
                    MarkLibraryDirty();
                    ShowNotification(new GUIContent($"パーツを {added} 件取り込みました"), 3);
                    GUIUtility.ExitGUI();
                }
            }
        }
    }
}
