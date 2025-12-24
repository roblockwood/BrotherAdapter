using System;
using System.Text.RegularExpressions;

namespace BrotherConnection.Schema
{
    /// <summary>
    /// Schema configuration for TOLNI/TOLNM files (tool table).
    /// Handles version-specific differences between C00 and D00.
    /// TOLNI = Inch units, TOLNM = Metric units.
    /// </summary>
    public class TolniSchemaConfig
    {
        /// <summary>
        /// Format string for tool number (e.g., "T{0:D2}" for T01-T99, "T{0:D3}" for T001-T099).
        /// </summary>
        public string ToolNumberFormat { get; set; }
        
        /// <summary>
        /// Field length for tool length offset (field 1).
        /// </summary>
        public int ToolLengthFieldLength { get; set; }
        
        /// <summary>
        /// Field length for T length wear offset (field 2).
        /// </summary>
        public int ToolLengthWearFieldLength { get; set; }
        
        /// <summary>
        /// Field length for cutter compensation/diameter (field 3).
        /// </summary>
        public int DiameterFieldLength { get; set; }
        
        /// <summary>
        /// Field length for cutter wear offset (field 4).
        /// </summary>
        public int CutterWearFieldLength { get; set; }
        
        /// <summary>
        /// Field length for tool life values (fields 6, 7, 8).
        /// </summary>
        public int ToolLifeFieldLength { get; set; }
        
        /// <summary>
        /// Field length for tool name (field 9).
        /// </summary>
        public int ToolNameFieldLength { get; set; }
        
        /// <summary>
        /// Field length for tool position offsets (fields 17, 19).
        /// </summary>
        public int ToolPositionFieldLength { get; set; }
        
        /// <summary>
        /// Field length for tool position wear offsets (fields 18, 20).
        /// </summary>
        public int ToolPositionWearFieldLength { get; set; }
        
        /// <summary>
        /// Field length for rotation feed (C00: field 10, D00: field 11).
        /// </summary>
        public int RotationFeedFieldLength { get; set; }
        
        /// <summary>
        /// Field length for F command value (C00: field 12, D00: field 13).
        /// </summary>
        public int FCommandFieldLength { get; set; }
        
        /// <summary>
        /// Field index for rotation feed (C00: 10, D00: 11).
        /// </summary>
        public int RotationFeedFieldIndex { get; set; }
        
        /// <summary>
        /// Field index for F command value (C00: 12, D00: 13).
        /// </summary>
        public int FCommandFieldIndex { get; set; }
        
        /// <summary>
        /// Whether D00 has peripheral speed field (appears before rotation feed as field 10).
        /// </summary>
        public bool HasPeripheralSpeedField { get; set; }
        
        /// <summary>
        /// Maximum number of tools supported (T01-T99 = 99, T001-T300 = 300).
        /// </summary>
        public int MaxToolNumber { get; set; }
        
        /// <summary>
        /// Maximum number of tools per group (Y01-Y99 entries, or Y001-Y300 for D00).
        /// </summary>
        public int MaxToolsPerGroup { get; set; }
        
        /// <summary>
        /// Format string for group number (e.g., "Y{0:D2}" for Y01-Y99, "Y{0:D3}" for Y001-Y300).
        /// </summary>
        public string GroupNumberFormat { get; set; }

        // C00 Configuration (from section_5_6_4_s300x1n.json)
        public static TolniSchemaConfig C00 = new TolniSchemaConfig
        {
            ToolNumberFormat = "T{0:D2}",  // T01, T02, ..., T99
            ToolLengthFieldLength = 8,  // Field 1: Tool length offset (-999.999~999.999 or -99.9999~99.9999 for inch)
            ToolLengthWearFieldLength = 7,  // Field 2: T length wear offset (-99.999~99.999 or -9.9999~9.9999 for inch)
            DiameterFieldLength = 8,  // Field 3: Cutter compensation/diameter (-999.999~999.999 or -99.9999~99.9999 for inch)
            CutterWearFieldLength = 7,  // Field 4: Cutter wear offset (-99.999~99.999 or -9.9999~9.9999 for inch)
            ToolLifeFieldLength = 6,  // Fields 6, 7, 8: Tool life values (0~999999)
            ToolNameFieldLength = 16,  // Field 9: Tool name (wrapped in single quotes)
            ToolPositionFieldLength = 8,  // Fields 17, 19: Tool position offsets (X, Y)
            ToolPositionWearFieldLength = 7,  // Fields 18, 20: Tool position wear offsets (X, Y)
            RotationFeedFieldLength = 4,  // Field 10: Rotation feed (0.01~9.99 or 0.001~0.999 for inch)
            FCommandFieldLength = 9,  // Field 12: F command value (0.01~999999.99 or 0.001~99999.999 for inch)
            RotationFeedFieldIndex = 10,  // Field 10
            FCommandFieldIndex = 12,  // Field 12
            HasPeripheralSpeedField = false,  // C00 does not have peripheral speed field
            MaxToolNumber = 99,  // T01-T99
            MaxToolsPerGroup = 30,  // Up to 30 tools per group (Y01-Y99)
            GroupNumberFormat = "Y{0:D2}",  // Y01, Y02, ..., Y99
        };

        // D00 Configuration (from section_3_6_5_d00.json)
        public static TolniSchemaConfig D00 = new TolniSchemaConfig
        {
            ToolNumberFormat = "T{0:D3}",  // T001, T002, ..., T300
            ToolLengthFieldLength = 8,  // Field 1: Tool length offset (-999.999~999.999 or -99.9999~99.9999 for inch)
            ToolLengthWearFieldLength = 7,  // Field 2: T length wear offset (-99.999~99.999 or -9.9999~9.9999 for inch)
            DiameterFieldLength = 8,  // Field 3: Cutter compensation/diameter (-999.999~999.999 or -99.9999~99.9999 for inch)
            CutterWearFieldLength = 7,  // Field 4: Cutter wear offset (-99.999~99.999 or -9.9999~9.9999 for inch)
            ToolLifeFieldLength = 7,  // Fields 6, 7, 8: Tool life values (0~9999999) - DIFFERENT from C00!
            ToolNameFieldLength = 16,  // Field 9: Tool name (wrapped in single quotes)
            ToolPositionFieldLength = 8,  // Fields: Tool position offsets (X, Y) - field positions may vary
            ToolPositionWearFieldLength = 7,  // Fields: Tool position wear offsets (X, Y)
            RotationFeedFieldLength = 9,  // Field 11: Rotation feed (0.01~9.99 or 0.001~0.999 for inch) - DIFFERENT from C00!
            FCommandFieldLength = 14,  // Field 13: F command value (0.01~999999.99 or 0.001~99999.999 for inch) - DIFFERENT from C00!
            RotationFeedFieldIndex = 11,  // Field 11 (shifted due to peripheral speed field)
            FCommandFieldIndex = 13,  // Field 13 (shifted due to peripheral speed field)
            HasPeripheralSpeedField = true,  // D00 has peripheral speed field (field 10, 6 chars: 0.1~9999.9)
            MaxToolNumber = 300,  // T001-T300
            MaxToolsPerGroup = 30,  // Up to 30 tools per group (likely Y001-Y300 format)
            GroupNumberFormat = "Y{0:D3}",  // Y001, Y002, ..., Y300 (assumed, format may vary)
        };

        /// <summary>
        /// Gets the appropriate schema configuration based on control version.
        /// </summary>
        public static TolniSchemaConfig GetConfig(ControlVersion version)
        {
            switch (version)
            {
                case ControlVersion.D00:
                    return D00;
                case ControlVersion.C00:
                default:
                    return C00;
            }
        }

        /// <summary>
        /// Normalizes tool number to consistent format for output.
        /// Converts T001 -> T1, T01 -> T1, etc.
        /// </summary>
        public string NormalizeToolNumber(string toolNumber)
        {
            if (string.IsNullOrWhiteSpace(toolNumber))
                return toolNumber;

            var upper = toolNumber.ToUpper().Trim();
            
            // Handle T offsets: T001 -> T1, T01 -> T1, T1 -> T1
            if (upper.StartsWith("T"))
            {
                var match = Regex.Match(upper, @"^T(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                {
                    return $"T{num}";  // Remove leading zeros
                }
            }
            
            // Return as-is if no pattern matched
            return toolNumber;
        }

        /// <summary>
        /// Checks if a tool number matches the expected format for this schema.
        /// </summary>
        public bool MatchesToolNumberFormat(string toolNumber)
        {
            if (string.IsNullOrWhiteSpace(toolNumber))
                return false;

            var upper = toolNumber.ToUpper().Trim();
            
            // Check T offsets
            if (upper.StartsWith("T"))
            {
                var match = Regex.Match(upper, @"^T(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                {
                    return num >= 1 && num <= MaxToolNumber;
                }
            }
            
            return false;
        }
    }
}

