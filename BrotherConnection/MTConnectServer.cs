using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BrotherConnection
{
    /// <summary>
    /// HTTP server for exposing MTConnect REST API endpoints.
    /// </summary>
    public class MTConnectServer
    {
        private HttpListener _listener;
        private int _port;
        private bool _running;
        private Thread _serverThread;
        private Dictionary<string, string> _latestData;
        private object _dataLock = new object();

        public MTConnectServer(int port = 7878)
        {
            _port = port;
            _latestData = new Dictionary<string, string>();
        }

        /// <summary>
        /// Start the HTTP server in a background thread.
        /// </summary>
        public void Start()
        {
            if (_running)
            {
                Console.WriteLine($"[WARNING] MTConnect server already running on port {_port}");
                return;
            }

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{_port}/");
            
            try
            {
                _listener.Start();
                _running = true;
                Console.WriteLine($"[INFO] MTConnect HTTP server started on port {_port}");
                Console.WriteLine($"[INFO] Endpoints available:");
                Console.WriteLine($"[INFO]   - http://localhost:{_port}/probe");
                Console.WriteLine($"[INFO]   - http://localhost:{_port}/current");
                Console.WriteLine($"[INFO]   - http://localhost:{_port}/sample");

                _serverThread = new Thread(Listen);
                _serverThread.IsBackground = true;
                _serverThread.Start();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Failed to start MTConnect HTTP server: {ex.Message}");
                Console.Error.WriteLine($"[ERROR] Make sure port {_port} is available and you have permission to bind to it");
                _running = false;
            }
        }

        /// <summary>
        /// Stop the HTTP server.
        /// </summary>
        public void Stop()
        {
            if (!_running)
                return;

            _running = false;
            _listener?.Stop();
            _listener?.Close();
            Console.WriteLine($"[INFO] MTConnect HTTP server stopped");
        }

        /// <summary>
        /// Update the latest data collected from the CNC machine.
        /// </summary>
        public void UpdateData(Dictionary<string, string> data)
        {
            lock (_dataLock)
            {
                _latestData = new Dictionary<string, string>(data);
            }
        }

        /// <summary>
        /// Main server loop - handles incoming HTTP requests.
        /// </summary>
        private void Listen()
        {
            while (_running)
            {
                try
                {
                    var context = _listener.GetContext();
                    Task.Run(() => HandleRequest(context));
                }
                catch (HttpListenerException ex)
                {
                    if (_running)
                    {
                        Console.Error.WriteLine($"[ERROR] HTTP listener error: {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    if (_running)
                    {
                        Console.Error.WriteLine($"[ERROR] Unexpected error in HTTP server: {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Handle an incoming HTTP request.
        /// </summary>
        private void HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                string path = request.Url.AbsolutePath;
                string method = request.HttpMethod;

                Console.WriteLine($"[DEBUG] {method} {path} from {request.RemoteEndPoint}");

                // Set CORS headers
                response.AddHeader("Access-Control-Allow-Origin", "*");
                response.AddHeader("Access-Control-Allow-Methods", "GET, OPTIONS");
                response.AddHeader("Access-Control-Allow-Headers", "Content-Type");

                // Handle OPTIONS (CORS preflight)
                if (method == "OPTIONS")
                {
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }

                // Route requests
                string xmlResponse = "";
                string contentType = "application/xml";

                if (path == "/probe" || path == "/probe/")
                {
                    xmlResponse = GenerateProbeXml();
                }
                else if (path == "/current" || path == "/current/")
                {
                    xmlResponse = GenerateCurrentXml();
                }
                else if (path == "/sample" || path == "/sample/")
                {
                    xmlResponse = GenerateSampleXml();
                }
                else if (path == "/" || path == "")
                {
                    // Root endpoint - return simple info
                    response.ContentType = "text/plain";
                    var info = Encoding.UTF8.GetBytes("MTConnect Agent for Brother CNC\nEndpoints: /probe, /current, /sample");
                    response.ContentLength64 = info.Length;
                    response.OutputStream.Write(info, 0, info.Length);
                    response.StatusCode = 200;
                    response.Close();
                    return;
                }
                else
                {
                    response.StatusCode = 404;
                    response.ContentType = "text/plain";
                    var notFound = Encoding.UTF8.GetBytes("404 Not Found\nAvailable endpoints: /probe, /current, /sample");
                    response.ContentLength64 = notFound.Length;
                    response.OutputStream.Write(notFound, 0, notFound.Length);
                    response.Close();
                    return;
                }

                // Send XML response
                byte[] buffer = Encoding.UTF8.GetBytes(xmlResponse);
                response.ContentType = contentType;
                response.ContentLength64 = buffer.Length;
                response.StatusCode = 200;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.Close();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] Error handling request: {ex.Message}");
                try
                {
                    response.StatusCode = 500;
                    response.ContentType = "text/plain";
                    var error = Encoding.UTF8.GetBytes($"500 Internal Server Error: {ex.Message}");
                    response.ContentLength64 = error.Length;
                    response.OutputStream.Write(error, 0, error.Length);
                    response.Close();
                }
                catch
                {
                    // Ignore errors when sending error response
                }
            }
        }

        /// <summary>
        /// Generate MTConnect probe XML (device structure).
        /// </summary>
        private string GenerateProbeXml()
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var uuid = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                .Select(ni => ni.GetPhysicalAddress().ToString())
                .FirstOrDefault() ?? "brother-cnc-agent";

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<MTConnectDevices xmlns=""urn:mtconnect.org:MTConnectDevices:1.7"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:schemaLocation=""urn:mtconnect.org:MTConnectDevices:1.7 http://www.mtconnect.org/schemas/MTConnectDevices_1.7.xsd"">
  <Header creationTime=""{timestamp}"" sender=""BrotherAdapter"" version=""1.7.0"" instanceId=""1"" bufferSize=""131072"" />
  <Devices>
    <Device id=""brother-cnc"" uuid=""{uuid}"" name=""Brother CNC Machine"">
      <Description manufacturer=""Brother"" model=""CNC"">Brother CNC Machine via MTConnect Adapter</Description>
      <DataItems>
        <DataItem id=""avail"" type=""AVAILABILITY"" category=""EVENT"" />
        <DataItem id=""execution"" type=""EXECUTION"" category=""EVENT"" />
        <DataItem id=""mode"" type=""CONTROLLER_MODE"" category=""EVENT"" />
        <DataItem id=""program"" type=""PROGRAM"" category=""EVENT"" />
        <DataItem id=""spindle_speed"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""REVOLUTION/MINUTE"" />
        <DataItem id=""path_feedrate"" type=""PATH_FEEDRATE"" category=""SAMPLE"" units=""MILLIMETER/SECOND"" />
        <DataItem id=""Xact"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""MACHINE"" />
        <DataItem id=""Yact"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""MACHINE"" />
        <DataItem id=""Zact"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""MACHINE"" />
        <DataItem id=""alarm"" type=""ALARM"" category=""EVENT"" />
      </DataItems>
      <Components>
        <Controller id=""controller"" name=""controller"">
          <DataItemRefs>
            <DataItemRef dataItemId=""avail"" />
            <DataItemRef dataItemId=""execution"" />
            <DataItemRef dataItemId=""mode"" />
            <DataItemRef dataItemId=""program"" />
            <DataItemRef dataItemId=""alarm"" />
          </DataItemRefs>
        </Controller>
        <Axes id=""axes"" name=""axes"">
          <Linear id=""X"" name=""X"">
            <DataItemRefs>
              <DataItemRef dataItemId=""Xact"" />
            </DataItemRefs>
          </Linear>
          <Linear id=""Y"" name=""Y"">
            <DataItemRefs>
              <DataItemRef dataItemId=""Yact"" />
            </DataItemRefs>
          </Linear>
          <Linear id=""Z"" name=""Z"">
            <DataItemRefs>
              <DataItemRef dataItemId=""Zact"" />
            </DataItemRefs>
          </Linear>
        </Axes>
        <Spindle id=""spindle"" name=""spindle"">
          <DataItemRefs>
            <DataItemRef dataItemId=""spindle_speed"" />
          </DataItemRefs>
        </Spindle>
        <Path id=""path"" name=""path"">
          <DataItemRefs>
            <DataItemRef dataItemId=""path_feedrate"" />
          </DataItemRefs>
        </Path>
      </Components>
    </Device>
  </Devices>
</MTConnectDevices>";
        }

        /// <summary>
        /// Generate MTConnect current XML (current data values).
        /// </summary>
        private string GenerateCurrentXml()
        {
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            
            lock (_dataLock)
            {
                // Map Brother data to MTConnect data items
                string avail = MapAvailability();
                string execution = MapExecution();
                string mode = "AUTOMATIC"; // Default, could be mapped from Brother data
                string program = _latestData.ContainsKey("Program name") ? _latestData["Program name"] : "";
                
                // Spindle speed (RPM)
                string spindleSpeed = _latestData.ContainsKey("Spindle Speed") ? _latestData["Spindle Speed"] : "0";
                
                // Feedrate - convert from percentage to actual feedrate if available
                // Brother provides "Feedrate override" as percentage, and "Feedrate" as actual value
                string feedrate = "0";
                if (_latestData.ContainsKey("Feedrate"))
                {
                    feedrate = _latestData["Feedrate"];
                }
                else if (_latestData.ContainsKey("Feedrate override"))
                {
                    // Use override as fallback (this is percentage, not ideal but better than nothing)
                    feedrate = _latestData["Feedrate override"];
                }
                
                // Machine coordinate positions (in mm typically for Brother)
                string xPos = _latestData.ContainsKey("Machine coordinate position (X-Axis)") ? _latestData["Machine coordinate position (X-Axis)"] : "0";
                string yPos = _latestData.ContainsKey("Machine coordinate position (Y-Axis)") ? _latestData["Machine coordinate position (Y-Axis)"] : "0";
                string zPos = _latestData.ContainsKey("Machine coordinate position (Z-Axis)") ? _latestData["Machine coordinate position (Z-Axis)"] : "0";
                string alarm = MapAlarm();

                return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<MTConnectStreams xmlns=""urn:mtconnect.org:MTConnectStreams:1.7"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" xsi:schemaLocation=""urn:mtconnect.org:MTConnectStreams:1.7 http://www.mtconnect.org/schemas/MTConnectStreams_1.7.xsd"">
  <Header creationTime=""{timestamp}"" sender=""BrotherAdapter"" version=""1.7.0"" instanceId=""1"" bufferSize=""131072"" nextSequence=""1"" firstSequence=""1"" lastSequence=""1"" />
  <Streams>
    <DeviceStream name=""Brother CNC Machine"" uuid=""brother-cnc"">
      <ComponentStream component=""Controller"" name=""controller"">
        <Events>
          <Availability dataItemId=""avail"" timestamp=""{timestamp}"">{avail}</Availability>
          <Execution dataItemId=""execution"" timestamp=""{timestamp}"">{execution}</Execution>
          <ControllerMode dataItemId=""mode"" timestamp=""{timestamp}"">{mode}</ControllerMode>
          <Program dataItemId=""program"" timestamp=""{timestamp}"">{EscapeXml(program)}</Program>
          {alarm}
        </Events>
      </ComponentStream>
      <ComponentStream component=""Axes"" name=""axes"">
        <Samples>
          <Position dataItemId=""Xact"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""MACHINE"">{EscapeXml(xPos)}</Position>
          <Position dataItemId=""Yact"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""MACHINE"">{EscapeXml(yPos)}</Position>
          <Position dataItemId=""Zact"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""MACHINE"">{EscapeXml(zPos)}</Position>
        </Samples>
      </ComponentStream>
      <ComponentStream component=""Spindle"" name=""spindle"">
        <Samples>
          <RotaryVelocity dataItemId=""spindle_speed"" timestamp=""{timestamp}"">{EscapeXml(spindleSpeed)}</RotaryVelocity>
        </Samples>
      </ComponentStream>
      <ComponentStream component=""Path"" name=""path"">
        <Samples>
          <PathFeedrate dataItemId=""path_feedrate"" timestamp=""{timestamp}"">{EscapeXml(feedrate)}</PathFeedrate>
        </Samples>
      </ComponentStream>
    </DeviceStream>
  </Streams>
</MTConnectStreams>";
            }
        }

        /// <summary>
        /// Generate MTConnect sample XML (historical samples).
        /// </summary>
        private string GenerateSampleXml()
        {
            // For now, return current data as sample
            // In a full implementation, this would return historical data
            return GenerateCurrentXml();
        }

        /// <summary>
        /// Map Brother availability to MTConnect availability.
        /// </summary>
        private string MapAvailability()
        {
            // If we have data, assume available
            lock (_dataLock)
            {
                return _latestData.Count > 0 ? "AVAILABLE" : "UNAVAILABLE";
            }
        }

        /// <summary>
        /// Map Brother execution state to MTConnect execution.
        /// </summary>
        private string MapExecution()
        {
            lock (_dataLock)
            {
                // Try to infer execution state from Brother data
                if (_latestData.ContainsKey("Spindle Speed"))
                {
                    if (int.TryParse(_latestData["Spindle Speed"], out int speed) && speed > 0)
                    {
                        return "ACTIVE";
                    }
                }
                
                // Default to READY if we have data but no clear indication
                return _latestData.Count > 0 ? "READY" : "UNAVAILABLE";
            }
        }

        /// <summary>
        /// Map Brother alarms to MTConnect alarm format.
        /// </summary>
        private string MapAlarm()
        {
            lock (_dataLock)
            {
                // Check for alarm indicators in Brother data
                // This is a simplified mapping - would need to check actual alarm data
                if (_latestData.ContainsKey("Door interlock"))
                {
                    var doorStatus = _latestData["Door interlock"];
                    if (doorStatus == "Enabled" || doorStatus.Contains("Alarm"))
                    {
                        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                        return $"<Alarm dataItemId=\"alarm\" timestamp=\"{timestamp}\" sequence=\"1\" type=\"SYSTEM\" nativeCode=\"DOOR\" severity=\"WARNING\">Door interlock active</Alarm>";
                    }
                }
                return "";
            }
        }

        /// <summary>
        /// Escape XML special characters.
        /// </summary>
        private string EscapeXml(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            
            return value
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
        }
    }
}

