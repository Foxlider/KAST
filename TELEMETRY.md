# KAST OpenTelemetry Instrumentation

This document describes the OpenTelemetry instrumentation implemented in KAST for enhanced observability and debugging.

## Overview

KAST now includes comprehensive OpenTelemetry instrumentation providing:
- **Distributed tracing** with detailed spans for all major operations
- **Structured logging** correlated with trace context
- **Rich telemetry data** with operation context and performance metrics
- **Error tracking** with proper span status and exception details

## Instrumented Services

### Core Services
- **ConfigService**: Configuration loading, updating, and environment variable override tracking
- **InstanceManagerService**: Server instance management, loading, and bulk operations
- **ThemeService**: Theme mode changes, system preference detection, and UI updates
- **ServerInfoService**: System metrics collection (CPU, memory usage)
- **ConfigFileService**: File operations, watching, loading, and saving configurations

## Key Features

### 1. TracedServiceBase
All services inherit from `TracedServiceBase` which provides:
- Automatic span creation with consistent naming
- Error handling and status tracking
- Structured logging with operation context
- Rich tagging with operation metadata

### 2. Operation Naming Convention
Spans follow the pattern: `{ServiceName}.{OperationName}`
- `ConfigService.GetConfig`
- `ThemeService.SetThemeMode`
- `InstanceManagerService.LoadServers`
- `ServerInfoService.GetCpuUsage`
- `ConfigFileService.LoadFile`

### 3. Rich Telemetry Tags
Operations include contextual tags:
- `config.isNew`: Whether configuration is newly created
- `config.envOverrideCount`: Number of environment variable overrides applied
- `server.count`: Number of servers processed
- `theme.mode`: Current theme mode
- `file.path`: File operations path
- `system.platform`: Operating system platform

### 4. Structured Logging
All operations include correlated structured logs:
```
info: KAST.Core.Services.ThemeService[0]
      Theme mode changed to: dark, resulting in dark mode: True
```

## Configuration

### ServiceDefaults Setup
OpenTelemetry is configured in `KAST.ServiceDefaults/Extensions.cs`:
- ASP.NET Core instrumentation enabled
- HTTP client instrumentation enabled
- OTLP exporter configured
- Activity sources registered for all KAST services

### Activity Sources
Each service type gets its own activity source:
- Main application: `builder.Environment.ApplicationName`
- KAST services: `KAST`
- File operations: `KAST.Core.ConfigFileService`

## Usage Examples

### Viewing Telemetry in Development
1. Run the application with Aspire dashboard: `dotnet run --project KAST.AppHost`
2. Navigate to the dashboard to view traces and logs
3. Look for spans with names like `ThemeService.SetThemeMode`

### Key Operations to Monitor
- **Application Startup**: Server loading and theme initialization
- **Configuration Changes**: Settings updates and theme changes
- **System Monitoring**: CPU and memory usage collection
- **File Operations**: Configuration file loading and saving

## Benefits for Debugging

### 1. Job Tracing
Every major operation creates a span, making it easy to trace job execution:
- Configuration loading and environment variable processing
- Server instance management operations
- Theme system operations
- File system operations with detailed metadata

### 2. Performance Monitoring
Spans include timing information for:
- Database operations
- File I/O operations
- System metrics collection
- Configuration processing

### 3. Error Correlation
Failed operations include:
- Exception details in span tags
- Proper span status (Error/Ok)
- Structured error logs correlated to traces
- Operation context for debugging

### 4. System Observability
Comprehensive visibility into:
- Service interactions and dependencies
- Operation patterns and frequency
- System resource usage patterns
- Configuration change history

## Integration with Aspire

KAST's telemetry integrates seamlessly with .NET Aspire:
- Dashboard provides rich visualization of traces
- Distributed tracing across service boundaries
- Log correlation with trace context
- Metric collection and visualization

This instrumentation provides the "lots of Spans to know what job is being made" and "logging with events and data related" as requested in the original issue.