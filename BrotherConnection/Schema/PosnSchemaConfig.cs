using System;
using System.Text.RegularExpressions;

namespace BrotherConnection.Schema
{
    /// <summary>
    /// Schema configuration for POSN files (workpiece coordinate zero).
    /// Handles version-specific differences between C00 and D00.
    /// </summary>
    public class PosnSchemaConfig : IFileSchemaConfig
    {
        public int CoordinateFieldLength { get; set; }
        public string WorkOffsetFormat { get; set; }
        public string ExtendedOffsetFormat { get; set; }
        public OffsetRange ExtendedOffsetRange { get; set; }
        public string FixtureOffsetFormat { get; set; }
        public string RotaryOffsetFormat { get; set; }
        public OffsetRange RotaryOffsetRange { get; set; }

        // C00 Configuration
        public static PosnSchemaConfig C00 = new PosnSchemaConfig
        {
            CoordinateFieldLength = 9,
            WorkOffsetFormat = "G{0}",  // G54, G55, G56, G57, G58, G59
            ExtendedOffsetFormat = "X{0:D2}",  // X01, X02, ..., X48
            ExtendedOffsetRange = new OffsetRange(1, 48),  // X01-X48
            FixtureOffsetFormat = "H{0:D2}",  // H01
            RotaryOffsetFormat = "B{0:D2}",  // B01
            RotaryOffsetRange = new OffsetRange(1, 1),  // B01 only
        };

        // D00 Configuration
        public static PosnSchemaConfig D00 = new PosnSchemaConfig
        {
            CoordinateFieldLength = 11,
            WorkOffsetFormat = "G{0:D3}",  // G054, G055, G056, G057, G058, G059
            ExtendedOffsetFormat = "X{0:D3}",  // X001, X002, ..., X300
            ExtendedOffsetRange = new OffsetRange(1, 300),  // X001-X300
            FixtureOffsetFormat = "H{0:D3}",  // H001
            RotaryOffsetFormat = "B{0:D3}",  // B001
            RotaryOffsetRange = new OffsetRange(1, 8),  // B001-B008
        };

        /// <summary>
        /// Gets the appropriate schema configuration based on control version.
        /// </summary>
        public static PosnSchemaConfig GetConfig(ControlVersion version)
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
        /// Normalizes offset names to consistent format for output.
        /// Converts G054 -> G54, X001 -> X1, H001 -> H1, B001 -> B1, etc.
        /// </summary>
        public string NormalizeOffsetName(string offsetName)
        {
            if (string.IsNullOrWhiteSpace(offsetName))
                return offsetName;

            var upper = offsetName.ToUpper().Trim();
            
            // Handle G offsets: G054 -> G54, G54 -> G54
            if (upper.StartsWith("G"))
            {
                var match = Regex.Match(upper, @"^G(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                {
                    return $"G{num}";  // Remove leading zeros
                }
            }
            
            // Handle X offsets: X001 -> X1, X01 -> X1
            if (upper.StartsWith("X"))
            {
                var match = Regex.Match(upper, @"^X(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                {
                    return $"X{num}";  // Remove leading zeros
                }
            }
            
            // Handle H offsets: H001 -> H1, H01 -> H1
            if (upper.StartsWith("H"))
            {
                var match = Regex.Match(upper, @"^H(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                {
                    return $"H{num}";  // Remove leading zeros
                }
            }
            
            // Handle B offsets: B001 -> B1, B01 -> B1
            if (upper.StartsWith("B"))
            {
                var match = Regex.Match(upper, @"^B(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                {
                    return $"B{num}";  // Remove leading zeros
                }
            }
            
            // Return as-is if no pattern matched
            return offsetName;
        }

        /// <summary>
        /// Checks if an offset name matches the expected format for this schema.
        /// </summary>
        public bool MatchesOffsetFormat(string offsetName)
        {
            if (string.IsNullOrWhiteSpace(offsetName))
                return false;

            var upper = offsetName.ToUpper().Trim();
            
            // Check G offsets (G54-G59 for C00, G054-G059 for D00)
            if (upper.StartsWith("G"))
            {
                var match = Regex.Match(upper, @"^G(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                {
                    // C00: G54-G59, D00: G054-G059
                    if (CoordinateFieldLength == 9) // C00
                    {
                        return num >= 54 && num <= 59;
                    }
                    else // D00
                    {
                        return num >= 54 && num <= 59; // G054-G059
                    }
                }
            }
            
            // Check X offsets
            if (upper.StartsWith("X"))
            {
                var match = Regex.Match(upper, @"^X(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                {
                    return num >= ExtendedOffsetRange.Min && num <= ExtendedOffsetRange.Max;
                }
            }
            
            // Check H offsets
            if (upper.StartsWith("H"))
            {
                var match = Regex.Match(upper, @"^H(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                {
                    // Both C00 and D00 have H offsets, just different format
                    return num >= 1;
                }
            }
            
            // Check B offsets
            if (upper.StartsWith("B"))
            {
                var match = Regex.Match(upper, @"^B(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                {
                    return num >= RotaryOffsetRange.Min && num <= RotaryOffsetRange.Max;
                }
            }
            
            return false;
        }
    }
}

