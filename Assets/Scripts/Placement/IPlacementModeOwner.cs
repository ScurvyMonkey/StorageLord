namespace StorageLord.Placement
{
    /// <summary>
    /// A runtime placer that owns its own placement mode (#51): container placement, conveyor
    /// placement, Receiving-building placement. Each registers with GridManager, which keeps the
    /// modes mutually exclusive and answers "is any placement mode active" for every
    /// outside-all-modes click handler, so adding a placer never means re-wiring every gate.
    /// </summary>
    public interface IPlacementModeOwner
    {
        /// <summary>True while this placer's placement mode is active.</summary>
        bool IsPlacementModeActive { get; }

        /// <summary>Force-exits this placer's placement mode if active, cleaning up any preview or in-progress drag.</summary>
        void ExitPlacementMode();
    }
}
