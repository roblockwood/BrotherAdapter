using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Schema;

namespace BrotherConnection
{
    /// <summary>
    /// Utility class for validating MTConnect XML output against XSD schemas.
    /// Enhanced with detailed error reporting and schema dependency resolution.
    /// </summary>
    public class XmlValidator
    {
        private readonly string _schemasPath;

        public XmlValidator(string schemasPath = "schemas")
        {
            _schemasPath = schemasPath;
        }

        /// <summary>
        /// Validates XML string against MTConnect Devices 2.5 schema.
        /// </summary>
        /// <param name="xmlContent">The XML content to validate</param>
        /// <returns>List of validation errors, empty if valid</returns>
        public List<string> ValidateDevicesXml(string xmlContent)
        {
            return ValidateXml(xmlContent, Path.Combine(_schemasPath, "MTConnectDevices_2.5.xsd"), "Devices");
        }

        /// <summary>
        /// Validates XML string against MTConnect Streams 2.5 schema.
        /// </summary>
        /// <param name="xmlContent">The XML content to validate</param>
        /// <returns>List of validation errors, empty if valid</returns>
        public List<string> ValidateStreamsXml(string xmlContent)
        {
            return ValidateXml(xmlContent, Path.Combine(_schemasPath, "MTConnectStreams_2.5.xsd"), "Streams");
        }

        /// <summary>
        /// Validates XML against a schema with enhanced error reporting.
        /// </summary>
        private List<string> ValidateXml(string xmlContent, string schemaPath, string schemaType)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(xmlContent))
            {
                errors.Add($"ERROR: XML content is empty or null");
                return errors;
            }

            if (!File.Exists(schemaPath))
            {
                errors.Add($"ERROR: Schema file not found: {schemaPath}");
                errors.Add($"       Expected path: {Path.GetFullPath(schemaPath)}");
                return errors;
            }

            try
            {
                // First, validate that XML is well-formed
                try
                {
                    var xmlDoc = new XmlDocument();
                    xmlDoc.LoadXml(xmlContent);
                }
                catch (XmlException xmlEx)
                {
                    errors.Add($"ERROR: XML is not well-formed: {xmlEx.Message}");
                    errors.Add($"       Line {xmlEx.LineNumber}, Position {xmlEx.LinePosition}");
                    return errors;
                }

                // Set up schema validation
                var schemaSet = new XmlSchemaSet();
                
                // Enable schema compilation to catch schema errors early
                schemaSet.CompilationSettings.EnableUpaCheck = true;
                
                // Resolve schema imports (like xlink.xsd)
                schemaSet.XmlResolver = new XmlUrlResolver();

                // Add the main schema
                try
                {
                    schemaSet.Add(null, schemaPath);
                }
                catch (XmlSchemaException schemaEx)
                {
                    errors.Add($"ERROR: Schema compilation failed: {schemaEx.Message}");
                    errors.Add($"       Schema: {schemaPath}");
                    errors.Add($"       Line {schemaEx.LineNumber}, Position {schemaEx.LinePosition}");
                    if (schemaEx.InnerException != null)
                    {
                        errors.Add($"       Inner exception: {schemaEx.InnerException.Message}");
                    }
                    return errors;
                }

                // Compile schemas to catch errors early
                try
                {
                    schemaSet.Compile();
                }
                catch (XmlSchemaException compileEx)
                {
                    errors.Add($"ERROR: Schema compilation failed: {compileEx.Message}");
                    errors.Add($"       Schema: {schemaPath}");
                    errors.Add($"       Line {compileEx.LineNumber}, Position {compileEx.LinePosition}");
                    return errors;
                }

                var settings = new XmlReaderSettings
                {
                    ValidationType = ValidationType.Schema,
                    Schemas = schemaSet,
                    ValidationFlags = XmlSchemaValidationFlags.ReportValidationWarnings |
                                      XmlSchemaValidationFlags.ProcessIdentityConstraints |
                                      XmlSchemaValidationFlags.ProcessSchemaLocation
                };

                var errorDetails = new List<ValidationErrorDetail>();

                settings.ValidationEventHandler += (sender, e) =>
                {
                    var detail = new ValidationErrorDetail
                    {
                        Severity = e.Severity,
                        Message = e.Message,
                        LineNumber = e.Exception?.LineNumber ?? 0,
                        LinePosition = e.Exception?.LinePosition ?? 0,
                        SourceUri = e.Exception?.SourceUri,
                        SchemaObject = null  // Not available in .NET Framework 4.6.1
                    };
                    errorDetails.Add(detail);
                };

                // Perform validation
                using (var reader = XmlReader.Create(new StringReader(xmlContent), settings))
                {
                    try
                    {
                        while (reader.Read()) { }
                    }
                    catch (XmlException xmlEx)
                    {
                        errors.Add($"ERROR: XML parsing error during validation: {xmlEx.Message}");
                        errors.Add($"       Line {xmlEx.LineNumber}, Position {xmlEx.LinePosition}");
                    }
                }

                // Format errors with detailed information
                foreach (var detail in errorDetails)
                {
                    var severity = detail.Severity == XmlSeverityType.Error ? "ERROR" : "WARNING";
                    var sb = new StringBuilder();
                    sb.Append($"{severity}: {detail.Message}");
                    
                    if (detail.LineNumber > 0)
                    {
                        sb.Append($" (Line {detail.LineNumber}");
                        if (detail.LinePosition > 0)
                        {
                            sb.Append($", Position {detail.LinePosition}");
                        }
                        sb.Append(")");
                    }
                    
                    if (!string.IsNullOrEmpty(detail.SourceUri))
                    {
                        sb.Append($" [Source: {detail.SourceUri}]");
                    }
                    
                    errors.Add(sb.ToString());
                }

                // Add context information for errors
                if (errors.Count > 0)
                {
                    errors.Insert(0, $"Schema Type: {schemaType}");
                    errors.Insert(1, $"Schema File: {Path.GetFileName(schemaPath)}");
                    errors.Insert(2, $"---");
                }
            }
            catch (XmlSchemaException schemaEx)
            {
                errors.Add($"ERROR: Schema exception: {schemaEx.Message}");
                errors.Add($"       Schema: {schemaPath}");
                if (schemaEx.LineNumber > 0)
                {
                    errors.Add($"       Line {schemaEx.LineNumber}, Position {schemaEx.LinePosition}");
                }
                if (schemaEx.InnerException != null)
                {
                    errors.Add($"       Inner exception: {schemaEx.InnerException.Message}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"ERROR: Unexpected validation exception: {ex.Message}");
                errors.Add($"       Type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    errors.Add($"       Inner exception: {ex.InnerException.Message}");
                }
            }

            return errors;
        }

        /// <summary>
        /// Validates XML and prints results to console with enhanced formatting.
        /// </summary>
        public bool ValidateAndPrint(string xmlContent, string schemaType)
        {
            List<string> errors;
            if (schemaType == "Devices")
            {
                errors = ValidateDevicesXml(xmlContent);
            }
            else if (schemaType == "Streams")
            {
                errors = ValidateStreamsXml(xmlContent);
            }
            else
            {
                Console.WriteLine($"[ERROR] Unknown schema type: {schemaType}");
                return false;
            }

            if (errors.Count == 0)
            {
                Console.WriteLine($"[PASS] {schemaType} XML validation passed");
                return true;
            }
            else
            {
                Console.WriteLine($"[FAIL] {schemaType} XML validation failed:");
                Console.WriteLine();
                foreach (var error in errors)
                {
                    if (error == "---")
                    {
                        Console.WriteLine(error);
                    }
                    else if (error.StartsWith("Schema Type:") || error.StartsWith("Schema File:"))
                    {
                        Console.WriteLine($"[INFO] {error}");
                    }
                    else
                    {
                        Console.WriteLine($"  {error}");
                    }
                }
                Console.WriteLine();
                return false;
            }
        }

        /// <summary>
        /// Internal class to hold detailed validation error information.
        /// </summary>
        private class ValidationErrorDetail
        {
            public XmlSeverityType Severity { get; set; }
            public string Message { get; set; }
            public int LineNumber { get; set; }
            public int LinePosition { get; set; }
            public string SourceUri { get; set; }
            public XmlSchemaObject SchemaObject { get; set; }
        }
    }
}
