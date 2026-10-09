using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    /// <summary>
    /// 「新しく始める」画面。アバターのプレハブ（Hierarchy のアバターでも）をドロップして、表情データと表情設定のプレハブを作る。
    /// - 表情データ：新しく作る（元FXのジェスチャーの表情を使うか、使わずに自分で作るか。アニメーションのフォルダも入れられる）か、
    ///   ほかの顔のアバターと共有する。元の表情を使うときは、パーツらしい元FXのレイヤー（頬・涙など）もパーツとして取り込む
    /// - 顔が元のプレハブと違えば、その顔を顔バリアントにする（共有するときは、同じ顔の顔バリアントがあればそれを使う）
    /// - できた表情設定のプレハブは、使うアバターの中へ入れる（Hierarchy のアバターをドロップしたときは、そのアバターに入れられる）
    /// メニューから開いたときは「アバターを選ぶ」画面になり、選んだアバターの表情設定が1つだけならそのまま開く。
    /// </summary>
    internal partial class ExpressionEditorWindow
    {
        private const string SetupSuffix = "_FEE";
        private const string DataSuffix = "_表情データ";

        [SerializeField] private bool _starting;
        // メニューから開いて、アバターを選んでいるところか（選んだアバターの表情設定があれば開く）。
        [SerializeField] private bool _selectingAvatar;
        [SerializeField] private GameObject _startAvatar;
        [SerializeField] private bool _startShare;
        [SerializeField] private ExpressionSet _startSharedSet;
        [SerializeField] private bool _startImportFx = true;
        [SerializeField] private List<DefaultAsset> _startFolders = new List<DefaultAsset>();
        [SerializeField] private List<AnimationClip> _startClips = new List<AnimationClip>();
        [SerializeField] private string _startName = "";
        [SerializeField] private string _startDataFolder = "";
        [SerializeField] private bool _startPlaceInAvatar = true;

        private Vector2 _startScroll;

        // ドロップしたアバターを調べた結果。アバターか共有する表情データが変わったときだけ調べ直す。
        private sealed class StartInfo
        {
            public GameObject Avatar;
            public ExpressionSet SharedSet;
            public List<string> GestureLayers = new List<string>();
            public int GestureClips;
            // パーツとして取り込む元FXのレイヤーと、そのクリップの数。
            public List<string> PartLayers = new List<string>();
            public int PartClips;
            // 元のプレハブとの違い（ベース顔になるもの）と、比べた元のプレハブ。
            public List<BaseFaceKey> FaceDifferences = new List<BaseFaceKey>();
            public string OriginalName;
            // 表情データの候補（このアバターで動くものが多い順）。
            public List<ExpressionSet> Sets = new List<ExpressionSet>();
            // 共有するとき：同じ顔の顔バリアントと、同じ表情データと顔の表情設定のプレハブ。
            public FaceVariant MatchingVariant;
            public FacialExpressionAvatar ExistingSetup;
        }

        private StartInfo _startInfo;

        /// <summary>
        /// 「新しく始める」画面を出す。avatar を渡すと、そのアバターをドロップした状態から始める。
        /// </summary>
        private void BeginStart(GameObject avatar)
        {
            _starting = true;
            _selectingAvatar = false;
            if (avatar != null) SetStartAvatar(avatar);
            GUI.FocusControl(null);
        }

        /// <summary>
        /// 「アバターを選ぶ」画面を出す（メニューから開いたとき）。
        /// </summary>
        private void BeginSelectAvatar()
        {
            _starting = true;
            _selectingAvatar = true;
            _startAvatar = null;
            _startInfo = null;
            GUI.FocusControl(null);
        }

        /// <summary>
        /// アバターの表情設定。アバターの中に入っているものと、そのアバター（の元のプレハブ）を編集に使うプロジェクトのプレハブ。
        /// </summary>
        private List<FacialExpressionAvatar> SetupsFor(GameObject avatar)
        {
            var result = avatar.GetComponentsInChildren<FacialExpressionAvatar>(true).ToList();
            var prefab = AvatarSetup.PrefabOf(avatar);
            result.AddRange(Setups.Where(s => !AvatarSetup.IsInScene(s) && !result.Contains(s) && s.sourceAvatar != null &&
                                              (s.sourceAvatar == avatar || s.sourceAvatar == prefab)));
            return result;
        }

        private void SetStartAvatar(GameObject avatar)
        {
            // アバターを選んでいるときは、そのアバターの表情設定が1つに決まればそのまま開く（中に入っているものを優先）。
            if (_selectingAvatar && avatar != null)
            {
                var inside = avatar.GetComponentsInChildren<FacialExpressionAvatar>(true);
                var setups = inside.Length > 0 ? inside.ToList() : SetupsFor(avatar);
                if (setups.Count == 1 && !FaceVariantUtility.SourceFaceChanged(setups[0].faceVariant, setups[0].expressionSet, avatar))
                {
                    SetAvatar(setups[0]);
                    return;
                }
            }

            _startAvatar = avatar;
            _startInfo = null;
            if (avatar == null) return;

            _startName = avatar.name;
            _startDataFolder = AvatarSetup.DefaultDataFolder(avatar.name);
            _startFolders.Clear();
            _startClips.Clear();
            _targetsAvatar = null;
            _startPlaceInAvatar = true;
            _startShare = false;
            _startSharedSet = null;

            // 同じ素体の表情データがあれば、共有するのを標準にする。
            var info = AnalyzeStart();
            _startImportFx = false;
            _startShare = false;
            _startSharedSet = null;
            _startInfo = null;
        }

        private StartInfo AnalyzeStart()
        {
            var avatar = _startAvatar;
            var shared = _startShare ? _startSharedSet : null;
            if (_startInfo != null && _startInfo.Avatar == avatar && _startInfo.SharedSet == shared) return _startInfo;

            var info = new StartInfo { Avatar = avatar, SharedSet = shared };
            _startInfo = info;
            if (avatar == null) return info;

            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            (info.GestureLayers, info.GestureClips) = FxImporter.PeekGestures(descriptor);

            // パーツらしいレイヤーは、まだ何も取り込んでいない表情データで調べる（ジェスチャーのレイヤーはパーツにならない）。
            var empty = CreateInstance<ExpressionSet>();
            try
            {
                var analysis = AnalyzeFx(descriptor, empty);
                info.PartLayers = analysis.PartCandidates;
                info.PartClips = info.PartLayers.Sum(l => analysis.FaceLayerClips[l].Count);
            }
            finally
            {
                DestroyImmediate(empty);
            }

            info.FaceDifferences = FaceVariantUtility.CompareWithOriginal(shared, avatar, null, out _).Where(k => k.enabled).ToList();
            var face = AvatarSetup.FaceRenderer(avatar);
            var original = face != null ? FaceVariantUtility.OriginalRenderer(face) : null;
            info.OriginalName = original != null ? Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(original)) : null;

            info.Sets = AssetDatabase.FindAssets("t:" + nameof(ExpressionSet))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<ExpressionSet>)
                .Where(s => s != null)
                .OrderByDescending(s => MatchRatio(s, avatar))
                .ThenBy(s => s.name)
                .ToList();

            if (shared != null)
            {
                info.MatchingVariant = info.FaceDifferences.Count == 0 ? null : VariantsOf(shared).FirstOrDefault(v => SameFace(v, info.FaceDifferences));
                var variant = info.FaceDifferences.Count == 0 ? null : info.MatchingVariant;
                if (info.FaceDifferences.Count == 0 || variant != null)
                {
                    info.ExistingSetup = Setups.FirstOrDefault(s => !AvatarSetup.IsInScene(s) && s.expressionSet == shared && s.faceVariant == variant);
                }
            }
            return info;
        }

        /// <summary>
        /// 表情データのクリップが動かすシェイプキーのうち、このアバターにあるものの割合（同じ素体かどうかの目安）。
        /// </summary>
        private static float MatchRatio(ExpressionSet set, GameObject avatar)
        {
            var animated = FaceVariantUtility.AnimatedBlendShapes(set);
            if (animated.Count == 0) return 0;

            var found = 0;
            foreach (var group in animated.GroupBy(a => a.path))
            {
                var transform = string.IsNullOrEmpty(group.Key) ? avatar.transform : avatar.transform.Find(group.Key);
                var mesh = transform != null ? transform.GetComponent<SkinnedMeshRenderer>()?.sharedMesh : null;
                if (mesh != null) found += group.Count(a => mesh.GetBlendShapeIndex(a.blendShape) >= 0);
            }
            return (float)found / animated.Count;
        }

        private static IEnumerable<FaceVariant> VariantsOf(ExpressionSet set)
        {
            return AssetDatabase.FindAssets("t:" + nameof(FaceVariant), new[] { AssetPathUtility.FolderOf(set) })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<FaceVariant>)
                .Where(v => v != null);
        }

        // 顔バリアントのベース顔が、この違いと同じか（同じ顔か）。
        private static bool SameFace(FaceVariant variant, List<BaseFaceKey> differences)
        {
            var keys = variant.baseFace.Where(k => k.enabled).ToList();
            if (keys.Count != differences.Count) return false;
            return differences.All(d => keys.Any(k => k.path == d.path && k.blendShape == d.blendShape && Mathf.Abs(k.variantValue - d.variantValue) < 0.01f));
        }

        // ---- 画面 ----

        private void DrawStart()
        {
            _startScroll = EditorGUILayout.BeginScrollView(_startScroll);
            using (new EditorGUILayout.VerticalScope(GUILayout.MaxWidth(680)))
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField(_selectingAvatar ? "アバターを選ぶ" : "新しく始める", EditorStyles.boldLabel);
                DrawStartAvatar();

                if (_startAvatar == null)
                {
                    DrawSceneAvatars();
                    DrawOpenExisting();
                }
                else
                {
                    DrawSetupsOfAvatar();
                    var info = AnalyzeStart();
                    EditorGUILayout.Space(8);
                    DrawStartFace(info);
                    EditorGUILayout.Space(8);
                    DrawStartData(info);
                    EditorGUILayout.Space(8);
                    DrawStartButtons(info);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawStartAvatar()
        {
            var rect = GUILayoutUtility.GetRect(0, 56, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.15f));
            GUI.Label(rect, _startAvatar == null
                    ? "アバターのプレハブをここにドロップ（Hierarchy のアバターでも）"
                    : "別のアバターにするときは、ここにドロップ",
                DropHint);

            var e = Event.current;
            if ((e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && rect.Contains(e.mousePosition))
            {
                var avatar = DragAndDrop.objectReferences.Select(AvatarSetup.FindAvatarRoot).FirstOrDefault(a => a != null);
                DragAndDrop.visualMode = avatar != null ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;
                if (e.type == EventType.DragPerform && avatar != null)
                {
                    DragAndDrop.AcceptDrag();
                    SetStartAvatar(avatar);
                }
                e.Use();
            }

            EditorGUI.BeginChangeCheck();
            var picked = (GameObject)EditorGUILayout.ObjectField("アバター", _startAvatar, typeof(GameObject), true);
            if (EditorGUI.EndChangeCheck())
            {
                var avatar = AvatarSetup.FindAvatarRoot(picked);
                if (picked != null && avatar == null) ShowNotification(new GUIContent("VRC Avatar Descriptor のあるアバターを選んでください"), 3);
                else SetStartAvatar(avatar);
            }
        }

        /// <summary>
        /// 開いているシーンのアバター。選ぶと、そのアバターで始める（表情設定があれば開く）。
        /// </summary>
        private void DrawSceneAvatars()
        {
            var avatars = FindObjectsByType<VRCAvatarDescriptor>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(d => AvatarSetup.IsInScene(d) && (d.gameObject.hideFlags & HideFlags.HideInHierarchy) == 0 &&
                            !d.gameObject.scene.name.StartsWith("___"))
                .OrderBy(d => d.gameObject.scene.name)
                .ThenBy(d => d.name)
                .ToList();
            if (avatars.Count == 0) return;

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("シーンのアバター", EditorStyles.boldLabel);
            foreach (var descriptor in avatars)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    var hasSetup = descriptor.GetComponentInChildren<FacialExpressionAvatar>(true) != null;
                    EditorGUILayout.LabelField($"{descriptor.name}（{descriptor.gameObject.scene.name}）{(hasSetup ? "　表情設定あり" : "")}");
                    if (GUILayout.Button(hasSetup ? "開く" : "選ぶ", GUILayout.Width(60)))
                    {
                        SetStartAvatar(descriptor.gameObject);
                        GUIUtility.ExitGUI();
                    }
                }
            }
        }

        /// <summary>
        /// 選んだアバターに表情設定が既にあれば、それを開けるようにする（新しく作ることもできる）。
        /// </summary>
        private void DrawSetupsOfAvatar()
        {
            var setups = SetupsFor(_startAvatar);
            if (setups.Count == 0) return;

            EditorGUILayout.Space(8);
            if (setups.Any(s => FaceVariantUtility.SourceFaceChanged(s.faceVariant, s.expressionSet, _startAvatar)))
                EditorGUILayout.HelpBox("読み込み時からベース顔が変わっています。下の設定から、この顔用の表情設定を新しく作成してください。以前の設定は残ります。", MessageType.Info);
            EditorGUILayout.LabelField("このアバターの表情設定", EditorStyles.boldLabel);
            foreach (var setup in setups)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(AvatarSetup.DisplayName(setup));
                    if (GUILayout.Button("開く", GUILayout.Width(60)))
                    {
                        SetAvatar(setup);
                        GUIUtility.ExitGUI();
                    }
                }
            }
            EditorGUILayout.LabelField("新しく作るときは、下で設定して「始める」を押してください。", EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>
        /// 作った表情設定のプレハブを開く（アバターをまだ選んでいないとき）。
        /// </summary>
        private void DrawOpenExisting()
        {
            var setups = Setups.Where(s => !AvatarSetup.IsInScene(s)).ToList();
            if (setups.Count == 0)
            {
                if (_avatar != null && GUILayout.Button("戻る", GUILayout.Width(100))) _starting = false;
                return;
            }

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("表情設定のプレハブ", EditorStyles.boldLabel);
            foreach (var setup in setups)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(AvatarSetup.DisplayName(setup));
                    if (GUILayout.Button("開く", GUILayout.Width(60))) SetAvatar(setup);
                }
            }
            if (_avatar != null && GUILayout.Button("戻る", GUILayout.Width(100))) _starting = false;
        }

        private void DrawStartFace(StartInfo info)
        {
            EditorGUILayout.LabelField("顔", EditorStyles.boldLabel);
            string text;
            if (info.OriginalName == null)
            {
                text = "元のプレハブが見つからないため、顔の違いを調べられません（このままの顔で始めます）。";
            }
            else if (info.FaceDifferences.Count == 0)
            {
                text = $"元のプレハブ（{info.OriginalName}）と同じ顔です。";
            }
            else
            {
                var shown = string.Join("、", info.FaceDifferences.Take(4).Select(k => $"{k.blendShape} {k.referenceValue:0.#}→{k.variantValue:0.#}"));
                var more = info.FaceDifferences.Count > 4 ? $" ほか {info.FaceDifferences.Count - 4} 件" : "";
                text = $"元のプレハブ（{info.OriginalName}）との違い：{shown}{more}。\n" +
                       "この顔をベース顔として保存し、複製した表情に適用します。";
            }
            EditorGUILayout.LabelField(text, EditorStyles.wordWrappedLabel);
        }

        private void DrawStartData(StartInfo info)
        {
            _startShare = false;
            EditorGUILayout.LabelField("このベース顔専用の表情データ", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("シーン上の顔から始め、取り込んだ表情は編集用に複製します。他の顔の表情は変更しません。", MessageType.Info);
            DrawStartTargets();
            DrawStartNew(info);
        }

        private void DrawStartNew(StartInfo info)
        {
            EditorGUILayout.Space(4);
            _startImportFx = false;
            EditorGUILayout.LabelField("取り込む表情ファイル", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("クリップ／フォルダを指定します。この顔専用に複製して一覧へ取り込み、目口の合成・左右への割り当て・Addへの登録を行います。FXからの自動割り当ては行いません。", MessageType.Info);
            foreach (var clip in _startClips.ToList())
                using (new EditorGUILayout.HorizontalScope())
                { EditorGUILayout.ObjectField(clip, typeof(AnimationClip), false); if (GUILayout.Button("×", GUILayout.Width(22))) _startClips.Remove(clip); }
            var addedClip = (AnimationClip)EditorGUILayout.ObjectField("クリップを追加", null, typeof(AnimationClip), false);
            if (addedClip != null && !_startClips.Contains(addedClip)) _startClips.Add(addedClip);
            var drop = GUILayoutUtility.GetRect(0, 34, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "複数のクリップ／フォルダをここにドロップ");
            foreach (var source in AcceptExpressionSources(drop))
            {
                if (source is AnimationClip c && !_startClips.Contains(c)) _startClips.Add(c);
                if (source is DefaultAsset folder && !_startFolders.Contains(folder)) _startFolders.Add(folder);
            }
            DrawStartFolders();

            EditorGUILayout.Space(4);
            _startName = EditorGUILayout.TextField("名前", _startName);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("保存先", _startDataFolder);
                if (GUILayout.Button("変更…", GUILayout.Width(60)))
                {
                    var folder = PickProjectFolder("表情データの保存先");
                    if (folder != null) _startDataFolder = folder;
                }
            }
        }

        private void DrawStartFolders()
        {
            for (var i = 0; i < _startFolders.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.ObjectField(_startFolders[i], typeof(DefaultAsset), false);
                    }
                    if (GUILayout.Button("×", GUILayout.Width(22)))
                    {
                        _startFolders.RemoveAt(i);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            var rect = GUILayoutUtility.GetRect(0, 34, GUILayout.ExpandWidth(true));
            rect = EditorGUI.IndentedRect(rect);
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.12f));
            GUI.Label(rect, "フォルダをここにドロップ", DropHint);
            foreach (var folder in AcceptFolders(rect))
            {
                if (!_startFolders.Contains(folder)) _startFolders.Add(folder);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15);
                if (GUILayout.Button("フォルダを追加…", GUILayout.Width(120)))
                {
                    var path = PickProjectFolder("ライブラリに入れるフォルダ");
                    var folder = path != null ? AssetDatabase.LoadAssetAtPath<DefaultAsset>(path) : null;
                    if (folder != null && !_startFolders.Contains(folder)) _startFolders.Add(folder);
                }
            }
        }

        private void DrawStartShared(StartInfo info)
        {
            EditorGUILayout.Space(4);
            string text;
            if (info.FaceDifferences.Count == 0) text = "元の顔なので、顔バリアントは使いません。";
            else if (info.MatchingVariant != null) text = $"同じ顔の顔バリアント「{info.MatchingVariant.name}」を使います。";
            else text = "この顔の顔バリアントを新しく作ります。";
            EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);
            if (info.FaceDifferences.Count > 0 && info.MatchingVariant == null)
            {
                _startName = EditorGUILayout.TextField("顔バリアントの名前", _startName);
            }

            if (info.ExistingSetup != null)
            {
                EditorGUILayout.HelpBox($"同じ表情データと顔の表情設定「{info.ExistingSetup.gameObject.name}」が既にあります。" +
                                        "このアバターにも、そのプレハブを入れて使えます。", MessageType.Info);
            }
        }

        private void DrawStartButtons(StartInfo info)
        {
            var inScene = AvatarSetup.IsInScene(_startAvatar);
            var current = inScene ? _startAvatar.GetComponentInChildren<FacialExpressionAvatar>(true) : null;
            if (current != null)
            {
                EditorGUILayout.LabelField($"このアバターには表情設定「{current.gameObject.name}」が既に入っています。入れ替えるときは、" +
                                           "古い表情設定を外してから、できたプレハブを入れてください。", EditorStyles.wordWrappedMiniLabel);
            }
            else if (inScene)
            {
                _startPlaceInAvatar = EditorGUILayout.ToggleLeft($"表情設定を、このアバター（{_startAvatar.name}）の中に入れる", _startPlaceInAvatar);
            }
            else
            {
                EditorGUILayout.LabelField("できた表情設定のプレハブを、使うアバターの中へドラッグしてください（ツールバーの「プレハブ」からもドラッグできます）。",
                    EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                var existing = _startShare ? info.ExistingSetup : null;
                var invalid = _startShare ? _startSharedSet == null : string.IsNullOrWhiteSpace(_startName) || string.IsNullOrEmpty(_startDataFolder);
                using (new EditorGUI.DisabledScope(invalid))
                {
                    if (GUILayout.Button(existing != null ? "既にある表情設定を使う" : "始める", GUILayout.Height(28), GUILayout.Width(200)))
                    {
                        if (existing != null) UseExistingSetup(existing);
                        else StartSetup(info);
                        GUIUtility.ExitGUI();
                    }
                }
                if (GUILayout.Button("やめる", GUILayout.Height(28), GUILayout.Width(100)))
                {
                    _startAvatar = null;
                    _startInfo = null;
                    if (_avatar != null) _starting = false;
                }
            }
        }

        // ラジオボタン（選ばれていないものを押したときだけ true）。
        private static bool Radio(bool on, string label, params GUILayoutOption[] options)
        {
            var rect = EditorGUI.IndentedRect(EditorGUILayout.GetControlRect(options));
            return GUI.Toggle(rect, on, label, EditorStyles.radioButton) && !on;
        }

        // ---- 作る ----

        private void StartSetup(StartInfo info)
        {
            var avatar = _startAvatar;
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            var name = _startName.Trim();
            ExpressionSet set;
            FaceVariant variant = null;
            string folder;
            FxImporter.Result imported = null;

            if (_startShare)
            {
                set = _startSharedSet;
                folder = AssetPathUtility.FolderOf(set);
                if (info.FaceDifferences.Count > 0) variant = info.MatchingVariant ?? FaceVariantUtility.Create(set, avatar, name);
            }
            else
            {
                var parent = EnsureProjectFolder(_startDataFolder);
                var uniqueFolder = AssetDatabase.GenerateUniqueAssetPath($"{parent}/{AssetPathUtility.SafeFileName(name)}");
                folder = EnsureProjectFolder(uniqueFolder);
                set = CreateInstance<ExpressionSet>();
                set.independentClips = true;
                set.motionRulesConfigured = true;
                set.previewHeight = _startPreviewHeight; set.previewZoom = _startPreviewZoom;
                set.faceMeshPaths = _startMeshPaths.ToList();
                set.linkedObjectPaths = _startObjectPaths.ToList();
                set.lipSyncMeshPath = _startLipMesh;
                set.explicitLayerSelection = true;
                set.importLayerNames = _startLayerNames.ToList();
                set.explicitMenuSelection = true;
                set.replacedMenuItems = _startRemovedMenus.ToList();
                AssetDatabase.CreateAsset(set, AssetDatabase.GenerateUniqueAssetPath($"{folder}/{AssetPathUtility.SafeFileName(name)}{DataSuffix}.asset"));
                if (_startImportFx && info.GestureClips > 0)
                {
                    imported = FxImporter.ImportGestures(descriptor, set);
                }
                else
                {
                    set.gestureSets.Add(new GestureSet { name = GestureSet.DefaultName });
                    set.faceControlImported = true;
                    set.originalGestureLayers = _startLayerNames.ToList();
                    ExpressionSetUtility.AddMenuFromSets(set);
                    set.menu.Add(new MenuNode { kind = MenuNodeKind.Folder, name = ExpressionSetUtility.AddFolderName });
                }
                if (_startImportFx)
                {
                    foreach (var layer in AnalyzeFx(descriptor, set).PartCandidates.Where(set.importLayerNames.Contains)) FxImporter.ImportPartsFromLayer(descriptor, set, layer);
                }
                foreach (var part in set.parts.Where(p => !ExpressionSetUtility.OwnsPartClip(set, p.clip)))
                    ExpressionSetUtility.MakePartClipEditable(set, part);
                foreach (var library in _startFolders.Where(f => f != null)) FxImporter.AddFolder(set, library);
                ExpressionFileImporter.Import(set, _startClips.Cast<Object>().Concat(_startFolders));
                EditorUtility.SetDirty(set);

                // 取り込んだ表情で動くシェイプキーも含めて、元のプレハブと違えば顔バリアントにする。
                var differences = FaceVariantUtility.CompareWithOriginal(set, avatar, null, out _);
                variant = FaceVariantUtility.Create(set, avatar, name + "_ベース顔");
                variant.useCapturedValues = true;
                EditorUtility.SetDirty(variant);
            }
            AssetDatabase.SaveAssets();

            var prefab = AvatarSetup.CreateSetupPrefab(folder, name + SetupSuffix, set, variant, SourceAvatarFor(avatar, set, variant));
            OpenCreatedSetup(prefab, avatar);
            ShowNotification(new GUIContent(imported != null
                ? $"表情設定「{prefab.gameObject.name}」を作り、元FXから {imported.AddedExpressions} 個の表情を取り込みました"
                : $"表情設定「{prefab.gameObject.name}」を作りました"), 4);
        }

        /// <summary>
        /// 元FXのうち、表情データで置き換えていない顔のレイヤー（パーツの候補など）を調べる。
        /// </summary>
        private static OriginalFxAnalysis AnalyzeFx(VRCAvatarDescriptor descriptor, ExpressionSet set)
        {
            var defaults = AvatarFaceDefaults.Find(descriptor, set);
            var handled = defaults.MouthCancelerLayers.ToList();
            if (defaults.BlinkLayer != null) handled.Add(defaults.BlinkLayer);
            return OriginalFxAnalysis.Analyze(descriptor, set, null, handled);
        }

        private void UseExistingSetup(FacialExpressionAvatar prefab)
        {
            OpenCreatedSetup(prefab, _startAvatar);
        }

        /// <summary>
        /// 作った（または既にある）表情設定を開く。Hierarchy のアバターから始めたときは、そのアバターの中に入れる。
        /// </summary>
        private void OpenCreatedSetup(FacialExpressionAvatar prefab, GameObject avatar)
        {
            var target = prefab;
            if (AvatarSetup.IsInScene(avatar) && _startPlaceInAvatar)
            {
                var existing = avatar.GetComponentsInChildren<FacialExpressionAvatar>(true);
                if (existing.Length == 0) target = AvatarSetup.PlaceInAvatar(prefab, avatar);
                else if (existing.Length == 1)
                {
                    // 同時に二つ生成せず、シーンの参照だけ新設定へ切り替える。旧アセットは保持する。
                    target = existing[0];
                    Undo.RecordObject(target, "新しいベース顔の表情設定に切り替え");
                    target.expressionSet = prefab.expressionSet;
                    target.faceVariant = prefab.faceVariant;
                    EditorUtility.SetDirty(target);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                }
            }

            _startAvatar = null;
            _startInfo = null;
            _setups = null;
            _avatar = null;
            SetAvatar(target);
            EditorGUIUtility.PingObject(prefab.gameObject);
        }

        /// <summary>
        /// 表情設定に覚えておく、編集に使うアバター（プレハブ）。シーンのアバターなら元のプレハブだが、
        /// 顔が違う（シーンで顔を変えている）ときは、プレビューの顔が変わってしまうので覚えない。
        /// </summary>
        private static GameObject SourceAvatarFor(GameObject avatar, ExpressionSet set, FaceVariant variant)
        {
            var prefab = AvatarSetup.PrefabOf(avatar);
            if (prefab == null || prefab == avatar) return prefab;

            var sceneFace = FaceVariantUtility.CompareWithOriginal(set, avatar, null, out _).Where(k => k.enabled).ToList();
            var prefabFace = FaceVariantUtility.CompareWithOriginal(set, prefab, null, out _).Where(k => k.enabled).ToList();
            var same = sceneFace.Count == prefabFace.Count &&
                       sceneFace.All(a => prefabFace.Any(b => a.path == b.path && a.blendShape == b.blendShape && Mathf.Abs(a.variantValue - b.variantValue) < 0.01f));
            return same ? prefab : null;
        }

        // ---- 編集に使うアバターが無い表情設定 ----

        private void DrawMissingAvatar()
        {
            EditorGUILayout.HelpBox("この表情設定には、プレビューと元FXの読み取りに使うアバターがありません。" +
                                    "アバターのプレハブをドロップするか、アバターの中に入れた表情設定を開いてください。", MessageType.Info);
            var rect = GUILayoutUtility.GetRect(0, 56, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) EditorGUI.DrawRect(rect, new Color(0, 0, 0, 0.15f));
            GUI.Label(rect, "アバターのプレハブをここにドロップ", DropHint);

            var e = Event.current;
            if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !rect.Contains(e.mousePosition)) return;
            var avatar = DragAndDrop.objectReferences.Select(AvatarSetup.FindAvatarRoot).FirstOrDefault(a => a != null && EditorUtility.IsPersistent(a));
            DragAndDrop.visualMode = avatar != null ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
            if (e.type == EventType.DragPerform && avatar != null)
            {
                DragAndDrop.AcceptDrag();
                Undo.RecordObject(_avatar, "編集に使うアバターを設定");
                _avatar.sourceAvatar = avatar;
                EditorUtility.SetDirty(_avatar);
                AssetDatabase.SaveAssets();
                DisposePreview();
            }
            e.Use();
        }

        // ---- フォルダ ----

        /// <summary>
        /// ライブラリにフォルダをドロップしたら、ライブラリに追加する。
        /// </summary>
        private void AcceptFolderDrop(Rect rect, ExpressionSet set)
        {
            var sources = AcceptExpressionSources(rect).ToList();
            if (sources.Count > 0) ImportExpressionSources(set, sources);
        }

        /// <summary>
        /// rect にドロップされたフォルダ（Project のフォルダ）。ドロップされたときだけ返す。
        /// </summary>
        private static IEnumerable<DefaultAsset> AcceptFolders(Rect rect)
        {
            var e = Event.current;
            if ((e.type != EventType.DragUpdated && e.type != EventType.DragPerform) || !rect.Contains(e.mousePosition)) yield break;

            var folders = DragAndDrop.objectReferences.OfType<DefaultAsset>()
                .Where(f => AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(f)))
                .ToList();
            if (folders.Count == 0) yield break;

            DragAndDrop.visualMode = DragAndDropVisualMode.Link;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                e.Use();
                foreach (var folder in folders) yield return folder;
                yield break;
            }
            e.Use();
        }

        /// <summary>
        /// プロジェクトの中のフォルダを選ぶ。Assets/ からのパスを返す（選ばなければ null）。
        /// </summary>
        private static string PickProjectFolder(string title)
        {
            var absolute = EditorUtility.OpenFolderPanel(title, "Assets", "");
            if (string.IsNullOrEmpty(absolute)) return null;

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            absolute = Path.GetFullPath(absolute).Replace('\\', '/');
            if (!absolute.StartsWith(projectRoot + "/Assets"))
            {
                EditorUtility.DisplayDialog(title, "プロジェクトの Assets の中のフォルダを選んでください。", "OK");
                return null;
            }
            return absolute.Substring(projectRoot.Length + 1);
        }

        // Assets/ からのパスのフォルダを、無ければ作る。
        private static string EnsureProjectFolder(string path)
        {
            var parts = path.Split('/');
            return AssetPathUtility.EnsureFolder(parts[0], parts.Skip(1).ToArray());
        }
    }
}
