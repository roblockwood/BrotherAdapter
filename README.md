# BrotherAdapter

MTConnect Adapter for Brother CNC Machines

This adapter translates data from Brother CNC machines into the MTConnect standard format, providing real-time machine data via REST API endpoints. It communicates with Brother CNC machines using the LOD (Load) protocol to access system files and production data.

## Features

- **Real-time Data Collection**: Polls CNC machine every 2 seconds for production data
- **MTConnect 2.5 Compliant**: Standard MTConnect REST API endpoints
- **Comprehensive Data Coverage**: Supports machine positions, tool data, work offsets, alarms, counters, and more
- **Automatic Configuration Detection**: Automatically detects control version and unit system before parsing
- **Version-Aware Parsing**: Supports C00 and D00 control versions with schema-specific parsing
- **Unit System Support**: Handles both Metric and Inch unit systems with appropriate schema handling
- **Docker Ready**: Pre-built Docker images available on GitHub Container Registry
- **Linux/Mono Compatible**: Runs on Linux using Mono runtime (.NET Framework 4.6.1)

## Configuration Detection

The adapter automatically detects the machine configuration before any data parsing occurs:

### Control Version Detection

The adapter detects the Brother CNC control version by checking for PRD files:
- **C00**: Detected when `PRDC2.nc` is present
- **D00**: Detected when `PRDD2.nc` is present (preferred if both exist)

Detection happens at startup and is logged to the console. The detected version determines which file schemas and parsing logic to use, as different control versions may have different field positions, digit counts, or extended schemas.

### Unit System Detection

After control version detection, the adapter determines the unit system (Metric/Inch) by parsing the MSRRS file:
- **C00**: Reads `MSRRSC.nc`
- **D00**: Reads `MSRRSD.nc`

The unit system value is read from the first line (C01) of the MSRRS file:
- `0` = Metric (millimeters)
- `1` = Inch

The detected unit system affects parsing, as field positions, digit counts, and coordinate formats may differ between Metric and Inch configurations.

### Detection Logging

Both detection processes log their results to the console (visible in Docker logs):
- `[INFO] Control version detected: D00 (PRDD2.nc found)`
- `[INFO] Unit system detected: Metric (from MSRRSC, value: 0)`

If detection fails, the adapter defaults to C00 control version and Metric unit system with warnings logged.

## Quick Start

### Docker (Recommended)

```bash
docker run -d \
  --name brother-mtconnect \
  -p 7878:7878 \
  -e CNC_IP_ADDRESS=192.168.1.100 \
  -e CNC_PORT=10000 \
  -e AGENT_PORT=7878 \
  ghcr.io/roblockwood/brotheradapter:latest
```

### Environment Variables

- `CNC_IP_ADDRESS` - IP address of the Brother CNC machine (default: `10.0.0.25`)
- `CNC_PORT` - Port for LOD protocol communication (default: `10000`)
- `AGENT_PORT` - Port for MTConnect HTTP server (default: `7878`)

## MTConnect Endpoints

The adapter exposes standard MTConnect REST API endpoints:

- **`GET /probe`** - Device capabilities and available data items
- **`GET /current`** - Current values of all data items
- **`GET /sample`** - Streaming sample data (currently returns current data)

### Example Usage

```bash
# Get device capabilities
curl http://localhost:7878/probe

# Get current data
curl http://localhost:7878/current
```

## Supported Data Items

### Machine Position & Motion

- **Machine Coordinates**: `Xact`, `Yact`, `Zact` (POSITION, MACHINE coordinate system)
- **Spindle Speed**: `spindle_speed` (ROTARY_VELOCITY, RPM)
- **Feedrate**: `path_feedrate` (PATH_FEEDRATE, mm/s)

### Work Offsets

- **G54-G59 Work Offsets**: X, Y, Z coordinates for each offset
  - `work_offset_g54_x`, `work_offset_g54_y`, `work_offset_g54_z`
  - `work_offset_g55_x`, `work_offset_g55_y`, `work_offset_g55_z`
  - `work_offset_g56_x`, `work_offset_g56_y`, `work_offset_g56_z`
  - `work_offset_g57_x`, `work_offset_g57_y`, `work_offset_g57_z`
  - `work_offset_g58_x`, `work_offset_g58_y`, `work_offset_g58_z`
  - `work_offset_g59_x`, `work_offset_g59_y`, `work_offset_g59_z`

- **Rotary Axes (A, B, C)**: For G54-G59 work offsets
  - `work_offset_g54_a`, `work_offset_g54_b`, `work_offset_g54_c`
  - (and similarly for G55-G59)

- **Extended Work Offsets**: X01-X10 offsets
  - `extended_offset_x01_x`, `extended_offset_x01_y`, `extended_offset_x01_z`
  - (and similarly for X02-X10)

### Tool Data

- **Tool Table** (`tool_table`): Complete tool definitions library from TOLNI/TOLNM files
  - **File Selection**: Automatically selects TOLNI1 (Inch) or TOLNM1 (Metric) based on detected unit system
  - **Version Support**: 
    - C00: T01-T99 (2-digit tool numbers)
    - D00: T001-T300 (3-digit tool numbers, normalized to T1-T300 in output)
  - **Format**: Pipe-delimited string with tool number, length, diameter, name
  - **Unit Conversion**: All dimension values (length, diameter, position offsets) are converted to millimeters for MTConnect output, regardless of source file unit system
  - **Example**: `T1,LEN=127.0,DIA=12.7,NAME=End Mill|T2,LEN=50.8,DIA=6.35,NAME=Drill`
  - Tool numbers are normalized (leading zeros removed) for consistent output

- **ATC Tool Table** (`atc_table`): Tools currently loaded in ATC magazine
  - **File Selection**: Automatically selects ATCTL (C00) or ATCTLD (D00) based on detected control version
  - **Version Support**:
    - C00: ATCTL file, M01-M51 entries (Spindle + Pots 1-50), Tool No. 0/1-99/255
    - D00: ATCTLD file, M01-M51 entries (Spindle + Pots 1-50), Tool No. 0/1-99/201-299/999, additional stocker entries (R01-R51, L01-L51)
  - **Format**: Pipe-delimited string with schema fields only: pot/spindle identifier, tool number, name (cross-referenced), conversation/NC, group/main tool, type, color, store tool stocker (D00 only)
  - **Fields from ATCTL schema**: Tool Number, Conversation/NC (0=Conversation, 1=NC), Group/Main Tool, Type (1=Standard, 2=Large diameter, 3=Medium diameter for C00), Color (0-7), Store Tool Stocker (D00 only: 0=Possible, 1=Not possible)
  - **Note**: Tool name is cross-referenced from tool table for convenience. Length, diameter, and life are NOT in ATCTL schema - see tool table (`tool_table`) for those values.
  - **Example**: `SPINDLE:T24:End Mill:CONVNC=1:GRP=1:TYPE=1:COL=0|P1:T1:.250 3FL:CONVNC=1:GRP=1:TYPE=1:COL=0:STORE=0|...`
  - All fields are unitless (no conversion needed)

- **ATC Stockers** (`atc_stockers`): Tool stockers (D00 only)
  - Right stockers (R01-R51) and Left stockers (L01-L51)
  - Format: Pipe-delimited string with stocker identifier, tool number, name, conversation/NC, group, type, color, store tool stocker
  - Example: `R1:T5:Drill:CONVNC=1:GRP=2:TYPE=3:COL=1:STORE=0|L1:T10:End Mill:CONVNC=1:GRP=1:TYPE=1:COL=0:STORE=0|...`

### Workpiece Counters

- **Counter Values**: `work_counter_1`, `work_counter_2`, `work_counter_3`, `work_counter_4` (PART_COUNT)
- **Counter Targets**: `counter_1_target`, `counter_2_target`, `counter_3_target`, `counter_4_target` (PART_COUNT)
- **Counter End Signals**: `counter_1_end_signal`, `counter_2_end_signal`, `counter_3_end_signal`, `counter_4_end_signal` (PART_COUNT)
- **Counter Status**: `counter_1_status`, `counter_2_status`, `counter_3_status`, `counter_4_status` (AVAILABILITY)

### Alarms

- **Alarm Code**: `alarm_code` (ALARM, EVENT)
- **Alarm Message**: `alarm_message` (ALARM, EVENT)
- **Alarm Program**: `alarm_program` (PROGRAM, EVENT)
- **Alarm Block**: `alarm_block` (PROGRAM, EVENT)
- **Alarm Severity**: `alarm_severity` (ALARM, EVENT)

### Timing & Operation Data

- **Cycle Time**: `cycle_time` (PROCESS_TIMER, CYCLE, seconds)
- **Cutting Time**: `cutting_time` (PROCESS_TIMER, CUTTING, seconds)
- **Operation Time**: `operation_time` (PROCESS_TIMER, OPERATION, seconds)
- **Power On Hours**: `power_on_hours` (POWER_ON_TIME, hours)

### Controller Status

- **Availability**: `avail` (AVAILABILITY, EVENT)
- **Execution State**: `execution` (EXECUTION, EVENT)
- **Controller Mode**: `mode` (CONTROLLER_MODE, EVENT)
- **Program Name**: `program` (PROGRAM, EVENT)

### Macro Variables

- **Macro Variables** (`macro_variables`): User-defined macro variables (DATA_SET, EVENT)
  - **File Selection**: Automatically selects MCRNI1 (Inch) or MCRNM1 (Metric) based on detected unit system
  - **Version Support**:
    - C00: Comma-delimited format, C500-C999 variables
    - D00: CR+LF (line break) delimited format, C500-C999 variables
  - **Format**: Pipe-delimited string with variable name and value: `C500=value|C501=value|...`
  - **Data Length**: 11 characters per variable value
  - **Range**: -999999.999~999999.999 (Metric) or -99999.9999~99999.9999 (Inch)
  - **Example**: `C500=123.456|C501=-45.678|C502=0.000|...`
  - Values are unit-aware (Metric/Inch) but typically represent unitless numeric values used in programs
  - **Note**: C00 Type 1 (MCRNun) - last digit is blank space when unit is micron. D00 Type 2 (MCRSun) - one more decimal digit when smallest unit system option purchased.

## Data Sources

The adapter collects data from Brother CNC machine files via the LOD protocol. All parsers are version-aware (C00/D00) and unit-aware (Metric/Inch), automatically using the appropriate schema based on the detected configuration. Position and dimension values are automatically converted to millimeters for MTConnect output, regardless of the source file unit system.

### Detection Files (Loaded Once at Startup)

- **PRDC2.nc / PRDD2.nc** - Control version detection
  - Determines which control version (C00/D00) is running

- **MSRRSC.nc / MSRRSD.nc** - Unit system detection
  - Determines unit system (Metric/Inch) from first line (C01)

### Production Data Files

- **PDSP** - Production data (real-time, every 2 seconds)
  - Machine positions, spindle speed, feedrate, status
  - Uses version-specific mapping file (`ProductionData3.json` or `ProductionData3_C00.json` / `ProductionData3_D00.json`)

- **MEM.NC** - Program name (every 10 seconds)
  - Version and unit-aware parsing

- **MONTR.NC** - Monitor data (every 10 seconds)
  - Cycle time, cutting time, operation time, power on hours
  - Version and unit-aware parsing

- **ALARM.NC** - Alarm/error status (every 10 seconds)
  - Alarm codes, messages, program, block, severity
  - Version and unit-aware parsing

- **WKCNTR.NC** - Workpiece counters (every 10 seconds)
  - Counter values, targets, end signals, status
  - Version and unit-aware parsing

- **TOLNI1.NC / TOLNM1.NC** - Tool table (every 10 seconds)
  - **File Selection**: TOLNI1 = Inch unit system, TOLNM1 = Metric unit system (automatically selected)
  - Complete tool definitions library with length, diameter, name, life, type, position offsets
  - **Version Differences**:
    - C00: T01-T99 format, tool life fields 6 chars, rotation feed at field 10
    - D00: T001-T300 format, tool life fields 7 chars, peripheral speed field added, rotation feed at field 11, F command at field 13
  - **Unit Conversion**: All position/dimension values automatically converted to millimeters for MTConnect output
  - Version and unit-aware parsing with schema-specific field positions and digit counts

- **POSNI1.NC / POSNM1.NC** - Work offsets (every 10 seconds)
  - POSNI1 = Inch unit system, POSNM1 = Metric unit system
  - G54-G59 and X01-X48 work offsets with rotary axes (C00)
  - G054-G059 and X001-X300 work offsets with rotary axes (D00)
  - Version and unit-aware parsing (coordinate values and digit counts may vary)

- **ATCTL.NC / ATCTLD.NC** - ATC tool control (every 10 seconds)
  - **File Selection**: Automatically selects ATCTL (C00) or ATCTLD (D00) based on detected control version
  - Tools currently loaded in ATC magazine (Spindle M01 + Pots M02-M51)
  - **C00 Format**: ATCTL file with M01-M51 entries, 5 fields per entry (Tool No., Conversation/NC, Group/Main Tool, Type, Color)
  - **D00 Format**: ATCTLD file with M01-M51 entries (6 fields: adds Store Tool Stocker), plus R01-R51 (right stockers), L01-L51 (left stockers), W01/E01 (stocker attributes)
  - Version-aware parsing (tool number ranges, type values, additional fields differ by version)
  - All fields are unitless (no unit conversion needed)

- **PANEL.NC** - Panel/operator interface state (every 10 seconds)
  - Version and unit-aware parsing

- **MCRNI1.NC / MCRNM1.NC** - Macro variables (Type 1, every 10 seconds)
  - **File Selection**: MCRNI1 = Inch unit system, MCRNM1 = Metric unit system (automatically selected)
  - User-defined macro variables C500-C999
  - **Version Differences**:
    - C00: Comma-delimited format (`,`) - variables separated by commas on one or few lines
    - D00: CR+LF (line break) delimited format - one variable per line
  - **Data Format**: Each variable has 11-character value
  - **Range**: -999999.999~999999.999 (Metric) or -99999.9999~99999.9999 (Inch)
  - **Special Notes**:
    - C00: Last digit is blank space when unit is micron
    - D00 Type 2 (MCRSun): One more decimal digit when smallest unit system option purchased
  - Values are unit-aware but typically represent unitless numeric values used in CNC programs
  - Version and unit-aware parsing with schema-specific delimiter handling

## Docker Deployment

### Using Docker Compose

```yaml
services:
  mtconnect-agent:
    image: ghcr.io/roblockwood/brotheradapter:latest
    ports:
      - "7878:7878"
    environment:
      - CNC_IP_ADDRESS=192.168.1.100
      - CNC_PORT=10000
      - AGENT_PORT=7878
    restart: unless-stopped
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:7878/probe"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 40s
```

### Health Check

The container includes a health check that verifies the MTConnect server is responding:

```bash
docker inspect --format='{{.State.Health.Status}}' <container-name>
```

## Development

### Building from Source

```bash
# Clone the repository
git clone https://github.com/roblockwood/BrotherAdapter.git
cd BrotherAdapter

# Build Docker image
docker build -t brotheradapter:local .

# Run locally
docker run -p 7878:7878 \
  -e CNC_IP_ADDRESS=192.168.1.100 \
  -e CNC_PORT=10000 \
  -e AGENT_PORT=7878 \
  brotheradapter:local
```

### Project Structure

- `BrotherConnection/` - Main application code
  - `Program.cs` - Main entry point, data collection loop, version/unit detection
  - `MTConnectServer.cs` - HTTP server for MTConnect endpoints
  - `FileLoader.cs` - File loading and parsing via LOD protocol (version and unit-aware)
  - `Request.cs` - LOD protocol communication
  - `ControlVersion.cs` - Control version enum (C00, D00, Unknown)
  - `ControlVersionDetector.cs` - Detects control version from PRD files
  - `UnitSystem.cs` - Unit system enum (Metric, Inch, Unknown)
  - `UnitSystemDetector.cs` - Detects unit system from MSRRS files
  - `Schema/` - Schema configuration classes for version-specific file parsing
    - `FileSchemaConfig.cs` - Base interface for schema configurations
    - `PosnSchemaConfig.cs` - Schema config for POSN files (work offsets)
    - `TolniSchemaConfig.cs` - Schema config for TOLN files (tool table)
  - `Mapping/` - Data mapping definitions

### Requirements

- .NET Framework 4.6.1
- Mono runtime (for Linux)
- Access to Brother CNC machine on network

## Troubleshooting

### Container Won't Start

- Check that the CNC machine IP address is correct and reachable
- Verify the CNC_PORT (default 10000) is correct
- Check container logs: `docker logs <container-name>`

### No Data in MTConnect Endpoints

- Verify the agent can connect to the CNC machine
- Check container logs for connection errors
- Ensure the CNC machine is powered on and network-accessible

### Connection Errors

The adapter logs detailed error messages for connection failures:
- `ConnectionRefused` - CNC machine not accepting connections
- `TimedOut` - Network timeout, check firewall/routing
- `HostUnreachable` - Cannot reach CNC machine IP

### Detection Issues

If control version or unit system detection fails:
- Check Docker logs for detection messages: `docker logs <container-name>`
- Verify the CNC machine is accessible and responding to LOD commands
- Ensure PRDC2/PRDD2 files are accessible for version detection
- Ensure MSRRSC/MSRRSD files are accessible for unit system detection
- The adapter will default to C00 and Metric with warnings if detection fails
- Incorrect defaults may cause parsing errors - check logs for schema mismatch warnings

## MTConnect 2.5 Migration

This adapter has been migrated from MTConnect 1.7 to 2.5. Key changes:

- **Namespaces**: Updated to MTConnect 2.5 namespaces
- **Schemas**: Validated against MTConnect 2.5 XSD schemas
- **Version**: Header version updated to 2.5.0

### Validation

XML output can be validated using the provided `XmlValidator` class or external tools. See [VALIDATION.md](VALIDATION.md) for details.

### Backward Compatibility

**Breaking Change**: The adapter now uses MTConnect 2.5 namespaces and schemas. Clients using MTConnect 1.7 may need to be updated to support 2.5. See [BACKWARD_COMPATIBILITY.md](BACKWARD_COMPATIBILITY.md) for details.

### Testing

- Integration testing guide: [INTEGRATION_TESTING.md](INTEGRATION_TESTING.md)
- Schema validation: [VALIDATION.md](VALIDATION.md)
- Backward compatibility: [BACKWARD_COMPATIBILITY.md](BACKWARD_COMPATIBILITY.md)

## License

This project is licensed under the terms of the MIT license.

## Acknowledgments

Based on the original [BrotherAdapter](https://github.com/Lathejockey81/BrotherAdapter) by Lathejockey81.
