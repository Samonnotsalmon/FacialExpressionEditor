using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
namespace Samon.FacialExpressionEditor.Editor
{
    internal static class LipSyncSettingsGUI
    {
        public static void Draw(FacialExpressionAvatar owner, GameObject root)
        {
            LipSyncProfileUtility.EnsureDraft(owner);
            var so = new SerializedObject(owner);
            var settings=so.FindProperty("lipSyncSwitch");
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("enabled"), new GUIContent("リップシンク切り替えを使用"));
            var config=owner.lipSyncSwitch;
            if(config.targets.Count>0)
            {
                var names=config.targets.Select(t=>t.label).Concat(new[]{"All"}).Concat(config.patterns.Select(p=>p.label)).ToArray();
                var initial=settings.FindPropertyRelative("defaultSelection");
                initial.intValue=EditorGUILayout.Popup("初期の発話先",Mathf.Clamp(initial.intValue-1,0,names.Length-1),names)+1;
            }
            so.ApplyModifiedProperties();
            EditorGUILayout.LabelField("このアバターの顔メッシュ",EditorStyles.boldLabel);
            foreach(var target in config.targets)
            {
                EditorGUI.BeginChangeCheck();
                var mesh=(SkinnedMeshRenderer)EditorGUILayout.ObjectField(target.label,target.mesh,typeof(SkinnedMeshRenderer),true);
                if(EditorGUI.EndChangeCheck())
                {
                    try {
                        if(owner.lipSyncProfile)LipSyncProfileUtility.Bind(owner,target.id,mesh);
                        else {Undo.RecordObject(owner,"顔メッシュを指定");target.mesh=mesh;Dirty(owner);}
                    }catch(Exception e){EditorUtility.DisplayDialog("リップシンク設定",e.Message,"閉じる");}
                }
                if(config.enabled&&!target.mesh)EditorGUILayout.HelpBox(target.label+"の顔メッシュを指定してください。",MessageType.Warning);
            }
            DrawStorage(owner);
            if (GUILayout.Button("詳細・撮影設定を開く")) LipSyncEditorWindow.Open(owner);
        }
        internal static void DrawStorage(FacialExpressionAvatar owner)
        {
            EditorGUILayout.HelpBox(owner.lipSyncProfile
                ? "Play・ビルドは保存済みの共通設定を使います。編集後は「Prefabへ保存」を押してください。顔メッシュの割り当てはこの配置先だけに即時適用されます。"
                : "未移行の個別設定です。「Prefabへ保存」で、この内容を共通設定として保存します。",MessageType.Info);
            if(owner.lipSyncProfile && owner.lipSyncDraftProfileStamp!=LipSyncProfileUtility.Stamp(owner.lipSyncProfile))
                EditorGUILayout.HelpBox("Prefabの共通設定が更新されています。読み直すか、現在の編集内容で保存してください。",MessageType.Warning);
            using(new EditorGUILayout.HorizontalScope())
            {
                using(new EditorGUI.DisabledScope(!LipSyncProfileUtility.Prefab(owner)))
                if(GUILayout.Button("Prefabへ保存"))
                {
                    var changed=owner.lipSyncProfile && owner.lipSyncDraftProfileStamp!=LipSyncProfileUtility.Stamp(owner.lipSyncProfile);
                    if(!changed || EditorUtility.DisplayDialog("共通設定を更新","他の編集で更新された共通設定を、現在の内容で上書きしますか？","保存","キャンセル"))
                    try{LipSyncProfileUtility.Save(owner);}catch(Exception e){EditorUtility.DisplayDialog("保存できません",e.Message,"閉じる");}
                }
                using(new EditorGUI.DisabledScope(!owner.lipSyncProfile))
                if(GUILayout.Button("Prefabから読み直す")&&EditorUtility.DisplayDialog("共通設定を読み直す","未反映の共通設定の編集を破棄して読み直しますか？ 顔メッシュの割り当ては残ります。","読み直す","キャンセル"))LipSyncProfileUtility.LoadDraft(owner);
            }
        }
        private static void Field(SerializedProperty parent,string name,string label)=>EditorGUILayout.PropertyField(parent.FindPropertyRelative(name),new GUIContent(label));
        private static void Dirty(FacialExpressionAvatar owner){EditorUtility.SetDirty(owner);PrefabUtility.RecordPrefabInstancePropertyModifications(owner);}
        internal static void Move(FacialExpressionAvatar owner,int from,int to)
        {
            Undo.RecordObject(owner,"対象の優先順位を変更");var c=owner.lipSyncSwitch;var item=c.targets[from];c.targets[from]=c.targets[to];c.targets[to]=item;
            foreach(var p in c.patterns) for(int i=0;i<p.targets.Count;i++) { if(p.targets[i]==from)p.targets[i]=to;else if(p.targets[i]==to)p.targets[i]=from; }
            if(c.defaultSelection==from+1)c.defaultSelection=to+1;else if(c.defaultSelection==to+1)c.defaultSelection=from+1;Dirty(owner);
        }
        internal static void Remove(FacialExpressionAvatar owner,int index)
        {
            Undo.RecordObject(owner,"リップシンク対象を削除");var c=owner.lipSyncSwitch;c.targets.RemoveAt(index);
            foreach(var p in c.patterns) { p.targets.RemoveAll(i=>i==index);for(int i=0;i<p.targets.Count;i++)if(p.targets[i]>index)p.targets[i]--; }
            if(c.defaultSelection==index+1)c.defaultSelection=1;else if(c.defaultSelection>index+1)c.defaultSelection--;Dirty(owner);
        }
        internal static void Mappings(SerializedProperty array,SkinnedMeshRenderer renderer)
        {
            array.isExpanded=EditorGUILayout.Foldout(array.isExpanded,"口形（15種類）",true);if(!array.isExpanded)return;
            if(array.arraySize!=15)array.arraySize=15;var mesh=renderer?renderer.sharedMesh:null;
            var names=mesh?Enumerable.Range(0,mesh.blendShapeCount).Select(mesh.GetBlendShapeName).ToArray():new string[0];
            for(int i=0;i<15;i++)
            {
                var item=array.GetArrayElementAtIndex(i);var value=item.stringValue;var options=new[]{"(None)"}.Concat(names).ToList();
                if(!string.IsNullOrEmpty(value)&&!names.Contains(value))options.Add(value+" [Missing]");
                int index=string.IsNullOrEmpty(value)?0:Array.IndexOf(names,value)+1;if(!string.IsNullOrEmpty(value)&&index==0)index=options.Count-1;
                int next=EditorGUILayout.Popup(LipSyncSetup.Visemes[i],index,options.ToArray());if(next!=index)item.stringValue=next==0?"":next<=names.Length?names[next-1]:value;
            }
        }
    }
}
