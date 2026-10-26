using UnityEditor;
using UnityEngine;

// The namespace is EditorTools, not Editor, so that the name "Editor" still refers to
// UnityEditor.Editor inside this assembly.
namespace TheSpaceBetween.EditorTools
{
    /// <summary>Draws fields marked with [ReadOnly] as greyed out and non-editable.</summary>
    [CustomPropertyDrawer(typeof(ReadOnlyAttribute))]
    public sealed class ReadOnlyDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.PropertyField(position, property, label, true);
            }
        }
    }
}
