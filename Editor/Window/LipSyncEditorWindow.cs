using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Samon.FacialExpressionEditor.Editor
{
    internal sealed class LipSyncEditorWindow : EditorWindow
    {
        [SerializeField] private FacialExpressionAvatar owner;
        [SerializeField] private int targetIndex, patternIndex=-1;
        private Vector2 scroll, targetScroll;
        internal Rect PreviewRect { get; private set; }
        private Texture2D photo;
        private bool photoDirty = true;
        private bool details, advanced;
        private string error;
        private LipSyncSwitchSettings Config=>owner.lipSyncSwitch;
        public static void Open(FacialExpressionAvatar avatar)
        {
            var window=GetWindow<LipSyncEditorWindow>("リップシンク設定");
            window.ClearPhoto();window.owner=avatar;window.scroll=Vector2.zero;window.minSize=new Vector2(960,680);window.Show();
            LipSyncProfileUtility.EnsureDraft(avatar);
        }
        private void OnEnable()=>Undo.undoRedoPerformed+=Refresh;
        private void OnDisable(){Undo.undoRedoPerformed-=Refresh;ClearPhoto();}
        private void Refresh(){ClearPhoto();Repaint();}
        private void ClearPhoto(){if(photo)DestroyImmediate(photo);photo=null;photoDirty=true;}
        // Coalesce field changes outside the IMGUI layout pass.
        private void Update()
        {
            if(!photoDirty||!owner||!Config.enabled||patternIndex>=0||targetIndex<0||targetIndex>=Config.targets.Count||EditorApplication.isPlayingOrWillChangePlaymode)return;
            var root=AvatarSetup.AvatarRootOf(owner);var target=Config.targets[targetIndex];
            if(!root||!target.mesh)return;
            photoDirty=false;
            Try(()=>{
                var next=LipSyncPortraits.Capture(root,target);
                if(photo)DestroyImmediate(photo);
                photo=next;
            });
        }
        private void Edit(string name,Action action){Undo.RecordObject(owner,name);action();EditorUtility.SetDirty(owner);PrefabUtility.RecordPrefabInstancePropertyModifications(owner);}
        private static void Field(SerializedProperty p,string name,string label)=>EditorGUILayout.PropertyField(p.FindPropertyRelative(name),new GUIContent(label),GUILayout.MinWidth(0));
        private void OnGUI()
        {
            EditorGUIUtility.wideMode=true;EditorGUIUtility.labelWidth=140;EditorGUIUtility.fieldWidth=40;
            EditorGUI.BeginChangeCheck();
            owner=(FacialExpressionAvatar)EditorGUILayout.ObjectField("アバター設定",owner,typeof(FacialExpressionAvatar),true);
            if(EditorGUI.EndChangeCheck()){targetIndex=0;patternIndex=-1;ClearPhoto();}
            if(!owner){EditorGUILayout.HelpBox("アバターの表情設定を指定してください。",MessageType.Info);return;}
            LipSyncProfileUtility.EnsureDraft(owner);
            LipSyncSettingsGUI.DrawStorage(owner);
            var root=AvatarSetup.AvatarRootOf(owner);
            if(Config.schemaVersion<4)Edit("リップシンク設定を更新",()=>LipSyncSetup.Upgrade(Config,root?root.GetComponent<VRCAvatarDescriptor>():null));
            var so=new SerializedObject(owner);so.Update();var settings=so.FindProperty("lipSyncSwitch");
            Field(settings,"enabled","リップシンク切り替えを使用");so.ApplyModifiedProperties();
            if(!Config.enabled){EditorGUILayout.HelpBox("ONにすると、対象・発話パターン・メニューを設定できます。",MessageType.Info);return;}
            if(!string.IsNullOrEmpty(error))EditorGUILayout.HelpBox(error,MessageType.Error);
            using(var view=new EditorGUILayout.ScrollViewScope(scroll)) {
                scroll=view.scrollPosition;
                EditorGUILayout.LabelField("1  対象を登録",EditorStyles.boldLabel);
                using(var strip=new EditorGUILayout.ScrollViewScope(targetScroll,GUILayout.Height(125))) {
                    targetScroll=strip.scrollPosition;
                    using(new EditorGUILayout.HorizontalScope()) {
                        for(int i=0;i<Config.targets.Count;i++)if(Card(Config.targets[i].label,Config.targets[i].icon,patternIndex<0&&targetIndex==i)) {targetIndex=i;patternIndex=-1;ClearPhoto();}
                        using(new EditorGUI.DisabledScope(Config.targets.Count>=16))if(GUILayout.Button("＋ 対象",GUILayout.Width(90),GUILayout.Height(95))) {
                            Edit("対象を追加",()=>{if(Config.targets.Count>0&&Config.defaultSelection>=Config.targets.Count+1)Config.defaultSelection++;Config.targets.Add(new LipSyncTarget{label="Target "+(Config.targets.Count+1),followExpressions=Config.targets.Count==0});});
                            targetIndex=Config.targets.Count-1;patternIndex=-1;ClearPhoto();GUIUtility.ExitGUI();
                        }
                    }
                }
                using(new EditorGUILayout.HorizontalScope()) {
                    using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox,GUILayout.Width(190))) { DrawPatternList(); }
                    using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox,GUILayout.MinWidth(360))) {
                        if(patternIndex>=0&&patternIndex<Config.patterns.Count)DrawPattern(so,settings);
                        else if(Config.targets.Count>0){targetIndex=Mathf.Clamp(targetIndex,0,Config.targets.Count-1);DrawTarget(so,settings,root);}
                        else EditorGUILayout.HelpBox("「＋ 対象」で顔メッシュを登録してください。",MessageType.Info);
                    }
                    using(new EditorGUILayout.VerticalScope(EditorStyles.helpBox,GUILayout.Width(240))) { DrawMenu(so,settings); }
                }
                so.ApplyModifiedProperties();
                advanced=EditorGUILayout.Foldout(advanced,"旧版からの取り込み",true);
                if(advanced){var legacy=LipSyncSwitchMigration.Find(root);using(new EditorGUI.DisabledScope(!legacy))if(GUILayout.Button("単体版から取り込む（現在の設定を上書き）")){LipSyncSwitchMigration.Import(owner,legacy);GUIUtility.ExitGUI();}}
            }
        }
        private static bool Card(string label,Texture image,bool selected)
        {
            var old=GUI.backgroundColor;if(selected)GUI.backgroundColor=new Color(.35f,.75f,1);
            bool clicked=GUILayout.Button(new GUIContent((selected?"✓ ":"")+label,image),new GUIStyle(GUI.skin.button){imagePosition=ImagePosition.ImageAbove,wordWrap=true},GUILayout.Width(110),GUILayout.Height(95));
            GUI.backgroundColor=old;return clicked;
        }
        private void DrawPatternList()
        {
            EditorGUILayout.LabelField("2  発話パターン",EditorStyles.boldLabel);
            EditorGUILayout.LabelField("喋る顔を組み合わせる",EditorStyles.miniLabel);
            for(int i=0;i<Config.patterns.Count;i++)if(GUILayout.Button(new GUIContent((patternIndex==i?"● ":"")+Config.patterns[i].label,Config.patterns[i].icon),GUILayout.Height(40))){patternIndex=i;ClearPhoto();}
            using(new EditorGUI.DisabledScope(Config.targets.Count+Config.patterns.Count+1>=255))if(GUILayout.Button("＋ パターン")){Edit("パターンを追加",()=>Config.patterns.Add(new LipSyncPattern{label=UniqueName("Pattern")}));patternIndex=Config.patterns.Count-1;GUIUtility.ExitGUI();}
            EditorGUILayout.Space();EditorGUILayout.HelpBox("個別・Allは標準で用意されます。必要な項目だけメニューに表示できます。",MessageType.None);
        }
        private string UniqueName(string basis){var names=Config.targets.Select(t=>t.label).Concat(Config.patterns.Select(p=>p.label)).ToArray();int i=1;while(names.Contains(basis+" "+i))i++;return basis+" "+i;}
        private void DrawTarget(SerializedObject so,SerializedProperty settings,GameObject root)
        {
            var t=Config.targets[targetIndex];var p=settings.FindPropertyRelative("targets").GetArrayElementAtIndex(targetIndex);
            EditorGUILayout.LabelField("対象の設定",EditorStyles.boldLabel);Field(p,"label","名前");
            EditorGUI.BeginChangeCheck();
            using(new EditorGUI.DisabledScope(owner.lipSyncProfile!=null))Field(p,"mesh","顔メッシュ（Inspectorで指定）");
            if(EditorGUI.EndChangeCheck()){so.ApplyModifiedProperties();Edit("口形を自動検出",()=>t.visemes=LipSyncSetup.Detect(t.mesh));ClearPhoto();so.Update();}
            Field(p,"showInMenu","個別選択をメニューに表示");Field(p,"followExpressions","表情の口設定と連動");
            Field(p,"icon","メニューアイコン（差し替え可）");
            EditorGUILayout.Space();
            var rect=PreviewNavigation.Square(280);PreviewRect=rect;EditorGUI.DrawRect(rect,new Color(.16f,.17f,.19f));
            if(photo||t.icon)GUI.DrawTexture(rect,photo?photo:t.icon,ScaleMode.ScaleToFit,true);else GUI.Label(rect,"顔メッシュを指定してください",new GUIStyle(EditorStyles.centeredGreyMiniLabel){alignment=TextAnchor.MiddleCenter});
            var offset=t.photoOffset;var yaw=t.photoYaw;var pitch=t.photoPitch;var zoom=t.photoZoom;
            if(PreviewNavigation.Handle(rect,ref offset,ref yaw,ref pitch,ref zoom)){
                so.ApplyModifiedProperties();Edit("プレビューの構図",()=>{t.photoOffset=offset;t.photoYaw=yaw;t.photoPitch=pitch;t.photoZoom=zoom;});so.Update();photoDirty=true;Repaint();
            }
            EditorGUILayout.LabelField(PreviewNavigation.Hint,EditorStyles.wordWrappedMiniLabel);
            if(GUILayout.Button("構図をリセット")){so.ApplyModifiedProperties();Edit("構図をリセット",()=>{t.photoOffset=Vector3.zero;t.photoYaw=0;t.photoPitch=0;t.photoZoom=1;});so.Update();photoDirty=true;}
            EditorGUI.BeginChangeCheck();Field(p,"photoRoot","撮影範囲（空欄は自動）");Field(p,"photoFocus","撮影中心（空欄は自動）");Field(p,"photoOffset","位置調整");
            p.FindPropertyRelative("photoZoom").floatValue=EditorGUILayout.Slider("ズーム",p.FindPropertyRelative("photoZoom").floatValue,.2f,8);
            p.FindPropertyRelative("photoYaw").floatValue=EditorGUILayout.Slider("左右の角度",p.FindPropertyRelative("photoYaw").floatValue,-180,180);
            p.FindPropertyRelative("photoPitch").floatValue=EditorGUILayout.Slider("上下の角度",p.FindPropertyRelative("photoPitch").floatValue,-80,80);Field(p,"photoBackground","背景色（A=0で透過）");
            bool framingChanged=EditorGUI.EndChangeCheck();so.ApplyModifiedProperties();if(framingChanged)photoDirty=true;
            using(new EditorGUI.DisabledScope(!t.mesh||!root||EditorApplication.isPlayingOrWillChangePlaymode))
            using(new EditorGUILayout.HorizontalScope()) {
                if(GUILayout.Button("プレビューを更新"))photoDirty=true;
                using(new EditorGUI.DisabledScope(!photo||photoDirty))if(GUILayout.Button("この写真をアイコンに保存"))Try(()=>{var icon=LipSyncPortraits.Save(photo,t.label);Edit("顔写真を保存",()=>t.icon=icon);so.Update();});
            }
            details=EditorGUILayout.Foldout(details,"詳細：口形の割り当て",true);if(details){if(GUILayout.Button("口形を再検出")){Edit("口形を検出",()=>t.visemes=LipSyncSetup.Detect(t.mesh));so.Update();}LipSyncSettingsGUI.Mappings(p.FindPropertyRelative("visemes"),t.mesh);}
            so.ApplyModifiedProperties();
            using(new EditorGUILayout.HorizontalScope()) {
                using(new EditorGUI.DisabledScope(targetIndex==0))if(GUILayout.Button("← 左へ")){LipSyncSettingsGUI.Move(owner,targetIndex,targetIndex-1);targetIndex--;GUIUtility.ExitGUI();}
                using(new EditorGUI.DisabledScope(targetIndex==Config.targets.Count-1))if(GUILayout.Button("右へ →")){LipSyncSettingsGUI.Move(owner,targetIndex,targetIndex+1);targetIndex++;GUIUtility.ExitGUI();}
                if(GUILayout.Button("対象を削除")){LipSyncSettingsGUI.Remove(owner,targetIndex);ClearPhoto();GUIUtility.ExitGUI();}
            }
        }
        private void DrawPattern(SerializedObject so,SerializedProperty settings)
        {
            var pattern=Config.patterns[patternIndex];var p=settings.FindPropertyRelative("patterns").GetArrayElementAtIndex(patternIndex);
            EditorGUILayout.LabelField("一緒に喋る顔を選択",EditorStyles.boldLabel);Field(p,"label","パターン名");Field(p,"showInMenu","メニューに表示");Field(p,"icon","アイコン（差し替え可）");so.ApplyModifiedProperties();
            for(int row=0;row<Config.targets.Count;row+=3)using(new EditorGUILayout.HorizontalScope())for(int i=row;i<Math.Min(row+3,Config.targets.Count);i++) {
                int index=i;var t=Config.targets[i];if(Card(t.label,t.icon,pattern.targets.Contains(i))){Edit("発話対象を変更",()=>{if(!pattern.targets.Remove(index))pattern.targets.Add(index);});so.Update();}
            }
            EditorGUILayout.HelpBox(pattern.targets.Count==0?"全対象の音声リップシンクを停止します。":"選択した顔が同じ音声で喋ります。表示状態は変更しません。",MessageType.Info);
            if(GUILayout.Button("選んだ顔からアイコンを生成"))Try(()=>{
                var images=pattern.targets.Select(i=>Config.targets[i].icon).ToArray();if(images.Any(t=>!t))throw new InvalidOperationException("選択した対象の顔写真を先に保存してください。");
                SaveComposite(images,pattern.label,icon=>pattern.icon=icon);so.Update();
            });
            using(new EditorGUILayout.HorizontalScope()) {
                using(new EditorGUI.DisabledScope(Config.targets.Count+Config.patterns.Count+1>=255))if(GUILayout.Button("複製")){Edit("パターンを複製",()=>{var copy=JsonUtility.FromJson<LipSyncPattern>(JsonUtility.ToJson(pattern));copy.label=UniqueName(pattern.label);Config.patterns.Add(copy);});patternIndex=Config.patterns.Count-1;GUIUtility.ExitGUI();}
                using(new EditorGUI.DisabledScope(patternIndex==0))if(GUILayout.Button("↑")){MovePattern(patternIndex-1);GUIUtility.ExitGUI();}
                using(new EditorGUI.DisabledScope(patternIndex+1==Config.patterns.Count))if(GUILayout.Button("↓")){MovePattern(patternIndex+1);GUIUtility.ExitGUI();}
                if(GUILayout.Button("削除")){Edit("パターンを削除",()=>{Config.patterns.RemoveAt(patternIndex);int mode=Config.targets.Count+2+patternIndex;if(Config.defaultSelection==mode)Config.defaultSelection=1;else if(Config.defaultSelection>mode)Config.defaultSelection--;});patternIndex=-1;GUIUtility.ExitGUI();}
            }
        }
        private void MovePattern(int to)
        {
            int from=patternIndex;Edit("パターンの順序を変更",()=>{
                var p=Config.patterns[from];Config.patterns[from]=Config.patterns[to];Config.patterns[to]=p;
                int start=Config.targets.Count+2;if(Config.defaultSelection==start+from)Config.defaultSelection=start+to;else if(Config.defaultSelection==start+to)Config.defaultSelection=start+from;
            });patternIndex=to;
        }
        private void DrawMenu(SerializedObject so,SerializedProperty settings)
        {
            float previousLabelWidth=EditorGUIUtility.labelWidth;EditorGUIUtility.labelWidth=95;
            EditorGUILayout.LabelField("3  メニューの仕上げ",EditorStyles.boldLabel);Field(settings,"menuName","メニュー名");Field(settings,"menuIcon","アイコン");Field(settings,"showAll","Allを表示");
            Field(settings,"allIcon","Allアイコン");
            var labels=Config.targets.Select(t=>t.label).Concat(new[]{"All"}).Concat(Config.patterns.Select(p=>p.label)).ToArray();
            var initial=settings.FindPropertyRelative("defaultSelection");initial.intValue=EditorGUILayout.Popup("初期選択",Mathf.Clamp(initial.intValue-1,0,labels.Length-1),labels)+1;so.ApplyModifiedProperties();
            if(GUILayout.Button("Allアイコンを生成"))Try(()=>{
                var images=Config.targets.Select(t=>t.icon).ToArray();if(images.Any(t=>!t))throw new InvalidOperationException("各対象の顔写真を先に保存してください。");
                SaveComposite(images,"All",icon=>Config.allIcon=icon);so.Update();
            });
            EditorGUILayout.Space();EditorGUILayout.LabelField(Config.menuName+" / Speaker",EditorStyles.boldLabel);
            int count=0;int total=(Config.showAll?1:0)+Config.targets.Count(t=>t.showInMenu)+Config.patterns.Count(p=>p.showInMenu);
            void Entry(string label,Texture icon,int mode){if(count>0&&count%7==0&&total-count>1)EditorGUILayout.LabelField("↳ Next",EditorStyles.miniLabel);using(new EditorGUILayout.HorizontalScope()){GUILayout.Label(icon?new GUIContent(icon):new GUIContent("○"),GUILayout.Width(36),GUILayout.Height(36));GUILayout.Label((Config.defaultSelection==mode?"● ":"")+label,GUILayout.Height(36));}count++;}
            for(int i=0;i<Config.targets.Count;i++)if(Config.targets[i].showInMenu)Entry(Config.targets[i].label,Config.targets[i].icon,i+1);
            if(Config.showAll)Entry("All",Config.allIcon,Config.targets.Count+1);
            for(int i=0;i<Config.patterns.Count;i++)if(Config.patterns[i].showInMenu)Entry(Config.patterns[i].label,Config.patterns[i].icon,Config.targets.Count+2+i);
            if(count==0)EditorGUILayout.HelpBox("表示する発話項目がありません。",MessageType.Warning);
            EditorGUILayout.LabelField("● は初期選択。撮影画像はAssetsに保存されます。",EditorStyles.wordWrappedMiniLabel);
            EditorGUIUtility.labelWidth=previousLabelWidth;
        }
        private void SaveComposite(Texture2D[] images,string label,Action<Texture2D> assign){var image=LipSyncPortraits.Compose(images);try{var icon=LipSyncPortraits.Save(image,label);Edit("アイコンを生成",()=>assign(icon));}finally{DestroyImmediate(image);}}
        private void Try(Action action){try{action();error=null;}catch(Exception e){error=e.Message;Debug.LogException(e);}Repaint();}
    }
}
