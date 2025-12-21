using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BrotherConnection
{
    /// <summary>
    /// Helper class to load and parse different file types from Brother CNC via LOD command.
    /// </summary>
    internal class FileLoader
    {
        private Request _request;

        public FileLoader(Request request = null)
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
        /// Parse MEM file - simple format, just contains program name (O####).
        /// </summary>
        public Dictionary<string, string> ParseMem(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

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
        /// Parse ATCTL file - ATC (Automatic Tool Changer) control data.
        /// Format (from Brother documentation):
        /// - M01 = Spindle (current tool)
        /// - M02-M51 = Pots 1-50
        /// Each entry: M##,tool_num,conversation_nc,group_main_tool,type,color
        /// Fields:
        ///   1. Tool No. (0: Not set, 1-99: Tool No, 255: Cap setting)
        ///   2. Spindle (Conversation/NC) - 0: Conversation, 1:NC
        ///   3. Spindle (Group No. (NC) / Main tool No. (Conversation)) - Group No.: 0 (Not set), 1-30, Main tool No.: 0 (Not set), 1-99
        ///   4. Spindle (Type) - 1: Standard, 2: Large diameter, 3: Medium diameter
        ///   5. Spindle (Graph color) - 0: No color, 1: Blue, 2: Red, 3: Purple, 4: Green, 5: Light blue, 6: Yellow, 7: White
        /// Tool data (diameter, length, name, life) should be cross-referenced with tool table (TOLNI1).
        /// </summary>
        /// <param name="lines">ATCTL file lines</param>
        /// <param name="toolTableData">Optional tool table data dictionary to cross-reference tool specs</param>
        public Dictionary<string, string> ParseAtctl(string[] lines, Dictionary<string, string> toolTableData = null)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
            {
                Console.Error.WriteLine("[DEBUG] ParseAtctl: lines is null or empty");
                return result;
            }

            Console.Error.WriteLine($"[DEBUG] ParseAtctl: Processing {lines.Length} lines");
            var atcTools = new List<string>();
            int toolCount = 0;
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
                
                // Format: M##,tool_num,conversation_nc,group_main_tool,type,color
                // M01 = Spindle (current tool) - skip for now
                // M02-M51 = Pots 1-50 (M02 = pot 1, M03 = pot 2, etc., so pot = M## - 1)
                int potNum = -1;
                int toolNum = -1;
                int conversationNc = 0;
                int groupMainTool = 0;
                int type = 1;
                int color = 0;
                
                // Extract pot number from M## prefix
                var potStr = parts[0].Trim();
                if (potStr.StartsWith("M"))
                {
                    if (int.TryParse(potStr.Substring(1), out int mNum))
                    {
                        if (mNum == 1)
                        {
                            // M01 = Spindle (current tool), skip for now
                            continue;
                        }
                        else if (mNum >= 2 && mNum <= 51)
                        {
                            // M02-M51 = Pots 1-50
                            potNum = mNum - 1; // M02 = pot 1, M11 = pot 10
                        }
                    }
                }
                
                // Field 1: Tool No. (0: Not set, 1-99: Tool No, 255: Cap setting)
                if (parts.Length > 1)
                {
                    var toolNumStr = parts[1].Trim();
                    if (int.TryParse(toolNumStr, out toolNum))
                    {
                        // 0 = Not set, 255 = Cap setting - skip these
                        if (toolNum == 0 || toolNum == 255)
                        {
                            continue;
                        }
                    }
                }
                
                // Field 2: Spindle (Conversation/NC) - 0: Conversation, 1:NC
                if (parts.Length > 2)
                {
                    var convNcStr = parts[2].Trim();
                    int.TryParse(convNcStr, out conversationNc);
                }
                
                // Field 3: Spindle (Group No. (NC) / Main tool No. (Conversation))
                // Group No.: 0 (Not set), 1-30
                // Main tool No.: 0 (Not set), 1-99
                if (parts.Length > 3)
                {
                    var groupStr = parts[3].Trim();
                    int.TryParse(groupStr, out groupMainTool);
                }
                
                // Field 4: Spindle (Type) - 1: Standard, 2: Large diameter, 3: Medium diameter
                if (parts.Length > 4)
                {
                    var typeStr = parts[4].Trim();
                    if (int.TryParse(typeStr, out int parsedType))
                    {
                        type = parsedType;
                    }
                }
                
                // Field 5: Spindle (Graph color) - 0: No color, 1: Blue, 2: Red, 3: Purple, 4: Green, 5: Light blue, 6: Yellow, 7: White
                if (parts.Length > 5)
                {
                    var colorStr = parts[5].Trim();
                    // Remove CR+LF if present
                    colorStr = colorStr.Replace("\r", "").Replace("\n", "").Trim();
                    if (int.TryParse(colorStr, out int parsedColor))
                    {
                        color = parsedColor;
                    }
                }
                
                // If we found both pot and tool, extract the data
                if (potNum > 0 && toolNum > 0)
                {
                    // Debug: log first few successful parses, especially pot 10 -> tool 24
                    if (toolCount < 5 || (potNum == 10 && toolNum == 24))
                    {
                        Console.Error.WriteLine($"[DEBUG] ParseAtctl: Parsed pot {potNum}, tool {toolNum}, type={type}, color={color}, line: {trimmed.Substring(0, Math.Min(100, trimmed.Length))}");
                    }
                    
                    // Tool name should ALWAYS come from tool table (TOLNI1), not from ATCTL
                    var toolName = "";
                    // Get tool data from tool table (TOLNI1) if available
                    var length = "0";
                    var diameter = "0";
                    var group = groupMainTool > 0 ? groupMainTool.ToString() : "";
                    var life = "0";
                    var toolType = type.ToString(); // Use type from ATCTL (1: Standard, 2: Large diameter, 3: Medium diameter)
                    
                    if (toolTableData != null)
                    {
                        // Look up tool data by tool number
                        var toolKey = $"Tool {toolNum}";
                        if (toolTableData.ContainsKey($"{toolKey} Length"))
                            length = toolTableData[$"{toolKey} Length"];
                        if (toolTableData.ContainsKey($"{toolKey} Diameter"))
                            diameter = toolTableData[$"{toolKey} Diameter"];
                        // Use group from ATCTL if available, otherwise from tool table
                        if (string.IsNullOrEmpty(group) && toolTableData.ContainsKey($"{toolKey} Group"))
                            group = toolTableData[$"{toolKey} Group"];
                        if (toolTableData.ContainsKey($"{toolKey} Life"))
                            life = toolTableData[$"{toolKey} Life"];
                        // Tool name ALWAYS from tool table (TOLNI1 has the correct tool name)
                        if (toolTableData.ContainsKey($"{toolKey} Name"))
                            toolName = toolTableData[$"{toolKey} Name"];
                    }

                    // Store individual tool data
                    result[$"ATC Pot {potNum} Tool Number"] = toolNum.ToString();
                    result[$"ATC Pot {potNum} Tool Name"] = toolName;
                    result[$"ATC Pot {potNum} Length"] = length;
                    result[$"ATC Pot {potNum} Diameter"] = diameter;
                    result[$"ATC Pot {potNum} Group"] = group;
                    result[$"ATC Pot {potNum} Life"] = life;
                    result[$"ATC Pot {potNum} Type"] = toolType;
                    result[$"ATC Pot {potNum} Color"] = color.ToString();

                    // Store as ATC tool entry
                    atcTools.Add($"P{potNum}:T{toolNum}:{toolName}:LEN={length}:DIA={diameter}:GRP={group}:LIFE={life}:TYPE={toolType}:COL={color}");
                    toolCount++;
                }
                else
                {
                    skippedLines++;
                    if (skippedLines <= 5)
                    {
                        Console.Error.WriteLine($"[DEBUG] ParseAtctl: Could not find pot/tool in line: {trimmed.Substring(0, Math.Min(100, trimmed.Length))}");
                    }
                }
            }
            
            Console.Error.WriteLine($"[DEBUG] ParseAtctl: Total lines processed: {lines.Length}, Tools found: {toolCount}, Skipped: {skippedLines}");

            // Store ATC tool count and combined ATC tool list
            result["ATC Tool count"] = toolCount.ToString();
            if (atcTools.Count > 0)
            {
                result["ATC Tools"] = string.Join("|", atcTools);
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
        /// </summary>
        public Dictionary<string, string> ParsePanel(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

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
        /// Parse TOLNI1 file - tool table data (comma-delimited format).
        /// Format (from Brother documentation):
        /// T01-T99: Tool entries
        ///   Field 0: Tool number (T##)
        ///   Field 1: Tool length offset (8 chars)
        ///   Field 2: T length wear offset (7 chars)
        ///   Field 3: Cutter compensation (8 chars) - THIS IS THE DIAMETER
        ///   Field 4: Cutter wear offset (7 chars)
        ///   Field 5: Tool life unit (1 char, 1-4)
        ///   Field 6: Initial tool life / End of tool life (6 chars, 0-999999)
        ///   Field 7: Tool life warning (6 chars, 0-999999)
        ///   Field 8: Tool life (6 chars, 0-999999) - CURRENT TOOL LIFE
        ///   Field 9: Tool name (16 chars, wrapped in single quotes)
        ///   Field 10: Rotation feed (4 chars)
        ///   Field 11: S command value (5 chars, 1-99999)
        ///   Field 12: F command value (9 chars)
        ///   Field 13: Maximum speed (5 chars, 0-999999, 0 = rotation not possible)
        ///   Field 14: Tool wash (1 char, 0: Possible, 1: Not possible)
        ///   Field 15: CTS (1 char, 0: Possible, 1: Not possible)
        ///   Field 16: Tool type number (8 chars, 0-99999999, 0 = Not set)
        ///   Field 17: Tool position offset (X) (8 chars)
        ///   Field 18: Tool position wear offset (X) (7 chars)
        ///   Field 19: Tool position offset (Y) (8 chars)
        ///   Field 20: Tool position wear offset (Y) (7 chars)
        ///   Field 21: Virtual teeth direction (1 char)
        /// Y01-Y99: Group data (tool numbers in groups)
        /// M01-M99: Min/max values
        /// </summary>
        public Dictionary<string, string> ParseTolni(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

            var tools = new List<string>();
            int toolCount = 0;
            
            // Dictionary to store group assignments (from Y01-Y99 entries)
            var groupAssignments = new Dictionary<int, int>(); // tool number -> group number

            // Debug: log first few lines to understand format
            Console.Error.WriteLine($"[DEBUG] ParseTolni: Processing {lines.Length} lines");
            for (int i = 0; i < Math.Min(3, lines.Length); i++)
            {
                Console.Error.WriteLine($"[DEBUG] ParseTolni: Line {i}: '{lines[i]}'");
            }
            
            // First pass: Parse Y01-Y99 entries to get group assignments
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
                
                // Handle Y01-Y99 entries (group data)
                // Format: Y##,tool1,tool2,...,tool30 (up to 30 tools per group)
                if (firstField.StartsWith("Y"))
                {
                    if (int.TryParse(firstField.Substring(1), out int groupNum))
                    {
                        // Parse tool numbers in this group (fields 1-30)
                        for (int i = 1; i < parts.Length && i <= 30; i++)
                        {
                            var toolNumStr = parts[i].Trim();
                            if (int.TryParse(toolNumStr, out int toolNum) && toolNum >= 1 && toolNum <= 99)
                            {
                                groupAssignments[toolNum] = groupNum;
                            }
                        }
                    }
                }
            }
            
            // Second pass: Parse T01-T99 entries (tool data)
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
                
                // Handle M01-M99 entries (min/max values) - skip for now
                if (firstField.StartsWith("M"))
                {
                    continue;
                }
                
                // Handle Y01-Y99 entries - already processed in first pass
                if (firstField.StartsWith("Y"))
                {
                    continue;
                }

                // First field is tool number (T##)
                if (!firstField.StartsWith("T"))
                    continue;
                    
                var toolNumStr = firstField;

                if (int.TryParse(toolNumStr.Substring(1), out int toolNum))
                {
                    // Debug: log T01 line to see all fields
                    if (toolNum == 1)
                    {
                        Console.Error.WriteLine($"[DEBUG] ParseTolni: T01 line has {parts.Length} fields:");
                        for (int i = 0; i < parts.Length; i++)
                        {
                            Console.Error.WriteLine($"[DEBUG]   Field {i}: '{parts[i]}'");
                        }
                    }
                    
                    // Extract key fields
                    var toolData = new StringBuilder();
                    toolData.Append($"T{toolNum:D2},");
                    
                    // Format per documentation:
                    // Field 0: Tool number (T##)
                    // Field 1: Tool length offset
                    // Field 2: T length wear offset
                    // Field 3: Cutter compensation (DIAMETER)
                    // Field 4: Cutter wear offset
                    // Field 5: Tool life unit (1-4)
                    // Field 6: Initial tool life / End of tool life
                    // Field 7: Tool life warning
                    // Field 8: Tool life (CURRENT TOOL LIFE)
                    // Field 9: Tool name (wrapped in single quotes)
                    // Field 10: Rotation feed
                    // Field 11: S command value
                    // Field 12: F command value
                    // Field 13: Maximum speed
                    // Field 14: Tool wash
                    // Field 15: CTS
                    // Field 16: Tool type number
                    // Field 17+: Tool position offsets, etc.
                    
                    // Tool length offset (field 1)
                    if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        var len = parts[1].Trim();
                        toolData.Append($"LEN={len},");
                        result[$"Tool {toolNum} Length"] = len;
                    }
                    
                    // Cutter compensation = Diameter (field 3)
                    if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
                    {
                        var dia = parts[3].Trim();
                        toolData.Append($"DIA={dia},");
                        result[$"Tool {toolNum} Diameter"] = dia;
                    }
                    else
                    {
                        // Default to 0 if not found
                        toolData.Append($"DIA=0.0000,");
                        result[$"Tool {toolNum} Diameter"] = "0.0000";
                    }
                    
                    // Tool life unit (field 5) - not currently used but documented
                    // Initial tool life / End of tool life (field 6) - this is the tool life limit
                    if (parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6]))
                    {
                        var lifeLimit = parts[6].Trim();
                        result[$"Tool {toolNum} Life Limit"] = lifeLimit;
                    }
                    
                    // Tool life warning (field 7) - not currently used but documented
                    
                    // Tool life (field 8) - CURRENT TOOL LIFE
                    if (parts.Length > 8 && !string.IsNullOrWhiteSpace(parts[8]))
                    {
                        var life = parts[8].Trim();
                        result[$"Tool {toolNum} Life"] = life;
                    }
                    
                    // Tool name (field 9, wrapped in single quotes)
                    if (parts.Length > 9 && !string.IsNullOrWhiteSpace(parts[9]))
                    {
                        var toolName = parts[9].Trim().Trim('\'').Trim();
                        if (!string.IsNullOrWhiteSpace(toolName))
                        {
                            toolData.Append($"NAME={toolName},");
                            result[$"Tool {toolNum} Name"] = toolName;
                        }
                    }
                    
                    // Tool type number (field 16)
                    if (parts.Length > 16 && !string.IsNullOrWhiteSpace(parts[16]))
                    {
                        var toolType = parts[16].Trim();
                        result[$"Tool {toolNum} Type"] = toolType;
                    }
                    
                    // Group assignment (from Y01-Y99 entries parsed above)
                    if (groupAssignments.ContainsKey(toolNum))
                    {
                        var group = groupAssignments[toolNum].ToString();
                        result[$"Tool {toolNum} Group"] = group;
                    }
                    
                    // Store as tool data
                    result[$"Tool {toolNum}"] = toolData.ToString().TrimEnd(',');
                    tools.Add(toolData.ToString().TrimEnd(','));
                    toolCount++;
                }
            }

            // Store tool count and tool list
            result["Tool count"] = toolCount.ToString();
            result["Tool table"] = string.Join("|", tools);

            return result;
        }

        /// <summary>
        /// Parse MONTR file - monitor data (cycle time, cutting time, operation time, power on hours).
        /// Format: CSV-like or key-value pairs
        /// </summary>
        public Dictionary<string, string> ParseMontr(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

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
        /// Parse POSNI1 file - work offset data (CSV format).
        /// Format: G##,X,Y,Z,A,B,C or X##,X,Y,Z,A,B,C
        /// Extended to parse G54-G59 and X01-X48
        /// </summary>
        public Dictionary<string, string> ParsePosni(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                var trimmed = line.Trim();
                var parts = trimmed.Split(',');
                if (parts.Length < 4)
                    continue;

                var offsetName = parts[0].Trim().ToUpper();
                
                try
                {
                    var x = parts.Length > 1 ? parts[1].Trim() : "0";
                    var y = parts.Length > 2 ? parts[2].Trim() : "0";
                    var z = parts.Length > 3 ? parts[3].Trim() : "0";

                    // G54-G59 work offsets
                    if (offsetName.StartsWith("G5"))
                    {
                        var offsetNum = offsetName.Substring(1);
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
                    // X01-X48 extended offsets
                    else if (offsetName.StartsWith("X"))
                    {
                        var offsetNum = offsetName.Substring(1);
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
                    // H01-H99 fixture offsets
                    else if (offsetName.StartsWith("H"))
                    {
                        var offsetNum = offsetName.Substring(1);
                        result[$"Fixture offset H{offsetNum} X"] = x;
                        result[$"Fixture offset H{offsetNum} Y"] = y;
                        result[$"Fixture offset H{offsetNum} Z"] = z;
                    }
                    // B01-B08 rotary offsets
                    else if (offsetName.StartsWith("B"))
                    {
                        var offsetNum = offsetName.Substring(1);
                        result[$"Rotary offset B{offsetNum} X"] = x;
                        result[$"Rotary offset B{offsetNum} Y"] = y;
                        result[$"Rotary offset B{offsetNum} Z"] = z;
                    }
                }
                catch
                {
                    // Skip malformed lines
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
    }
}

