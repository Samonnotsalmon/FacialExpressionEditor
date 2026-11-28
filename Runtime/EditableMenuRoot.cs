using UnityEngine;
using VRC.SDKBase;

namespace Samon.FacialExpressionEditor
{
    [AddComponentMenu("")]
    public sealed class EditableMenuRoot : MonoBehaviour, IEditorOnly
    {
        [HideInInspector] public string signature;
    }
}
