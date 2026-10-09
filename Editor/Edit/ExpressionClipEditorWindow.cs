using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 表情の編集ウィンドウ。表情のクリップのシェイプキー・オブジェクト・マテリアル・シェーダーのパラメータを編集する。
    /// - 編集はすぐクリップに書き込む（Ctrl+Z で戻せる。「開いたときの状態に戻す」もある）
    /// - 顔バリアントの差し替えがある表情は、そのバリアント専用のクリップを編集する（そのことを表示する）
    /// - 作者のクリップ（表情データの Expressions フォルダの外）は、最初に編集したときに複製して、複製を編集する
    /// - パーツ（汗・涙・頬染めなど）と、元FXのAFKのアニメーションも開ける
    /// </summary>
    internal partial class ExpressionClipEditorWindow : EditorWindow
    {
        private const float LeftWidth = 360f;

        private enum Tab { BlendShapes, ObjectsAndMaterials, ShaderProperties }
        private static readonly string[] TabLabels = { "シェイプキー", "オブジェクト・マテリアル", "シェーダーのパラメータ" };

        // 表情エディタのプレビューとサムネイルを更新するため、編集したら知らせる。
        public static event Action Edited;

        [SerializeField] private FacialExpressionAvatar _avatar;
        [SerializeField] private string _expressionId;
        [SerializeField] private Tab _tab;
        [SerializeField] private float _zoom = 1f;
        [SerializeField] private float _yaw;

        private FacePreview _preview;
        private Texture2D _previewTexture;
        private bool _previewDirty = true;

        // 開いたときのクリップの中身（「開いたときの状態に戻す」用）と、それがどの表情・どのクリップのものか。
        private AnimationClip _snapshot;
        private List<BlendShapeValue> _faceValuesSnapshot;
        private string _snapshotExpressionId;

        private ExpressionSet Set => _avatar != null ? _avatar.expressionSet : null;
        private FaceVariant Variant => _avatar != null ? _avatar.faceVariant : null;
        private Expression Expression => IsAfk ? AfkExpression : IsPart ? PartExpression(Part) : Set != null ? Set.FindExpression(_expressionId) : null;
        private ExpressionOverride Override => Variant != null && !IsPart ? Variant.FindOverride(_expressionId) : null;
        // プレビューと元FXの読み取りに使うアバター（表情設定が入っているアバターか、編集に使うアバター）。
        private GameObject AvatarRoot => AvatarSetup.AvatarRootOf(_avatar);
        private Transform Root => AvatarRoot != null ? AvatarRoot.transform : null;
        private bool IsSelectedTarget(Transform target)
        {
            if (Set == null || Set.faceMeshPaths.Count + Set.linkedObjectPaths.Count == 0) return true;
            var path = PathOf(target, Root);
            return Set.faceMeshPaths.Contains(path) || Set.linkedObjectPaths.Contains(path);
        }

        // 今編集している（次に書き込む）クリップ。差し替えがあればそちら。AFKでは元のクリップを変えないので無し。
        private AnimationClip TargetClip => IsAfk ? null : Override != null ? Override.clip : Expression?.clip;

        // 共有の表情を編集しているか（差し替えではなく）。
        private bool EditsShared => Override == null;

        public static void Open(FacialExpressionAvatar avatar, Expression expression)
        {
            var window = GetWindow<ExpressionClipEditorWindow>();
            window.titleContent = new GUIContent("表情の編集");
            window.minSize = new Vector2(900, 560);
            window.SetTarget(avatar, expression?.id);
            window.Show();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            DisposePreview();
            ReleaseAfk();
            if (_snapshot != null) DestroyImmediate(_snapshot);
        }

        private void OnUndoRedo()
        {
            _previewDirty = true;
            InvalidateValues();
            InvalidateAfk();
            Edited?.Invoke();
            Repaint();
        }

        private void SetTarget(FacialExpressionAvatar avatar, string expressionId)
        {
            if (_avatar != avatar) DisposePreview();
            _avatar = avatar;
            _expressionId = expressionId;
            _previewDirty = true;
            InvalidateValues();
            TakeSnapshot();
        }

        private void TakeSnapshot()
        {
            if (_snapshot != null) DestroyImmediate(_snapshot);
            _snapshot = null;
            _snapshotExpressionId = _expressionId;
            if (IsAfk) TakeAfkSnapshot();

            var faceValues = Variant != null ? Variant.FindFaceValues(_expressionId) : null;
            _faceValuesSnapshot = faceValues?.values
                .Select(v => new BlendShapeValue { path = v.path, blendShape = v.blendShape, value = v.value })
                .ToList();

            var clip = TargetClip;
            if (clip == null) return;

            _snapshot = Instantiate(clip);
            _snapshot.hideFlags = HideFlags.HideAndDontSave;
        }

        private void OnGUI()
        {
            if (_avatar == null || Set == null || AvatarRoot == null)
            {
                EditorGUILayout.HelpBox("表情エディタで表情を選び、「この表情を編集」から開いてください。", MessageType.Info);
                return;
            }

            var expression = Expression;
            DrawToolbar(expression);
            if (expression == null)
            {
                EditorGUILayout.HelpBox("編集する表情を選んでください。", MessageType.Info);
                return;
            }
            if (_snapshotExpressionId != _expressionId) TakeSnapshot();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(LeftWidth)))
                {
                    DrawPreview(expression);
                    DrawTargetInfo(expression);
                }

                using (new EditorGUILayout.VerticalScope())
                {
                    if (IsAfk)
                    {
                        DrawAfkTimeline();
                    }
                    else
                    {
                        _tab = (Tab)GUILayout.Toolbar((int)_tab, TabLabels, GUILayout.Height(24));
                        switch (_tab)
                        {
                            case Tab.BlendShapes: DrawBlendShapes(expression); break;
                            case Tab.ObjectsAndMaterials: DrawObjectsAndMaterials(); break;
                            case Tab.ShaderProperties: DrawShaderProperties(); break;
                        }
                    }
                }
            }
        }

        private void DrawToolbar(Expression expression)
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(AvatarSetup.DisplayName(_avatar), EditorStyles.miniLabel);
                GUILayout.Space(8);
                GUILayout.Label("表情", GUILayout.Width(28));

                // 表情の後ろに、元FXのAFK（あれば）とパーツを並べる。
                var targets = Set.expressions.Select(e => (id: e.id, label: e.name)).ToList();
                if (AfkExpression != null) targets.Add((FaceVariant.AfkId, "AFK（元FXのアニメーション）"));
                targets.AddRange(Set.parts.Select(p => (id: p.id, label: $"パーツ：{p.name}")));
                var index = targets.FindIndex(t => t.id == _expressionId);
                var next = EditorGUILayout.Popup(index, targets.Select(t => t.label).ToArray(), EditorStyles.toolbarPopup, GUILayout.Width(220));
                if (next != index && next >= 0) SetTarget(_avatar, targets[next].id);

                GUILayout.FlexibleSpace();
            }
        }

        // ---- プレビュー ----

        private void DrawPreview(Expression expression)
        {
            var size = LeftWidth - 10;
            var rect = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));

            EditorGUI.BeginChangeCheck();
            _zoom = EditorGUILayout.Slider("寄る", _zoom, 0.5f, 3f);
            _yaw = EditorGUILayout.Slider("向き", _yaw, -80f, 80f);
            if (EditorGUI.EndChangeCheck()) _previewDirty = true;

            if (Event.current.type != EventType.Repaint) return;

            if (_preview == null || _preview.Source != AvatarRoot)
            {
                DisposePreview();
                _preview = new FacePreview(AvatarRoot);
                _previewDirty = true;
            }

            if (_previewDirty || _previewTexture == null)
            {
                // ビルドと同じ処理（差し替えとベース顔）を通したクリップで描く。AFKは今の時間の形。
                _preview.Zoom = _zoom;
                _preview.Yaw = _yaw;
                if (IsAfk)
                {
                    var clip = AfkProcessedClip();
                    _preview.Apply(clip, clip != null && clip.length > 0 ? _afkSeconds / clip.length : 0);
                }
                else
                {
                    var clip = PreviewClipFor(expression);
                    _preview.Apply(clip, 1f);
                    PreviewClips.Release(clip);
                }

                if (_previewTexture != null) DestroyImmediate(_previewTexture);
                _previewTexture = _preview.RenderStatic(Mathf.RoundToInt(size * EditorGUIUtility.pixelsPerPoint));
                _previewTexture.hideFlags = HideFlags.HideAndDontSave;
                _previewDirty = false;
            }
            GUI.DrawTexture(rect, _previewTexture, ScaleMode.ScaleToFit, false);
        }

        private void DisposePreview()
        {
            if (_previewTexture != null) DestroyImmediate(_previewTexture);
            _previewTexture = null;
            _preview?.Dispose();
            _preview = null;
        }

        /// <summary>
        /// 何を編集しているか（共有の表情か、バリアント専用の差し替えか、作者のクリップか）と、戻すボタン。
        /// </summary>
        private void DrawTargetInfo(Expression expression)
        {
            EditorGUILayout.Space();
            if (IsAfk || IsPart)
            {
                if (IsAfk) DrawAfkTargetInfo();
                else DrawPartTargetInfo(Part);
                DrawRevertButton();
                return;
            }

            var variant = Variant;
            var entry = Override;
            if (entry != null)
            {
                EditorGUILayout.HelpBox($"顔バリアント「{variant.name}」専用の差し替えクリップを編集しています。", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(Set.independentClips ? "このベース顔専用の表情クリップを編集しています。" : "この表情データで使うクリップを編集しています。", MessageType.Info);
                if (expression.clip != null && !ExpressionSetUtility.OwnsClip(Set, expression.clip))
                {
                    EditorGUILayout.LabelField("作者のクリップは、最初に編集したときに複製して編集します（元のファイルは変わりません）。",
                        EditorStyles.wordWrappedMiniLabel);
                }
            }
            if (variant != null && variant.baseFace.Any(k => k.enabled))
            {
                EditorGUILayout.LabelField("青い行（ベース顔）は、この顔のこの表情だけの値を変えます。", EditorStyles.wordWrappedMiniLabel);
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("クリップ", TargetClip, typeof(AnimationClip), false);
                if (expression.originalClip != null && entry == null)
                {
                    EditorGUILayout.ObjectField("複製元", expression.originalClip, typeof(AnimationClip), false);
                }
            }

            DrawRevertButton();
        }

        private void DrawRevertButton()
        {
            using (new EditorGUI.DisabledScope(!IsAfk && (_snapshot == null || TargetClip == null)))
            {
                if (GUILayout.Button("開いたときの状態に戻す") &&
                    EditorUtility.DisplayDialog("表情の編集", "このウィンドウで開いたときの状態に戻しますか？（Ctrl+Z で取り消せます）", "戻す", "キャンセル"))
                {
                    var clip = TargetClip;
                    if (clip != null && _snapshot != null)
                    {
                        Undo.RecordObject(clip, "開いたときの状態に戻す");
                        ClipEditing.CopyCurves(_snapshot, clip);
                        EditorUtility.SetDirty(clip);
                    }
                    RestoreFaceValues();
                    AfterEdit();
                }
            }
        }

        /// <summary>
        /// この顔のこの表情だけの値を、開いたときの状態に戻す。
        /// </summary>
        private void RestoreFaceValues()
        {
            if (IsAfk)
            {
                RestoreAfkCurves();
                return;
            }

            var variant = Variant;
            if (variant == null) return;

            Undo.RecordObject(variant, "開いたときの状態に戻す");
            variant.faceValues.RemoveAll(v => v.expressionId == _expressionId);
            if (_faceValuesSnapshot != null && _faceValuesSnapshot.Count > 0)
            {
                variant.faceValues.Add(new ExpressionFaceValues
                {
                    expressionId = _expressionId,
                    values = _faceValuesSnapshot.Select(v => new BlendShapeValue { path = v.path, blendShape = v.blendShape, value = v.value }).ToList(),
                });
            }
            EditorUtility.SetDirty(variant);
        }

        // ---- 書き込み ----

        /// <summary>
        /// クリップに書き込む。書き込む前に、編集できるクリップを用意する（作者のクリップの複製、未編集の差し替えへのベース顔の反映）。
        /// </summary>
        private void Modify(string undoName, Action<AnimationClip> change)
        {
            if (IsAfk) return;
            var clip = PrepareTargetClip();
            if (clip == null) return;

            var part = Part;
            var before = part != null ? PartPropertyValues(clip) : null;
            Undo.RecordObject(clip, undoName);
            change(clip);
            EditorUtility.SetDirty(clip);
            if (part != null) SyncPartProperties(part, before, clip);
            AfterEdit();
        }

        private void AfterEdit()
        {
            _previewDirty = true;
            InvalidateValues();
            Edited?.Invoke();
            Repaint();
        }

        private AnimationClip PrepareTargetClip()
        {
            if (IsPart) return PreparePartClip(Part);

            var set = Set;
            var expression = Expression;
            var variant = Variant;
            var entry = Override;

            if (entry != null)
            {
                if (entry.clip == null) return null;

                // 未編集の差し替えは、プレビューでは共有の表情＋ベース顔に見えている。その見えている形を書き込んでから編集する
                // （書き込まずに編集すると、編集した途端に差し替えクリップがそのまま使われてベース顔が消えるため）。
                if (!FaceVariantUtility.IsEdited(entry, expression))
                {
                    var clip = entry.clip;
                    Undo.RecordObject(clip, "ベース顔を反映");
                    BaseFaceProcessor.Apply(variant, expression.id, false, AvatarRoot,
                        b => AnimationUtility.GetEditorCurve(clip, b),
                        (b, c) => AnimationUtility.SetEditorCurve(clip, b, c),
                        includeFaceValues: false);
                    Undo.RecordObject(variant, "ベース顔を反映");
                    entry.baseFaceBaked = true;
                    EditorUtility.SetDirty(clip);
                    EditorUtility.SetDirty(variant);
                }
                return entry.clip;
            }

            if (expression.clip != null && ExpressionSetUtility.OwnsClip(set, expression.clip)) return expression.clip;

            var original = expression.clip;
            var copy = ExpressionSetUtility.MakeClipEditable(set, expression);
            if (copy != null && original != null)
            {
                ShowNotification(new GUIContent($"「{original.name}」を複製して編集します"), 3);
            }
            return copy;
        }

        // ---- 値 ----

        // クリップの値（float のカーブ）。クリップが変わったときだけ読み直す。
        private readonly Dictionary<EditorCurveBinding, float> _values = new Dictionary<EditorCurveBinding, float>();
        private AnimationClip _valuesOf;
        private int _valuesDirtyCount = -1;

        private void InvalidateValues()
        {
            _valuesOf = null;
        }

        private Dictionary<EditorCurveBinding, float> Values()
        {
            var clip = TargetClip;
            var dirty = clip != null ? EditorUtility.GetDirtyCount(clip) : 0;
            if (_valuesOf == clip && _valuesDirtyCount == dirty && clip != null) return _values;

            _values.Clear();
            _valuesOf = clip;
            _valuesDirtyCount = dirty;
            if (clip == null) return _values;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (ClipEditing.TryGetFloat(clip, binding, out var value)) _values[binding] = value;
            }
            return _values;
        }

        private static string PathOf(Transform transform, Transform root) => AnimationUtility.CalculateTransformPath(transform, root);

        private VRCAvatarDescriptor Descriptor => AvatarRoot != null ? AvatarRoot.GetComponent<VRCAvatarDescriptor>() : null;
    }
}
