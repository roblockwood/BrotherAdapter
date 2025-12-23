namespace BrotherConnection.Schema
{
    /// <summary>
    /// Represents a range with minimum and maximum values.
    /// </summary>
    public struct OffsetRange
    {
        public int Min;
        public int Max;
        
        public OffsetRange(int min, int max)
        {
            Min = min;
            Max = max;
        }
    }
    
    /// <summary>
    /// Base interface for version/unit-specific schema configurations.
    /// Provides common properties and methods for schema-aware parsing.
    /// </summary>
    public interface IFileSchemaConfig
    {
        /// <summary>
        /// Gets the coordinate field length for this schema (e.g., 9 for C00, 11 for D00).
        /// </summary>
        int CoordinateFieldLength { get; }
        
        /// <summary>
        /// Gets the format string for work offsets (e.g., "G{0}" for C00, "G{0:D3}" for D00).
        /// </summary>
        string WorkOffsetFormat { get; }
        
        /// <summary>
        /// Gets the format string for extended offsets (e.g., "X{0:D2}" for C00, "X{0:D3}" for D00).
        /// </summary>
        string ExtendedOffsetFormat { get; }
        
        /// <summary>
        /// Gets the range of extended offsets as (min, max) tuple.
        /// </summary>
        OffsetRange ExtendedOffsetRange { get; }
        
        /// <summary>
        /// Gets the format string for fixture offsets (e.g., "H{0:D2}" for C00, "H{0:D3}" for D00).
        /// </summary>
        string FixtureOffsetFormat { get; }
        
        /// <summary>
        /// Gets the format string for rotary offsets (e.g., "B{0:D2}" for C00, "B{0:D3}" for D00).
        /// </summary>
        string RotaryOffsetFormat { get; }
        
        /// <summary>
        /// Gets the range of rotary offsets as (min, max) tuple.
        /// </summary>
        OffsetRange RotaryOffsetRange { get; }
        
        /// <summary>
        /// Parses an offset name and returns normalized format (e.g., "G054" -> "G54", "X001" -> "X1").
        /// </summary>
        /// <param name="offsetName">The offset name from the file</param>
        /// <returns>Normalized offset name for consistent output</returns>
        string NormalizeOffsetName(string offsetName);
        
        /// <summary>
        /// Checks if an offset name matches the expected format for this schema.
        /// </summary>
        /// <param name="offsetName">The offset name to check</param>
        /// <returns>True if the format matches this schema</returns>
        bool MatchesOffsetFormat(string offsetName);
    }
}

