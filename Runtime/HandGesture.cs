namespace Samon.FacialExpressionEditor
{
    /// <summary>
    /// 値はVRChatのGestureLeft / GestureRightパラメータと一致させている。
    /// </summary>
    public enum HandGesture
    {
        Neutral = 0,
        Fist = 1,
        HandOpen = 2,
        FingerPoint = 3,
        Victory = 4,
        RockNRoll = 5,
        HandGun = 6,
        ThumbsUp = 7,
    }

    public enum Hand
    {
        Left,
        Right,
    }
}
