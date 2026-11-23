using System;

namespace TheSpaceBetween
{
    /// <summary>
    /// Starts a collapsible section in the inspector of a component drawn by SectionedInspector (Editor assembly).
    /// Every field after it belongs to the section until the next one. Use it instead of [Header] on components with
    /// many settings, so the inspector stays readable. It changes nothing at runtime.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InspectorSectionAttribute : Attribute
    {
        /// <summary>The section's title.</summary>
        public string Title { get; }

        /// <summary>Whether the section starts open.</summary>
        public bool Expanded { get; }

        public InspectorSectionAttribute(string title, bool expanded = true)
        {
            Title = title;
            Expanded = expanded;
        }
    }
}
