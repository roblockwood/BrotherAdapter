using System;

namespace BrotherConnection
{
    /// <summary>
    /// Detects the Brother CNC control version by attempting to load PRD files.
    /// Control versions are identified by the presence of specific PRD files:
    /// - C00: PRDC2.nc exists
    /// - D00: PRDD2.nc exists
    /// </summary>
    public class ControlVersionDetector
    {
        private Request _request;

        public ControlVersionDetector(Request request = null)
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
        /// Detects the control version by attempting to load PRD files.
        /// </summary>
        /// <returns>The detected control version, or Unknown if detection fails</returns>
        public ControlVersion DetectVersion()
        {
            Console.WriteLine("[INFO] Attempting to detect control version...");
            Console.WriteLine("[INFO] Checking for PRD files: PRDA2, PRDB2, PRDC2, PRDD2");

            bool prdc2Exists = false;
            bool prdd2Exists = false;

            // Try to load PRDC2 (C00)
            try
            {
                _request.Arguments = "PRDC2";
                var response = _request.Send();
                // Check if we got a valid response with actual data
                // Response format: %<command>\r\n<data>\r\n<checksum>%\r\n
                // If file exists, there should be data between the % markers
                if (!string.IsNullOrWhiteSpace(response))
                {
                    // Extract data portion (between first \r\n and last \r\n before final %)
                    var firstNewline = response.IndexOf("\r\n", StringComparison.Ordinal);
                    var lastPercent = response.LastIndexOf("%");
                    if (firstNewline > 0 && lastPercent > firstNewline)
                    {
                        var lastNewline = response.LastIndexOf("\r\n", lastPercent);
                        if (lastNewline > firstNewline)
                        {
                            var dataPortion = response.Substring(firstNewline + 2, lastNewline - firstNewline - 2);
                            // If we have actual data (not just whitespace or error messages), file exists
                            if (!string.IsNullOrWhiteSpace(dataPortion) && 
                                dataPortion.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) < 0 &&
                                dataPortion.IndexOf("NOT FOUND", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                prdc2Exists = true;
                                Console.WriteLine("[INFO] PRDC2.nc found - indicates C00 control version");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] PRDC2.nc not found or error loading: {ex.Message}");
            }

            // Try to load PRDD2 (D00)
            try
            {
                _request.Arguments = "PRDD2";
                var response = _request.Send();
                // Check if we got a valid response with actual data
                if (!string.IsNullOrWhiteSpace(response))
                {
                    var firstNewline = response.IndexOf("\r\n", StringComparison.Ordinal);
                    var lastPercent = response.LastIndexOf("%");
                    if (firstNewline > 0 && lastPercent > firstNewline)
                    {
                        var lastNewline = response.LastIndexOf("\r\n", lastPercent);
                        if (lastNewline > firstNewline)
                        {
                            var dataPortion = response.Substring(firstNewline + 2, lastNewline - firstNewline - 2);
                            if (!string.IsNullOrWhiteSpace(dataPortion) && 
                                dataPortion.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) < 0 &&
                                dataPortion.IndexOf("NOT FOUND", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                prdd2Exists = true;
                                Console.WriteLine("[INFO] PRDD2.nc found - indicates D00 control version");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] PRDD2.nc not found or error loading: {ex.Message}");
            }

            // Also check PRDA2 and PRDB2 for logging (not supported yet)
            try
            {
                _request.Arguments = "PRDA2";
                var response = _request.Send();
                if (!string.IsNullOrWhiteSpace(response))
                {
                    var firstNewline = response.IndexOf("\r\n", StringComparison.Ordinal);
                    var lastPercent = response.LastIndexOf("%");
                    if (firstNewline > 0 && lastPercent > firstNewline)
                    {
                        var lastNewline = response.LastIndexOf("\r\n", lastPercent);
                        if (lastNewline > firstNewline)
                        {
                            var dataPortion = response.Substring(firstNewline + 2, lastNewline - firstNewline - 2);
                            if (!string.IsNullOrWhiteSpace(dataPortion) && 
                                dataPortion.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) < 0 &&
                                dataPortion.IndexOf("NOT FOUND", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                Console.WriteLine("[INFO] PRDA2.nc found (A00 control version - not currently supported)");
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore
            }

            try
            {
                _request.Arguments = "PRDB2";
                var response = _request.Send();
                if (!string.IsNullOrWhiteSpace(response))
                {
                    var firstNewline = response.IndexOf("\r\n", StringComparison.Ordinal);
                    var lastPercent = response.LastIndexOf("%");
                    if (firstNewline > 0 && lastPercent > firstNewline)
                    {
                        var lastNewline = response.LastIndexOf("\r\n", lastPercent);
                        if (lastNewline > firstNewline)
                        {
                            var dataPortion = response.Substring(firstNewline + 2, lastNewline - firstNewline - 2);
                            if (!string.IsNullOrWhiteSpace(dataPortion) && 
                                dataPortion.IndexOf("ERROR", StringComparison.OrdinalIgnoreCase) < 0 &&
                                dataPortion.IndexOf("NOT FOUND", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                Console.WriteLine("[INFO] PRDB2.nc found (B00 control version - not currently supported)");
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore
            }

            // Determine version based on what was found
            ControlVersion detectedVersion = ControlVersion.Unknown;

            if (prdd2Exists)
            {
                detectedVersion = ControlVersion.D00;
                Console.WriteLine($"[INFO] Control version detected: D00 (PRDD2.nc found)");
            }
            else if (prdc2Exists)
            {
                detectedVersion = ControlVersion.C00;
                Console.WriteLine($"[INFO] Control version detected: C00 (PRDC2.nc found)");
            }
            else
            {
                Console.WriteLine($"[WARNING] Could not detect control version - neither PRDC2.nc nor PRDD2.nc found");
                Console.WriteLine($"[WARNING] Defaulting to C00 (this may cause parsing errors if machine is D00)");
                detectedVersion = ControlVersion.C00; // Default to C00 with warning
            }

            return detectedVersion;
        }
    }
}

