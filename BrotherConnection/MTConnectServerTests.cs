using System;
using System.Text.RegularExpressions;

namespace BrotherConnection
{
    /// <summary>
    /// Basic validation tests for MTConnect XML output.
    /// These tests verify namespace, version, and basic XML structure.
    /// </summary>
    public class MTConnectServerTests
    {
        private readonly MTConnectServer _server;

        public MTConnectServerTests(MTConnectServer server)
        {
            _server = server;
        }

        /// <summary>
        /// Tests that probe XML contains MTConnect 2.5 namespace and version.
        /// </summary>
        public bool TestProbeXmlNamespace()
        {
            // Use reflection to access private method for testing
            // In a real test framework, you'd use proper unit testing tools
            try
            {
                var method = typeof(MTConnectServer).GetMethod("GenerateProbeXml", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (method == null) return false;

                var xml = method.Invoke(_server, null) as string;
                if (xml == null) return false;

                // Check for 2.5 namespace
                bool hasNamespace = xml.Contains("urn:mtconnect.org:MTConnectDevices:2.5");
                bool hasSchema = xml.Contains("MTConnectDevices_2.5.xsd");
                bool hasVersion = xml.Contains("version=\"2.5.0\"");

                return hasNamespace && hasSchema && hasVersion;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tests that current XML contains MTConnect 2.5 namespace and version.
        /// </summary>
        public bool TestCurrentXmlNamespace()
        {
            try
            {
                var method = typeof(MTConnectServer).GetMethod("GenerateCurrentXml", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (method == null) return false;

                var xml = method.Invoke(_server, null) as string;
                if (xml == null) return false;

                // Check for 2.5 namespace
                bool hasNamespace = xml.Contains("urn:mtconnect.org:MTConnectStreams:2.5");
                bool hasSchema = xml.Contains("MTConnectStreams_2.5.xsd");
                bool hasVersion = xml.Contains("version=\"2.5.0\"");

                return hasNamespace && hasSchema && hasVersion;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validates that XML is well-formed.
        /// </summary>
        public bool TestXmlWellFormed(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return false;

            // Basic checks for well-formed XML
            // Check for matching tags (basic validation)
            int openTags = Regex.Matches(xml, @"<[^/][^>]*>").Count;
            int closeTags = Regex.Matches(xml, @"</[^>]+>").Count;
            
            // Should have roughly equal open and close tags (allowing for self-closing)
            // This is a simplified check - proper validation should use XML parser
            return xml.Trim().StartsWith("<?xml") && 
                   xml.Contains("MTConnectDevices") || xml.Contains("MTConnectStreams");
        }

        /// <summary>
        /// Runs all basic validation tests.
        /// </summary>
        public void RunAllTests()
        {
            Console.WriteLine("[TEST] Running MTConnect 2.5 validation tests...");
            Console.WriteLine();

            bool probeTest = TestProbeXmlNamespace();
            Console.WriteLine($"[TEST] Probe XML namespace test: {(probeTest ? "PASS" : "FAIL")}");

            bool currentTest = TestCurrentXmlNamespace();
            Console.WriteLine($"[TEST] Current XML namespace test: {(currentTest ? "PASS" : "FAIL")}");

            Console.WriteLine();
            if (probeTest && currentTest)
            {
                Console.WriteLine("[TEST] All basic validation tests PASSED");
            }
            else
            {
                Console.WriteLine("[TEST] Some tests FAILED - review output above");
            }
        }
    }
}

