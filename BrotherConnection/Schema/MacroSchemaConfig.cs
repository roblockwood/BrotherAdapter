using System;

namespace BrotherConnection.Schema
{
    /// <summary>
    /// Schema configuration for macro variable files (MCRNun/MCRSun).
    /// Handles version-specific differences between C00 and D00.
    /// MCRNI = Inch units, MCRNM = Metric units.
    /// </summary>
    public class MacroSchemaConfig
    {
        /// <summary>
        /// Gets the delimiter used to separate variables in the file.
        /// C00 uses comma (,), D00 uses CR+LF (line breaks).
        /// </summary>
        public string Delimiter { get; set; }

        /// <summary>
        /// Gets whether the delimiter is a line break (CR+LF) or comma.
        /// </summary>
        public bool UsesLineBreakDelimiter { get; set; }

        /// <summary>
        /// Gets the data length for each variable value (11 characters).
        /// </summary>
        public int VariableDataLength { get; set; }

        /// <summary>
        /// Gets the minimum variable number (C500).
        /// </summary>
        public int MinVariableNumber { get; set; }

        /// <summary>
        /// Gets the maximum variable number (C999).
        /// </summary>
        public int MaxVariableNumber { get; set; }

        /// <summary>
        /// Gets the format string for variable names (e.g., "C{0}" for C500-C999).
        /// </summary>
        public string VariableFormat { get; set; }

        /// <summary>
        /// Gets the maximum value range for Metric units (-999999.999~999999.999).
        /// </summary>
        public string MetricRange { get; set; }

        /// <summary>
        /// Gets the maximum value range for Inch units (-99999.9999~99999.9999).
        /// </summary>
        public string InchRange { get; set; }

        /// <summary>
        /// Gets whether the last digit is a blank space when unit is micron (C00 only).
        /// </summary>
        public bool HasMicronBlankSpace { get; set; }

        /// <summary>
        /// Gets whether Type 2 (MCRSun) has one more decimal digit than Type 1 (D00 only).
        /// </summary>
        public bool Type2HasExtendedPrecision { get; set; }

        // C00 Configuration (from section_5_6_4_s300x1n.json)
        public static MacroSchemaConfig C00 = new MacroSchemaConfig
        {
            Delimiter = ",",
            UsesLineBreakDelimiter = false,
            VariableDataLength = 11,
            MinVariableNumber = 500,
            MaxVariableNumber = 999,
            VariableFormat = "C{0}",
            MetricRange = "-999999.999~999999.999",
            InchRange = "-99999.9999~99999.9999",
            HasMicronBlankSpace = true,  // Last digit is blank space when unit is micron
            Type2HasExtendedPrecision = false  // Type 2 same as Type 1 for C00
        };

        // D00 Configuration (from section_3_6_5_d00.json)
        public static MacroSchemaConfig D00 = new MacroSchemaConfig
        {
            Delimiter = "\r\n",  // CR+LF (line break)
            UsesLineBreakDelimiter = true,
            VariableDataLength = 11,
            MinVariableNumber = 500,
            MaxVariableNumber = 999,
            VariableFormat = "C{0}",
            MetricRange = "-999999.999~999999.999",
            InchRange = "-99999.9999~99999.9999",
            HasMicronBlankSpace = false,  // D00 doesn't mention this
            Type2HasExtendedPrecision = true  // Type 2 has one more decimal digit when option purchased
        };

        /// <summary>
        /// Gets the appropriate schema configuration based on control version.
        /// </summary>
        public static MacroSchemaConfig GetConfig(ControlVersion version)
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
        /// Normalizes variable name to consistent format for output.
        /// Converts C500 -> C500, etc. (no change needed, but method for consistency).
        /// </summary>
        public string NormalizeVariableName(string variableName)
        {
            if (string.IsNullOrWhiteSpace(variableName))
                return variableName;

            var upper = variableName.ToUpper().Trim();
            
            // Handle C variables: C500, C501, ..., C999
            if (upper.StartsWith("C"))
            {
                var match = System.Text.RegularExpressions.Regex.Match(upper, @"^C(\d+)$");
                if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
                {
                    if (num >= MinVariableNumber && num <= MaxVariableNumber)
                    {
                        return $"C{num}";  // Keep as-is (C500, not C0500)
                    }
                }
            }
            
            // Return as-is if no pattern matched
            return variableName;
        }

        /// <summary>
        /// Checks if a variable number is valid for this schema.
        /// </summary>
        public bool IsValidVariableNumber(int variableNum)
        {
            return variableNum >= MinVariableNumber && variableNum <= MaxVariableNumber;
        }
    }
}

