using UnityEngine;
using VRC.SDKBase;

namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// 表情設定。使う表情データと顔バリアントを指定する。
    /// 表情エディタでプレハブにして、各アバターの中へ入れて使う（アバターのルートに直接付けてもよい）。
    /// IEditorOnly なのでアップロード時には取り除かれる。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("NotSalmon/NSL_Facial Expression Editor/表情設定")]
    public class FacialExpressionAvatar : MonoBehaviour, IEditorOnly
    {
        public ExpressionSet expressionSet;

        public LipSyncSwitchSettings lipSyncSwitch = new LipSyncSwitchSettings();
        [HideInInspector] public LipSyncProfile lipSyncProfile;
        [HideInInspector] public System.Collections.Generic.List<LipSyncMeshBinding> lipSyncMeshBindings = new System.Collections.Generic.List<LipSyncMeshBinding>();
        [HideInInspector] public LipSyncSwitchSettings lipSyncMigrationBackup;
        [HideInInspector] public bool hasLipSyncMigrationBackup;
        [HideInInspector] public string lipSyncDraftProfileStamp;

        // 未設定なら、ベース顔の処理と表情の差し替えをしない。
        public FaceVariant faceVariant;

        // 表情エディタで、プレビューと元FXの読み取りに使うアバター（プレハブ）。
        // アバターの中に入れたときは、入れた先のアバターを使う。
        public GameObject sourceAvatar;

        // Temporary Play-mode cache reference. Serialized so scene/domain reloads and
        // build clones retain it; the editor clears it on returning to Edit mode.
        [HideInInspector] public string playModeMenuIconToken;
    }
}
