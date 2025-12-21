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
                // Merge data instead of replacing, so that infrequently updated data
                // (like Tool table, ATC Tools, work offsets) doesn't get cleared
                // when UpdateData is called with only PDSP data
                foreach (var kvp in data)
                {
                    _latestData[kvp.Key] = kvp.Value;
                }
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
        <DataItem id=""work_counter_1"" type=""PART_COUNT"" category=""SAMPLE"" />
        <DataItem id=""work_counter_2"" type=""PART_COUNT"" category=""SAMPLE"" />
        <DataItem id=""work_counter_3"" type=""PART_COUNT"" category=""SAMPLE"" />
        <DataItem id=""work_counter_4"" type=""PART_COUNT"" category=""SAMPLE"" />
        <DataItem id=""tool_table"" type=""TOOL_MAGAZINE"" category=""EVENT"" />
        <DataItem id=""atc_table"" type=""TOOL_MAGAZINE"" category=""EVENT"" />
        <DataItem id=""work_offset_g54_x"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g54_y"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g54_z"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g55_x"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g55_y"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g55_z"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g56_x"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g56_y"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g56_z"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g57_x"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g57_y"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g57_z"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g58_x"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g58_y"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g58_z"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g59_x"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g59_y"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g59_z"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""work_offset_g54_a"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g54_b"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g54_c"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g55_a"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g55_b"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g55_c"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g56_a"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g56_b"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g56_c"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g57_a"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g57_b"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g57_c"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g58_a"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g58_b"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g58_c"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g59_a"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g59_b"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""work_offset_g59_c"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""DEGREE"" />
        <DataItem id=""extended_offset_x01_x"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x01_y"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x01_z"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x02_x"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x02_y"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x02_z"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x03_x"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x03_y"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x03_z"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x04_x"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x04_y"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x04_z"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x05_x"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x05_y"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x05_z"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x06_x"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x06_y"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x06_z"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x07_x"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x07_y"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x07_z"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x08_x"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x08_y"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x08_z"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x09_x"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x09_y"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x09_z"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x10_x"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x10_y"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""extended_offset_x10_z"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""WORK"" />
        <DataItem id=""counter_1_end_signal"" type=""PART_COUNT"" category=""SAMPLE"" />
        <DataItem id=""counter_2_end_signal"" type=""PART_COUNT"" category=""SAMPLE"" />
        <DataItem id=""counter_3_end_signal"" type=""PART_COUNT"" category=""SAMPLE"" />
        <DataItem id=""counter_4_end_signal"" type=""PART_COUNT"" category=""SAMPLE"" />
        <DataItem id=""counter_1_status"" type=""AVAILABILITY"" category=""EVENT"" />
        <DataItem id=""counter_2_status"" type=""AVAILABILITY"" category=""EVENT"" />
        <DataItem id=""counter_3_status"" type=""AVAILABILITY"" category=""EVENT"" />
        <DataItem id=""counter_4_status"" type=""AVAILABILITY"" category=""EVENT"" />
        <DataItem id=""alarm_program"" type=""PROGRAM"" category=""EVENT"" />
        <DataItem id=""alarm_block"" type=""PROGRAM"" category=""EVENT"" />
        <DataItem id=""alarm_severity"" type=""ALARM"" category=""EVENT"" />
        <DataItem id=""cycle_time"" type=""PATH_FEEDRATE"" category=""SAMPLE"" />
        <DataItem id=""cutting_time"" type=""PATH_FEEDRATE"" category=""SAMPLE"" />
        <DataItem id=""operation_time"" type=""PATH_FEEDRATE"" category=""SAMPLE"" />
        <DataItem id=""power_on_hours"" type=""PATH_FEEDRATE"" category=""SAMPLE"" />
        <DataItem id=""alarm_code"" type=""ALARM"" category=""EVENT"" />
        <DataItem id=""alarm_message"" type=""ALARM"" category=""EVENT"" />
        <DataItem id=""alarms_table"" type=""ALARM"" category=""EVENT"" />
        <DataItem id=""counter_1_target"" type=""PART_COUNT"" category=""SAMPLE"" />
        <DataItem id=""counter_2_target"" type=""PART_COUNT"" category=""SAMPLE"" />
        <DataItem id=""counter_3_target"" type=""PART_COUNT"" category=""SAMPLE"" />
        <DataItem id=""counter_4_target"" type=""PART_COUNT"" category=""SAMPLE"" />
        <DataItem id=""spindle_speed"" type=""ROTARY_VELOCITY"" category=""SAMPLE"" units=""REVOLUTION/MINUTE"" />
        <DataItem id=""path_feedrate"" type=""PATH_FEEDRATE"" category=""SAMPLE"" units=""MILLIMETER/SECOND"" />
        <DataItem id=""Xact"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""MACHINE"" />
        <DataItem id=""Yact"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""MACHINE"" />
        <DataItem id=""Zact"" type=""POSITION"" category=""SAMPLE"" subType=""ACTUAL"" units=""MILLIMETER"" coordinateSystem=""MACHINE"" />
      </DataItems>
      <Components>
        <Controller id=""controller"" name=""controller"">
          <DataItemRefs>
          <DataItemRef dataItemId=""avail"" />
          <DataItemRef dataItemId=""execution"" />
          <DataItemRef dataItemId=""mode"" />
          <DataItemRef dataItemId=""program"" />
          <DataItemRef dataItemId=""work_counter_1"" />
          <DataItemRef dataItemId=""work_counter_2"" />
          <DataItemRef dataItemId=""work_counter_3"" />
          <DataItemRef dataItemId=""work_counter_4"" />
          <DataItemRef dataItemId=""tool_table"" />
          <DataItemRef dataItemId=""atc_table"" />
        </DataItemRefs>
        </Controller>
        <Systems id=""systems"" name=""systems"">
          <DataItemRefs>
            <DataItemRef dataItemId=""work_offset_g54_x"" />
            <DataItemRef dataItemId=""work_offset_g54_y"" />
            <DataItemRef dataItemId=""work_offset_g54_z"" />
            <DataItemRef dataItemId=""work_offset_g55_x"" />
            <DataItemRef dataItemId=""work_offset_g55_y"" />
            <DataItemRef dataItemId=""work_offset_g55_z"" />
            <DataItemRef dataItemId=""work_offset_g56_x"" />
            <DataItemRef dataItemId=""work_offset_g56_y"" />
            <DataItemRef dataItemId=""work_offset_g56_z"" />
            <DataItemRef dataItemId=""work_offset_g57_x"" />
            <DataItemRef dataItemId=""work_offset_g57_y"" />
            <DataItemRef dataItemId=""work_offset_g57_z"" />
            <DataItemRef dataItemId=""work_offset_g58_x"" />
            <DataItemRef dataItemId=""work_offset_g58_y"" />
            <DataItemRef dataItemId=""work_offset_g58_z"" />
            <DataItemRef dataItemId=""work_offset_g59_x"" />
            <DataItemRef dataItemId=""work_offset_g59_y"" />
            <DataItemRef dataItemId=""work_offset_g59_z"" />
            <DataItemRef dataItemId=""work_offset_g54_a"" />
            <DataItemRef dataItemId=""work_offset_g54_b"" />
            <DataItemRef dataItemId=""work_offset_g54_c"" />
            <DataItemRef dataItemId=""work_offset_g55_a"" />
            <DataItemRef dataItemId=""work_offset_g55_b"" />
            <DataItemRef dataItemId=""work_offset_g55_c"" />
            <DataItemRef dataItemId=""work_offset_g56_a"" />
            <DataItemRef dataItemId=""work_offset_g56_b"" />
            <DataItemRef dataItemId=""work_offset_g56_c"" />
            <DataItemRef dataItemId=""work_offset_g57_a"" />
            <DataItemRef dataItemId=""work_offset_g57_b"" />
            <DataItemRef dataItemId=""work_offset_g57_c"" />
            <DataItemRef dataItemId=""work_offset_g58_a"" />
            <DataItemRef dataItemId=""work_offset_g58_b"" />
            <DataItemRef dataItemId=""work_offset_g58_c"" />
            <DataItemRef dataItemId=""work_offset_g59_a"" />
            <DataItemRef dataItemId=""work_offset_g59_b"" />
            <DataItemRef dataItemId=""work_offset_g59_c"" />
            <DataItemRef dataItemId=""extended_offset_x01_x"" />
            <DataItemRef dataItemId=""extended_offset_x01_y"" />
            <DataItemRef dataItemId=""extended_offset_x01_z"" />
            <DataItemRef dataItemId=""extended_offset_x02_x"" />
            <DataItemRef dataItemId=""extended_offset_x02_y"" />
            <DataItemRef dataItemId=""extended_offset_x02_z"" />
            <DataItemRef dataItemId=""extended_offset_x03_x"" />
            <DataItemRef dataItemId=""extended_offset_x03_y"" />
            <DataItemRef dataItemId=""extended_offset_x03_z"" />
            <DataItemRef dataItemId=""extended_offset_x04_x"" />
            <DataItemRef dataItemId=""extended_offset_x04_y"" />
            <DataItemRef dataItemId=""extended_offset_x04_z"" />
            <DataItemRef dataItemId=""extended_offset_x05_x"" />
            <DataItemRef dataItemId=""extended_offset_x05_y"" />
            <DataItemRef dataItemId=""extended_offset_x05_z"" />
            <DataItemRef dataItemId=""extended_offset_x06_x"" />
            <DataItemRef dataItemId=""extended_offset_x06_y"" />
            <DataItemRef dataItemId=""extended_offset_x06_z"" />
            <DataItemRef dataItemId=""extended_offset_x07_x"" />
            <DataItemRef dataItemId=""extended_offset_x07_y"" />
            <DataItemRef dataItemId=""extended_offset_x07_z"" />
            <DataItemRef dataItemId=""extended_offset_x08_x"" />
            <DataItemRef dataItemId=""extended_offset_x08_y"" />
            <DataItemRef dataItemId=""extended_offset_x08_z"" />
            <DataItemRef dataItemId=""extended_offset_x09_x"" />
            <DataItemRef dataItemId=""extended_offset_x09_y"" />
            <DataItemRef dataItemId=""extended_offset_x09_z"" />
            <DataItemRef dataItemId=""extended_offset_x10_x"" />
            <DataItemRef dataItemId=""extended_offset_x10_y"" />
            <DataItemRef dataItemId=""extended_offset_x10_z"" />
          </DataItemRefs>
        </Systems>
        <Controller id=""controller_times"" name=""controller_times"">
          <DataItemRefs>
            <DataItemRef dataItemId=""cycle_time"" />
            <DataItemRef dataItemId=""cutting_time"" />
            <DataItemRef dataItemId=""operation_time"" />
            <DataItemRef dataItemId=""power_on_hours"" />
            <DataItemRef dataItemId=""alarm_code"" />
            <DataItemRef dataItemId=""alarm_message"" />
            <DataItemRef dataItemId=""alarm_program"" />
            <DataItemRef dataItemId=""alarm_block"" />
            <DataItemRef dataItemId=""alarm_severity"" />
            <DataItemRef dataItemId=""counter_1_target"" />
            <DataItemRef dataItemId=""counter_2_target"" />
            <DataItemRef dataItemId=""counter_3_target"" />
            <DataItemRef dataItemId=""counter_4_target"" />
            <DataItemRef dataItemId=""counter_1_end_signal"" />
            <DataItemRef dataItemId=""counter_2_end_signal"" />
            <DataItemRef dataItemId=""counter_3_end_signal"" />
            <DataItemRef dataItemId=""counter_4_end_signal"" />
            <DataItemRef dataItemId=""counter_1_status"" />
            <DataItemRef dataItemId=""counter_2_status"" />
            <DataItemRef dataItemId=""counter_3_status"" />
            <DataItemRef dataItemId=""counter_4_status"" />
            <DataItemRef dataItemId=""alarm_code"" />
            <DataItemRef dataItemId=""alarm_message"" />
            <DataItemRef dataItemId=""alarm_program"" />
            <DataItemRef dataItemId=""alarm_block"" />
            <DataItemRef dataItemId=""alarm_severity"" />
            <DataItemRef dataItemId=""alarms_table"" />
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
                
                // Workpiece counters (from WKCNTR file)
                string counter1 = _latestData.ContainsKey("Counter 1") ? _latestData["Counter 1"] : "0";
                string counter2 = _latestData.ContainsKey("Counter 2") ? _latestData["Counter 2"] : "0";
                string counter3 = _latestData.ContainsKey("Counter 3") ? _latestData["Counter 3"] : "0";
                string counter4 = _latestData.ContainsKey("Counter 4") ? _latestData["Counter 4"] : "0";
                string counter1Target = _latestData.ContainsKey("Counter 1 Target") ? _latestData["Counter 1 Target"] : "0";
                string counter2Target = _latestData.ContainsKey("Counter 2 Target") ? _latestData["Counter 2 Target"] : "0";
                string counter3Target = _latestData.ContainsKey("Counter 3 Target") ? _latestData["Counter 3 Target"] : "0";
                string counter4Target = _latestData.ContainsKey("Counter 4 Target") ? _latestData["Counter 4 Target"] : "0";
                string counter1EndSignal = _latestData.ContainsKey("Counter 1 End Signal") ? _latestData["Counter 1 End Signal"] : "0";
                string counter2EndSignal = _latestData.ContainsKey("Counter 2 End Signal") ? _latestData["Counter 2 End Signal"] : "0";
                string counter3EndSignal = _latestData.ContainsKey("Counter 3 End Signal") ? _latestData["Counter 3 End Signal"] : "0";
                string counter4EndSignal = _latestData.ContainsKey("Counter 4 End Signal") ? _latestData["Counter 4 End Signal"] : "0";
                string counter1Status = _latestData.ContainsKey("Counter 1 Status") ? _latestData["Counter 1 Status"] : "normal";
                string counter2Status = _latestData.ContainsKey("Counter 2 Status") ? _latestData["Counter 2 Status"] : "normal";
                string counter3Status = _latestData.ContainsKey("Counter 3 Status") ? _latestData["Counter 3 Status"] : "normal";
                string counter4Status = _latestData.ContainsKey("Counter 4 Status") ? _latestData["Counter 4 Status"] : "normal";
                
                // Tool table (from TOLNI1 file) - complete tool definitions
                string toolTable = _latestData.ContainsKey("Tool table") ? _latestData["Tool table"] : "";
                
                // ATC table (from ATCTL file) - tools currently loaded in ATC magazine
                string atcTable = _latestData.ContainsKey("ATC Tools") ? _latestData["ATC Tools"] : "";
                
                // Work offsets (from POSNI1 file) - G54-G59
                string g54_x = _latestData.ContainsKey("Work offset G54 X") ? _latestData["Work offset G54 X"] : "0";
                string g54_y = _latestData.ContainsKey("Work offset G54 Y") ? _latestData["Work offset G54 Y"] : "0";
                string g54_z = _latestData.ContainsKey("Work offset G54 Z") ? _latestData["Work offset G54 Z"] : "0";
                string g55_x = _latestData.ContainsKey("Work offset G55 X") ? _latestData["Work offset G55 X"] : "0";
                string g55_y = _latestData.ContainsKey("Work offset G55 Y") ? _latestData["Work offset G55 Y"] : "0";
                string g55_z = _latestData.ContainsKey("Work offset G55 Z") ? _latestData["Work offset G55 Z"] : "0";
                string g56_x = _latestData.ContainsKey("Work offset G56 X") ? _latestData["Work offset G56 X"] : "0";
                string g56_y = _latestData.ContainsKey("Work offset G56 Y") ? _latestData["Work offset G56 Y"] : "0";
                string g56_z = _latestData.ContainsKey("Work offset G56 Z") ? _latestData["Work offset G56 Z"] : "0";
                string g57_x = _latestData.ContainsKey("Work offset G57 X") ? _latestData["Work offset G57 X"] : "0";
                string g57_y = _latestData.ContainsKey("Work offset G57 Y") ? _latestData["Work offset G57 Y"] : "0";
                string g57_z = _latestData.ContainsKey("Work offset G57 Z") ? _latestData["Work offset G57 Z"] : "0";
                string g58_x = _latestData.ContainsKey("Work offset G58 X") ? _latestData["Work offset G58 X"] : "0";
                string g58_y = _latestData.ContainsKey("Work offset G58 Y") ? _latestData["Work offset G58 Y"] : "0";
                string g58_z = _latestData.ContainsKey("Work offset G58 Z") ? _latestData["Work offset G58 Z"] : "0";
                string g59_x = _latestData.ContainsKey("Work offset G59 X") ? _latestData["Work offset G59 X"] : "0";
                string g59_y = _latestData.ContainsKey("Work offset G59 Y") ? _latestData["Work offset G59 Y"] : "0";
                string g59_z = _latestData.ContainsKey("Work offset G59 Z") ? _latestData["Work offset G59 Z"] : "0";
                
                // Rotary axes for work offsets (A, B, C)
                string g54_a = _latestData.ContainsKey("Work offset G54 A") ? _latestData["Work offset G54 A"] : "0";
                string g54_b = _latestData.ContainsKey("Work offset G54 B") ? _latestData["Work offset G54 B"] : "0";
                string g54_c = _latestData.ContainsKey("Work offset G54 C") ? _latestData["Work offset G54 C"] : "0";
                string g55_a = _latestData.ContainsKey("Work offset G55 A") ? _latestData["Work offset G55 A"] : "0";
                string g55_b = _latestData.ContainsKey("Work offset G55 B") ? _latestData["Work offset G55 B"] : "0";
                string g55_c = _latestData.ContainsKey("Work offset G55 C") ? _latestData["Work offset G55 C"] : "0";
                string g56_a = _latestData.ContainsKey("Work offset G56 A") ? _latestData["Work offset G56 A"] : "0";
                string g56_b = _latestData.ContainsKey("Work offset G56 B") ? _latestData["Work offset G56 B"] : "0";
                string g56_c = _latestData.ContainsKey("Work offset G56 C") ? _latestData["Work offset G56 C"] : "0";
                string g57_a = _latestData.ContainsKey("Work offset G57 A") ? _latestData["Work offset G57 A"] : "0";
                string g57_b = _latestData.ContainsKey("Work offset G57 B") ? _latestData["Work offset G57 B"] : "0";
                string g57_c = _latestData.ContainsKey("Work offset G57 C") ? _latestData["Work offset G57 C"] : "0";
                string g58_a = _latestData.ContainsKey("Work offset G58 A") ? _latestData["Work offset G58 A"] : "0";
                string g58_b = _latestData.ContainsKey("Work offset G58 B") ? _latestData["Work offset G58 B"] : "0";
                string g58_c = _latestData.ContainsKey("Work offset G58 C") ? _latestData["Work offset G58 C"] : "0";
                string g59_a = _latestData.ContainsKey("Work offset G59 A") ? _latestData["Work offset G59 A"] : "0";
                string g59_b = _latestData.ContainsKey("Work offset G59 B") ? _latestData["Work offset G59 B"] : "0";
                string g59_c = _latestData.ContainsKey("Work offset G59 C") ? _latestData["Work offset G59 C"] : "0";
                
                // Extended offsets X01-X10 (parser uses "X1", "X2", etc. without leading zero)
                string x01_x = _latestData.ContainsKey("Extended offset X1 X") ? _latestData["Extended offset X1 X"] : "0";
                string x01_y = _latestData.ContainsKey("Extended offset X1 Y") ? _latestData["Extended offset X1 Y"] : "0";
                string x01_z = _latestData.ContainsKey("Extended offset X1 Z") ? _latestData["Extended offset X1 Z"] : "0";
                string x02_x = _latestData.ContainsKey("Extended offset X2 X") ? _latestData["Extended offset X2 X"] : "0";
                string x02_y = _latestData.ContainsKey("Extended offset X2 Y") ? _latestData["Extended offset X2 Y"] : "0";
                string x02_z = _latestData.ContainsKey("Extended offset X2 Z") ? _latestData["Extended offset X2 Z"] : "0";
                string x03_x = _latestData.ContainsKey("Extended offset X3 X") ? _latestData["Extended offset X3 X"] : "0";
                string x03_y = _latestData.ContainsKey("Extended offset X3 Y") ? _latestData["Extended offset X3 Y"] : "0";
                string x03_z = _latestData.ContainsKey("Extended offset X3 Z") ? _latestData["Extended offset X3 Z"] : "0";
                string x04_x = _latestData.ContainsKey("Extended offset X4 X") ? _latestData["Extended offset X4 X"] : "0";
                string x04_y = _latestData.ContainsKey("Extended offset X4 Y") ? _latestData["Extended offset X4 Y"] : "0";
                string x04_z = _latestData.ContainsKey("Extended offset X4 Z") ? _latestData["Extended offset X4 Z"] : "0";
                string x05_x = _latestData.ContainsKey("Extended offset X5 X") ? _latestData["Extended offset X5 X"] : "0";
                string x05_y = _latestData.ContainsKey("Extended offset X5 Y") ? _latestData["Extended offset X5 Y"] : "0";
                string x05_z = _latestData.ContainsKey("Extended offset X5 Z") ? _latestData["Extended offset X5 Z"] : "0";
                string x06_x = _latestData.ContainsKey("Extended offset X6 X") ? _latestData["Extended offset X6 X"] : "0";
                string x06_y = _latestData.ContainsKey("Extended offset X6 Y") ? _latestData["Extended offset X6 Y"] : "0";
                string x06_z = _latestData.ContainsKey("Extended offset X6 Z") ? _latestData["Extended offset X6 Z"] : "0";
                string x07_x = _latestData.ContainsKey("Extended offset X7 X") ? _latestData["Extended offset X7 X"] : "0";
                string x07_y = _latestData.ContainsKey("Extended offset X7 Y") ? _latestData["Extended offset X7 Y"] : "0";
                string x07_z = _latestData.ContainsKey("Extended offset X7 Z") ? _latestData["Extended offset X7 Z"] : "0";
                string x08_x = _latestData.ContainsKey("Extended offset X8 X") ? _latestData["Extended offset X8 X"] : "0";
                string x08_y = _latestData.ContainsKey("Extended offset X8 Y") ? _latestData["Extended offset X8 Y"] : "0";
                string x08_z = _latestData.ContainsKey("Extended offset X8 Z") ? _latestData["Extended offset X8 Z"] : "0";
                string x09_x = _latestData.ContainsKey("Extended offset X9 X") ? _latestData["Extended offset X9 X"] : "0";
                string x09_y = _latestData.ContainsKey("Extended offset X9 Y") ? _latestData["Extended offset X9 Y"] : "0";
                string x09_z = _latestData.ContainsKey("Extended offset X9 Z") ? _latestData["Extended offset X9 Z"] : "0";
                string x10_x = _latestData.ContainsKey("Extended offset X10 X") ? _latestData["Extended offset X10 X"] : "0";
                string x10_y = _latestData.ContainsKey("Extended offset X10 Y") ? _latestData["Extended offset X10 Y"] : "0";
                string x10_z = _latestData.ContainsKey("Extended offset X10 Z") ? _latestData["Extended offset X10 Z"] : "0";
                
                // Monitor data (from MONTR file)
                string cycleTime = _latestData.ContainsKey("Cycle time") ? _latestData["Cycle time"] : "";
                string cuttingTime = _latestData.ContainsKey("Cutting time") ? _latestData["Cutting time"] : "";
                string operationTime = _latestData.ContainsKey("Operation time") ? _latestData["Operation time"] : "";
                string powerOnHours = _latestData.ContainsKey("Power on time") ? _latestData["Power on time"] : "";
                
                // Alarm data (from ALARM file)
                // Get first alarm for backward compatibility (alarm_code, alarm_message)
                string alarmCode = _latestData.ContainsKey("Alarm 0 Code") ? _latestData["Alarm 0 Code"] : "";
                string alarmMessage = _latestData.ContainsKey("Alarm 0 Message") ? _latestData["Alarm 0 Message"] : "";
                string alarmProgram = _latestData.ContainsKey("Alarm 0 Program") ? _latestData["Alarm 0 Program"] : "";
                string alarmBlock = _latestData.ContainsKey("Alarm 0 Block") ? _latestData["Alarm 0 Block"] : "";
                string alarmSeverity = _latestData.ContainsKey("Alarm 0 Severity") ? _latestData["Alarm 0 Severity"] : "";
                
                // Build alarms table: pipe-delimited format
                // Format: CODE:MESSAGE:PROGRAM:BLOCK:SEVERITY|CODE:MESSAGE:PROGRAM:BLOCK:SEVERITY|...
                // Debug: check what alarm keys are in _latestData
                var alarmKeys = new List<string>();
                foreach (var key in _latestData.Keys)
                {
                    if (key.StartsWith("Alarm "))
                    {
                        alarmKeys.Add(key);
                    }
                }
                if (alarmKeys.Count > 0)
                {
                    var keysToShow = alarmKeys.Count > 10 ? string.Join(", ", alarmKeys.Take(10)) : string.Join(", ", alarmKeys);
                    Console.Error.WriteLine($"[DEBUG] MTConnectServer: Found {alarmKeys.Count} alarm keys in _latestData: {keysToShow}");
                }
                else
                {
                    Console.Error.WriteLine("[DEBUG] MTConnectServer: NO alarm keys found in _latestData!");
                }
                
                var alarmsList = new List<string>();
                int alarmIndex = 0;
                while (_latestData.ContainsKey($"Alarm {alarmIndex} Code"))
                {
                    var code = _latestData[$"Alarm {alarmIndex} Code"];
                    var message = _latestData.ContainsKey($"Alarm {alarmIndex} Message") ? _latestData[$"Alarm {alarmIndex} Message"] : "";
                    var alarmProg = _latestData.ContainsKey($"Alarm {alarmIndex} Program") ? _latestData[$"Alarm {alarmIndex} Program"] : "";
                    var alarmBlk = _latestData.ContainsKey($"Alarm {alarmIndex} Block") ? _latestData[$"Alarm {alarmIndex} Block"] : "";
                    var alarmSev = _latestData.ContainsKey($"Alarm {alarmIndex} Severity") ? _latestData[$"Alarm {alarmIndex} Severity"] : "error";
                    
                    if (!string.IsNullOrWhiteSpace(code) && code != "0")
                    {
                        alarmsList.Add($"{code}:{message}:{alarmProg}:{alarmBlk}:{alarmSev}");
                    }
                    alarmIndex++;
                }
                string alarmsTable = string.Join("|", alarmsList);
                // Debug: log alarms_table value
                if (!string.IsNullOrEmpty(alarmsTable))
                {
                    Console.Error.WriteLine($"[DEBUG] MTConnectServer: alarms_table has {alarmsList.Count} alarms: {alarmsTable.Substring(0, Math.Min(200, alarmsTable.Length))}");
                }
                else
                {
                    Console.Error.WriteLine("[DEBUG] MTConnectServer: alarms_table is EMPTY");
                }

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
        </Events>
        <Samples>
          <PartCount dataItemId=""work_counter_1"" timestamp=""{timestamp}"">{EscapeXml(counter1)}</PartCount>
          <PartCount dataItemId=""work_counter_2"" timestamp=""{timestamp}"">{EscapeXml(counter2)}</PartCount>
          <PartCount dataItemId=""work_counter_3"" timestamp=""{timestamp}"">{EscapeXml(counter3)}</PartCount>
          <PartCount dataItemId=""work_counter_4"" timestamp=""{timestamp}"">{EscapeXml(counter4)}</PartCount>
        </Samples>
      </ComponentStream>
      <ComponentStream component=""Systems"" name=""systems"">
        <Samples>
          <Position dataItemId=""work_offset_g54_x"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g54_x)}</Position>
          <Position dataItemId=""work_offset_g54_y"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g54_y)}</Position>
          <Position dataItemId=""work_offset_g54_z"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g54_z)}</Position>
          <Position dataItemId=""work_offset_g55_x"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g55_x)}</Position>
          <Position dataItemId=""work_offset_g55_y"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g55_y)}</Position>
          <Position dataItemId=""work_offset_g55_z"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g55_z)}</Position>
          <Position dataItemId=""work_offset_g56_x"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g56_x)}</Position>
          <Position dataItemId=""work_offset_g56_y"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g56_y)}</Position>
          <Position dataItemId=""work_offset_g56_z"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g56_z)}</Position>
          <Position dataItemId=""work_offset_g57_x"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g57_x)}</Position>
          <Position dataItemId=""work_offset_g57_y"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g57_y)}</Position>
          <Position dataItemId=""work_offset_g57_z"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g57_z)}</Position>
          <Position dataItemId=""work_offset_g58_x"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g58_x)}</Position>
          <Position dataItemId=""work_offset_g58_y"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g58_y)}</Position>
          <Position dataItemId=""work_offset_g58_z"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g58_z)}</Position>
          <Position dataItemId=""work_offset_g59_x"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g59_x)}</Position>
          <Position dataItemId=""work_offset_g59_y"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g59_y)}</Position>
          <Position dataItemId=""work_offset_g59_z"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(g59_z)}</Position>
          <RotaryVelocity dataItemId=""work_offset_g54_a"" timestamp=""{timestamp}"">{EscapeXml(g54_a)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g54_b"" timestamp=""{timestamp}"">{EscapeXml(g54_b)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g54_c"" timestamp=""{timestamp}"">{EscapeXml(g54_c)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g55_a"" timestamp=""{timestamp}"">{EscapeXml(g55_a)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g55_b"" timestamp=""{timestamp}"">{EscapeXml(g55_b)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g55_c"" timestamp=""{timestamp}"">{EscapeXml(g55_c)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g56_a"" timestamp=""{timestamp}"">{EscapeXml(g56_a)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g56_b"" timestamp=""{timestamp}"">{EscapeXml(g56_b)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g56_c"" timestamp=""{timestamp}"">{EscapeXml(g56_c)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g57_a"" timestamp=""{timestamp}"">{EscapeXml(g57_a)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g57_b"" timestamp=""{timestamp}"">{EscapeXml(g57_b)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g57_c"" timestamp=""{timestamp}"">{EscapeXml(g57_c)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g58_a"" timestamp=""{timestamp}"">{EscapeXml(g58_a)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g58_b"" timestamp=""{timestamp}"">{EscapeXml(g58_b)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g58_c"" timestamp=""{timestamp}"">{EscapeXml(g58_c)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g59_a"" timestamp=""{timestamp}"">{EscapeXml(g59_a)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g59_b"" timestamp=""{timestamp}"">{EscapeXml(g59_b)}</RotaryVelocity>
          <RotaryVelocity dataItemId=""work_offset_g59_c"" timestamp=""{timestamp}"">{EscapeXml(g59_c)}</RotaryVelocity>
          <Position dataItemId=""extended_offset_x01_x"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x01_x)}</Position>
          <Position dataItemId=""extended_offset_x01_y"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x01_y)}</Position>
          <Position dataItemId=""extended_offset_x01_z"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x01_z)}</Position>
          <Position dataItemId=""extended_offset_x02_x"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x02_x)}</Position>
          <Position dataItemId=""extended_offset_x02_y"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x02_y)}</Position>
          <Position dataItemId=""extended_offset_x02_z"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x02_z)}</Position>
          <Position dataItemId=""extended_offset_x03_x"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x03_x)}</Position>
          <Position dataItemId=""extended_offset_x03_y"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x03_y)}</Position>
          <Position dataItemId=""extended_offset_x03_z"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x03_z)}</Position>
          <Position dataItemId=""extended_offset_x04_x"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x04_x)}</Position>
          <Position dataItemId=""extended_offset_x04_y"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x04_y)}</Position>
          <Position dataItemId=""extended_offset_x04_z"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x04_z)}</Position>
          <Position dataItemId=""extended_offset_x05_x"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x05_x)}</Position>
          <Position dataItemId=""extended_offset_x05_y"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x05_y)}</Position>
          <Position dataItemId=""extended_offset_x05_z"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x05_z)}</Position>
          <Position dataItemId=""extended_offset_x06_x"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x06_x)}</Position>
          <Position dataItemId=""extended_offset_x06_y"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x06_y)}</Position>
          <Position dataItemId=""extended_offset_x06_z"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x06_z)}</Position>
          <Position dataItemId=""extended_offset_x07_x"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x07_x)}</Position>
          <Position dataItemId=""extended_offset_x07_y"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x07_y)}</Position>
          <Position dataItemId=""extended_offset_x07_z"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x07_z)}</Position>
          <Position dataItemId=""extended_offset_x08_x"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x08_x)}</Position>
          <Position dataItemId=""extended_offset_x08_y"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x08_y)}</Position>
          <Position dataItemId=""extended_offset_x08_z"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x08_z)}</Position>
          <Position dataItemId=""extended_offset_x09_x"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x09_x)}</Position>
          <Position dataItemId=""extended_offset_x09_y"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x09_y)}</Position>
          <Position dataItemId=""extended_offset_x09_z"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x09_z)}</Position>
          <Position dataItemId=""extended_offset_x10_x"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x10_x)}</Position>
          <Position dataItemId=""extended_offset_x10_y"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x10_y)}</Position>
          <Position dataItemId=""extended_offset_x10_z"" timestamp=""{timestamp}"" subType=""ACTUAL"" coordinateSystem=""WORK"">{EscapeXml(x10_z)}</Position>
        </Samples>
        <Events>
          <ToolMagazine dataItemId=""tool_table"" timestamp=""{timestamp}"">{EscapeXml(toolTable)}</ToolMagazine>
          <ToolMagazine dataItemId=""atc_table"" timestamp=""{timestamp}"">{EscapeXml(atcTable)}</ToolMagazine>
        </Events>
      </ComponentStream>
      <ComponentStream component=""Controller"" name=""controller_times"">
        <Samples>
          <PathFeedrate dataItemId=""cycle_time"" timestamp=""{timestamp}"">{EscapeXml(cycleTime)}</PathFeedrate>
          <PathFeedrate dataItemId=""cutting_time"" timestamp=""{timestamp}"">{EscapeXml(cuttingTime)}</PathFeedrate>
          <PathFeedrate dataItemId=""operation_time"" timestamp=""{timestamp}"">{EscapeXml(operationTime)}</PathFeedrate>
          <PathFeedrate dataItemId=""power_on_hours"" timestamp=""{timestamp}"">{EscapeXml(powerOnHours)}</PathFeedrate>
          <PartCount dataItemId=""counter_1_target"" timestamp=""{timestamp}"">{EscapeXml(counter1Target)}</PartCount>
          <PartCount dataItemId=""counter_2_target"" timestamp=""{timestamp}"">{EscapeXml(counter2Target)}</PartCount>
          <PartCount dataItemId=""counter_3_target"" timestamp=""{timestamp}"">{EscapeXml(counter3Target)}</PartCount>
          <PartCount dataItemId=""counter_4_target"" timestamp=""{timestamp}"">{EscapeXml(counter4Target)}</PartCount>
          <PartCount dataItemId=""counter_1_end_signal"" timestamp=""{timestamp}"">{EscapeXml(counter1EndSignal)}</PartCount>
          <PartCount dataItemId=""counter_2_end_signal"" timestamp=""{timestamp}"">{EscapeXml(counter2EndSignal)}</PartCount>
          <PartCount dataItemId=""counter_3_end_signal"" timestamp=""{timestamp}"">{EscapeXml(counter3EndSignal)}</PartCount>
          <PartCount dataItemId=""counter_4_end_signal"" timestamp=""{timestamp}"">{EscapeXml(counter4EndSignal)}</PartCount>
        </Samples>
        <Events>
          <Availability dataItemId=""counter_1_status"" timestamp=""{timestamp}"">{EscapeXml(counter1Status)}</Availability>
          <Availability dataItemId=""counter_2_status"" timestamp=""{timestamp}"">{EscapeXml(counter2Status)}</Availability>
          <Availability dataItemId=""counter_3_status"" timestamp=""{timestamp}"">{EscapeXml(counter3Status)}</Availability>
          <Availability dataItemId=""counter_4_status"" timestamp=""{timestamp}"">{EscapeXml(counter4Status)}</Availability>
          <Alarm dataItemId=""alarm_code"" timestamp=""{timestamp}"">{EscapeXml(alarmCode)}</Alarm>
          <Alarm dataItemId=""alarm_message"" timestamp=""{timestamp}"">{EscapeXml(alarmMessage)}</Alarm>
          <Program dataItemId=""alarm_program"" timestamp=""{timestamp}"">{EscapeXml(alarmProgram)}</Program>
          <Program dataItemId=""alarm_block"" timestamp=""{timestamp}"">{EscapeXml(alarmBlock)}</Program>
          <Alarm dataItemId=""alarm_severity"" timestamp=""{timestamp}"">{EscapeXml(alarmSeverity)}</Alarm>
          <Alarm dataItemId=""alarms_table"" timestamp=""{timestamp}"">{EscapeXml(alarmsTable)}</Alarm>
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
        /// 
        /// NOTE: The PDSP data file does not contain explicit execution state.
        /// Execution state is typically available from the HTTP /running_log endpoint
        /// which provides status like "Running", "Stopped", etc. Since we're only
        /// using the PDSP protocol data, we cannot accurately determine execution state.
        /// </summary>
        private string MapExecution()
        {
            lock (_dataLock)
            {
                // PDSP data does not include execution state
                // We cannot infer this from spindle speed alone (spindle can be on in manual mode, etc.)
                // Return UNAVAILABLE to indicate we don't have this data
                return "UNAVAILABLE";
            }
        }

        /// <summary>
        /// Map Brother alarms to MTConnect alarm format.
        /// 
        /// NOTE: The PDSP data file does not contain alarm information.
        /// Alarms are typically available from the HTTP /alarm_log endpoint.
        /// Door interlock is a safety status, not an alarm condition.
        /// </summary>
        private string MapAlarm()
        {
            lock (_dataLock)
            {
                // PDSP data does not include alarm information
                // Door interlock is a safety status (Enabled/Disabled), not an alarm
                // To get actual alarms, we would need to query the /alarm_log HTTP endpoint
                // For now, return empty (no alarms reported)
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

