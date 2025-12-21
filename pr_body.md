This PR adds the remaining MTConnect data items to complete coverage of all data parsed from Brother CNC machine files.

## Changes

### New Data Items Added:
- **Rotary Axes (18 items)**: A, B, C axes for G54-G59 work offsets
- **Extended Work Offsets (30 items)**: X01-X10 offsets with X, Y, Z coordinates
- **Counter Details (8 items)**: End signals and status for counters 1-4
- **Alarm Details (3 items)**: Program, block number, and severity fields

### Implementation Details:
- Added `controller_times` component to organize timing and alarm data
- Extended `Systems` component to include all work offsets and rotary axes
- Updated `GenerateCurrentXml()` to populate all new data items
- All data is already parsed by `FileLoader.cs`, now exposed via MTConnect

### Files Changed:
- `BrotherConnection/MTConnectServer.cs` - Added data items and XML generation
- `BrotherConnection/FileLoader.cs` - Already parsing all required data
- `BrotherConnection/Program.cs` - Already loading all required files
- `BrotherConnection/BrotherConnection.csproj` - Added FileLoader reference

## Testing
- All data items are defined in probe XML
- All data items are referenced in appropriate components
- All data items are populated in current XML generation
- No linter errors

This completes the gap analysis from the data audit document.

