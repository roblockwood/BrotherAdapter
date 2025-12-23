namespace BrotherConnection
{
    /// <summary>
    /// Represents the Brother CNC control version.
    /// </summary>
    public enum ControlVersion
    {
        /// <summary>
        /// Control version C00 (detected by presence of PRDC2.nc)
        /// </summary>
        C00,
        
        /// <summary>
        /// Control version D00 (detected by presence of PRDD2.nc)
        /// </summary>
        D00,
        
        /// <summary>
        /// Control version could not be determined
        /// </summary>
        Unknown
    }
}

