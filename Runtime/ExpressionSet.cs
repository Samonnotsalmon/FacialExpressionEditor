using System.Collections.Generic;
using UnityEngine;

namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// 素体ごとに1つ作る共有の表情データ（UI上の名前）。全バリアントから参照される。
    /// ジェスチャーの組（UI上の「表情セット」）は GestureSet、表情メニューの項目は MenuNode。
    /// </summary>
    [CreateAssetMenu(menuName = "NotSalmon/NSL_Facial Expression Editor/表情データ", fileName = "表情データ")]
    public class ExpressionSet : ScriptableObject
    {
        // 登録フォルダはGUIDで持つ（フォルダを移動・改名しても外れないように）。
        public List<string> libraryFolderGuids = new List<string>();
        public bool independentClips;
        public List<string> exclusiveGroups = new List<string>();
        public float previewHeight = 0.03f;
        public float previewZoom = 1f;
        public List<string> faceMeshPaths = new List<string>();
        public List<string> linkedObjectPaths = new List<string>();
        public string lipSyncMeshPath = "";
        public bool customFistEyeProperties;
        public List<string> fistEyeProperties = new List<string>();
        public bool explicitLayerSelection;
        public List<string> importLayerNames = new List<string>();
        public bool explicitMenuSelection;
        public List<OriginalMenuSelection> replacedMenuItems = new List<OriginalMenuSelection>();
        public AnimationClip blinkAnimation;
        public AnimationClip mouthCancelAnimation;
        public bool mouthCancelUseClipValues;
        [HideInInspector] public bool protectDance = true;
        [HideInInspector] public string danceParameter = "VRCEmoteFXConect";

        public float defaultTransitionDuration = 0.1f;

        public List<Expression> expressions = new List<Expression>();

        public List<GestureSet> gestureSets = new List<GestureSet>();

        // 表情メニュー。表情セットと固定の表情を、フォルダに分けて並べる。
        public List<MenuNode> menu = new List<MenuNode>();

        // メニューで何も選んでいないときのモード（MenuNode.id）。空なら、メニューで最初の表情セット。
        public string defaultModeId = "";

        // 固定の表情に切り替えたときの演出の標準（初期値は演出なし＝そのまま切り替える）。
        public SwitchEffect fixedSwitchEffect = new SwitchEffect();

        // 表情メニューで、固定の表情にサムネイルのアイコンを付ける。
        public bool menuIcons = true;
        public int menuIconSize = 128;

        public List<FacialPart> parts = new List<FacialPart>();

        // まばたきは必ず、VRChatのまばたきを止めてまばたきのアニメーションに置き換える（表情ごとにまばたきを止めても、視線は動かせる）。
        // まばたきと口モーフキャンセラーは、標準では元アバターのもの（元FXのアニメーション、無ければアバターの設定）を使う。
        // 自分で編集するときだけ、下の一覧を使う。
        public bool customBlink;
        public List<BlinkShape> blinkShapes = new List<BlinkShape>();
        public bool customMouthMorphs;
        // 口モーフキャンセラーで、話している間にベース顔の値へ戻すシェイプキー。
        public List<BlendShapeRef> mouthMorphs = new List<BlendShapeRef>();

        // 表情ごとのまばたき・リップシンクを、元FXから取り込み済みか（この機能より前に作った表情データのため）。
        public bool faceControlImported;

        // コンタクト・PhysBoneで出す表情（上にあるものほど優先）。
        public List<ContactTrigger> contactTriggers = new List<ContactTrigger>();

        // 顔を動かしている元FXのレイヤーの扱い（指定が無いものはそのまま使う）。
        public List<OriginalLayerSetting> originalLayerSettings = new List<OriginalLayerSetting>();

        // 元FXのジェスチャーレイヤー名。ビルド時に取り除き、その位置に生成した表情レイヤーを入れる。
        public List<string> originalGestureLayers = new List<string>();

        // 元FXのパーツ用レイヤー名。ビルド時に取り除き、その位置に生成したパーツレイヤーを入れる。
        public List<string> originalPartLayers = new List<string>();

        // 旧形式の固定メニュー。表情メニュー（menu）へ移したら空にする。
        [HideInInspector] public List<FixedMenuFolder> fixedMenu = new List<FixedMenuFolder>();

        public Expression FindExpression(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return expressions.Find(e => e.id == id);
        }

        public GestureSet FindGestureSet(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return gestureSets.Find(s => s.id == id);
        }

        public float GetTransitionDuration(Expression expression)
        {
            return expression != null && expression.overrideTransitionDuration
                ? expression.transitionDuration
                : defaultTransitionDuration;
        }

        /// <summary>
        /// 固定の表情に切り替えたときに使う演出。演出なしなら null。
        /// </summary>
        public SwitchEffect GetFixedSwitchEffect(Expression expression)
        {
            SwitchEffect effect;
            switch (expression.fixedSwitchMode)
            {
                case SwitchEffectMode.None: return null;
                case SwitchEffectMode.Custom: effect = expression.fixedSwitchEffect; break;
                default: effect = fixedSwitchEffect; break;
            }
            return effect != null && effect.enabled && FindExpression(effect.betweenExpressionId) != null ? effect : null;
        }

        private void OnValidate()
        {
            foreach (var gestureSet in gestureSets) gestureSet.mapping.EnsureSize();
        }
    }
}
