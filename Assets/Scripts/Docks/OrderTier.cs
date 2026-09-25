namespace StorageLord.Docks
{
    /// <summary>
    /// Which category an order belongs to (#24). Priority is the Company's endless demand loop —
    /// the only tier that counts toward GameManager's missed-order loss condition. Random and
    /// Special exist here as forward-declared values only; nothing generates an order of either
    /// tier yet (see the future issues that build on this one).
    /// </summary>
    public enum OrderTier
    {
        Priority = 0,
        Random,
        Special
    }
}
