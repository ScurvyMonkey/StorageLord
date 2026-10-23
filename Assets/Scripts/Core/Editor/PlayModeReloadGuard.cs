using UnityEditor;

namespace StorageLord.Core.Editor
{
    /// <summary>
    /// Defers C# domain reloads until Play Mode ends, for this project only (#40). With the
    /// Editor's "Script Changes While Playing" set to Recompile And Continue Playing, a script
    /// edit mid-Play reloads the domain. That wipes every manager's non-serializable runtime
    /// state (ShippingManager._activeOrders, ShippingHUD._rows, ConveyorManager's segment
    /// dictionaries) while their scene GameObjects survive, which produces frozen orphan HUD rows,
    /// orders that vanish without a missed event, and conveyor segments the network no longer
    /// knows about. Scripts still compile during Play; the reload just waits until Stop. This uses
    /// LockReloadAssemblies rather than writing EditorPrefs, since that preference is
    /// machine-global and would change every other Unity project on this machine too.
    /// Lock ownership lives in SessionState, not a static field: Unity's native lock count
    /// survives a domain reload but statics don't, and a static flag here once leaked a lock
    /// (the deferred reload ran during the exit transition, the static reset, and the lock was
    /// never released, leaving the Editor unable to load any script change).
    /// </summary>
    [InitializeOnLoad]
    public static class PlayModeReloadGuard
    {
        private const string LockHeldKey = "StorageLord.PlayModeReloadGuard.LockHeld";

        static PlayModeReloadGuard()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;

            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Unlock();
            }
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    Lock();
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    Unlock();
                    break;
            }
        }

        private static void Lock()
        {
            if (SessionState.GetBool(LockHeldKey, false))
            {
                return;
            }

            EditorApplication.LockReloadAssemblies();
            SessionState.SetBool(LockHeldKey, true);
        }

        private static void Unlock()
        {
            if (!SessionState.GetBool(LockHeldKey, false))
            {
                return;
            }

            SessionState.SetBool(LockHeldKey, false);
            EditorApplication.UnlockReloadAssemblies();
        }
    }
}
