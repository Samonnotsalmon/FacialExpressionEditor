using System;
using System.Collections.Generic;
using UnityEngine;

namespace Samon.FacialExpressionEditor
{
    public enum MotionConditionMode { IsTrue, IsFalse, Equals, NotEqual, Greater, Less }
    [Serializable]
    public class MotionCondition
    {
        public string parameter = "AFK";
        public MotionConditionMode mode = MotionConditionMode.IsTrue;
        public float value;
    }
    [Serializable]
    public class MotionIntegrationRule
    {
        public string id = Expression.NewId();
        public string name = "既存モーション";
        public bool enabled = true;
        // Conditions within a rule are AND; matching rules independently yield their selected targets.
        public List<MotionCondition> conditions = new List<MotionCondition>();
        public bool pauseExpressions = true;
        public bool pauseBlink = true;
        public bool pauseMouthCancel = true;
        public bool pauseParts = true;
        public string sourcePlayable = "FX";
        public string sourceLayer;
        public string sourceState;
        public List<AnimationClip> sourceClips = new List<AnimationClip>();
        public bool applyBaseFace;
    }
}
