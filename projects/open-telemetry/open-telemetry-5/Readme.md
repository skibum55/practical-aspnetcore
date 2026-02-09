# OpenTelemetry-5 - OTLP Exporter Example

This example demonstrates how to export OpenTelemetry traces to an OTLP endpoint (like Jaeger, Zipkin, or an OpenTelemetry Collector) using environment variables for configuration.

## Features

- Exports traces to OTLP endpoint at `localhost:4317` (configurable)
- Supports both gRPC and HTTP/Protobuf protocols
- Environment variable configuration support
- Console exporter for local debugging
- Demonstrates automatic instrumentation (ASP.NET Core, HttpClient)
- Demonstrates manual instrumentation with custom spans
- Shows nested spans and distributed tracing
- Error and exception tracking

## Running the Example

### Option 1: Using Environment Variables

```bash
# Set environment variables
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
export OTEL_EXPORTER_OTLP_PROTOCOL=grpc
export OTEL_SERVICE_NAME=my-custom-service

# Run the application
dotnet run