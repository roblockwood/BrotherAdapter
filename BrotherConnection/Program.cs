using BrotherConnection.Mapping;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BrotherConnection
{
    class Program
    {
        static void Main(string[] args)
        {
            // Check for validation mode
            if (args.Length > 0 && args[0] == "--validate-schemas")
            {
                RunSchemaValidation();
                return;
            }
            
            // Get CNC IP from environment for logging
            var cncIp = Environment.GetEnvironmentVariable("CNC_IP_ADDRESS") ?? "192.168.86.89";
            var cncPort = Environment.GetEnvironmentVariable("CNC_PORT") ?? "10000";
            
            // Get agent port from environment
            var agentPortStr = Environment.GetEnvironmentVariable("AGENT_PORT") ?? "7878";
            int agentPort = 7878;
            if (!int.TryParse(agentPortStr, out agentPort))
            {
                agentPort = 7878;
            }
            
            Console.WriteLine($"[INFO] Starting MTConnect Agent for Brother CNC");
            Console.WriteLine($"[INFO] Target CNC: {cncIp}:{cncPort}");
            Console.WriteLine($"[INFO] Agent HTTP server port: {agentPort}");
            Console.WriteLine();
            
            // Detect control version BEFORE any parsing
            ControlVersion detectedVersion = ControlVersion.Unknown;
            try
            {
                var versionDetector = new ControlVersionDetector();
                detectedVersion = versionDetector.DetectVersion();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Failed to detect control version: {ex.Message}");
                Console.Error.WriteLine($"[WARNING] Defaulting to C00 (this may cause parsing errors)");
                detectedVersion = ControlVersion.C00;
            }
            
            // Detect unit system AFTER version detection (needs version to know which MSRRS file to load)
            UnitSystem detectedUnitSystem = UnitSystem.Unknown;
            try
            {
                var unitDetector = new UnitSystemDetector();
                detectedUnitSystem = unitDetector.DetectUnitSystem(detectedVersion);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Failed to detect unit system: {ex.Message}");
                Console.Error.WriteLine($"[WARNING] Defaulting to Metric (this may cause parsing errors)");
                detectedUnitSystem = UnitSystem.Metric;
            }
            
            Console.WriteLine();
            Console.WriteLine($"[INFO] Agent will attempt to connect every 2 seconds...");
            Console.WriteLine();
            
            // Start MTConnect HTTP server
            var mtconnectServer = new MTConnectServer(agentPort, detectedUnitSystem);
            mtconnectServer.Start();
            
            // Load data mapping for PDSP - version-specific if available
            DataMap prodData3Map = null;
            string mappingFileName = "ProductionData3.json";
            if (detectedVersion == ControlVersion.D00)
            {
                // Try D00-specific mapping first (could also be unit-specific: ProductionData3_D00_Metric.json, etc.)
                string d00MappingFile = "ProductionData3_D00.json";
                if (File.Exists(d00MappingFile))
                {
                    mappingFileName = d00MappingFile;
                    Console.WriteLine($"[INFO] Using D00-specific mapping file: {d00MappingFile}");
                }
                else
                {
                    Console.WriteLine($"[WARNING] D00 mapping file not found, using default: ProductionData3.json");
                }
            }
            else
            {
                // For C00, use default or C00-specific if available
                string c00MappingFile = "ProductionData3_C00.json";
                if (File.Exists(c00MappingFile))
                {
                    mappingFileName = c00MappingFile;
                    Console.WriteLine($"[INFO] Using C00-specific mapping file: {c00MappingFile}");
                }
            }
            
            prodData3Map = JsonConvert.DeserializeObject<DataMap>(File.ReadAllText(mappingFileName));
            
            // Initialize file loader with detected version and unit system
            var fileLoader = new FileLoader(detectedVersion, detectedUnitSystem);
            
            var consecutiveErrors = 0;
            var maxConsecutiveErrors = 5;
            
            // Configuration: which files to load and how often
            // PDSP: every cycle (2 seconds) - real-time data
            // Other files: every 5 cycles (10 seconds) - status data changes less frequently
            int cycleCount = 0;
            const int STATUS_FILE_INTERVAL = 5; // Load status files every 5 cycles
            
            while (true)
            {
                var DecodedResults = new Dictionary<String, String>();
                String[] rawData = null;

                try
                {
                    Console.Clear();
                    var req = new Request();
                    req.Command = "LOD";
                    req.Arguments = prodData3Map.FileName;

                    rawData = req.Send().Split(new String[] { "\r\n" },StringSplitOptions.None);

                    Console.Write(req.Send());
                    
                    // Reset error counter on successful connection
                    if (consecutiveErrors > 0)
                    {
                        Console.WriteLine($"[INFO] Connection restored to {cncIp}:{cncPort}");
                        consecutiveErrors = 0;
                    }
                }
                catch (System.Net.Sockets.SocketException ex)
                {
                    consecutiveErrors++;
                    Console.Error.WriteLine();
                    Console.Error.WriteLine($"[ERROR] Cannot connect to CNC machine at {cncIp}:{cncPort}");
                    Console.Error.WriteLine($"[ERROR] Socket error: {ex.SocketErrorCode} (Error code: {ex.ErrorCode})");
                    Console.Error.WriteLine($"[ERROR] Message: {ex.Message}");
                    
                    if (ex.SocketErrorCode == System.Net.Sockets.SocketError.TimedOut)
                    {
                        Console.Error.WriteLine($"[ERROR] Connection timeout - machine may be unreachable or firewall blocking");
                    }
                    else if (ex.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused)
                    {
                        Console.Error.WriteLine($"[ERROR] Connection refused - check if CNC is powered on and port {cncPort} is open");
                    }
                    else if (ex.SocketErrorCode == System.Net.Sockets.SocketError.HostUnreachable)
                    {
                        Console.Error.WriteLine($"[ERROR] Host unreachable - check network connectivity and IP address {cncIp}");
                    }
                    // Note: NoRouteToHost doesn't exist in .NET Framework 4.6.1/Mono
                    // Using NetworkUnreachable as alternative
                    else if (ex.SocketErrorCode == System.Net.Sockets.SocketError.NetworkUnreachable)
                    {
                        Console.Error.WriteLine($"[ERROR] Network unreachable - check network routing and IP address {cncIp}");
                    }
                    
                    Console.Error.WriteLine($"[ERROR] Retrying in 2 seconds... (Error count: {consecutiveErrors}/{maxConsecutiveErrors})");
                    
                    if (consecutiveErrors >= maxConsecutiveErrors)
                    {
                        Console.Error.WriteLine($"[ERROR] Maximum consecutive errors reached. Continuing to retry...");
                    }
                    
                    // Skip data processing on error
                    Thread.Sleep(2000);
                    continue;
                }
                catch (Exception ex)
                {
                    consecutiveErrors++;
                    Console.Error.WriteLine();
                    Console.Error.WriteLine($"[ERROR] Unexpected error communicating with CNC at {cncIp}:{cncPort}");
                    Console.Error.WriteLine($"[ERROR] Exception type: {ex.GetType().Name}");
                    Console.Error.WriteLine($"[ERROR] Message: {ex.Message}");
                    Console.Error.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
                    Console.Error.WriteLine($"[ERROR] Retrying in 2 seconds... (Error count: {consecutiveErrors})");
                    
                    // Skip data processing on error
                    Thread.Sleep(2000);
                    continue;
                }

                // Only process data if we successfully got rawData
                if (rawData == null)
                {
                    Thread.Sleep(2000);
                    continue;
                }

                // Parse PDSP data (always loaded - real-time data)
                foreach (var line in prodData3Map.Lines)
                {
                    if (line.Number >= rawData.Length)
                        continue;
                        
                    var rawLine = rawData[line.Number].Split(',');
                    if (rawLine.Length == 0 || rawLine[0] != line.Symbol)
                        continue;

                    rawLine = rawLine.Skip(1).ToArray();
                    for (int i = 1; i < line.Items.Count && (i - 1) < rawLine.Length; i++)
                    {
                        if (line.Items[i].Type == "Number")
                        {
                            DecodedResults[line.Items[i].Name] = rawLine[i - 1].Trim();
                        }
                        else if (line.Items[i].EnumValues != null && line.Items[i].EnumValues.Count > 0)
                        {
                            if (int.TryParse(rawLine[i - 1].Trim(), out int enumIndex))
                            {
                                var enumValue = line.Items[i].EnumValues.FirstOrDefault(v => v.Index == enumIndex);
                                if (enumValue != null)
                                {
                                    DecodedResults[line.Items[i].Name] = enumValue.Value;
                                }
                            }
                        }
                    }
                }
                
                // Load status files periodically (every 5 cycles = 10 seconds)
                cycleCount++;
                if (cycleCount >= STATUS_FILE_INTERVAL)
                {
                    cycleCount = 0;
                    
                    // Load MEM (program name)
                    try
                    {
                        var memLines = fileLoader.LoadFile("MEM");
                        if (memLines != null)
                        {
                            var memData = fileLoader.ParseMem(memLines);
                            foreach (var kvp in memData)
                            {
                                DecodedResults[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WARNING] Failed to load/parse MEM: {ex.Message}");
                    }
                    
                    // Load ALARM
                    try
                    {
                        var alarmLines = fileLoader.LoadFile("ALARM");
                        if (alarmLines != null)
                        {
                            var alarmData = fileLoader.ParseAlarm(alarmLines);
                            foreach (var kvp in alarmData)
                            {
                                DecodedResults[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WARNING] Failed to load/parse ALARM: {ex.Message}");
                    }
                    
                    // Load WKCNTR (workpiece counters)
                    try
                    {
                        var wkcntrLines = fileLoader.LoadFile("WKCNTR");
                        if (wkcntrLines != null)
                        {
                            var wkcntrData = fileLoader.ParseWkcntr(wkcntrLines);
                            foreach (var kvp in wkcntrData)
                            {
                                DecodedResults[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WARNING] Failed to load/parse WKCNTR: {ex.Message}");
                    }
                    
                    // Load TOLN file (tool table) - uses TOLNI for Inch or TOLNM for Metric
                    try
                    {
                        var tolniLines = fileLoader.LoadTolniFile(1);
                        if (tolniLines != null && tolniLines.Length > 0)
                        {
                            Console.Error.WriteLine($"[DEBUG] Loaded TOLN file ({tolniLines.Length} lines), first 5 lines:");
                            for (int i = 0; i < Math.Min(5, tolniLines.Length); i++)
                            {
                                Console.Error.WriteLine($"[DEBUG]   [{i}] {tolniLines[i]}");
                            }
                            
                            var tolniData = fileLoader.ParseTolni(tolniLines);
                            foreach (var kvp in tolniData)
                            {
                                DecodedResults[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WARNING] Failed to load/parse TOLN file: {ex.Message}");
                    }
                    
                    // Load POSN file (work offsets) - uses POSNI for Inch or POSNM for Metric
                    try
                    {
                        var posnLines = fileLoader.LoadPosnFile(1); // Data bank 1 (POSNI1 or POSNM1)
                        if (posnLines != null)
                        {
                            var posnData = fileLoader.ParsePosni(posnLines);
                            foreach (var kvp in posnData)
                            {
                                DecodedResults[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WARNING] Failed to load/parse POSN file: {ex.Message}");
                    }
                    
                    // Load MONTR (monitor data - cycle time, cutting time, etc.)
                    try
                    {
                        var montrLines = fileLoader.LoadFile("MONTR");
                        if (montrLines != null)
                        {
                            var montrData = fileLoader.ParseMontr(montrLines);
                            foreach (var kvp in montrData)
                            {
                                DecodedResults[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WARNING] Failed to load/parse MONTR: {ex.Message}");
                    }
                    
                    // Load ATCTL/ATCTLD (ATC control - tools currently loaded in ATC magazine)
                    // C00 uses ATCTL, D00 uses ATCTLD
                    // Cross-reference with tool table (TOLNI1) for tool specs (diameter, length, group, life, type)
                    try
                    {
                        // Use version-specific filename
                        var atctlFilename = (detectedVersion == ControlVersion.D00) ? "ATCTLD" : "ATCTL";
                        var atctlLines = fileLoader.LoadFile(atctlFilename);
                        if (atctlLines != null && atctlLines.Length > 0)
                        {
                            Console.Error.WriteLine($"[DEBUG] Loaded {atctlFilename} ({atctlLines.Length} lines), first 10 lines:");
                            for (int i = 0; i < Math.Min(10, atctlLines.Length); i++)
                            {
                                Console.Error.WriteLine($"[DEBUG]   [{i}] {atctlLines[i]}");
                            }
                            
                            // Get tool table data for cross-referencing
                            var toolTableData = new Dictionary<string, string>();
                            foreach (var kvp in DecodedResults)
                            {
                                if (kvp.Key.StartsWith("Tool ") && (kvp.Key.Contains(" Length") || kvp.Key.Contains(" Diameter") || kvp.Key.Contains(" Group") || kvp.Key.Contains(" Life") || kvp.Key.Contains(" Name")))
                                {
                                    toolTableData[kvp.Key] = kvp.Value;
                                }
                            }
                            
                            var atcData = fileLoader.ParseAtctl(atctlLines, toolTableData);
                            if (atcData.ContainsKey("ATC Tools") && !string.IsNullOrWhiteSpace(atcData["ATC Tools"]))
                            {
                                // Validate: check for pot 10 -> tool 24
                                if (atcData["ATC Tools"].Contains("P10:T24"))
                                {
                                    Console.Error.WriteLine($"[INFO] SUCCESS! ATCTL contains pot 10 -> tool 24 mapping!");
                                }
                                else
                                {
                                    Console.Error.WriteLine($"[WARNING] ATCTL parsed but doesn't contain pot 10 -> tool 24. Found: {atcData["ATC Tools"].Substring(0, Math.Min(200, atcData["ATC Tools"].Length))}");
                                }
                                
                                foreach (var kvp in atcData)
                                {
                                    DecodedResults[kvp.Key] = kvp.Value;
                                }
                            }
                            else
                            {
                                Console.Error.WriteLine($"[WARNING] ATCTL loaded but ParseAtctl didn't extract any ATC tools. Need to fix parser.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WARNING] Failed to load/parse ATCTL: {ex.Message}");
                    }
                    
                    // Load PANEL (panel data - may contain ATC status)
                    try
                    {
                        var panelLines = fileLoader.LoadFile("PANEL");
                        if (panelLines != null)
                        {
                            var panelData = fileLoader.ParsePanel(panelLines);
                            foreach (var kvp in panelData)
                            {
                                DecodedResults[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WARNING] Failed to load/parse PANEL: {ex.Message}");
                    }
                    
                    // Load macro variable file (MCRNun) - uses MCRNI for Inch or MCRNM for Metric
                    try
                    {
                        var macroLines = fileLoader.LoadMacroFile(1); // Data bank 1 (MCRNI1 or MCRNM1)
                        if (macroLines != null && macroLines.Length > 0)
                        {
                            var macroData = fileLoader.ParseMacro(macroLines);
                            foreach (var kvp in macroData)
                            {
                                DecodedResults[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WARNING] Failed to load/parse macro variable file: {ex.Message}");
                    }
                }
                
                // Update MTConnect server with all decoded data
                mtconnectServer.UpdateData(DecodedResults);

                /*
                var filesToLoad = new List<String>
                {
                    "POSNI#",
                    "POSSI#",
                    "TOLSI#",
                    "MCRNI#",
                    "MCRSI#",
                    "EXIO#",
                    "ATCTL",
                    "GCOMT",
                    "CSTPL1",
                    "CSTTP1",
                    "SYSC99",
                    "SYSC98",
                    "SYSC97",
                    "SYSC96",
                    "SYSC95",
                    "SYSC94",
                    "SYSC89",
                    "PRD1",
                    "PRDC2",
                    "PRD3",
                    "MAINTC",
                    "WKCNTR",
                    "MSRRSC",
                    "PAINT",
                    "WVPRM",
                    "PLCDAT",
                    "PLCMON",
                    "SHTCUT",
                    "IO",
                    "MEM",
                    "PANEL",
                    "PDSP",
                    "VER",
                    "LOG",
                    "LOGBK",
                    "ALARM",
                    "OPLOG",
                    "PRTCTC"
                };

                foreach (var file in filesToLoad)
                {                    
                    if (file.Contains("#"))
                    {
                        for (int i = 0; i <= 10 ; i++)
                        {
                            var toLoad = file.Replace('#', i.ToString().Last()); // 0 is 10, not 0
                            req.Arguments = toLoad;
                            File.WriteAllText(toLoad + ".RAW", req.Send());
                            Console.WriteLine($"Loaded {toLoad}");
                        }
                    }
                    else
                    {
                        req.Arguments = file;
                        File.WriteAllText(file + ".RAW", req.Send());
                        Console.WriteLine($"Loaded {file}");
                    }
                    Thread.Sleep(500);
                } //*/

                //req.Arguments = "PANEL";
                //Console.Write(req.Send());
                //req.Arguments = "MEM";
                //Console.Write(req.Send());
                //req.Arguments = "ALARM";
                //Console.Write(req.Send());
                //req.Arguments = "TOLNI1";
                //Console.Write(req.Send());
                //req.Arguments = "MCRNI1";
                //Console.Write(req.Send());

                //*
                foreach (var decode in DecodedResults)
                {
                    Console.WriteLine($"{decode.Key}: {decode.Value}");
                } //*/

                //throw new NotImplementedException();

                Thread.Sleep(2000);
            }
        }

        /// <summary>
        /// Runs schema validation and exits with appropriate exit code.
        /// </summary>
        private static void RunSchemaValidation()
        {
            Console.WriteLine("========================================");
            Console.WriteLine("MTConnect 2.5 Schema Validation");
            Console.WriteLine("========================================");
            Console.WriteLine();

            var validator = new XmlValidator("schemas");
            var server = new MTConnectServer(7878);
            
            int errorCount = 0;
            bool allPassed = true;

            // Validate Probe XML
            Console.WriteLine("[1/2] Validating Probe XML (MTConnectDevices_2.5.xsd)...");
            try
            {
                var probeMethod = typeof(MTConnectServer).GetMethod("GenerateProbeXml",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (probeMethod == null)
                {
                    Console.WriteLine("[ERROR] Could not find GenerateProbeXml method");
                    Environment.Exit(1);
                    return;
                }

                var probeXml = probeMethod.Invoke(server, null) as string;
                
                if (string.IsNullOrEmpty(probeXml))
                {
                    Console.WriteLine("[ERROR] GenerateProbeXml returned null or empty");
                    Environment.Exit(1);
                    return;
                }

                var errors = validator.ValidateDevicesXml(probeXml);
                if (errors.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Validation Errors:");
                    foreach (var error in errors)
                    {
                        Console.WriteLine($"  {error}");
                        errorCount++;
                    }
                    Console.WriteLine();
                    allPassed = false;
                }
                else
                {
                    Console.WriteLine("[PASS] Probe XML is valid");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Exception during probe validation: {ex.Message}");
                Console.WriteLine($"        Stack trace: {ex.StackTrace}");
                errorCount++;
                allPassed = false;
            }

            Console.WriteLine();

            // Validate Current XML
            Console.WriteLine("[2/2] Validating Current XML (MTConnectStreams_2.5.xsd)...");
            try
            {
                var currentMethod = typeof(MTConnectServer).GetMethod("GenerateCurrentXml",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                
                if (currentMethod == null)
                {
                    Console.WriteLine("[ERROR] Could not find GenerateCurrentXml method");
                    Environment.Exit(1);
                    return;
                }

                var currentXml = currentMethod.Invoke(server, null) as string;
                
                if (string.IsNullOrEmpty(currentXml))
                {
                    Console.WriteLine("[ERROR] GenerateCurrentXml returned null or empty");
                    Environment.Exit(1);
                    return;
                }

                var errors = validator.ValidateStreamsXml(currentXml);
                if (errors.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Validation Errors:");
                    foreach (var error in errors)
                    {
                        Console.WriteLine($"  {error}");
                        errorCount++;
                    }
                    Console.WriteLine();
                    allPassed = false;
                }
                else
                {
                    Console.WriteLine("[PASS] Current XML is valid");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Exception during current validation: {ex.Message}");
                Console.WriteLine($"        Stack trace: {ex.StackTrace}");
                errorCount++;
                allPassed = false;
            }

            Console.WriteLine();
            Console.WriteLine("========================================");
            if (allPassed)
            {
                Console.WriteLine("✅ All schema validations PASSED");
                Console.WriteLine("========================================");
                Environment.Exit(0);
            }
            else
            {
                Console.WriteLine($"❌ Schema validation FAILED ({errorCount} error(s))");
                Console.WriteLine("========================================");
                Environment.Exit(1);
            }
        }
    }
}

