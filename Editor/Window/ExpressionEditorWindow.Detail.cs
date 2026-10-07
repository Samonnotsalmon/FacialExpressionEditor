using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Samon.FacialExpressionEditor.Editor
{
    internal partial class ExpressionEditorWindow
    {
        // 右のプレビューに使う一時クリップと、描画済みの画像。選択や表情データが変わったときだけ作り直す
        // （ウィンドウの再描画のたびに描き直すと重いため）。
        private AnimationClip _detailClip;
        private bool _detailClipReady;
        private bool _detailTimeVarying;
        private Texture2D _detailTexture;
        private bool _detailTextureDirty = true;
        private Vector2 _detailScroll;

        private void InvalidateDetailPreview()
        {
            PreviewClips.Release(_detailClip);
            _detailClip = null;
            _detailClipReady = false;
            _detailTextureDirty = true;
        }

        private void ReleaseDetailTexture()
        {
            if (_detailTexture != null) DestroyImmediate(_detailTexture);
            _detailTexture = null;
            _detailTextureDirty = true;
        }

        private void DrawDetail(ExpressionSet set)
        {
            EditorGUILayout.LabelField("プレビュー", EditorStyles.boldLabel);
            var size = DetailWidth - 10;
            var rect = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));

            var isFist = _selectionKind == SelectionKind.Fist;
            var expression = _selectionKind == SelectionKind.Expression || isFist ? set.FindExpression(_selectedId) : null;
            var part = _selectionKind == SelectionKind.Part ? set.parts.Find(p => p.id == _selectedId) : null;
            PrepareDetailClip(expression, part);

            if (Event.current.type == EventType.Repaint)
            {
                if (_detailTextureDirty || _detailTexture == null)
                {
                    if (part != null)
                    {
                        _preview.Apply(null);
                        _preview.Overlay(_detailClip);
                    }
                    else
                    {
                        // 握り具合はFistのマスを選んだときだけ。表情そのものは、最後まで再生した形（ゲーム内で固定したときと同じ）。
                        _preview.Apply(_detailClip, isFist ? _triggerWeight : 1f);
                    }
                    if (_detailTexture != null) DestroyImmediate(_detailTexture);
                    _detailTexture = _preview.RenderStatic(Mathf.RoundToInt(size * EditorGUIUtility.pixelsPerPoint));
                    _detailTexture.hideFlags = HideFlags.HideAndDontSave;
                    _detailTextureDirty = false;
                }
                GUI.DrawTexture(rect, _detailTexture, ScaleMode.ScaleToFit, false);
            }

            if (isFist && _detailTimeVarying)
            {
                EditorGUI.BeginChangeCheck();
                _triggerWeight = EditorGUILayout.Slider("握り具合", _triggerWeight, 0f, 1f);
                if (EditorGUI.EndChangeCheck()) _detailTextureDirty = true;
            }

            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
            switch (_selectionKind)
            {
                case SelectionKind.Expression when expression != null:
                    DrawExpressionDetail(set, expression);
                    break;
                case SelectionKind.Fist when expression != null:
                    DrawFistDetail(expression, _selectedHand);
                    DrawExpressionDetail(set, expression);
                    break;
                case SelectionKind.Clip when _selectedClip != null:
                    DrawClipDetail(set, _selectedClip);
                    break;
                case SelectionKind.Part when part != null:
                    DrawPartDetail(set, part);
                    break;
                default:
                    EditorGUILayout.LabelField("表情を選ぶと、ここに設定が出ます。", EditorStyles.wordWrappedMiniLabel);
                    break;
            }
            EditorGUILayout.EndScrollView();
        }

        private void PrepareDetailClip(Expression expression, FacialPart part)
        {
            if (_detailClipReady) return;
            _detailClipReady = true;

            if (expression != null)
            {
                _detailClip = _selectionKind == SelectionKind.Fist
                    ? PreviewClips.ForFist(expression, Variant, AvatarRoot)
                    : PreviewClips.ForExpression(expression, Variant, AvatarRoot);
            }
            else if (part != null) _detailClip = PreviewClips.ForPart(part);
            else if (_selectionKind == SelectionKind.Clip && _selectedClip != null)
            {
                _detailClip = Object.Instantiate(_selectedClip);
                _detailClip.hideFlags = HideFlags.HideAndDontSave;
            }

            _detailTimeVarying = _detailClip != null && part == null && PreviewClips.IsTimeVarying(_detailClip);
            _detailTextureDirty = true;
        }

        /// <summary>
        /// 握り具合をオンにしたFistのマスを選んだときの説明（プレビューのスライダーで握り具合を確認できる）。
        /// </summary>
        private void DrawFistDetail(Expression expression, Hand hand)
        {
            var handName = hand == Hand.Left ? "左手" : "右手";
            EditorGUILayout.LabelField($"{handName}のFist（握り具合）", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                PreviewClips.IsTimeVarying(expression.clip)
                    ? "この表情はクリップ自体が握り具合で動くように作られているので、そのまま握り具合で動かします。上のスライダーで確認できます。"
                    : $"握り具合0でベース顔（無表情）、握り切ると「{expression.name}」になります。上のスライダーで確認できます。",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();
        }

        private void DrawExpressionDetail(ExpressionSet set, Expression expression)
        {
            if (GUILayout.Button("この表情を編集", GUILayout.Height(24)))
            {
                ExpressionClipEditorWindow.Open(_avatar, expression);
            }

            EditorGUI.BeginChangeCheck();
            var name = EditorGUILayout.TextField("名前", expression.name);
            if (EditorGUI.EndChangeCheck()) Modify(set, "表情の名前を変更", () => expression.name = name);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("クリップ", expression.clip, typeof(AnimationClip), false);
            }

            EditorGUI.BeginChangeCheck();
            var overrideDuration = EditorGUILayout.ToggleLeft("遷移時間を個別に設定", expression.overrideTransitionDuration);
            float duration;
            using (new EditorGUI.DisabledScope(!overrideDuration))
            {
                duration = EditorGUILayout.FloatField("遷移時間（秒）",
                    overrideDuration ? expression.transitionDuration : set.defaultTransitionDuration);
            }
            if (EditorGUI.EndChangeCheck())
            {
                Modify(set, "遷移時間を変更", () =>
                {
                    expression.overrideTransitionDuration = overrideDuration;
                    if (overrideDuration) expression.transitionDuration = duration;
                });
            }

            DrawFaceControlForExpression(set, expression);

            if (set.menu.Any(n => n.kind == MenuNodeKind.Expression && n.expressionId == expression.id))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("固定したときの切り替え演出", EditorStyles.boldLabel);
                var standard = set.fixedSwitchEffect.enabled && set.FindExpression(set.fixedSwitchEffect.betweenExpressionId) != null
                    ? $"標準（{set.FindExpression(set.fixedSwitchEffect.betweenExpressionId).name}を挟む）"
                    : "標準（なし）";
                EditorGUI.BeginChangeCheck();
                var mode = (SwitchEffectMode)EditorGUILayout.Popup("演出", (int)expression.fixedSwitchMode,
                    new[] { standard, "なし", "この表情だけ設定" });
                if (EditorGUI.EndChangeCheck()) Modify(set, "切り替え演出を変更", () => expression.fixedSwitchMode = mode);
                if (expression.fixedSwitchMode == SwitchEffectMode.Custom)
                {
                    DrawSwitchEffect(set, expression.fixedSwitchEffect, "切り替え演出を変更");
                }
            }

            var variant = Variant;
            if (variant != null) DrawVariantDetail(variant, expression);

            EditorGUILayout.Space();
            var uses = _usage.Of(expression.clip);
            EditorGUILayout.LabelField(uses.Count > 1 ? $"使っている場所（重複：{uses.Count} か所）" : "使っている場所",
                uses.Count > 1 ? WarningBoldLabel : EditorStyles.boldLabel);
            if (uses.Count == 0) EditorGUILayout.LabelField("（未割り当て）", EditorStyles.miniLabel);
            foreach (var use in uses)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(use.Label, EditorStyles.miniLabel);
                    if (GUILayout.Button(new GUIContent("外す", "ここへの割り当てを外す"), EditorStyles.miniButton, GUILayout.Width(40)))
                    {
                        Modify(set, "割り当てを外す", use.Remove);
                        MarkLibraryDirty();
                        GUIUtility.ExitGUI();
                    }
                }
            }

            if (ExpressionSetUtility.IsInGestureOfMenu(set, expression.id))
            {
                EditorGUILayout.LabelField("ジェスチャーに割り当てているので、ゲーム内の「表情選択」から固定もできます。", EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("この表情を削除") &&
                EditorUtility.DisplayDialog("表情を削除",
                    $"「{expression.name}」を表情データから削除し、割り当てからも外しますか？（クリップのファイルは消えません）", "削除", "キャンセル"))
            {
                Modify(set, "表情を削除", () => ExpressionSetUtility.RemoveExpression(set, expression));
                Select(SelectionKind.None, null, null);
                MarkLibraryDirty();
                GUIUtility.ExitGUI();
            }
        }

        /// <summary>
        /// この表情の間のまばたき・視線・リップシンク・口モーフキャンセラー。
        /// </summary>
        private void DrawFaceControlForExpression(ExpressionSet set, Expression expression)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("この表情の間の動き", EditorStyles.boldLabel);

            var hasCanceler = MouthMorphsInUse(set).Count > 0;
            EditorGUI.BeginChangeCheck();
            var blink = EditorGUILayout.ToggleLeft("まばたき", expression.enableBlink);
            var eyes = EditorGUILayout.ToggleLeft("視線（目の動き）", expression.enableEyeTracking);
            var lipSync = EditorGUILayout.ToggleLeft("リップシンク", expression.enableLipSync);
            bool cancel;
            using (new EditorGUI.DisabledScope(!lipSync || !hasCanceler))
            {
                cancel = EditorGUILayout.ToggleLeft(
                    hasCanceler ? "口モーフキャンセラー" : "口モーフキャンセラー（シェイプキー未設定）", expression.mouthMorphCancel);
            }
            if (EditorGUI.EndChangeCheck())
            {
                Modify(set, "表情の間の動きを変更", () =>
                {
                    expression.enableBlink = blink;
                    expression.enableEyeTracking = eyes;
                    expression.enableLipSync = lipSync;
                    expression.mouthMorphCancel = cancel;
                });
            }

            if (!expression.enableBlink && expression.enableEyeTracking && !set.replaceBlink)
            {
                EditorGUILayout.LabelField("まばたきを置き換えない設定なので、この表情では視線も止まります。", WarningMiniLabel);
            }
        }

        private void DrawVariantDetail(FaceVariant variant, Expression expression)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"顔バリアント：{variant.name}", EditorStyles.boldLabel);

            // ベース顔はすべての表情に適用する。この表情だけ変えるところは、表情の編集ウィンドウで「この顔だけの値」にする。
            var faceValues = variant.FindFaceValues(expression.id);
            var count = faceValues != null ? faceValues.values.Count : 0;
            EditorGUILayout.LabelField(count > 0
                    ? $"ベース顔を適用し、この表情だけの値が {count} 件あります（「この表情を編集」で変えられます）。"
                    : "ベース顔を適用します。この表情だけ変えるときは「この表情を編集」で。",
                EditorStyles.wordWrappedMiniLabel);

            var entry = variant.FindOverride(expression.id);
            if (entry == null)
            {
                using (new EditorGUI.DisabledScope(expression.clip == null))
                {
                    if (GUILayout.Button("このバリアント用に複製して差し替え"))
                    {
                        FaceVariantUtility.DuplicateForVariant(variant, expression);
                        AssetDatabase.SaveAssets();
                        InvalidateDetailPreview();
                        GUIUtility.ExitGUI();
                    }
                }
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("差し替えクリップ", entry.clip, typeof(AnimationClip), false);
            }
            if (FaceVariantUtility.IsSourceUpdated(entry, expression))
            {
                EditorGUILayout.HelpBox("複製した後に、共有の表情が更新されています。", MessageType.Info);
            }
            if (GUILayout.Button("共有に戻す"))
            {
                FaceVariantUtility.RevertOverride(variant, expression.id);
                InvalidateDetailPreview();
                GUIUtility.ExitGUI();
            }
        }

        private void DrawClipDetail(ExpressionSet set, AnimationClip clip)
        {
            EditorGUILayout.LabelField(clip.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(AssetDatabase.GetAssetPath(clip), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("まだ表情として使っていないクリップです。中央の表やフォルダへドラッグすると割り当てられます。",
                EditorStyles.wordWrappedMiniLabel);

            if (GUILayout.Button("表情として追加（割り当てはしない）"))
            {
                Expression added = null;
                Modify(set, "表情を追加", () => added = ExpressionSetUtility.FindOrCreateExpression(set, clip));
                Select(SelectionKind.Expression, added.id, null);
            }
        }

        private void DrawPartDetail(ExpressionSet set, FacialPart part)
        {
            EditorGUI.BeginChangeCheck();
            var name = EditorGUILayout.TextField("名前", part.name);
            var group = EditorGUILayout.TextField("排他グループ", part.exclusiveGroup);
            if (EditorGUI.EndChangeCheck())
            {
                Modify(set, "パーツを変更", () =>
                {
                    part.name = name;
                    part.exclusiveGroup = group;
                });
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("クリップ", part.clip, typeof(AnimationClip), false);
            }

            var total = part.clip == null ? 0
                : AnimationUtility.GetCurveBindings(part.clip).Length + AnimationUtility.GetObjectReferenceCurveBindings(part.clip).Length;
            EditorGUILayout.LabelField("動かすプロパティ", $"{part.properties.Count} / {total}");

            var groups = set.parts.Select(p => p.exclusiveGroup).Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();
            if (groups.Count > 0)
            {
                EditorGUILayout.LabelField($"排他グループ：{string.Join("、", groups)}", EditorStyles.wordWrappedMiniLabel);
            }

            if (GUILayout.Button("インスペクタでプロパティを選ぶ"))
            {
                Selection.activeObject = set;
            }
        }
    }
}
