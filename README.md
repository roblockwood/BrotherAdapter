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

- **Tool Table** (`tool_table`): Complete tool definitions library from TOLNI1.NC
  - Format: Pipe-delimited string with tool number, length, diameter, name

- **ATC Tool Table** (`atc_table`): Tools currently loaded in ATC magazine from ATCTL.NC
  - Format: Pipe-delimited string with pot number, tool number, name, length, diameter, group, life, type, color
  - Example: `P1:T1:.250 3FL:LEN=3.4494:DIA=0.25:GRP=1:LIFE=9952:TYPE=1:COL=0|...`

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

## Data Sources

The adapter collects data from Brother CNC machine files via the LOD protocol. All parsers are version-aware (C00/D00) and unit-aware (Metric/Inch), automatically using the appropriate schema based on the detected configuration.

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

- **TOLNI1.NC** - Tool table (every 10 seconds)
  - Complete tool definitions library
  - Version and unit-aware parsing (field positions and digit counts may vary)

- **POSNI1.NC / POSNM1.NC** - Work offsets (every 10 seconds)
  - POSNI1 = Inch unit system, POSNM1 = Metric unit system
  - G54-G59 and X01-X48 work offsets with rotary axes (C00)
  - G054-G059 and X001-X300 work offsets with rotary axes (D00)
  - Version and unit-aware parsing (coordinate values and digit counts may vary)

- **ATCTL.NC** - ATC tool control (every 10 seconds)
  - Tools currently loaded in ATC magazine
  - Version and unit-aware parsing

- **PANEL.NC** - Panel/operator interface state (every 10 seconds)
  - Version and unit-aware parsing

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
