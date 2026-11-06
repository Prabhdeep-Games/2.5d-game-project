using UnityEngine;

namespace TheSpaceBetween
{
    /// <summary>
    /// Shows a field only while another field of the same class allows it: a bool that is on, or an enum set to one of the
    /// given values. A field whose controlling field is itself hidden is hidden too. Editors that support it, such as the
    /// Room Generator, read it through ShowIfFields in the Editor assembly.
    /// </summary>
    public sealed class ShowIfAttribute : PropertyAttribute
    {
        /// <summary>The name of the controlling field.</summary>
        public string Field { get; }

        /// <summary>The enum values that show the field. Empty means the controlling field is a bool that must be on.</summary>
        public object[] Values { get; }

        public ShowIfAttribute(string field, params object[] values)
        {
            Field = field;
            Values = values;
        }
    }
}
