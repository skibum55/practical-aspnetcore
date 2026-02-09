using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// OpenTelemetry Configuration with OTLP Export
// ============================================================================
// 
// Environment Variables Supported:
// --------------------------------
// OTEL_EXPORTER_OTLP_ENDPOINT  - The OTLP endpoint URL (default: http://localhost:4317)
// OTEL_EXPORTER_OTLP_PROTOCOL  - The protocol to use: "grpc" or "http/protobuf" (default: grpc)
// OTEL_SERVICE_NAME            - The service name for traces (default: open-telemetry-5)
// OTEL_TRACES_EXPORTER         - Set to "console" to also output to console (default: otlp)
//
// Example usage:
//   OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317 dotnet run
//   OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318/v1/traces dotnet run
// ============================================================================

// Read configuration from environment variables with sensible defaults
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] 
    ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") 
    ?? "http://localhost:4317";

var otlpProtocol = builder.Configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] 
    ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL") 
    ?? "grpc";

var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] 
    ?? Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") 
    ?? "open-telemetry-5";

var tracesExporter = builder.Configuration["OTEL_TRACES_EXPORTER"] 
    ?? Environment.GetEnvironmentVariable("OTEL_TRACES_EXPORTER") 
    ?? "otlp";

// Parse the OTLP protocol
var exporterProtocol = otlpProtocol.ToLowerInvariant() switch
{
    "http/protobuf" => OtlpExportProtocol.HttpProtobuf,
    "grpc" => OtlpExportProtocol.Grpc,
    _ => OtlpExportProtocol.Grpc
};

// Create a custom ActivitySource for manual instrumentation
var activitySource = new ActivitySource(serviceName);

builder.Services.AddSingleton(activitySource);

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: serviceName,
            serviceVersion: "1.0.0",
            serviceInstanceId: Environment.MachineName))
    .WithTracing(tracing =>
    {
        tracing
            // Add automatic instrumentation for ASP.NET Core
            .AddAspNetCoreInstrumentation(options =>
            {
                // Optionally filter out health check endpoints, etc.
                options.Filter = httpContext => 
                    !httpContext.Request.Path.StartsWithSegments("/health");
                
                // Enrich spans with additional request information
                options.EnrichWithHttpRequest = (activity, httpRequest) =>
                {
                    activity.SetTag("http.request.header.host", httpRequest.Host.ToString());
                };
                
                options.EnrichWithHttpResponse = (activity, httpResponse) =>
                {
                    activity.SetTag("http.response.content_type", httpResponse.ContentType);
                };
            })
            // Add automatic instrumentation for outgoing HTTP calls
            .AddHttpClientInstrumentation(options =>
            {
                options.RecordException = true;
            })
            // Add our custom ActivitySource
            .AddSource(serviceName);

        // Configure OTLP exporter
        tracing.AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(otlpEndpoint);
            options.Protocol = exporterProtocol;
            
            // Optional: Configure timeout and headers
            options.TimeoutMilliseconds = 10000;
            
            // You can add headers for authentication if needed
            // options.Headers = "api-key=your-api-key";
        });

        // Optionally add console exporter for debugging
        if (tracesExporter.Contains("console", StringComparison.OrdinalIgnoreCase))
        {
            tracing.AddConsoleExporter();
        }
    });

// Add HttpClientFactory for outgoing HTTP instrumentation
builder.Services.AddHttpClient();

var app = builder.Build();

// ============================================================================
// Endpoints
// ============================================================================

app.MapGet("/", (ActivitySource source) =>
{
    // Create a custom span for additional context
    using var activity = source.StartActivity("HomePageRequest");
    activity?.SetTag("custom.tag", "homepage");
    activity?.AddEvent(new ActivityEvent("Rendering homepage"));
    
    return Results.Content(
        GetHomePage(otlpEndpoint, otlpProtocol, serviceName),
        "text/html");
});

app.MapGet("/api/data", async (ActivitySource source, HttpContext context) =>
{
    using var activity = source.StartActivity("FetchData", ActivityKind.Internal);
    
    activity?.SetTag("data.source", "internal");
    activity?.AddEvent(new ActivityEvent("Starting data fetch"));
    
    // Simulate some work
    await Task.Delay(Random.Shared.Next(50, 200));
    
    activity?.AddEvent(new ActivityEvent("Data fetch complete"));
    
    var data = new
    {
        Id = Guid.NewGuid(),
        Timestamp = DateTime.UtcNow,
        TraceId = Activity.Current?.TraceId.ToString(),
        SpanId = Activity.Current?.SpanId.ToString(),
        Message = "Hello from OpenTelemetry!",
        Configuration = new
        {
            OtlpEndpoint = otlpEndpoint,
            OtlpProtocol = otlpProtocol,
            ServiceName = serviceName
        }
    };
    
    return Results.Json(data);
});

app.MapGet("/api/external", async (ActivitySource source, IHttpClientFactory? httpClientFactory) =>
{
    using var activity = source.StartActivity("ExternalApiCall", ActivityKind.Client);
    
    activity?.SetTag("external.api", "httpbin.org");
    
    try
    {
        using var httpClient = httpClientFactory?.CreateClient() ?? new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        
        activity?.AddEvent(new ActivityEvent("Making external request"));
        
        var response = await httpClient.GetStringAsync("https://httpbin.org/json");
        
        activity?.SetTag("external.response.length", response.Length);
        activity?.AddEvent(new ActivityEvent("External request complete"));
        
        return Results.Ok(new { Source = "httpbin.org", DataLength = response.Length });
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        
        return Results.Problem($"Error calling external API: {ex.Message}");
    }
});

app.MapGet("/api/error", (ActivitySource source) =>
{
    using var activity = source.StartActivity("ErrorSimulation");
    
    try
    {
        throw new InvalidOperationException("This is a simulated error for tracing demonstration");
    }
    catch (Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.RecordException(ex);
        
        return Results.Problem(
            detail: ex.Message,
            statusCode: 500,
            title: "Simulated Error");
    }
});

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

app.Run();

// ============================================================================
// Helper Methods
// ============================================================================

static string GetHomePage(string endpoint, string protocol, string serviceName) => $"""
<!DOCTYPE html>
<html>
<head>
    <title>OpenTelemetry OTLP Export Example</title>
</head>
<body>
    <h1>🔭 OpenTelemetry OTLP Export Example</h1>
    
    <div class="config">
        <h3>Current Configuration</h3>
        <p><strong>Service Name:</strong> <code>{serviceName}</code></p>
        <p><strong>OTLP Endpoint:</strong> <code>{endpoint}</code></p>
        <p><strong>OTLP Protocol:</strong> <code>{protocol}</code></p>
    </div>
    
    <h3>Available Endpoints</h3>
    <ul class="endpoints">
        <li>📊 <a href="/api/data">/api/data</a> - Returns sample data with trace information</li>
        <li>🌐 <a href="/api/external">/api/external</a> - Makes an external HTTP call (demonstrates distributed tracing)</li>
        <li>❌ <a href="/api/error">/api/error</a> - Simulates an error (demonstrates error tracing)</li>
        <li>💚 <a href="/health">/health</a> - Health check endpoint (excluded from tracing)</li>
    </ul>
    
    <div class="env-vars">
        <h3>Environment Variables</h3>
        <p>Configure the OTLP exporter using these environment variables:</p>
        <pre>
# OTLP endpoint (default: http://localhost:4317)
export OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317

# Protocol: "grpc" or "http/protobuf" (default: grpc)
export OTEL_EXPORTER_OTLP_PROTOCOL=grpc

# Service name (default: open-telemetry-5)
export OTEL_SERVICE_NAME=my-service

# Add console output for debugging
export OTEL_TRACES_EXPORTER=otlp,console
        </pre>
    </div>
    
    <h3>Running with Jaeger (Example)</h3>
    <p>Start Jaeger with OTLP support:</p>
    <pre style="background: #333; color: #0f0; padding: 10px; border-radius: 4px;">
docker run -d --name otel-lgtm \
	-p 3000:3000 \
	-p 4040:4040 \
	-p 4317:4317 \
	-p 4318:4318 \
	-p 9090:9090 \
  docker.io/grafana/otel-lgtm:latest
    </pre>
    <p>Then access OLTP UI at <a href="http://localhost:3000">http://localhost:3000</a></p>
</body>
</html>
""";