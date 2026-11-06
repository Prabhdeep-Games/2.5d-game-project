using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// A saved room: its values and the elements placed in it, relative to the room. Saved and loaded from the Room
    /// Generator window, so a useful arrangement can be generated again anywhere.
    /// </summary>
    public sealed class RoomPreset : ScriptableObject
    {
        /// <summary>One element of the arrangement, relative to the room.</summary>
        [Serializable]
        public class Element
        {
            public GameObject prefab;
            public Vector3 localPosition;
            public Quaternion localRotation = Quaternion.identity;
            public Vector3 localScale = Vector3.one;
        }

        [Tooltip("The room's values when it was saved.")]
        [SerializeField] private RoomSettings settings = new RoomSettings();

        [Tooltip("The elements of the room, relative to its entrance floor.")]
        [SerializeField] private List<Element> elements = new List<Element>();

        [Tooltip("What this arrangement is for, shown in the Room Generator.")]
        [SerializeField, TextArea] private string notes;

        public RoomSettings Settings => settings;

        public IReadOnlyList<Element> Elements => elements;

        public string Notes => notes;

        /// <summary>Replaces the stored room. Called by the generator when saving.</summary>
        public void Store(RoomSettings newSettings, List<Element> newElements)
        {
            settings = newSettings;
            elements = newElements;
        }
    }
}
