using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TheSpaceBetween.Tests
{
    /// <summary>
    /// Shared setup for PlayMode tests: tracks every object a test creates and destroys them
    /// afterwards, so one test can never affect the next.
    /// </summary>
    public abstract class PlayModeTestBase
    {
        private readonly List<Object> created = new List<Object>();

        /// <summary>Registers an object for automatic cleanup and returns it.</summary>
        protected T Track<T>(T obj) where T : Object
        {
            created.Add(obj);
            return obj;
        }

        /// <summary>
        /// Creates a GameObject that is still inactive, with component T attached. Set serialized
        /// fields with SetField, then activate it. Awake runs on activation, so the component sees
        /// the configured values just like it would after loading a scene.
        /// </summary>
        protected T CreateInactive<T>(string objectName) where T : Component
        {
            GameObject go = Track(new GameObject(objectName));
            go.SetActive(false);
            return go.AddComponent<T>();
        }

        /// <summary>Sets a private or public instance field, searching the base classes too.</summary>
        protected static void SetField(object target, string fieldName, object value)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(fieldName, flags);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }

            Assert.Fail($"{target.GetType().Name} has no field named '{fieldName}'.");
        }

        [TearDown]
        public void DestroyCreatedObjects()
        {
            foreach (Object obj in created)
            {
                if (obj != null)
                {
                    Object.DestroyImmediate(obj);
                }
            }

            created.Clear();
        }
    }
}
