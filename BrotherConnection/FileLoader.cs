using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BrotherConnection
{
    /// <summary>
    /// Helper class to load and parse different file types from Brother CNC via LOD command.
    /// </summary>
    public class FileLoader
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
        /// Format: CSV-like with pot number, tool number, tool data per line
        /// </summary>
        public Dictionary<string, string> ParseAtctl(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

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

                // Try CSV format: POT,TOOL_NUM,TOOL_NAME,LENGTH,DIAMETER,GROUP,LIFE,TYPE,COLOR
                var parts = trimmed.Split(',');
                if (parts.Length >= 3)
                {
                    // Pot number (slot in ATC)
                    var potStr = parts[0].Trim();
                    if (int.TryParse(potStr, out int potNum))
                    {
                        // Tool number
                        var toolNumStr = parts[1].Trim();
                        if (int.TryParse(toolNumStr, out int toolNum))
                        {
                            var toolName = parts.Length > 2 ? parts[2].Trim().Trim('\'').Trim() : "";
                            var length = parts.Length > 3 ? parts[3].Trim() : "0";
                            var diameter = parts.Length > 4 ? parts[4].Trim() : "0";
                            var group = parts.Length > 5 ? parts[5].Trim() : "";
                            var life = parts.Length > 6 ? parts[6].Trim() : "0";
                            var toolType = parts.Length > 7 ? parts[7].Trim() : "1";
                            var color = parts.Length > 8 ? parts[8].Trim() : "0";

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
                    }
                }
            }

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
        /// Format: T##,length,diameter,corner_radius,flute_length,group,spindle_speed_max,spindle_speed_min,life,tool_name,...
        /// </summary>
        public Dictionary<string, string> ParseTolni(string[] lines)
        {
            var result = new Dictionary<string, string>();
            
            if (lines == null || lines.Length == 0)
                return result;

            var tools = new List<string>();
            int toolCount = 0;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                var trimmed = line.Trim();
                // Skip comments
                if (trimmed.StartsWith("(") || trimmed.StartsWith(";"))
                    continue;

                // Format: T##,length,diameter,corner_radius,flute_length,group,spindle_speed_max,spindle_speed_min,life,tool_name,...
                var parts = trimmed.Split(',');
                if (parts.Length < 2)
                    continue;

                // First field is tool number (T##)
                var toolNumStr = parts[0].Trim().ToUpper();
                if (!toolNumStr.StartsWith("T"))
                    continue;

                if (int.TryParse(toolNumStr.Substring(1), out int toolNum))
                {
                    // Extract key fields
                    var toolData = new StringBuilder();
                    toolData.Append($"T{toolNum:D2},");
                    
                    // Length (field 1)
                    if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        var len = parts[1].Trim();
                        toolData.Append($"LEN={len},");
                        result[$"Tool {toolNum} Length"] = len;
                    }
                    
                    // Diameter (field 2) 
                    if (parts.Length > 2 && !string.IsNullOrWhiteSpace(parts[2]))
                    {
                        var dia = parts[2].Trim();
                        toolData.Append($"DIA={dia},");
                        result[$"Tool {toolNum} Diameter"] = dia;
                    }
                    
                    // Group (field 5)
                    if (parts.Length > 5 && !string.IsNullOrWhiteSpace(parts[5]))
                    {
                        var group = parts[5].Trim();
                        result[$"Tool {toolNum} Group"] = group;
                    }
                    
                    // Spindle speed max/min (fields 6, 7)
                    if (parts.Length > 6 && !string.IsNullOrWhiteSpace(parts[6]))
                        result[$"Tool {toolNum} Spindle Speed Max"] = parts[6].Trim();
                    if (parts.Length > 7 && !string.IsNullOrWhiteSpace(parts[7]))
                        result[$"Tool {toolNum} Spindle Speed Min"] = parts[7].Trim();
                    
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

