using UnityEngine;
using VRC.SDKBase;

namespace Samon.FacialExpressionEditor
{
    [AddComponentMenu("")]
    public sealed class EditableMenuItem : MonoBehaviour, IEditorOnly
    {
        [HideInInspector] public string key;
        [HideInInspector] public Texture2D defaultIcon;
    }
}
