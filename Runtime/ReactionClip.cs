using System;
using UnityEngine;

namespace Samon.FacialExpressionEditor
{
    // A layer-scoped clip replacement; all conditions and transitions stay in the source FX.
    [Serializable]
    public class ReactionClip
    {
        public string layerName;
        public AnimationClip source;
        public string expressionId;
    }
}
