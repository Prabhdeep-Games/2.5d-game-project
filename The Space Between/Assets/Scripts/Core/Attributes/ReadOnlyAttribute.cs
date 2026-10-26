using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Shows a serialized field in the Inspector but prevents it from being edited.
    /// Use it for runtime state and diagnostics that a designer should read but never change.
    /// The matching ReadOnlyDrawer lives in the Editor assembly.
    /// </summary>
    public sealed class ReadOnlyAttribute : PropertyAttribute
    {
    }
}