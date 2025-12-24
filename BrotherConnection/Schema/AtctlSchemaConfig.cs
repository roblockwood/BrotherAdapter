using System;
using System.Text.RegularExpressions;
using BrotherConnection;

namespace BrotherConnection.Schema
{
    /// <summary>
    /// Schema configuration for ATCTL file parsing.
    /// Handles version-specific differences between C00 and D00.
    /// </summary>
    public class AtctlSchemaConfig
    {
        /// <summary>
        /// Gets the filename for this schema version.
        /// </summary>
        public string Filename { get; set; }

        /// <summary>
        /// Gets whether this schema supports tool numbers 201-299.
        /// </summary>
        public bool SupportsExtendedToolNumbers { get; set; }

        /// <summary>
        /// Gets the cap setting value (255 for C00, 999 for D00).
        /// </summary>
        public int CapSettingValue { get; set; }

        /// <summary>
        /// Gets whether this schema supports "Medium diameter" type (3).
        /// </summary>
        public bool SupportsMediumDiameterType { get; set; }

        /// <summary>
        /// Gets whether this schema includes "Store tool stocker" field.
        /// </summary>
        public bool HasStoreToolStockerField { get; set; }

        /// <summary>
        /// Gets whether this schema includes tool stocker entries (R01-R51, L01-L51).
        /// </summary>
        public bool HasToolStockerEntries { get; set; }

        /// <summary>
        /// Gets whether this schema includes stocker attributes (W01, E01).
        /// </summary>
        public bool HasStockerAttributes { get; set; }

        /// <summary>
        /// Gets the maximum number of pots (M01-M51 = 50 pots).
        /// </summary>
        public int MaxPots { get; set; }

        /// <summary>
        /// Gets the maximum number of stockers per side (R01-R51, L01-L51 = 50 stockers).
        /// </summary>
        public int MaxStockersPerSide { get; set; }

        /// <summary>
        /// C00 schema configuration.
        /// </summary>
        public static AtctlSchemaConfig C00 = new AtctlSchemaConfig
        {
            Filename = "ATCTL",
            SupportsExtendedToolNumbers = false,
            CapSettingValue = 255,
            SupportsMediumDiameterType = true,
            HasStoreToolStockerField = false,
            HasToolStockerEntries = false,
            HasStockerAttributes = false,
            MaxPots = 50,
            MaxStockersPerSide = 50
        };

        /// <summary>
        /// D00 schema configuration.
        /// </summary>
        public static AtctlSchemaConfig D00 = new AtctlSchemaConfig
        {
            Filename = "ATCTLD",
            SupportsExtendedToolNumbers = true,
            CapSettingValue = 999,
            SupportsMediumDiameterType = false,
            HasStoreToolStockerField = true,
            HasToolStockerEntries = true,
            HasStockerAttributes = true,
            MaxPots = 50,
            MaxStockersPerSide = 50
        };

        /// <summary>
        /// Gets the schema configuration for the specified control version.
        /// </summary>
        public static AtctlSchemaConfig GetConfig(ControlVersion version)
        {
            return version == ControlVersion.D00 ? D00 : C00;
        }

        /// <summary>
        /// Checks if a tool number is valid for this schema.
        /// </summary>
        public bool IsValidToolNumber(int toolNum)
        {
            if (toolNum == 0 || toolNum == CapSettingValue)
                return false; // Not set or cap setting - skip

            if (toolNum >= 1 && toolNum <= 99)
                return true;

            if (SupportsExtendedToolNumbers && toolNum >= 201 && toolNum <= 299)
                return true;

            return false;
        }

        /// <summary>
        /// Checks if a type value is valid for this schema.
        /// </summary>
        public bool IsValidType(int type)
        {
            if (type == 1 || type == 2)
                return true; // Standard, Large diameter

            if (SupportsMediumDiameterType && type == 3)
                return true; // Medium diameter (C00 only)

            return false;
        }

        /// <summary>
        /// Normalizes a pot number from M## format (M02 = pot 1, M51 = pot 50).
        /// </summary>
        public int NormalizePotNumber(string mPrefix)
        {
            if (mPrefix.StartsWith("M", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(mPrefix.Substring(1), out int mNum))
                {
                    if (mNum >= 2 && mNum <= 51)
                    {
                        return mNum - 1; // M02 = pot 1, M51 = pot 50
                    }
                }
            }
            return -1;
        }

        /// <summary>
        /// Normalizes a stocker number from R## or L## format (R01 = stocker 1, R51 = stocker 50).
        /// </summary>
        public int NormalizeStockerNumber(string prefix)
        {
            if (prefix.StartsWith("R", StringComparison.OrdinalIgnoreCase) || 
                prefix.StartsWith("L", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(prefix.Substring(1), out int stockerNum))
                {
                    if (stockerNum >= 1 && stockerNum <= 51)
                    {
                        return stockerNum; // R01 = stocker 1, R51 = stocker 50
                    }
                }
            }
            return -1;
        }
    }
}

