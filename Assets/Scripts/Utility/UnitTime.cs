using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public struct UnitTime
{
    [SerializeField] UnitTimeMode timeMode;
    [SerializeField] float seconds;
    [SerializeField] int frames;

    public static implicit operator float(UnitTime self)
    {
        if (self.timeMode == UnitTimeMode.Frames) return self.frames / 60.0f;
        return self.seconds;
    }
    public static implicit operator UnitTime(float self)
    {
        UnitTime tmp = new();
        tmp.seconds = self;
        tmp.frames = Mathf.FloorToInt(self * 60.0f);
        return tmp;
    }
}
[System.Serializable]
public enum UnitTimeMode
{
    Seconds = 0,
    Frames = 1
}
#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(UnitTime))]
public class UnitTime_Drawer : PropertyDrawer
{
    const float valueWidth = 120;
    const float padding = 2;
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

        Rect modeRect = position;
        modeRect.width -= valueWidth + padding;
        SerializedProperty timeMode = property.FindPropertyRelative("timeMode");
        EditorGUI.PropertyField(modeRect, timeMode, GUIContent.none);

        Rect valueRect = position;
        valueRect.x += position.width - valueWidth;
        valueRect.width = valueWidth;

        SerializedProperty framesProperty = property.FindPropertyRelative("frames");
        SerializedProperty secondsProperty = property.FindPropertyRelative("seconds");
        if (timeMode.enumValueIndex == 1)
        {
            framesProperty.intValue = EditorGUI.IntField(valueRect, framesProperty.intValue);
            secondsProperty.floatValue = framesProperty.intValue / 60.0f;
        }
        else
        {
            secondsProperty.floatValue = EditorGUI.FloatField(valueRect, secondsProperty.floatValue);
            framesProperty.intValue = Mathf.FloorToInt(secondsProperty.floatValue * 60.0f);
        }
    }
}
#endif