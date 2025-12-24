using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BrotherConnection.Schema;

namespace BrotherConnection
{
    /// <summary>
    /// Helper class to load and parse different file types from Brother CNC via LOD command.
    /// </summary>
    internal class FileLoader
    {
        private Request _request;
        private ControlVersion _controlVersion;
        private UnitSystem _unitSystem;

        public FileLoader(ControlVersion controlVersion = ControlVersion.C00, UnitSystem unitSystem = UnitSystem.Metric, Request request = null)
        {
            _controlVersion = controlVersion;
            _unitSystem = unitSystem;
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
        /// Load a file and return raw response as string array (lines).
        /// </summary>
        public string[] LoadFile(string fileName)
        {
            try
            {
                _request.Arguments = fileName;
                var response = _request.Send();
                
                // Remove protocol wrapper (%...%)
                // Response format: %<command>\r\n<data>\r\n<checksum>%\r\n
                if (response.StartsWith("%"))
                {
                    // Find first \r\n (end of command line)
                    var firstNewline = response.IndexOf("\r\n", StringComparison.Ordinal);
                    if (firstNewline > 0)
                    {
                        // Find last \r\n before final %
                        var lastPercent = response.LastIndexOf("%");
                        var lastNewline = response.LastIndexOf("\r\n", lastPercent);
                        if (lastNewline > firstNewline)
                        {
                            // Extract data between first and last \r\n
                            response = response.Substring(firstNewline + 2, lastNewline - firstNewline - 2);
                        }
                    }
                }
                
                return response.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Failed to load file {fileName}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Load POSN file (workpiece coordinate zero) based on unit system.
        /// POSNI = Inch, POSNM = Metric
        /// </summary>
        /// <param name="dataBankNumber">Data bank number (0-9, where 0 represents 10)</param>
        /// <returns>File contents as string array, or null if load fails</returns>
        public string[] LoadPosnFile(int dataBankNumber = 1)
        {
            // Determine filename based on unit system
            string fileName;
            if (_unitSystem == UnitSystem.Inch)
            {
                fileName = $"POSNI{dataBankNumber}";
                Console.Error.WriteLine($"[DEBUG] Loading POSN file for Inch unit system: {fileName}");
            }
            else
            {
                fileName = $"POSNM{dataBankNumber}";
                Console.Error.WriteLine($"[DEBUG] Loading POSN file for Metric unit system: {fileName}");
            }
            
            return LoadFile(fileName);
        }

        /// <summary>
        /// Load TOLN file (tool table) based on unit system.
        /// TOLNI = Inch, TOLNM = Metric
        /// </summary>
        /// <param name="dataBankNumber">Data bank number (0-9, where 0 represents 10)</param>
        /// <returns>File contents as string array, or null if load fails</returns>
        public string[] LoadTolniFile(int dataBankNumber = 1)
        {
            // Determine filename based on unit system
            string fileName;
            if (_unitSystem == UnitSystem.Inch)
            {
                fileName = $"TOLNI{dataBankNumber}";
                Console.Error.WriteLine($"[DEBUG] Loading TOLN file for Inch unit system: {fileName}");
            }
            else
            {
                fileName = $"TOLNM{dataBankNumber}";
                Console.Error.WriteLine($"[DEBUG] Loading TOLN file for Metric unit system: {fileName}");
            }
            
            return LoadFile(fileName);
        }

        /// <summary>
        /// Load macro variable file (Type 1: MCRNun) based on unit system.
        /// MCRNI = Inch, MCRNM = Metric
        /// </summary>
        /// <param name="dataBankNumber">Data bank number (0-9, where 0 represents 10)</param>
        /// <returns>File contents as string array, or null if load fails</returns>
        public string[] LoadMacroFile(int dataBankNumber = 1)
        {
            // Determine filename based on unit system
            string fileName;
            if (_unitSystem == UnitSystem.Inch)
            {
                fileName = $"MCRNI{dataBankNumber}";
                Console.Error.WriteLine($"[DEBUG] Loading macro variable file for Inch unit system: {fileName}");
            }
            else
            {
                fileName = $"MCRNM{dataBankNumber}";
                Console.Error.WriteLine($"[DEBUG] Loading macro variable file for Metric unit system: {fileName}");
            }
            
            return LoadFile(fileName);
        }

        /// <summary>
        /// Parse MEM file - simple format, just contains program name (O####).
        /// </summary>
        public Dictionary<string, string> ParseMem(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

            // Version and unit-specific parsing (currently C00 and D00 use same format for MEM)
            if (_controlVersion == ControlVersion.D00)
            {
                Console.Error.WriteLine($"[DEBUG] Using D00 schema for MEM parsing");
            }
            else
            {
                Console.Error.WriteLine($"[DEBUG] Using C00 schema for MEM parsing");
            }
            
            if (_unitSystem == UnitSystem.Inch)
            {
                Console.Error.WriteLine($"[DEBUG] Using Inch unit system for MEM parsing");
            }
            else
            {
                Console.Error.WriteLine($"[DEBUG] Using Metric unit system for MEM parsing");
            }

            // MEM file typically contains just the program name like "O2045" or "O2045.NC"
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                var trimmed = line.Trim();
                // Look for O-number pattern
                if (trimmed.StartsWith("O", StringComparison.OrdinalIgnoreCase))
                {
                    // Extract O-number (remove .NC extension if present)
                    var programName = trimmed;
                    if (programName.EndsWith(".NC", StringComparison.OrdinalIgnoreCase))
                    {
                        programName = programName.Substring(0, programName.Length - 3);
                    }
                    result["Program name"] = programName.ToUpper();
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// Parse ALARM file - contains current alarm status.
        /// Format: [prefix][severity][code];
        /// Example: 052039; = IO2039 (05=IO prefix, 90=error severity, 2039=code)
        /// </summary>
        public Dictionary<string, string> ParseAlarm(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

            // Version and unit-specific parsing
            if (_controlVersion == ControlVersion.D00)
            {
                Console.Error.WriteLine($"[DEBUG] Using D00 schema for ALARM parsing");
            }
            else
            {
                Console.Error.WriteLine($"[DEBUG] Using C00 schema for ALARM parsing");
            }
            
            if (_unitSystem == UnitSystem.Inch)
            {
                Console.Error.WriteLine($"[DEBUG] Using Inch unit system for ALARM parsing");
            }
            else
            {
                Console.Error.WriteLine($"[DEBUG] Using Metric unit system for ALARM parsing");
            }

            var alarms = new List<string>();
            int alarmIndex = 0;

            // Debug: log first few lines to understand format
            Console.Error.WriteLine($"[DEBUG] ParseAlarm: Processing {lines.Length} lines");
            for (int i = 0; i < Math.Min(10, lines.Length); i++)
            {
                Console.Error.WriteLine($"[DEBUG] ParseAlarm: Line {i}: '{lines[i]}'");
            }
            
            // Map category codes to alarm prefixes (from Brother documentation)
            // Format: [category(2)][alarm_number(4)] = 6 digits total
            // Note: D00 may have different category mappings or digit counts
            var categoryMap = new Dictionary<string, string>
            {
                { "01", "EX" },
                { "02", "EC" },
                { "03", "SV" },
                { "04", "NC" },
                { "05", "IO" },
                { "06", "SP" },
                { "07", "SM" },
                { "08", "SL" },
                { "09", "CM" },
                { "10", "ES" },
                { "11", "FC" },
            };
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                var trimmed = line.Trim();
                // Skip comments
                if (trimmed.StartsWith("(") || (trimmed.StartsWith(";") && !trimmed.Contains("0")))
                    continue;

                // Remove semicolon if present
                if (trimmed.EndsWith(";"))
                {
                    trimmed = trimmed.Substring(0, trimmed.Length - 1);
                }
                
                // Format: LINE_ID,ALARM_CODE1,ALARM_CODE2,...
                // Example: E01,050518    ,052039    ,052047    ,907516    ,077586
                // Where E01 is a line identifier, and the rest are alarm codes (with trailing spaces)
                var parts = trimmed.Split(',');
                if (parts.Length < 2)
                    continue;
                
                // Skip first part (line identifier like E01, L01)
                // Process each alarm code
                for (int i = 1; i < parts.Length; i++)
                {
                    var alarmStr = parts[i].Trim();
                    if (string.IsNullOrWhiteSpace(alarmStr) || alarmStr == "0")
                        continue;
                    
                    // Format: [category(2)][alarm_number(4)] = 6 digits total
                    // Example: 050518 = 05 (IO) + 0518 (alarm number) -> IO0518
                    // Example: 052039 = 05 (IO) + 2039 (alarm number) -> IO2039
                    // Example: 077586 = 07 (SM) + 7586 (alarm number) -> SM7586
                    // Example: 097541 = 09 (CM) + 7541 (alarm number) -> CM7541
                    // Some codes may have extra digits (e.g., 9075110018 -> should be 7511, but 90 is not a valid category)
                    
                    string alarmCode = "";
                    string severity = "error"; // Default severity
                    
                    if (alarmStr.Length >= 4 && alarmStr.All(char.IsDigit))
                    {
                        // Standard format: 6 digits = [category(2)][alarm_number(4)]
                        if (alarmStr.Length >= 6)
                        {
                            string categoryCode = alarmStr.Substring(0, 2);
                            if (categoryMap.ContainsKey(categoryCode))
                            {
                                string category = categoryMap[categoryCode];
                                // Alarm number is next 4 digits
                                string alarmNumber = alarmStr.Substring(2, 4);
                                alarmCode = category + alarmNumber;
                            }
                            else
                            {
                                // Invalid category code - use just the 4-digit alarm number (skip category)
                                // This handles cases where the category code is not recognized
                                alarmCode = alarmStr.Substring(2, 4);
                            }
                        }
                        // Shorter codes (4 digits) - no category, just alarm number
                        else if (alarmStr.Length == 4)
                        {
                            alarmCode = alarmStr;
                        }
                        // Handle malformed codes with extra digits
                        else if (alarmStr.Length > 6)
                        {
                            // Try to find a valid 6-digit code within
                            // Example: 9075110018 -> might be 751100 or 7511
                            // Look for valid category in first 2 digits of any 6-digit window
                            bool found = false;
                            for (int start = 0; start <= alarmStr.Length - 6 && !found; start++)
                            {
                                string categoryCode = alarmStr.Substring(start, 2);
                                if (categoryMap.ContainsKey(categoryCode))
                                {
                                    string category = categoryMap[categoryCode];
                                    string alarmNumber = alarmStr.Substring(start + 2, 4);
                                    alarmCode = category + alarmNumber;
                                    found = true;
                                }
                            }
                            if (!found)
                            {
                                // No valid category found, extract 4-digit alarm number
                                // Remove trailing zeros and take last 4 digits
                                string trimmedAlarm = alarmStr.TrimEnd('0');
                                if (trimmedAlarm.Length >= 4)
                                {
                                    alarmCode = trimmedAlarm.Substring(Math.Max(0, trimmedAlarm.Length - 4));
                                }
                                else
                                {
                                    alarmCode = alarmStr.Substring(0, 4);
                                }
                            }
                        }
                        
                        if (!string.IsNullOrWhiteSpace(alarmCode) && alarmCode != "0")
                        {
                            // Store individual alarm
                            result[$"Alarm {alarmIndex} Code"] = alarmCode;
                            result[$"Alarm {alarmIndex} Message"] = ""; // Message not in ALARM file format
                            result[$"Alarm {alarmIndex} Severity"] = severity;
                            
                            alarms.Add($"{alarmCode}:{severity}");
                            alarmIndex++;
                        }
                    }
                }
            }

            // Store alarm count and combined alarm string
            result["Alarm count"] = alarmIndex.ToString();
            if (alarms.Count > 0)
            {
                result["Alarms"] = string.Join("|", alarms);
            }

            return result;
        }

        /// <summary>
        /// Parse WKCNTR file - workpiece counter data.
        /// Format: CSV-like with counter number, count, target, end_signal, status per line
        /// Example: 1,245,1000,0,normal or 1,245,1000,0
        /// </summary>
        public Dictionary<string, string> ParseWkcntr(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

            // Version and unit-specific parsing
            if (_controlVersion == ControlVersion.D00)
            {
                Console.Error.WriteLine($"[DEBUG] Using D00 schema for WKCNTR parsing");
            }
            else
            {
                Console.Error.WriteLine($"[DEBUG] Using C00 schema for WKCNTR parsing");
            }
            
            if (_unitSystem == UnitSystem.Inch)
            {
                Console.Error.WriteLine($"[DEBUG] Using Inch unit system for WKCNTR parsing");
            }
            else
            {
                Console.Error.WriteLine($"[DEBUG] Using Metric unit system for WKCNTR parsing");
            }

            for (int i = 0; i < lines.Length && i < 4; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Format: COUNTER_NUM,COUNT,TARGET,END_SIGNAL,STATUS
                var parts = line.Split(',');
                if (parts.Length >= 2)
                {
                    // Counter number (should match index + 1)
                    int counterNum = i + 1;
                    if (int.TryParse(parts[0].Trim(), out int parsedNum))
                    {
                        counterNum = parsedNum;
                    }

                    // Count value
                    if (int.TryParse(parts[1].Trim(), out int count))
                    {
                        result[$"Counter {counterNum}"] = count.ToString();
                    }

                    // Target value (if present)
                    if (parts.Length > 2 && int.TryParse(parts[2].Trim(), out int target))
                    {
                        result[$"Counter {counterNum} Target"] = target.ToString();
                    }

                    // End signal (if present)
                    if (parts.Length > 3 && int.TryParse(parts[3].Trim(), out int endSignal))
                    {
                        result[$"Counter {counterNum} End Signal"] = endSignal.ToString();
                    }

                    // Status (if present)
                    if (parts.Length > 4)
                    {
                        var status = parts[4].Trim();
                        if (!string.IsNullOrWhiteSpace(status))
                        {
                            result[$"Counter {counterNum} Status"] = status;
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Parse ATCTL/ATCTLD file - ATC (Automatic Tool Changer) control data.
        /// C00 Format (ATCTL):
        ///   - M01 = Spindle (current tool)
        ///   - M02-M51 = Pots 1-50
        ///   Each entry: M##,tool_num,conversation_nc,group_main_tool,type,color
        ///   Fields: Tool No. (0, 1-99, 255), Conversation/NC (0-1), Group/Main tool (0-30/0-99), Type (1-3), Color (0-7)
        /// D00 Format (ATCTLD):
        ///   - M01-M51 = Spindle and Pots 1-50 (same structure, but Tool No. includes 201-299, 999 for cap)
        ///   - Additional field: Store tool stocker (0-1)
        ///   - R01-R51 = Tool stocker (right) - same 6 fields as M01-M51
        ///   - L01-L51 = Tool stocker (left) - same 6 fields as M01-M51
        ///   - W01 = Stocker attributes (right) - Maximum tool for storing (1 or 3)
        ///   - E01 = Stocker attributes (left) - Maximum tool for storing (1 or 3)
        /// Tool data (diameter, length, name, life) should be cross-referenced with tool table (TOLNI1).
        /// All fields are unitless (no conversion needed).
        /// </summary>
        /// <param name="lines">ATCTL/ATCTLD file lines</param>
        /// <param name="toolTableData">Optional tool table data dictionary to cross-reference tool specs</param>
        public Dictionary<string, string> ParseAtctl(string[] lines, Dictionary<string, string> toolTableData = null)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
            {
                Console.Error.WriteLine("[DEBUG] ParseAtctl: lines is null or empty");
                return result;
            }

            // Get schema configuration based on control version
            var schema = AtctlSchemaConfig.GetConfig(_controlVersion);
            
            Console.Error.WriteLine($"[INFO] Parsing ATCTL using {_controlVersion} schema, {_unitSystem} units");
            Console.Error.WriteLine($"[DEBUG] Filename: {schema.Filename}, Supports extended tool numbers: {schema.SupportsExtendedToolNumbers}, Has store tool stocker: {schema.HasStoreToolStockerField}");

            var atcTools = new List<string>();
            var stockerTools = new List<string>();
            int potCount = 0;
            int stockerCount = 0;
            int skippedLines = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                var trimmed = line.Trim();
                // Skip comments
                if (trimmed.StartsWith("(") || trimmed.StartsWith(";"))
                    continue;

                var parts = trimmed.Split(',');
                if (parts.Length < 2)
                    continue;
                
                var prefix = parts[0].Trim().ToUpper();
                
                // Parse M01-M51 entries (Spindle and Pots)
                if (prefix.StartsWith("M"))
                {
                    int mNum = -1;
                    if (int.TryParse(prefix.Substring(1), out int parsedMNum))
                    {
                        mNum = parsedMNum;
                    }
                    
                    if (mNum < 1 || mNum > 51)
                        continue;
                    
                    // Parse all fields
                    int toolNum = 0;
                    int conversationNc = 0;
                    int groupMainTool = 0;
                    int type = 1;
                    int color = 0;
                    int storeToolStocker = 0; // D00 only
                    
                    // Field 1: Tool No.
                    if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out toolNum))
                    {
                        // Validate tool number against schema
                        if (!schema.IsValidToolNumber(toolNum))
                        {
                            skippedLines++;
                            continue;
                        }
                    }
                    else
                    {
                        skippedLines++;
                        continue;
                    }
                    
                    // Field 2: Conversation/NC
                    if (parts.Length > 2)
                    {
                        int.TryParse(parts[2].Trim(), out conversationNc);
                    }
                    
                    // Field 3: Group No. (NC) / Main tool No. (Conversation)
                    if (parts.Length > 3)
                    {
                        int.TryParse(parts[3].Trim(), out groupMainTool);
                    }
                    
                    // Field 4: Type
                    if (parts.Length > 4)
                    {
                        var typeStr = parts[4].Trim();
                        if (int.TryParse(typeStr, out int parsedType))
                        {
                            if (schema.IsValidType(parsedType))
                            {
                                type = parsedType;
                            }
                        }
                    }
                    
                    // Field 5: Graph color
                    if (parts.Length > 5)
                    {
                        var colorStr = parts[5].Trim().Replace("\r", "").Replace("\n", "").Trim();
                        int.TryParse(colorStr, out color);
                    }
                    
                    // Field 6: Store tool stocker (D00 only)
                    if (schema.HasStoreToolStockerField && parts.Length > 6)
                    {
                        var storeStr = parts[6].Trim().Replace("\r", "").Replace("\n", "").Trim();
                        int.TryParse(storeStr, out storeToolStocker);
                    }
                    
                    // M01 = Spindle (current tool), M02-M51 = Pots 1-50
                    if (mNum == 1)
                    {
                        // Spindle - store as special entry
                        // Get tool name from tool table for reference (not in ATCTL schema)
                        var toolName = "";
                        if (toolTableData != null)
                        {
                            var toolKey = $"Tool {toolNum}";
                            if (toolTableData.ContainsKey($"{toolKey} Name"))
                                toolName = toolTableData[$"{toolKey} Name"];
                        }
                        
                        // Store spindle data - ONLY fields from ATCTL schema
                        result["ATC Spindle Tool Number"] = toolNum.ToString();
                        result["ATC Spindle Tool Name"] = toolName; // Cross-referenced for convenience
                        result["ATC Spindle Conversation/NC"] = conversationNc.ToString();
                        result["ATC Spindle Group/Main Tool"] = groupMainTool.ToString();
                        result["ATC Spindle Type"] = type.ToString();
                        result["ATC Spindle Color"] = color.ToString();
                        if (schema.HasStoreToolStockerField)
                        {
                            result["ATC Spindle Store Tool Stocker"] = storeToolStocker.ToString();
                        }
                        
                        // Build output string with ONLY schema fields (no LEN, DIA, LIFE - those are in tool table)
                        var spindleData = new StringBuilder();
                        spindleData.Append($"SPINDLE:T{toolNum}:{toolName}:");
                        spindleData.Append($"CONVNC={conversationNc}:");
                        spindleData.Append($"GRP={groupMainTool}:");
                        spindleData.Append($"TYPE={type}:COL={color}");
                        if (schema.HasStoreToolStockerField)
                        {
                            spindleData.Append($":STORE={storeToolStocker}");
                        }
                        atcTools.Add(spindleData.ToString());
                    }
                    else if (mNum >= 2 && mNum <= 51)
                    {
                        // Pot entry (M02 = pot 1, M51 = pot 50)
                        int potNum = mNum - 1;
                        
                        // Get tool name from tool table for reference (not in ATCTL schema)
                        var toolName = "";
                        if (toolTableData != null)
                        {
                            var toolKey = $"Tool {toolNum}";
                            if (toolTableData.ContainsKey($"{toolKey} Name"))
                                toolName = toolTableData[$"{toolKey} Name"];
                        }
                        
                        // Store individual pot data - ONLY fields from ATCTL schema
                        result[$"ATC Pot {potNum} Tool Number"] = toolNum.ToString();
                        result[$"ATC Pot {potNum} Tool Name"] = toolName; // Cross-referenced for convenience
                        result[$"ATC Pot {potNum} Conversation/NC"] = conversationNc.ToString();
                        result[$"ATC Pot {potNum} Group/Main Tool"] = groupMainTool.ToString();
                        result[$"ATC Pot {potNum} Type"] = type.ToString();
                        result[$"ATC Pot {potNum} Color"] = color.ToString();
                        if (schema.HasStoreToolStockerField)
                        {
                            result[$"ATC Pot {potNum} Store Tool Stocker"] = storeToolStocker.ToString();
                        }
                        
                        // Build output string with ONLY schema fields (no LEN, DIA, LIFE - those are in tool table)
                        var potData = new StringBuilder();
                        potData.Append($"P{potNum}:T{toolNum}:{toolName}:");
                        potData.Append($"CONVNC={conversationNc}:");
                        potData.Append($"GRP={groupMainTool}:");
                        potData.Append($"TYPE={type}:COL={color}");
                        if (schema.HasStoreToolStockerField)
                        {
                            potData.Append($":STORE={storeToolStocker}");
                        }
                        atcTools.Add(potData.ToString());
                        potCount++;
                    }
                }
                // Parse R01-R51 entries (Tool stocker right, D00 only)
                else if (schema.HasToolStockerEntries && prefix.StartsWith("R"))
                {
                    int rNum = -1;
                    if (int.TryParse(prefix.Substring(1), out int parsedRNum))
                    {
                        rNum = parsedRNum;
                    }
                    
                    if (rNum < 1 || rNum > 51)
                        continue;
                    
                    int stockerNum = rNum; // R01 = stocker 1
                    
                    // Parse all fields (same structure as M01-M51)
                    int toolNum = 0;
                    int conversationNc = 0;
                    int groupMainTool = 0;
                    int type = 1;
                    int color = 0;
                    int storeToolStocker = 0;
                    
                    if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out toolNum))
                    {
                        if (!schema.IsValidToolNumber(toolNum))
                        {
                            skippedLines++;
                            continue;
                        }
                    }
                    else
                    {
                        skippedLines++;
                        continue;
                    }
                    
                    if (parts.Length > 2)
                        int.TryParse(parts[2].Trim(), out conversationNc);
                    if (parts.Length > 3)
                        int.TryParse(parts[3].Trim(), out groupMainTool);
                    if (parts.Length > 4)
                    {
                        var typeStr = parts[4].Trim();
                        if (int.TryParse(typeStr, out int parsedType))
                        {
                            // Right stocker: Type 1=Standard, 3=Medium (per schema)
                            if (parsedType == 1 || parsedType == 3)
                                type = parsedType;
                        }
                    }
                    if (parts.Length > 5)
                    {
                        var colorStr = parts[5].Trim().Replace("\r", "").Replace("\n", "").Trim();
                        int.TryParse(colorStr, out color);
                    }
                    if (parts.Length > 6)
                    {
                        var storeStr = parts[6].Trim().Replace("\r", "").Replace("\n", "").Trim();
                        int.TryParse(storeStr, out storeToolStocker);
                    }
                    
                    var toolName = "";
                    if (toolTableData != null)
                    {
                        var toolKey = $"Tool {toolNum}";
                        if (toolTableData.ContainsKey($"{toolKey} Name"))
                            toolName = toolTableData[$"{toolKey} Name"];
                    }
                    
                    // Store stocker data
                    result[$"ATC Stocker Right {stockerNum} Tool Number"] = toolNum.ToString();
                    result[$"ATC Stocker Right {stockerNum} Tool Name"] = toolName;
                    result[$"ATC Stocker Right {stockerNum} Conversation/NC"] = conversationNc.ToString();
                    result[$"ATC Stocker Right {stockerNum} Group"] = groupMainTool.ToString();
                    result[$"ATC Stocker Right {stockerNum} Type"] = type.ToString();
                    result[$"ATC Stocker Right {stockerNum} Color"] = color.ToString();
                    result[$"ATC Stocker Right {stockerNum} Store Tool Stocker"] = storeToolStocker.ToString();
                    
                    var stockerData = new StringBuilder();
                    stockerData.Append($"R{stockerNum}:T{toolNum}:{toolName}:");
                    stockerData.Append($"CONVNC={conversationNc}:GRP={groupMainTool}:");
                    stockerData.Append($"TYPE={type}:COL={color}:STORE={storeToolStocker}");
                    stockerTools.Add(stockerData.ToString());
                    stockerCount++;
                }
                // Parse L01-L51 entries (Tool stocker left, D00 only)
                else if (schema.HasToolStockerEntries && prefix.StartsWith("L"))
                {
                    int lNum = -1;
                    if (int.TryParse(prefix.Substring(1), out int parsedLNum))
                    {
                        lNum = parsedLNum;
                    }
                    
                    if (lNum < 1 || lNum > 51)
                        continue;
                    
                    int stockerNum = lNum; // L01 = stocker 1
                    
                    // Parse all fields (same structure as M01-M51)
                    int toolNum = 0;
                    int conversationNc = 0;
                    int groupMainTool = 0;
                    int type = 1;
                    int color = 0;
                    int storeToolStocker = 0;
                    
                    if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out toolNum))
                    {
                        if (!schema.IsValidToolNumber(toolNum))
                        {
                            skippedLines++;
                            continue;
                        }
                    }
                    else
                    {
                        skippedLines++;
                        continue;
                    }
                    
                    if (parts.Length > 2)
                        int.TryParse(parts[2].Trim(), out conversationNc);
                    if (parts.Length > 3)
                        int.TryParse(parts[3].Trim(), out groupMainTool);
                    if (parts.Length > 4)
                    {
                        var typeStr = parts[4].Trim();
                        if (int.TryParse(typeStr, out int parsedType))
                        {
                            // Left stocker: Type 1=Standard, 3=Medium (per schema)
                            if (parsedType == 1 || parsedType == 3)
                                type = parsedType;
                        }
                    }
                    if (parts.Length > 5)
                    {
                        var colorStr = parts[5].Trim().Replace("\r", "").Replace("\n", "").Trim();
                        int.TryParse(colorStr, out color);
                    }
                    if (parts.Length > 6)
                    {
                        var storeStr = parts[6].Trim().Replace("\r", "").Replace("\n", "").Trim();
                        int.TryParse(storeStr, out storeToolStocker);
                    }
                    
                    var toolName = "";
                    if (toolTableData != null)
                    {
                        var toolKey = $"Tool {toolNum}";
                        if (toolTableData.ContainsKey($"{toolKey} Name"))
                            toolName = toolTableData[$"{toolKey} Name"];
                    }
                    
                    // Store stocker data
                    result[$"ATC Stocker Left {stockerNum} Tool Number"] = toolNum.ToString();
                    result[$"ATC Stocker Left {stockerNum} Tool Name"] = toolName;
                    result[$"ATC Stocker Left {stockerNum} Conversation/NC"] = conversationNc.ToString();
                    result[$"ATC Stocker Left {stockerNum} Group"] = groupMainTool.ToString();
                    result[$"ATC Stocker Left {stockerNum} Type"] = type.ToString();
                    result[$"ATC Stocker Left {stockerNum} Color"] = color.ToString();
                    result[$"ATC Stocker Left {stockerNum} Store Tool Stocker"] = storeToolStocker.ToString();
                    
                    var stockerData = new StringBuilder();
                    stockerData.Append($"L{stockerNum}:T{toolNum}:{toolName}:");
                    stockerData.Append($"CONVNC={conversationNc}:GRP={groupMainTool}:");
                    stockerData.Append($"TYPE={type}:COL={color}:STORE={storeToolStocker}");
                    stockerTools.Add(stockerData.ToString());
                    stockerCount++;
                }
                // Parse W01 and E01 entries (Stocker attributes, D00 only)
                else if (schema.HasStockerAttributes && (prefix == "W01" || prefix == "E01"))
                {
                    int maxToolForStoring = 1;
                    if (parts.Length > 1)
                    {
                        var maxToolStr = parts[1].Trim().Replace("\r", "").Replace("\n", "").Trim();
                        if (int.TryParse(maxToolStr, out int parsedMaxTool))
                        {
                            maxToolForStoring = parsedMaxTool; // 1: Standard, 3: Medium
                        }
                    }
                    
                    if (prefix == "W01")
                    {
                        result["ATC Stocker Attributes Right Maximum Tool"] = maxToolForStoring.ToString();
                    }
                    else if (prefix == "E01")
                    {
                        result["ATC Stocker Attributes Left Maximum Tool"] = maxToolForStoring.ToString();
                    }
                }
                else
                {
                    skippedLines++;
                    if (skippedLines <= 5)
                    {
                        Console.Error.WriteLine($"[DEBUG] ParseAtctl: Unrecognized prefix: {prefix}");
                    }
                }
            }
            
            Console.Error.WriteLine($"[DEBUG] ParseAtctl: Total lines processed: {lines.Length}, Pots found: {potCount}, Stockers found: {stockerCount}, Skipped: {skippedLines}");

            // Store ATC tool count and combined ATC tool list
            result["ATC Tool count"] = potCount.ToString();
            if (atcTools.Count > 0)
            {
                result["ATC Tools"] = string.Join("|", atcTools);
            }
            
            // Store stocker data (D00 only)
            if (schema.HasToolStockerEntries && stockerTools.Count > 0)
            {
                result["ATC Stocker count"] = stockerCount.ToString();
                result["ATC Stockers"] = string.Join("|", stockerTools);
            }

            return result;
        }


        /// <summary>
        /// Parse TPTNC1 file - Tool Pattern file, contains ATC pot-to-tool mappings.
        /// Format: P###,tool1,tool2,tool3... where P### is pot number and tools are tool numbers
        /// Example: P012,1 means pot 12 has tool 1
        /// Example: P021,1,2,6 means pot 21 has tools 1, 2, and 6
        /// </summary>
        public Dictionary<string, string> ParseTptnc1(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
            {
                return result;
            }

            var atcTools = new List<string>();
            int toolCount = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                var trimmed = line.Trim();
                // Skip comments
                if (trimmed.StartsWith("(") || trimmed.StartsWith(";"))
                    continue;

                // Format: P###,tool1,tool2,tool3...
                // Pot number is in first column with "P" prefix
                var parts = trimmed.Split(',');
                if (parts.Length >= 2 && parts[0].StartsWith("P"))
                {
                    // Extract pot number from "P012" -> 12
                    var potStr = parts[0].Substring(1); // Remove "P"
                    if (int.TryParse(potStr, out int potNum))
                    {
                        // First tool number is in second column
                        if (int.TryParse(parts[1].Trim(), out int toolNum))
                        {
                            // Get tool name from tool table if available (we'll need to cross-reference)
                            // For now, just use the tool number as the name
                            var toolName = $"Tool {toolNum}";
                            
                            // Default values (we don't have length/diameter/group/life/type/color from this file)
                            var length = "0";
                            var diameter = "0";
                            var group = "";
                            var life = "0";
                            var toolType = "1";
                            var color = "0";

                            // Store as ATC tool entry
                            atcTools.Add($"P{potNum}:T{toolNum}:{toolName}:LEN={length}:DIA={diameter}:GRP={group}:LIFE={life}:TYPE={toolType}:COL={color}");
                            toolCount++;
                            
                            // If there are multiple tools in this pot (parts.Length > 2), we only take the first one
                            // as the ATC typically has one tool per pot
                        }
                    }
                }
            }
            
            Console.Error.WriteLine($"[DEBUG] ParseTptnc1: Total lines processed: {lines.Length}, Tools found: {toolCount}");

            // Store ATC tool count and combined ATC tool list
            result["ATC Tool count"] = toolCount.ToString();
            if (atcTools.Count > 0)
            {
                result["ATC Tools"] = string.Join("|", atcTools);
            }

            return result;
        }

        /// <summary>
        /// Parse PANEL file - panel/operator interface data.
        /// May contain ATC status or other panel-related information.
        /// Format: CSV-like or key-value pairs
        /// Note: D00 may have different field positions or field names.
        /// </summary>
        public Dictionary<string, string> ParsePanel(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

            // Version and unit-specific parsing
            if (_controlVersion == ControlVersion.D00)
            {
                Console.Error.WriteLine($"[DEBUG] Using D00 schema for PANEL parsing");
            }
            else
            {
                Console.Error.WriteLine($"[DEBUG] Using C00 schema for PANEL parsing");
            }
            
            if (_unitSystem == UnitSystem.Inch)
            {
                Console.Error.WriteLine($"[DEBUG] Using Inch unit system for PANEL parsing");
            }
            else
            {
                Console.Error.WriteLine($"[DEBUG] Using Metric unit system for PANEL parsing");
            }

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                var trimmed = line.Trim();
                // Skip comments
                if (trimmed.StartsWith("(") || trimmed.StartsWith(";"))
                    continue;

                // Try CSV format first
                if (trimmed.Contains(","))
                {
                    var parts = trimmed.Split(',');
                    // Store as key-value pairs based on position or try to identify fields
                    for (int i = 0; i < parts.Length; i++)
                    {
                        var value = parts[i].Trim();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            result[$"Panel Field {i}"] = value;
                        }
                    }
                }
                // Try key-value format
                else if (trimmed.Contains("="))
                {
                    var parts = trimmed.Split('=');
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();
                        result[key] = value;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Parse TOLN file - tool table data (comma-delimited format).
        /// C00 Format (from section_5_6_4_s300x1n.json):
        ///   T01-T99: Tool entries
        ///     Field 0: Tool number (T##)
        ///     Field 1: Tool length offset (8 chars)
        ///     Field 2: T length wear offset (7 chars)
        ///     Field 3: Cutter compensation (8 chars) - THIS IS THE DIAMETER
        ///     Field 4: Cutter wear offset (7 chars)
        ///     Field 5: Tool life unit (1 char, 1-4)
        ///     Field 6: Initial tool life / End of tool life (6 chars, 0-999999)
        ///     Field 7: Tool life warning (6 chars, 0-999999)
        ///     Field 8: Tool life (6 chars, 0-999999) - CURRENT TOOL LIFE
        ///     Field 9: Tool name (16 chars, wrapped in single quotes)
        ///     Field 10: Rotation feed (4 chars)
        ///     Field 11: S command value (5 chars, 1-99999)
        ///     Field 12: F command value (9 chars)
        ///     Field 13: Maximum speed (5 chars, 0-999999, 0 = rotation not possible)
        ///     Field 14: Tool wash (1 char, 0: Possible, 1: Not possible)
        ///     Field 15: CTS (1 char, 0: Possible, 1: Not possible)
        ///     Field 16: Tool type number (8 chars, 0-99999999, 0 = Not set)
        ///     Field 17: Tool position offset (X) (8 chars)
        ///     Field 18: Tool position wear offset (X) (7 chars)
        ///     Field 19: Tool position offset (Y) (8 chars)
        ///     Field 20: Tool position wear offset (Y) (7 chars)
        ///     Field 21: Virtual teeth direction (1 char)
        ///   Y01-Y99: Group data (tool numbers in groups)
        ///   M01-M99: Min/max values
        /// D00 Format (from section_3_6_5_d00.json):
        ///   T001-T300: Tool entries (3-digit tool numbers)
        ///     Field differences: Tool life values are 7 chars (not 6), peripheral speed field added,
        ///     rotation feed at field 11 (not 10), F command at field 13 (not 12)
        /// Values are in machine native units (Metric/Inch) and must be converted to millimeters for MTConnect.
        /// </summary>
        public Dictionary<string, string> ParseTolni(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

            // Get schema configuration based on control version
            var schema = TolniSchemaConfig.GetConfig(_controlVersion);
            
            Console.Error.WriteLine($"[INFO] Parsing TOLN using {_controlVersion} schema, {_unitSystem} units");
            Console.Error.WriteLine($"[DEBUG] Tool number format: {schema.ToolNumberFormat}, Max tools: {schema.MaxToolNumber}");

            var tools = new List<string>();
            int toolCount = 0;
            
            // Dictionary to store group assignments (from Y01-Y99 or Y001-Y300 entries)
            var groupAssignments = new Dictionary<int, int>(); // tool number -> group number

            // Debug: log first few lines to understand format
            Console.Error.WriteLine($"[DEBUG] ParseTolni: Processing {lines.Length} lines");
            for (int i = 0; i < Math.Min(3, lines.Length); i++)
            {
                Console.Error.WriteLine($"[DEBUG] ParseTolni: Line {i}: '{lines[i]}'");
            }
            
            // First pass: Parse Y entries (group data) - Y01-Y99 for C00, Y001-Y300 for D00
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                var trimmed = line.Trim();
                // Skip comments
                if (trimmed.StartsWith("(") || trimmed.StartsWith(";"))
                    continue;

                var parts = trimmed.Split(',');
                if (parts.Length < 2)
                    continue;

                var firstField = parts[0].Trim().ToUpper();
                
                // Handle Y entries (group data)
                // Format: Y##,tool1,tool2,...,tool30 (up to 30 tools per group)
                if (firstField.StartsWith("Y"))
                {
                    // Parse group number (Y01 -> 1, Y001 -> 1, etc.)
                    var groupNumStr = firstField.Substring(1);
                    if (int.TryParse(groupNumStr, out int groupNum))
                    {
                        // Parse tool numbers in this group (fields 1-30)
                        for (int i = 1; i < parts.Length && i <= schema.MaxToolsPerGroup; i++)
                        {
                            var toolNumStr = parts[i].Trim();
                            if (int.TryParse(toolNumStr, out int toolNum) && toolNum >= 1 && toolNum <= schema.MaxToolNumber)
                            {
                                groupAssignments[toolNum] = groupNum;
                            }
                        }
                    }
                }
            }
            
            // Second pass: Parse T entries (tool data) - T01-T99 for C00, T001-T300 for D00
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                var trimmed = line.Trim();
                // Skip comments
                if (trimmed.StartsWith("(") || trimmed.StartsWith(";"))
                    continue;

                var parts = trimmed.Split(',');
                if (parts.Length < 2)
                    continue;

                var firstField = parts[0].Trim().ToUpper();
                
                // Handle M entries (min/max values) - skip for now
                if (firstField.StartsWith("M"))
                {
                    continue;
                }
                
                // Handle Y entries - already processed in first pass
                if (firstField.StartsWith("Y"))
                {
                    continue;
                }

                // First field is tool number (T## or T###)
                if (!firstField.StartsWith("T"))
                    continue;
                    
                var toolNumStr = firstField;

                // Parse tool number (handle both T01 and T001 formats)
                var toolNumOnlyStr = toolNumStr.Substring(1);
                if (!int.TryParse(toolNumOnlyStr, out int toolNum))
                    continue;
                    
                // Validate tool number against schema
                if (toolNum < 1 || toolNum > schema.MaxToolNumber)
                    continue;
                    
                // Normalize tool number for consistent output
                var normalizedToolNum = schema.NormalizeToolNumber(toolNumStr);
                    
                // Debug: log first tool line to see all fields
                if (toolNum == 1)
                {
                    Console.Error.WriteLine($"[DEBUG] ParseTolni: {toolNumStr} line has {parts.Length} fields:");
                    for (int i = 0; i < parts.Length; i++)
                    {
                        Console.Error.WriteLine($"[DEBUG]   Field {i}: '{parts[i]}'");
                    }
                }
                    
                // Extract key fields
                var toolData = new StringBuilder();
                toolData.Append($"{normalizedToolNum},");
                
                    // Tool length offset (field 1) - CONVERT TO MM for MTConnect
                    if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        var lenRaw = parts[1].Trim();
                        var lenMm = ConvertToMillimeters(lenRaw);
                        toolData.Append($"LEN={lenMm},");
                        result[$"Tool {normalizedToolNum} Length"] = lenMm;  // Store converted value
                    }
                    
                    // T length wear offset (field 2) - CONVERT TO MM for MTConnect
                    if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
                    {
                        var lenWearRaw = parts[2].Trim();
                        var lenWearMm = ConvertToMillimeters(lenWearRaw);
                        if (lenWearMm != "0")
                        {
                            toolData.Append($"LENWEAR={lenWearMm},");
                            result[$"Tool {normalizedToolNum} Length Wear"] = lenWearMm;
                        }
                    }
                    
                    // Cutter compensation = Diameter (field 3) - CONVERT TO MM for MTConnect
                    if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
                    {
                        var diaRaw = parts[3].Trim();
                        var diaMm = ConvertToMillimeters(diaRaw);
                        toolData.Append($"DIA={diaMm},");
                        result[$"Tool {normalizedToolNum} Diameter"] = diaMm;  // Store converted value
                    }
                    else
                    {
                        // Default to 0 if not found
                        toolData.Append($"DIA=0.0000,");
                        result[$"Tool {normalizedToolNum} Diameter"] = "0.0000";
                    }
                    
                    // Cutter wear offset (field 4) - CONVERT TO MM for MTConnect
                    if (parts.Length > 4 && !string.IsNullOrWhiteSpace(parts[4]))
                    {
                        var diaWearRaw = parts[4].Trim();
                        var diaWearMm = ConvertToMillimeters(diaWearRaw);
                        if (diaWearMm != "0")
                        {
                            toolData.Append($"DIAWEAR={diaWearMm},");
                            result[$"Tool {normalizedToolNum} Diameter Wear"] = diaWearMm;
                        }
                    }
                
                    // Tool life unit (field 5)
                    // C00: 1~4, D00: 1-5 (1: Not counted, 2: Time (min.), 3: Drilling (holes), 4: Program (cycles), 5: Time (sec.))
                    if (parts.Length > 5 && !string.IsNullOrWhiteSpace(parts[5]))
                    {
                        var lifeUnit = parts[5].Trim();
                        toolData.Append($"LIFEUNIT={lifeUnit},");
                        result[$"Tool {normalizedToolNum} Life Unit"] = lifeUnit;
                    }
                    
                    // Initial tool life / End of tool life (field 6) - this is the tool life limit
                    if (parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6]))
                    {
                        var lifeLimit = parts[6].Trim();
                        toolData.Append($"LIFELIMIT={lifeLimit},");
                        result[$"Tool {normalizedToolNum} Life Limit"] = lifeLimit;
                    }
                    
                    // Tool life warning (field 7)
                    if (parts.Length > 7 && !string.IsNullOrWhiteSpace(parts[7]))
                    {
                        var lifeWarning = parts[7].Trim();
                        toolData.Append($"LIFEWARN={lifeWarning},");
                        result[$"Tool {normalizedToolNum} Life Warning"] = lifeWarning;
                    }
                    
                    // Tool life (field 8) - CURRENT TOOL LIFE (remaining)
                    if (parts.Length > 8 && !string.IsNullOrWhiteSpace(parts[8]))
                    {
                        var life = parts[8].Trim();
                        toolData.Append($"LIFE={life},");
                        result[$"Tool {normalizedToolNum} Life"] = life;
                    }
                
                // Tool name (field 9, wrapped in single quotes)
                if (parts.Length > 9 && !string.IsNullOrWhiteSpace(parts[9]))
                {
                    var toolName = parts[9].Trim().Trim('\'').Trim();
                    if (!string.IsNullOrWhiteSpace(toolName))
                    {
                        toolData.Append($"NAME={toolName},");
                        result[$"Tool {normalizedToolNum} Name"] = toolName;
                    }
                }
                
                    // D00 has peripheral speed field (field 10) - skip this for C00
                    // Peripheral speed (field 10, D00 only, 6 chars: 0.1~9999.9)
                    if (schema.HasPeripheralSpeedField && parts.Length > 10 && !string.IsNullOrWhiteSpace(parts[10]))
                    {
                        var peripheralSpeed = parts[10].Trim();
                        toolData.Append($"PERIPHERAL={peripheralSpeed},");
                        result[$"Tool {normalizedToolNum} Peripheral Speed"] = peripheralSpeed;
                    }
                    
                    // Rotation feed (C00: field 10, D00: field 11)
                    if (parts.Length > schema.RotationFeedFieldIndex && !string.IsNullOrWhiteSpace(parts[schema.RotationFeedFieldIndex]))
                    {
                        var rotationFeed = parts[schema.RotationFeedFieldIndex].Trim();
                        toolData.Append($"ROTFEED={rotationFeed},");
                        result[$"Tool {normalizedToolNum} Rotation Feed"] = rotationFeed;
                    }
                    
                    // S command value (field 11 for C00, field 12 for D00)
                    int sCommandFieldIndex = schema.RotationFeedFieldIndex + 1;
                    if (parts.Length > sCommandFieldIndex && !string.IsNullOrWhiteSpace(parts[sCommandFieldIndex]))
                    {
                        var sCommand = parts[sCommandFieldIndex].Trim();
                        toolData.Append($"S={sCommand},");
                        result[$"Tool {normalizedToolNum} S Command"] = sCommand;
                    }
                    
                    // F command value (C00: field 12, D00: field 13)
                    if (parts.Length > schema.FCommandFieldIndex && !string.IsNullOrWhiteSpace(parts[schema.FCommandFieldIndex]))
                    {
                        var fCommand = parts[schema.FCommandFieldIndex].Trim();
                        toolData.Append($"F={fCommand},");
                        result[$"Tool {normalizedToolNum} F Command"] = fCommand;
                    }
                    
                    // Maximum speed (after F command, 1 field later for C00)
                    int maxSpeedFieldIndex = schema.FCommandFieldIndex + 1;
                    if (parts.Length > maxSpeedFieldIndex && !string.IsNullOrWhiteSpace(parts[maxSpeedFieldIndex]))
                    {
                        var maxSpeed = parts[maxSpeedFieldIndex].Trim();
                        if (maxSpeed != "0")  // 0 means rotation not possible
                        {
                            toolData.Append($"MAXSPEED={maxSpeed},");
                            result[$"Tool {normalizedToolNum} Max Speed"] = maxSpeed;
                        }
                    }
                    
                    // Tool wash (after max speed, field 14 for C00)
                    // 0: Possible, 1: Not possible
                    int toolWashFieldIndex = schema.FCommandFieldIndex + 2;
                    if (parts.Length > toolWashFieldIndex && !string.IsNullOrWhiteSpace(parts[toolWashFieldIndex]))
                    {
                        var toolWash = parts[toolWashFieldIndex].Trim();
                        toolData.Append($"WASH={toolWash},");
                        result[$"Tool {normalizedToolNum} Wash"] = toolWash;
                    }
                    
                    // CTS (after tool wash, field 15 for C00)
                    // 0: Possible, 1: Not possible
                    int ctsFieldIndex = schema.FCommandFieldIndex + 3;
                    if (parts.Length > ctsFieldIndex && !string.IsNullOrWhiteSpace(parts[ctsFieldIndex]))
                    {
                        var cts = parts[ctsFieldIndex].Trim();
                        toolData.Append($"CTS={cts},");
                        result[$"Tool {normalizedToolNum} CTS"] = cts;
                    }
                    
                    // Tool type number (field position varies - typically after CTS)
                    // C00: field 16, D00: field position may vary
                    int toolTypeFieldIndex = schema.FCommandFieldIndex + 4;  // After F command, max speed, tool wash, CTS
                    if (parts.Length > toolTypeFieldIndex && !string.IsNullOrWhiteSpace(parts[toolTypeFieldIndex]))
                    {
                        var toolType = parts[toolTypeFieldIndex].Trim();
                        if (toolType != "0")  // 0 means not set
                        {
                            toolData.Append($"TYPE={toolType},");
                            result[$"Tool {normalizedToolNum} Type"] = toolType;
                        }
                    }
                
                    // Tool position offsets (X, Y) - CONVERT TO MM for MTConnect
                    // Field positions vary: C00 has at field 17/19, D00 positions may vary
                    // Note: These fields may not always be present, so we'll try to find them
                    // after tool type field (field 17 = X pos, 18 = X wear, 19 = Y pos, 20 = Y wear, 21 = virtual teeth)
                    int toolPosXFieldIndex = toolTypeFieldIndex + 1;  // Field 17: X position offset
                    int toolPosXWearFieldIndex = toolTypeFieldIndex + 2;  // Field 18: X position wear offset
                    int toolPosYFieldIndex = toolTypeFieldIndex + 3;  // Field 19: Y position offset
                    int toolPosYWearFieldIndex = toolTypeFieldIndex + 4;  // Field 20: Y position wear offset
                    int virtualTeethFieldIndex = toolTypeFieldIndex + 5;  // Field 21: Virtual teeth direction
                    
                    // Tool position offset (X) - CONVERT TO MM
                    if (parts.Length > toolPosXFieldIndex && !string.IsNullOrWhiteSpace(parts[toolPosXFieldIndex]))
                    {
                        var posXRaw = parts[toolPosXFieldIndex].Trim();
                        var posXMm = ConvertToMillimeters(posXRaw);
                        toolData.Append($"POSX={posXMm},");
                        result[$"Tool {normalizedToolNum} Position X"] = posXMm;
                    }
                    
                    // Tool position wear offset (X) - CONVERT TO MM
                    if (parts.Length > toolPosXWearFieldIndex && !string.IsNullOrWhiteSpace(parts[toolPosXWearFieldIndex]))
                    {
                        var posXWearRaw = parts[toolPosXWearFieldIndex].Trim();
                        var posXWearMm = ConvertToMillimeters(posXWearRaw);
                        if (posXWearMm != "0")
                        {
                            toolData.Append($"POSXWEAR={posXWearMm},");
                            result[$"Tool {normalizedToolNum} Position X Wear"] = posXWearMm;
                        }
                    }
                    
                    // Tool position offset (Y) - CONVERT TO MM
                    if (parts.Length > toolPosYFieldIndex && !string.IsNullOrWhiteSpace(parts[toolPosYFieldIndex]))
                    {
                        var posYRaw = parts[toolPosYFieldIndex].Trim();
                        var posYMm = ConvertToMillimeters(posYRaw);
                        toolData.Append($"POSY={posYMm},");
                        result[$"Tool {normalizedToolNum} Position Y"] = posYMm;
                    }
                    
                    // Tool position wear offset (Y) - CONVERT TO MM
                    if (parts.Length > toolPosYWearFieldIndex && !string.IsNullOrWhiteSpace(parts[toolPosYWearFieldIndex]))
                    {
                        var posYWearRaw = parts[toolPosYWearFieldIndex].Trim();
                        var posYWearMm = ConvertToMillimeters(posYWearRaw);
                        if (posYWearMm != "0")
                        {
                            toolData.Append($"POSYWEAR={posYWearMm},");
                            result[$"Tool {normalizedToolNum} Position Y Wear"] = posYWearMm;
                        }
                    }
                    
                    // Virtual teeth direction (field 21)
                    if (parts.Length > virtualTeethFieldIndex && !string.IsNullOrWhiteSpace(parts[virtualTeethFieldIndex]))
                    {
                        var virtualTeeth = parts[virtualTeethFieldIndex].Trim();
                        toolData.Append($"VIRTUALTEETH={virtualTeeth},");
                        result[$"Tool {normalizedToolNum} Virtual Teeth"] = virtualTeeth;
                    }
                    
                    // Group assignment (from Y entries parsed above)
                    if (groupAssignments.ContainsKey(toolNum))
                    {
                        var group = groupAssignments[toolNum].ToString();
                        toolData.Append($"GRP={group},");
                        result[$"Tool {normalizedToolNum} Group"] = group;
                    }
                
                // Store as tool data
                result[$"Tool {normalizedToolNum}"] = toolData.ToString().TrimEnd(',');
                tools.Add(toolData.ToString().TrimEnd(','));
                toolCount++;
            }

            // Store tool count and tool list
            result["Tool count"] = toolCount.ToString();
            result["Tool table"] = string.Join("|", tools);

            return result;
        }

        /// <summary>
        /// Parse MONTR file - monitor data (cycle time, cutting time, operation time, power on hours).
        /// Format: CSV-like or key-value pairs
        /// Note: D00 may have different field positions or field names.
        /// </summary>
        public Dictionary<string, string> ParseMontr(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

            // Version and unit-specific parsing
            if (_controlVersion == ControlVersion.D00)
            {
                Console.Error.WriteLine($"[DEBUG] Using D00 schema for MONTR parsing");
            }
            else
            {
                Console.Error.WriteLine($"[DEBUG] Using C00 schema for MONTR parsing");
            }
            
            if (_unitSystem == UnitSystem.Inch)
            {
                Console.Error.WriteLine($"[DEBUG] Using Inch unit system for MONTR parsing");
            }
            else
            {
                Console.Error.WriteLine($"[DEBUG] Using Metric unit system for MONTR parsing");
            }

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                var trimmed = line.Trim();
                // Skip comments
                if (trimmed.StartsWith("(") || trimmed.StartsWith(";"))
                    continue;

                // Try key-value format: KEY=VALUE or KEY:VALUE
                if (trimmed.Contains("="))
                {
                    var parts = trimmed.Split('=');
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();
                        result[key] = value;
                    }
                }
                else if (trimmed.Contains(":"))
                {
                    var parts = trimmed.Split(':');
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();
                        result[key] = value;
                    }
                }
                // Try CSV format: CYCLE_TIME,CUTTING_TIME,OPERATION_TIME,POWER_ON_HOURS
                else if (trimmed.Contains(","))
                {
                    var parts = trimmed.Split(',');
                    // Common field names based on HTTP endpoint
                    if (parts.Length >= 1 && !string.IsNullOrWhiteSpace(parts[0]))
                        result["Cycle time"] = parts[0].Trim();
                    if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
                        result["Cutting time"] = parts[1].Trim();
                    if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
                        result["Non cutting time"] = parts[2].Trim();
                    if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]))
                        result["Operation time"] = parts[3].Trim();
                    if (parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4]))
                        result["Power on time"] = parts[4].Trim();
                }
            }

            return result;
        }

        /// <summary>
        /// Parse POSN file - work offset data (CSV format).
        /// Supports both POSNI (Inch) and POSNM (Metric) files.
        /// Handles version-specific differences: C00 (G54, X01-X48) vs D00 (G054, X001-X300).
        /// </summary>
        public Dictionary<string, string> ParsePosni(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

            // Get schema configuration based on control version
            var schemaConfig = PosnSchemaConfig.GetConfig(_controlVersion);
            
            // Version and unit-specific parsing
            if (_controlVersion == ControlVersion.D00)
            {
                Console.Error.WriteLine($"[DEBUG] Using D00 schema for POSN parsing (G054-G059, X001-X300, 11-digit fields)");
            }
            else
            {
                Console.Error.WriteLine($"[DEBUG] Using C00 schema for POSN parsing (G54-G59, X01-X48, 9-digit fields)");
            }
            
            if (_unitSystem == UnitSystem.Inch)
            {
                Console.Error.WriteLine($"[DEBUG] Using Inch unit system for POSN parsing (from POSNI file)");
            }
            else
            {
                Console.Error.WriteLine($"[DEBUG] Using Metric unit system for POSN parsing (from POSNM file)");
            }

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                var trimmed = line.Trim();
                var parts = trimmed.Split(',');
                if (parts.Length < 4)
                    continue;

                var offsetName = parts[0].Trim().ToUpper();
                
                // Check if offset name matches expected format for this schema
                if (!schemaConfig.MatchesOffsetFormat(offsetName))
                {
                    // Skip offsets that don't match this schema's format
                    continue;
                }
                
                try
                {
                    var x = parts.Length > 1 ? parts[1].Trim() : "0";
                    var y = parts.Length > 2 ? parts[2].Trim() : "0";
                    var z = parts.Length > 3 ? parts[3].Trim() : "0";

                    // Normalize offset name for consistent output (G054 -> G54, X001 -> X1, etc.)
                    var normalizedOffset = schemaConfig.NormalizeOffsetName(offsetName);

                    // G54-G59 work offsets (C00: G54, D00: G054)
                    if (offsetName.StartsWith("G"))
                    {
                        // Extract number from G54 or G054
                        var numMatch = Regex.Match(offsetName, @"^G(\d+)$");
                        if (numMatch.Success && int.TryParse(numMatch.Groups[1].Value, out int gNum))
                        {
                            // C00: G54-G59, D00: G054-G059 (both represent same offsets)
                            if (gNum >= 54 && gNum <= 59)
                            {
                                var offsetNum = gNum.ToString();
                                result[$"Work offset G{offsetNum} X"] = x;
                                result[$"Work offset G{offsetNum} Y"] = y;
                                result[$"Work offset G{offsetNum} Z"] = z;
                                // Add rotary axes if present
                                if (parts.Length > 4)
                                    result[$"Work offset G{offsetNum} A"] = parts[4].Trim();
                                if (parts.Length > 5)
                                    result[$"Work offset G{offsetNum} B"] = parts[5].Trim();
                                if (parts.Length > 6)
                                    result[$"Work offset G{offsetNum} C"] = parts[6].Trim();
                            }
                        }
                    }
                    // Extended offsets (C00: X01-X48, D00: X001-X300)
                    else if (offsetName.StartsWith("X"))
                    {
                        // Extract number from X01, X001, etc.
                        var numMatch = Regex.Match(offsetName, @"^X(\d+)$");
                        if (numMatch.Success && int.TryParse(numMatch.Groups[1].Value, out int xNum))
                        {
                            // Check if within valid range for this schema
                            if (xNum >= schemaConfig.ExtendedOffsetRange.Min && xNum <= schemaConfig.ExtendedOffsetRange.Max)
                            {
                                var offsetNum = xNum.ToString();
                                result[$"Extended offset X{offsetNum} X"] = x;
                                result[$"Extended offset X{offsetNum} Y"] = y;
                                result[$"Extended offset X{offsetNum} Z"] = z;
                                // Add rotary axes if present
                                if (parts.Length > 4)
                                    result[$"Extended offset X{offsetNum} A"] = parts[4].Trim();
                                if (parts.Length > 5)
                                    result[$"Extended offset X{offsetNum} B"] = parts[5].Trim();
                                if (parts.Length > 6)
                                    result[$"Extended offset X{offsetNum} C"] = parts[6].Trim();
                            }
                        }
                    }
                    // H offsets (C00: H01, D00: H001)
                    else if (offsetName.StartsWith("H"))
                    {
                        // Extract number from H01, H001, etc.
                        var numMatch = Regex.Match(offsetName, @"^H(\d+)$");
                        if (numMatch.Success && int.TryParse(numMatch.Groups[1].Value, out int hNum))
                        {
                            var offsetNum = hNum.ToString();
                            result[$"Fixture offset H{offsetNum} X"] = x;
                            result[$"Fixture offset H{offsetNum} Y"] = y;
                            result[$"Fixture offset H{offsetNum} Z"] = z;
                        }
                    }
                    // B offsets (C00: B01, D00: B001-B008)
                    else if (offsetName.StartsWith("B"))
                    {
                        // Extract number from B01, B001, etc.
                        var numMatch = Regex.Match(offsetName, @"^B(\d+)$");
                        if (numMatch.Success && int.TryParse(numMatch.Groups[1].Value, out int bNum))
                        {
                            // Check if within valid range for this schema
                            if (bNum >= schemaConfig.RotaryOffsetRange.Min && bNum <= schemaConfig.RotaryOffsetRange.Max)
                            {
                                var offsetNum = bNum.ToString();
                                result[$"Rotary offset B{offsetNum} X"] = x;
                                result[$"Rotary offset B{offsetNum} Y"] = y;
                                result[$"Rotary offset B{offsetNum} Z"] = z;
                                // B offsets may have additional fields (A, B, C axes, reference offsets)
                                if (parts.Length > 4)
                                    result[$"Rotary offset B{offsetNum} A"] = parts[4].Trim();
                                if (parts.Length > 5)
                                    result[$"Rotary offset B{offsetNum} B"] = parts[5].Trim();
                                if (parts.Length > 6)
                                    result[$"Rotary offset B{offsetNum} C"] = parts[6].Trim();
                                // Additional fields for reference offsets (D00 has more fields)
                                if (parts.Length > 7)
                                    result[$"Rotary offset B{offsetNum} Reference X"] = parts[7].Trim();
                                if (parts.Length > 8)
                                    result[$"Rotary offset B{offsetNum} Reference Y"] = parts[8].Trim();
                                if (parts.Length > 9)
                                    result[$"Rotary offset B{offsetNum} Reference Z"] = parts[9].Trim();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Skip malformed lines, but log for debugging
                    Console.Error.WriteLine($"[DEBUG] Failed to parse POSN line: {trimmed.Substring(0, Math.Min(50, trimmed.Length))} - {ex.Message}");
                    continue;
                }
            }

            return result;
        }

        /// <summary>
        /// Parse file using DataMap (for files with JSON mapping like PDSP).
        /// </summary>
        public Dictionary<string, string> ParseWithDataMap(string[] lines, Mapping.DataMap dataMap)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0 || dataMap == null)
                return result;

            foreach (var line in dataMap.Lines)
            {
                if (line.Number >= lines.Length)
                    continue;
                    
                var rawLine = lines[line.Number].Split(',');
                if (rawLine.Length == 0 || rawLine[0] != line.Symbol)
                    continue;

                rawLine = rawLine.Skip(1).ToArray();
                for (int i = 1; i < line.Items.Count && i < rawLine.Length; i++)
                {
                    if (line.Items[i].Type == "Number")
                    {
                        result[line.Items[i].Name] = rawLine[i].Trim();
                    }
                    else if (line.Items[i].EnumValues != null && line.Items[i].EnumValues.Count > 0)
                    {
                        if (int.TryParse(rawLine[i].Trim(), out int enumIndex))
                        {
                            var enumValue = line.Items[i].EnumValues.FirstOrDefault(v => v.Index == enumIndex);
                            if (enumValue != null)
                            {
                                result[line.Items[i].Name] = enumValue.Value;
                            }
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Parse macro variable file (MCRNun/MCRSun).
        /// C00 Format (from section_5_6_4_s300x1n.json):
        ///   C500-C999: Macro variables
        ///     Delimiter: comma (,)
        ///     Data length: 11 characters
        ///     Range: -999999.999~999999.999 (Metric) or -99999.9999~99999.9999 (Inch)
        ///     Note: Last digit is blank space when unit is micron
        /// D00 Format (from section_3_6_5_d00.json):
        ///   C500-C999: Macro variables
        ///     Delimiter: CR+LF (line break)
        ///     Data length: 11 characters
        ///     Range: -999999.999~999999.999 (Metric) or -99999.9999~99999.9999 (Inch)
        ///     Type 2 (MCRSun): One more decimal digit when smallest unit system option purchased
        /// Values are unit-aware (Metric/Inch) but are typically unitless numeric values.
        /// </summary>
        public Dictionary<string, string> ParseMacro(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

            // Get schema configuration based on control version
            var schema = MacroSchemaConfig.GetConfig(_controlVersion);
            
            Console.Error.WriteLine($"[INFO] Parsing macro variables using {_controlVersion} schema, {_unitSystem} units");
            Console.Error.WriteLine($"[DEBUG] Delimiter: {(schema.UsesLineBreakDelimiter ? "CR+LF" : "comma")}, Variable range: C{schema.MinVariableNumber}-C{schema.MaxVariableNumber}");

            var macroVariables = new List<string>();
            int variableCount = 0;

            // C00 uses comma delimiter - all variables on one or few lines
            // D00 uses CR+LF delimiter - one variable per line
            if (schema.UsesLineBreakDelimiter)
            {
                // D00: One variable per line (CR+LF delimiter)
                // Format: C500<11-char-value>\r\nC501<11-char-value>\r\n...
                // OR: C500,<11-char-value>\r\nC501,<11-char-value>\r\n...
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                        
                    var trimmed = line.Trim();
                    
                    // Skip comments
                    if (trimmed.StartsWith("(") || trimmed.StartsWith(";"))
                        continue;

                    // Try pattern with comma first: C500,<11-char-value>
                    var match = Regex.Match(trimmed, @"^C(\d+),(.{11})$");
                    if (!match.Success)
                    {
                        // Try pattern without comma: C500<11-char-value>
                        match = Regex.Match(trimmed, @"^C(\d+)(.{11})$");
                    }
                    
                    if (match.Success)
                    {
                        if (int.TryParse(match.Groups[1].Value, out int varNum))
                        {
                            if (schema.IsValidVariableNumber(varNum))
                            {
                                var variableName = $"C{varNum}";
                                var value = match.Groups[2].Value.Trim();
                                
                                // Handle blank space in last digit for micron mode
                                if (schema.HasMicronBlankSpace && value.Length == 11 && value[10] == ' ')
                                {
                                    value = value.Substring(0, 10).Trim();
                                }
                                
                                // Store individual variable
                                result[$"Macro Variable {variableName}"] = value;
                                
                                // Add to aggregated output
                                macroVariables.Add($"{variableName}={value}");
                                variableCount++;
                            }
                        }
                    }
                }
            }
            else
            {
                // C00: Comma-delimited format - variables on one or few lines
                // Format: C500,<11-char-value>,C501,<11-char-value>,...
                // Empty values: C500,C501,<value> (C500 has empty value)
                // Use regex to find all C###,<value> patterns, handling empty values
                string combinedContent = string.Join("", lines);
                
                // Match pattern: C###, followed by either:
                // 1. Exactly 11 characters (value) then comma and next C### or end
                // 2. Immediately followed by next C### (empty value)
                // Use a more careful approach: find all C###, patterns and extract values
                var variableMatches = Regex.Matches(combinedContent, @"C(\d{3}),");
                
                for (int i = 0; i < variableMatches.Count; i++)
                {
                    var match = variableMatches[i];
                    if (int.TryParse(match.Groups[1].Value, out int varNum))
                    {
                        if (schema.IsValidVariableNumber(varNum))
                        {
                            var variableName = $"C{varNum}";
                            var startPos = match.Index + match.Length; // Position after "C###,"
                            
                            // Determine where this value ends
                            int endPos;
                            if (i + 1 < variableMatches.Count)
                            {
                                // Next variable starts at variableMatches[i+1].Index
                                endPos = variableMatches[i + 1].Index;
                            }
                            else
                            {
                                // Last variable, value goes to end of string
                                endPos = combinedContent.Length;
                            }
                            
                            // Extract the value (everything between "C###," and next "C###," or end)
                            var value = combinedContent.Substring(startPos, endPos - startPos).Trim();
                            
                            // Value should be exactly 11 characters, but handle empty/null values
                            if (string.IsNullOrWhiteSpace(value))
                            {
                                value = "0"; // Default empty value to 0
                            }
                            else if (value.Length > 11)
                            {
                                // Value is longer than expected, take first 11 chars
                                value = value.Substring(0, 11);
                            }
                            else if (value.Length < 11)
                            {
                                // Value is shorter, pad or use as-is (might be empty/null representation)
                                // Check if it starts with "C" (next variable name leaked in)
                                if (value.StartsWith("C"))
                                {
                                    // This is an empty value, the next variable name got included
                                    value = "0";
                                }
                            }
                            
                            // Handle blank space in last digit for micron mode
                            if (value.Length == 11 && value[10] == ' ')
                            {
                                value = value.Substring(0, 10).Trim();
                            }
                            
                            // Store individual variable
                            result[$"Macro Variable {variableName}"] = value;
                            
                            // Add to aggregated output
                            macroVariables.Add($"{variableName}={value}");
                            variableCount++;
                        }
                    }
                }
            }

            // Store variable count and combined macro string
            result["Macro Variable Count"] = variableCount.ToString();
            if (macroVariables.Count > 0)
            {
                result["Macro Variables"] = string.Join("|", macroVariables);
            }

            return result;
        }

        /// <summary>
        /// Helper method to convert position value from inches to millimeters if needed.
        /// </summary>
        private string ConvertToMillimeters(string value)
        {
            if (_unitSystem != UnitSystem.Inch)
                return value;
                
            if (string.IsNullOrWhiteSpace(value) || value == "0")
                return value;
                
            if (double.TryParse(value, out double inches))
            {
                double millimeters = inches * 25.4;
                return millimeters.ToString("F6").TrimEnd('0').TrimEnd('.');
            }
                
            return value;
        }
    }
}

