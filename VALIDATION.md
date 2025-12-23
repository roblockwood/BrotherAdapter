# MTConnect 2.5 Validation Guide

This document describes how to validate the MTConnect 2.5 XML output from the BrotherAdapter.

## Schema Files

The MTConnect 2.5 XSD schema files are located in the `schemas/` directory:
- `MTConnectDevices_2.5.xsd` - For validating `/probe` endpoint output
- `MTConnectStreams_2.5.xsd` - For validating `/current` and `/sample` endpoint output

## Validation Methods

### Method 1: Using XmlValidator Class

The `XmlValidator` class provides programmatic validation:

```csharp
var validator = new XmlValidator("schemas");
var errors = validator.ValidateDevicesXml(probeXml);
if (errors.Count == 0)
{
    Console.WriteLine("Validation passed!");
}
else
{
    foreach (var error in errors)
    {
        Console.WriteLine($"Error: {error}");
    }
}
```

### Method 2: Online XML Validators

1. Capture XML output from the adapter endpoints:
   ```bash
   curl http://localhost:7878/probe > probe_output.xml
   curl http://localhost:7878/current > current_output.xml
   ```

2. Use an online XML validator (e.g., https://www.xmlvalidation.com/) with the XSD files

### Method 3: Command Line Tools

Using `xmllint` (if available):
```bash
xmllint --noout --schema schemas/MTConnectDevices_2.5.xsd probe_output.xml
xmllint --noout --schema schemas/MTConnectStreams_2.5.xsd current_output.xml
```

## What to Validate

### Probe Endpoint (`/probe`)
- Namespace: `urn:mtconnect.org:MTConnectDevices:2.5`
- Schema: `MTConnectDevices_2.5.xsd`
- Header version: `2.5.0`
- All DataItem definitions
- Component structure

### Current/Sample Endpoints (`/current`, `/sample`)
- Namespace: `urn:mtconnect.org:MTConnectStreams:2.5`
- Schema: `MTConnectStreams_2.5.xsd`
- Header version: `2.5.0`
- ComponentStream structure
- Events and Samples containers
- Data element types

## Known Issues

None currently identified. The XML structure has been reviewed and appears to conform to MTConnect 2.5 specifications.

## Notes

- Full validation should be performed when the adapter is running and connected to a Brother CNC machine
- The validation utility (`XmlValidator.cs`) can be integrated into automated testing
- Schema files are downloaded from https://schemas.mtconnect.org/

