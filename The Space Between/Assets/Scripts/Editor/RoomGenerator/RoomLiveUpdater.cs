using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TheSpaceBetween.EditorTools
{
    /// <summary>
    /// Keeps generated rooms up to date with their values. Whatever changes a value (the Inspector, the window or Undo), the
    /// room is rebuilt on the next Editor update, because objects cannot be rebuilt while Unity is validating them or drawing
    /// the Inspector. Rebuilds wait a short moment between them, so dragging a slider stays smooth.
    /// </summary>
    [InitializeOnLoad]
    public static class RoomLiveUpdater
    {
        private const double SecondsBetweenRebuilds = 0.05;

        private static readonly HashSet<GeneratedRoom> Pending = new HashSet<GeneratedRoom>();
        private static double nextRebuildTime;

        static RoomLiveUpdater()
        {
            GeneratedRoom.ValuesChanged += Request;
            EditorApplication.update += OnEditorUpdate;
            Undo.undoRedoPerformed += RequestAll;
        }

        /// <summary>Asks for a room to be checked and, if its values have changed, rebuilt on the next update.</summary>
        public static void Request(GeneratedRoom room)
        {
            if (room != null && room.gameObject.scene.IsValid())
            {
                Pending.Add(room);
            }
        }

        /// <summary>Does any waiting rebuilds now.</summary>
        public static void Flush()
        {
            var rooms = new List<GeneratedRoom>(Pending);
            Pending.Clear();
            foreach (GeneratedRoom room in rooms)
            {
                if (room == null)
                {
                    continue;
                }

                // A room saved before signatures existed is taken as it is, not rebuilt just for being looked at.
                if (string.IsNullOrEmpty(room.BuiltSignature))
                {
                    room.AdoptCurrentValues();
                }
                else if (room.BuiltSignature != room.CurrentSignature)
                {
                    RoomBuilder.Refresh(room);
                }
            }
        }

        private static void RequestAll()
        {
            foreach (GeneratedRoom room in Object.FindObjectsByType<GeneratedRoom>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Request(room);
            }
        }

        private static void OnEditorUpdate()
        {
            bool enteringOrLeavingPlay = EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying;
            if (Pending.Count == 0 || enteringOrLeavingPlay || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now >= nextRebuildTime)
            {
                nextRebuildTime = now + SecondsBetweenRebuilds;
                Flush();
            }
        }
    }
}
