using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// Draws the fields of a serialized class, leaving out those whose <see cref="ShowIfAttribute"/> says they are not in use.
    /// </summary>
    public static class ShowIfFields
    {
        private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>Draws every visible child of a property whose class is <paramref name="ownerType"/>.</summary>
        public static void DrawChildren(SerializedProperty parent, Type ownerType)
        {
            SerializedProperty property = parent.Copy();
            SerializedProperty end = property.GetEndProperty();
            bool enter = true;
            while (property.NextVisible(enter) && !SerializedProperty.EqualContents(property, end))
            {
                enter = false;
                if (IsShown(property, ownerType))
                {
                    EditorGUILayout.PropertyField(property, true);
                }
            }
        }

        /// <summary>Whether a field of <paramref name="ownerType"/> is in use, following its controlling fields up the chain.</summary>
        public static bool IsShown(SerializedProperty property, Type ownerType)
        {
            ShowIfAttribute rule = ownerType.GetField(property.name, FieldFlags)?.GetCustomAttribute<ShowIfAttribute>();
            if (rule == null)
            {
                return true;
            }

            SerializedProperty controller = FindSibling(property, rule.Field);
            if (controller == null)
            {
                Debug.LogWarning($"ShowIf on {ownerType.Name}.{property.name} names a missing field '{rule.Field}'.");
                return true;
            }

            return Allows(controller, rule) && IsShown(controller, ownerType);
        }

        private static SerializedProperty FindSibling(SerializedProperty property, string name)
        {
            string path = property.propertyPath;
            int dot = path.LastIndexOf('.');
            return property.serializedObject.FindProperty(dot < 0 ? name : path.Substring(0, dot + 1) + name);
        }

        private static bool Allows(SerializedProperty controller, ShowIfAttribute rule)
        {
            if (rule.Values.Length == 0)
            {
                return controller.propertyType == SerializedPropertyType.Boolean && controller.boolValue;
            }

            foreach (object value in rule.Values)
            {
                if (controller.intValue == Convert.ToInt32(value))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
