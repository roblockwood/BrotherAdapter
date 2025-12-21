using BrotherConnection.Mapping;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BrotherConnection
{
    class Program
    {
        static void Main(string[] args)
        {
            // Get CNC IP from environment for logging
            var cncIp = Environment.GetEnvironmentVariable("CNC_IP_ADDRESS") ?? "10.0.0.25";
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
            Console.WriteLine($"[INFO] Agent will attempt to connect every 2 seconds...");
            Console.WriteLine();
            
            // Start MTConnect HTTP server
            var mtconnectServer = new MTConnectServer(agentPort);
            mtconnectServer.Start();
            
            // Load data mapping for PDSP
            var prodData3Map = JsonConvert.DeserializeObject<DataMap>(File.ReadAllText("ProductionData3.json"));
            
            // Initialize file loader
            var fileLoader = new FileLoader();
            
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
                    
                    // Load TOLNI1 (tool table)
                    try
                    {
                        var tolniLines = fileLoader.LoadFile("TOLNI1");
                        if (tolniLines != null)
                        {
                            var tolniData = fileLoader.ParseTolni(tolniLines);
                            foreach (var kvp in tolniData)
                            {
                                DecodedResults[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WARNING] Failed to load/parse TOLNI1: {ex.Message}");
                    }
                    
                    // Load POSNI1 (work offsets)
                    try
                    {
                        var posniLines = fileLoader.LoadFile("POSNI1");
                        if (posniLines != null)
                        {
                            var posniData = fileLoader.ParsePosni(posniLines);
                            foreach (var kvp in posniData)
                            {
                                DecodedResults[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[WARNING] Failed to load/parse POSNI1: {ex.Message}");
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
                    
                    // Load ATCTL (ATC control - tools currently loaded in ATC magazine)
                    try
                    {
                        var atctlLines = fileLoader.LoadFile("ATCTL");
                        if (atctlLines != null)
                        {
                            var atctlData = fileLoader.ParseAtctl(atctlLines);
                            foreach (var kvp in atctlData)
                            {
                                DecodedResults[kvp.Key] = kvp.Value;
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
    }
}

