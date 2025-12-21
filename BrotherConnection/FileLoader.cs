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
        /// Format: CSV-like with alarm code, message, program, block, severity
        /// </summary>
        public Dictionary<string, string> ParseAlarm(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

            var alarms = new List<string>();
            int alarmIndex = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                var trimmed = line.Trim();
                // Skip comments
                if (trimmed.StartsWith("(") || trimmed.StartsWith(";"))
                    continue;

                // Try CSV format: CODE,MESSAGE,PROGRAM,BLOCK,SEVERITY
                var parts = trimmed.Split(',');
                if (parts.Length >= 2)
                {
                    var code = parts[0].Trim();
                    var message = parts.Length > 1 ? parts[1].Trim() : "";
                    
                    // Only process if we have a valid alarm code
                    if (!string.IsNullOrWhiteSpace(code) && code != "0" && code != "")
                    {
                        var program = parts.Length > 2 ? parts[2].Trim() : "";
                        var block = parts.Length > 3 ? parts[3].Trim() : "";
                        var severity = parts.Length > 4 ? parts[4].Trim() : "error";
                        
                        // Store individual alarm
                        result[$"Alarm {alarmIndex} Code"] = code;
                        result[$"Alarm {alarmIndex} Message"] = message;
                        if (!string.IsNullOrWhiteSpace(program))
                            result[$"Alarm {alarmIndex} Program"] = program;
                        if (!string.IsNullOrWhiteSpace(block))
                            result[$"Alarm {alarmIndex} Block"] = block;
                        result[$"Alarm {alarmIndex} Severity"] = severity;
                        
                        alarms.Add($"{code}:{message}");
                        alarmIndex++;
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
        /// Contains information about tools currently loaded in the ATC magazine.
        /// Format: M##,tool_num,?,?,color where M## = pot (##-1) and last field is color.
        /// Tool data (diameter, length, group, life, type) should be cross-referenced with tool table (TOLNI1).
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
                int potNum = -1;
                int toolNum = -1;
                
                // Format appears to be: M##,tool_num,...
                // M## = Pot number (M10 = pot 10)
                // Second field = Tool number
                if (parts.Length >= 2)
                {
                    var potStr = parts[0].Trim();
                    // Try parsing pot number from "M##" format
                    // M## maps to pot (##-1), so M02 = pot 1, M11 = pot 10
                    if (potStr.StartsWith("M"))
                    {
                        if (int.TryParse(potStr.Substring(1), out int mNum))
                        {
                            potNum = mNum - 1; // M## = pot (##-1)
                        }
                    }
                    // Also try "P##" format
                    else if (potStr.StartsWith("P"))
                    {
                        if (int.TryParse(potStr.Substring(1), out potNum))
                        {
                            // Pot number found
                        }
                    }
                    // Or just a number
                    else if (int.TryParse(potStr, out potNum))
                    {
                        // Pot number found
                    }
                    
                    // Tool number is in second field
                    var toolNumStr = parts[1].Trim();
                    if (toolNumStr.StartsWith("T"))
                    {
                        if (int.TryParse(toolNumStr.Substring(1), out toolNum))
                        {
                            // Tool number found
                        }
                    }
                    else if (int.TryParse(toolNumStr, out toolNum))
                    {
                        // Tool number found
                    }
                }
                
                // Strategy 2: Look for pot and tool anywhere in the line
                if (potNum == -1 || toolNum == -1)
                {
                    // Search for patterns like "P10" or "10" for pot, "T24" or "24" for tool
                    foreach (var part in parts)
                    {
                        var cleanPart = part.Trim();
                        if (potNum == -1)
                        {
                            if (cleanPart.StartsWith("P") && int.TryParse(cleanPart.Substring(1), out int p))
                            {
                                potNum = p;
                            }
                            else if (int.TryParse(cleanPart, out int p2) && p2 >= 1 && p2 <= 100) // Pot numbers are typically 1-100
                            {
                                // Could be pot, but need to verify it's not tool number
                                if (toolNum == -1 || p2 != toolNum)
                                {
                                    potNum = p2;
                                }
                            }
                        }
                        
                        if (toolNum == -1)
                        {
                            if (cleanPart.StartsWith("T") && int.TryParse(cleanPart.Substring(1), out int t))
                            {
                                toolNum = t;
                            }
                            else if (int.TryParse(cleanPart, out int t2) && t2 >= 1 && t2 <= 99) // Tool numbers are typically 1-99
                            {
                                // Could be tool number
                                if (potNum == -1 || t2 != potNum)
                                {
                                    toolNum = t2;
                                }
                            }
                        }
                    }
                }
                
                // If we found both pot and tool, extract the data
                if (potNum > 0 && toolNum > 0)
                {
                    // Debug: log first few successful parses, especially pot 10 -> tool 24
                    if (toolCount < 5 || (potNum == 10 && toolNum == 24))
                    {
                        Console.Error.WriteLine($"[DEBUG] ParseAtctl: Parsed pot {potNum}, tool {toolNum}, line: {trimmed.Substring(0, Math.Min(100, trimmed.Length))}");
                    }
                    
                            // Format: M##,tool_num,?,?,color
                            // Last field is color
                            var toolName = parts.Length > 2 ? parts[2].Trim().Trim('\'').Trim() : "";
                            // Last field is color
                            var color = parts.Length > 1 ? parts[parts.Length - 1].Trim() : "0";
                            
                            // Get tool data from tool table (TOLNI1) if available
                            var length = "0";
                            var diameter = "0";
                            var group = "";
                            var life = "0";
                            var toolType = "1";
                            
                            if (toolTableData != null)
                            {
                                // Look up tool data by tool number
                                var toolKey = $"Tool {toolNum}";
                                if (toolTableData.ContainsKey($"{toolKey} Length"))
                                    length = toolTableData[$"{toolKey} Length"];
                                if (toolTableData.ContainsKey($"{toolKey} Diameter"))
                                    diameter = toolTableData[$"{toolKey} Diameter"];
                                if (toolTableData.ContainsKey($"{toolKey} Group"))
                                    group = toolTableData[$"{toolKey} Group"];
                                if (toolTableData.ContainsKey($"{toolKey} Life"))
                                    life = toolTableData[$"{toolKey} Life"];
                                // Tool name from tool table if not in ATCTL
                                if (string.IsNullOrWhiteSpace(toolName) && toolTableData.ContainsKey($"{toolKey} Name"))
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
                    result[$"ATC Pot {potNum} Color"] = color;

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
        /// Parse TOLNI1 file - tool table data (CSV format).
        /// Format: T##,length,field2,diameter,field4,group,tool_life_limit,field7,life,tool_name,...
        /// Example: T01,3.4494,0.0000,0.2500,0.0000,2,10000,9500,9952,'.250 3FL      ',...
        /// Field 6 is tool life limit (max life), field 8 is current life remaining.
        /// Field 7 is unknown and not currently mapped.
        /// </summary>
        public Dictionary<string, string> ParseTolni(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

            var tools = new List<string>();
            int toolCount = 0;

            // Debug: log first few lines to understand format
            Console.Error.WriteLine($"[DEBUG] ParseTolni: Processing {lines.Length} lines");
            for (int i = 0; i < Math.Min(3, lines.Length); i++)
            {
                Console.Error.WriteLine($"[DEBUG] ParseTolni: Line {i}: '{lines[i]}'");
            }
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                var trimmed = line.Trim();
                // Skip comments
                if (trimmed.StartsWith("(") || trimmed.StartsWith(";"))
                    continue;

                // Format: T##,length,diameter,corner_radius,flute_length,group,spindle_speed_max,spindle_speed_min,life,tool_name,...
                // OR: T##,field1,field2,field3,... (need to identify which field is diameter)
                var parts = trimmed.Split(',');
                if (parts.Length < 2)
                    continue;

                // First field is tool number (T##)
                var toolNumStr = parts[0].Trim().ToUpper();
                if (!toolNumStr.StartsWith("T"))
                    continue;

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
                    
                    // Format: T##,length,field2,diameter,field4,group,tool_life_limit,field7,life,tool_name,...
                    // Example: T01,3.4494,0.0000,0.2500,0.0000,2,10000,9500,9952,'.250 3FL      ',...
                    // Field 0: Tool number (T##)
                    // Field 1: Length
                    // Field 2: Unknown (often 0.0000)
                    // Field 3: Diameter (THIS IS THE DIAMETER!)
                    // Field 4: Unknown (often 0.0000)
                    // Field 5: Group
                    // Field 6: Tool life limit (maximum tool life)
                    // Field 7: Unknown (needs verification - might be related to tool life or another parameter)
                    // Field 8: Life (current tool life remaining)
                    // Field 9: Tool name
                    
                    // Length (field 1)
                    if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        var len = parts[1].Trim();
                        toolData.Append($"LEN={len},");
                        result[$"Tool {toolNum} Length"] = len;
                    }
                    
                    // Diameter (field 3) - THIS IS THE CORRECT FIELD
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
                    
                    // Group (field 5)
                    if (parts.Length > 5 && !string.IsNullOrWhiteSpace(parts[5]))
                    {
                        var group = parts[5].Trim();
                        result[$"Tool {toolNum} Group"] = group;
                    }
                    
                    // Tool life limit (field 6) - maximum tool life
                    if (parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6]))
                    {
                        var lifeLimit = parts[6].Trim();
                        result[$"Tool {toolNum} Life Limit"] = lifeLimit;
                    }
                    
                    // Field 7: Unknown - not mapping until we know what it represents
                    // (was previously mapped as "Spindle Speed Min" but that was incorrect)
                    
                    // Life (field 8)
                    if (parts.Length > 8 && !string.IsNullOrWhiteSpace(parts[8]))
                    {
                        var life = parts[8].Trim();
                        result[$"Tool {toolNum} Life"] = life;
                    }
                    
                    // Tool name (field 9, typically)
                    if (parts.Length > 9 && !string.IsNullOrWhiteSpace(parts[9]))
                    {
                        var toolName = parts[9].Trim().Trim('\'').Trim();
                        if (!string.IsNullOrWhiteSpace(toolName))
                        {
                            toolData.Append($"NAME={toolName},");
                            result[$"Tool {toolNum} Name"] = toolName;
                        }
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

