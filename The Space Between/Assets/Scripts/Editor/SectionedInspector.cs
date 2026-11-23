using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// Draws a component's fields in collapsible sections marked with [InspectorSection], and hides fields whose
    /// [ShowIf] rule says they are not in use (wobble settings while the line is straight, for example). Open and closed
    /// sections are remembered per component type. Derive an empty class with [CustomEditor] to use it.
    /// </summary>
    public abstract class SectionedInspector : Editor
    {
        private const string ScriptPropertyPath = "m_Script";
        private const BindingFlags FieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly List<KeyValuePair<PropertyField, string>> conditionalFields = new List<KeyValuePair<PropertyField, string>>();

        public override VisualElement CreateInspectorGUI()
        {
            conditionalFields.Clear();
            Type ownerType = target.GetType();

            VisualElement root = new VisualElement();
            VisualElement section = root;

            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (property.propertyPath == ScriptPropertyPath)
                {
                    PropertyField scriptField = new PropertyField(property.Copy());
                    scriptField.SetEnabled(false);
                    root.Add(scriptField);
                    continue;
                }

                FieldInfo field = FindField(ownerType, property.name);
                InspectorSectionAttribute sectionStart = field?.GetCustomAttribute<InspectorSectionAttribute>();
                if (sectionStart != null)
                {
                    section = CreateSection(ownerType, sectionStart);
                    root.Add(section);
                }

                PropertyField propertyField = new PropertyField(property.Copy());
                section.Add(propertyField);

                if (field?.GetCustomAttribute<ShowIfAttribute>() != null)
                {
                    conditionalFields.Add(new KeyValuePair<PropertyField, string>(propertyField, property.propertyPath));
                }
            }

            RefreshVisibility();
            root.TrackSerializedObjectValue(serializedObject, _ => RefreshVisibility());
            return root;
        }

        private static Foldout CreateSection(Type ownerType, InspectorSectionAttribute section)
        {
            string key = $"{ownerType.FullName}.{section.Title}";
            Foldout foldout = new Foldout
            {
                text = section.Title,
                value = SessionState.GetBool(key, section.Expanded)
            };

            foldout.style.marginTop = 4f;
            foldout.Q<Toggle>().style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            foldout.RegisterValueChangedCallback(change =>
            {
                if (change.target == foldout)
                {
                    SessionState.SetBool(key, change.newValue);
                }
            });

            return foldout;
        }

        private void RefreshVisibility()
        {
            if (serializedObject == null || serializedObject.targetObject == null)
            {
                return;
            }

            Type ownerType = target.GetType();
            foreach (KeyValuePair<PropertyField, string> entry in conditionalFields)
            {
                SerializedProperty property = serializedObject.FindProperty(entry.Value);
                bool shown = property == null || ShowIfFields.IsShown(property, ownerType);
                entry.Key.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private static FieldInfo FindField(Type type, string name)
        {
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, FieldFlags | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }
            }

            return null;
        }
    }

    /// <summary>Sectioned inspector for the swap logic.</summary>
    [CustomEditor(typeof(SwapAbility)), CanEditMultipleObjects]
    public sealed class SwapAbilityInspector : SectionedInspector
    {
    }

    /// <summary>Sectioned inspector for the swap presentation.</summary>
    [CustomEditor(typeof(SwapFeedback)), CanEditMultipleObjects]
    public sealed class SwapFeedbackInspector : SectionedInspector
    {
    }
}
