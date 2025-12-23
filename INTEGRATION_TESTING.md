# MTConnect 2.5 Integration Testing Guide

## Overview

This document outlines the integration testing requirements for verifying MTConnect 2.5 compatibility of the BrotherAdapter.

## Testing Requirements

### 1. MTConnect 2.5 Compatible Clients

Test the adapter with the following types of clients:

#### Recommended Testing Tools:
- **MTConnect Agent** (C++ or other implementations supporting 2.5)
- **MTConnect Browser/Viewer** tools that support 2.5
- **Custom MTConnect clients** built for 2.5

#### Test Scenarios:
1. **Probe Endpoint** (`/probe`)
   - Verify client can parse device information
   - Check that all DataItems are recognized
   - Validate component structure is understood

2. **Current Endpoint** (`/current`)
   - Verify client can parse current data values
   - Check that all data elements are correctly interpreted
   - Validate timestamp formats

3. **Sample Endpoint** (`/sample`)
   - Verify streaming data is correctly parsed
   - Check sequence numbers are handled properly

### 2. Data Validation

Verify that clients correctly interpret:
- Machine positions (X, Y, Z coordinates)
- Work offsets (G54-G59, extended offsets)
- Tool data (tool table, ATC table)
- Counters and status information
- Alarm information
- Spindle speed and feedrate

### 3. Performance Testing

- Verify response times are acceptable
- Check that the adapter handles multiple concurrent requests
- Validate that data updates are reflected in client views

## Testing Checklist

- [ ] Test with at least one MTConnect 2.5 compatible client
- [ ] Verify all three endpoints work correctly
- [ ] Validate data accuracy and completeness
- [ ] Test error handling and edge cases
- [ ] Verify performance under normal load
- [ ] Document any client-specific issues or requirements

## Known Limitations

- Integration testing requires access to a running Brother CNC machine
- Testing should be performed in a controlled environment
- Some MTConnect 2.5 clients may still be in development

## Notes

- The adapter has been updated to MTConnect 2.5 namespaces and schemas
- XML structure has been reviewed and appears compliant
- Full integration testing should be performed when the adapter is deployed

