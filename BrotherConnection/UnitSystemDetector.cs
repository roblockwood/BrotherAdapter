using System;

namespace BrotherConnection
{
    /// <summary>
    /// Detects the unit system (Metric/Inch) by parsing the MSRRS file.
    /// The unit system is determined from the first line (C01) of the MSRRS file:
    /// - 0 = Metric
    /// - 1 = Inch
    /// The MSRRS file name depends on the control version:
    /// - A00 → MSRRSA
    /// - B00 → MSRRSB
    /// - C00 → MSRRSC
    /// - D00 → MSRRSD
    /// </summary>
    public class UnitSystemDetector
    {
        private Request _request;

        public UnitSystemDetector(Request request = null)
        {
            if (request != null)
            {
                _request = request;
            }
            else
            {
                _request = new Request();
            }
            _request.Command = "LOD";
        }

        /// <summary>
        /// Detects the unit system by parsing the appropriate MSRRS file based on control version.
        /// </summary>
        /// <param name="controlVersion">The detected control version (determines which MSRRS file to load)</param>
        /// <returns>The detected unit system, or Unknown if detection fails</returns>
        public UnitSystem DetectUnitSystem(ControlVersion controlVersion)
        {
            Console.WriteLine("[INFO] Attempting to detect unit system...");

            // Determine which MSRRS file to load based on control version
            string msrrsFileName = "";
            switch (controlVersion)
            {
                case ControlVersion.C00:
                    msrrsFileName = "MSRRSC";
                    break;
                case ControlVersion.D00:
                    msrrsFileName = "MSRRSD";
                    break;
                case ControlVersion.Unknown:
                    // Try C00 as default
                    msrrsFileName = "MSRRSC";
                    Console.WriteLine("[WARNING] Control version unknown, attempting MSRRSC");
                    break;
                default:
                    // A00 and B00 not supported yet, but try C00 as fallback
                    msrrsFileName = "MSRRSC";
                    Console.WriteLine($"[WARNING] Control version {controlVersion} not fully supported, attempting MSRRSC");
                    break;
            }

            Console.WriteLine($"[INFO] Loading {msrrsFileName} to detect unit system...");

            try
            {
                _request.Arguments = msrrsFileName;
                var response = _request.Send();

                // Remove protocol wrapper (%...%)
                // Response format: %<command>\r\n<data>\r\n<checksum>%\r\n
                string dataPortion = "";
                if (response.StartsWith("%"))
                {
                    var firstNewline = response.IndexOf("\r\n", StringComparison.Ordinal);
                    if (firstNewline > 0)
                    {
                        var lastPercent = response.LastIndexOf("%");
                        var lastNewline = response.LastIndexOf("\r\n", lastPercent);
                        if (lastNewline > firstNewline)
                        {
                            dataPortion = response.Substring(firstNewline + 2, lastNewline - firstNewline - 2);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(dataPortion))
                {
                    Console.WriteLine($"[WARNING] {msrrsFileName} file appears to be empty or invalid");
                    Console.WriteLine($"[WARNING] Defaulting to Metric unit system");
                    return UnitSystem.Metric;
                }

                // Parse first line: C01,<value>CR+LF
                // Format: C01,0 or C01,1 followed by CR+LF
                var lines = dataPortion.Split(new string[] { "\r\n" }, StringSplitOptions.None);
                if (lines.Length == 0)
                {
                    Console.WriteLine($"[WARNING] {msrrsFileName} file has no lines");
                    Console.WriteLine($"[WARNING] Defaulting to Metric unit system");
                    return UnitSystem.Metric;
                }

                var firstLine = lines[0].Trim();
                Console.WriteLine($"[DEBUG] First line of {msrrsFileName}: '{firstLine}'");

                // Parse C01 line: C01,<value>
                if (firstLine.StartsWith("C01", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = firstLine.Split(',');
                    if (parts.Length >= 2)
                    {
                        var unitValueStr = parts[1].Trim();
                        if (int.TryParse(unitValueStr, out int unitValue))
                        {
                            if (unitValue == 0)
                            {
                                Console.WriteLine($"[INFO] Unit system detected: Metric (from {msrrsFileName}, value: 0)");
                                return UnitSystem.Metric;
                            }
                            else if (unitValue == 1)
                            {
                                Console.WriteLine($"[INFO] Unit system detected: Inch (from {msrrsFileName}, value: 1)");
                                return UnitSystem.Inch;
                            }
                            else
                            {
                                Console.WriteLine($"[WARNING] Unexpected unit system value: {unitValue} (expected 0 or 1)");
                                Console.WriteLine($"[WARNING] Defaulting to Metric unit system");
                                return UnitSystem.Metric;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[WARNING] Could not parse unit system value: '{unitValueStr}'");
                            Console.WriteLine($"[WARNING] Defaulting to Metric unit system");
                            return UnitSystem.Metric;
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[WARNING] C01 line format unexpected: '{firstLine}' (expected C01,<value>)");
                        Console.WriteLine($"[WARNING] Defaulting to Metric unit system");
                        return UnitSystem.Metric;
                    }
                }
                else
                {
                    Console.WriteLine($"[WARNING] First line does not start with C01: '{firstLine}'");
                    Console.WriteLine($"[WARNING] Defaulting to Metric unit system");
                    return UnitSystem.Metric;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to detect unit system from {msrrsFileName}: {ex.Message}");
                Console.WriteLine($"[WARNING] Defaulting to Metric unit system");
                return UnitSystem.Metric;
            }
        }
    }
}

