using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;
using System.Net.Sockets;

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

// ============================================================================
// OTLP Exporter Configuration with Error Checking
// ============================================================================

var otlpConfigurationValid = true;
var otlpConfigurationErrors = new List<string>();
Uri? otlpEndpointUri = null;
OtlpExportProtocol exporterProtocol = OtlpExportProtocol.Grpc;

// Validate and parse the OTLP protocol
Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
Console.WriteLine("║           OpenTelemetry OTLP Exporter Configuration              ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
Console.WriteLine();

// Step 1: Validate protocol
Console.WriteLine("[OTLP] Validating protocol configuration...");
switch (otlpProtocol.ToLowerInvariant())
{
    case "grpc":
        exporterProtocol = OtlpExportProtocol.Grpc;
        Console.WriteLine($"[OTLP] ✓ Protocol: gRPC");
        break;
    case "http/protobuf":
        exporterProtocol = OtlpExportProtocol.HttpProtobuf;
        Console.WriteLine($"[OTLP] ✓ Protocol: HTTP/Protobuf");
        break;
    default:
        otlpConfigurationValid = false;
        var protocolError = $"Invalid OTLP protocol '{otlpProtocol}'. Supported values: 'grpc', 'http/protobuf'";
        otlpConfigurationErrors.Add(protocolError);
        Console.WriteLine($"[OTLP] ✗ ERROR: {protocolError}");
        break;
}

// Step 2: Validate endpoint URL format
Console.WriteLine("[OTLP] Validating endpoint configuration...");
if (string.IsNullOrWhiteSpace(otlpEndpoint))
{
    otlpConfigurationValid = false;
    var endpointError = "OTLP endpoint is null or empty";
    otlpConfigurationErrors.Add(endpointError);
    Console.WriteLine($"[OTLP] ✗ ERROR: {endpointError}");
}
else if (!Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out otlpEndpointUri))
{
    otlpConfigurationValid = false;
    var endpointError = $"Invalid OTLP endpoint URL format: '{otlpEndpoint}'";
    otlpConfigurationErrors.Add(endpointError);
    Console.WriteLine($"[OTLP] ✗ ERROR: {endpointError}");
}
else
{
    // Validate URL scheme
    if (otlpEndpointUri.Scheme != "http" && otlpEndpointUri.Scheme != "https")
    {
        otlpConfigurationValid = false;
        var schemeError = $"Invalid URL scheme '{otlpEndpointUri.Scheme}'. Must be 'http' or 'https'";
        otlpConfigurationErrors.Add(schemeError);
        Console.WriteLine($"[OTLP] ✗ ERROR: {schemeError}");
    }
    else
    {
        Console.WriteLine($"[OTLP] ✓ Endpoint URL: {otlpEndpoint}");
        Console.WriteLine($"[OTLP] ✓ Scheme: {otlpEndpointUri.Scheme}");
        Console.WriteLine($"[OTLP] ✓ Host: {otlpEndpointUri.Host}");
        Console.WriteLine($"[OTLP] ✓ Port: {otlpEndpointUri.Port}");
    }

    // Warn about common port misconfigurations
    if (otlpConfigurationValid)
    {
        var expectedPort = exporterProtocol == OtlpExportProtocol.Grpc ? 4317 : 4318;
        if (otlpEndpointUri.Port != expectedPort && otlpEndpointUri.Port != 80 && otlpEndpointUri.Port != 443)
        {
            Console.WriteLine($"[OTLP] ⚠ WARNING: Using port {otlpEndpointUri.Port}. " +
                $"Standard port for {otlpProtocol} is {expectedPort}");
        }

        // For HTTP/Protobuf, check if path includes /v1/traces
        if (exporterProtocol == OtlpExportProtocol.HttpProtobuf)
        {
            if (string.IsNullOrEmpty(otlpEndpointUri.AbsolutePath) || otlpEndpointUri.AbsolutePath == "/")
            {
                Console.WriteLine($"[OTLP] ⚠ WARNING: HTTP/Protobuf endpoint may need path '/v1/traces'. " +
                    $"Current path: '{otlpEndpointUri.AbsolutePath}'");
            }
        }
    }
}

// Step 3: Validate service name
Console.WriteLine("[OTLP] Validating service name...");
if (string.IsNullOrWhiteSpace(serviceName))
{
    otlpConfigurationValid = false;
    var serviceNameError = "Service name is null or empty";
    otlpConfigurationErrors.Add(serviceNameError);
    Console.WriteLine($"[OTLP] ✗ ERROR: {serviceNameError}");
}
else if (serviceName.Length > 256)
{
    otlpConfigurationValid = false;
    var serviceNameError = $"Service name exceeds maximum length of 256 characters (current: {serviceName.Length})";
    otlpConfigurationErrors.Add(serviceNameError);
    Console.WriteLine($"[OTLP] ✗ ERROR: {serviceNameError}");
}
else
{
    Console.WriteLine($"[OTLP] ✓ Service name: {serviceName}");
}

// Step 4: Test endpoint connectivity (non-blocking)
if (otlpConfigurationValid && otlpEndpointUri != null)
{
    Console.WriteLine("[OTLP] Testing endpoint connectivity...");
    try
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var tcpClient = new TcpClient();
        
        await tcpClient.ConnectAsync(otlpEndpointUri.Host, otlpEndpointUri.Port, cts.Token);
        
        if (tcpClient.Connected)
        {
            Console.WriteLine($"[OTLP] ✓ Successfully connected to {otlpEndpointUri.Host}:{otlpEndpointUri.Port}");
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine($"[OTLP] ⚠ WARNING: Connection timeout to {otlpEndpointUri.Host}:{otlpEndpointUri.Port}. " +
            "Endpoint may not be available. Traces will be queued and retried.");
    }
    catch (SocketException ex)
    {
        Console.WriteLine($"[OTLP] ⚠ WARNING: Cannot connect to {otlpEndpointUri.Host}:{otlpEndpointUri.Port}. " +
            $"Socket error: {ex.SocketErrorCode}. Traces will be queued and retried.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[OTLP] ⚠ WARNING: Connectivity test failed: {ex.Message}. " +
            "Traces will be queued and retried.");
    }
}

// Step 5: Print configuration summary
Console.WriteLine();
if (otlpConfigurationValid)
{
    Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║        ✓ OTLP Exporter Configuration Successful                  ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
    Console.WriteLine();
    Console.WriteLine($"  Service Name : {serviceName}");
    Console.WriteLine($"  Endpoint     : {otlpEndpoint}");
    Console.WriteLine($"  Protocol     : {otlpProtocol}");
    Console.WriteLine($"  Exporters    : {tracesExporter}");
    Console.WriteLine();
}
else
{
    Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
    Console.WriteLine("║        ✗ OTLP Exporter Configuration Failed                      ║");
    Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
    Console.WriteLine();
    Console.WriteLine("  The following errors were detected:");
    foreach (var error in otlpConfigurationErrors)
    {
        Console.WriteLine($"    • {error}");
    }
    Console.WriteLine();
    Console.WriteLine("  OTLP export will be disabled. Only console export will be active.");
    Console.WriteLine();
}

// ============================================================================
// Configure OpenTelemetry Services
// ============================================================================

// Create a custom ActivitySource for manual instrumentation
var activitySource = new ActivitySource(serviceName);
builder.Services.AddSingleton(activitySource);

// Add HttpClientFactory for outgoing HTTP instrumentation
builder.Services.AddHttpClient();

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
                options.Filter = httpContext => 
                    !httpContext.Request.Path.StartsWithSegments("/health");
                
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

        // Configure OTLP exporter only if configuration is valid
        if (otlpConfigurationValid && otlpEndpointUri != null)
        {
            tracing.AddOtlpExporter(options =>
            {
                options.Endpoint = otlpEndpointUri;
                options.Protocol = exporterProtocol;
                options.TimeoutMilliseconds = 10000;
                
                // Configure batch export processor for better performance
                options.BatchExportProcessorOptions = new BatchExportProcessorOptions<Activity>
                {
                    MaxQueueSize = 2048,
                    ScheduledDelayMilliseconds = 5000,
                    ExporterTimeoutMilliseconds = 30000,
                    MaxExportBatchSize = 512
                };
            });
            
            Console.WriteLine("[OTLP] ✓ OTLP exporter registered successfully");
        }
        else
        {
            Console.WriteLine("[OTLP] ✗ OTLP exporter not registered due to configuration errors");
        }

        // Add console exporter for debugging or as fallback
        if (tracesExporter.Contains("console", StringComparison.OrdinalIgnoreCase) || !otlpConfigurationValid)
        {
            tracing.AddConsoleExporter();
            Console.WriteLine("[OTLP] ✓ Console exporter registered" + 
                (!otlpConfigurationValid ? " (fallback mode)" : ""));
        }
    });

Console.WriteLine();

var app = builder.Build();

// ============================================================================
// Endpoints
// ============================================================================

app.MapGet("/", (ActivitySource source) =>
{
    using var activity = source.StartActivity("HomePageRequest");
    activity?.SetTag("custom.tag", "homepage");
    activity?.AddEvent(new ActivityEvent("Rendering homepage"));
    
    return Results.Content(
        GetHomePage(otlpEndpoint, otlpProtocol, serviceName, otlpConfigurationValid, otlpConfigurationErrors),
        "text/html");
});

app.MapGet("/api/data", async (ActivitySource source, HttpContext context) =>
{
    using var activity = source.StartActivity("FetchData", ActivityKind.Internal);
    
    activity?.SetTag("data.source", "internal");
    activity?.AddEvent(new ActivityEvent("Starting data fetch"));
    
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
            ServiceName = serviceName,
            ConfigurationValid = otlpConfigurationValid
        }
    };
    
    return Results.Json(data);
});

app.MapGet("/api/external", async (ActivitySource source, IHttpClientFactory httpClientFactory) =>
{
    using var activity = source.StartActivity("ExternalApiCall", ActivityKind.Client);
    
    activity?.SetTag("external.api", "httpbin.org");
    
    try
    {
        using var httpClient = httpClientFactory.CreateClient();
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

app.MapGet("/api/config", () => Results.Ok(new
{
    ServiceName = serviceName,
    OtlpEndpoint = otlpEndpoint,
    OtlpProtocol = otlpProtocol,
    ConfigurationValid = otlpConfigurationValid,
    ConfigurationErrors = otlpConfigurationErrors,
    TracesExporter = tracesExporter
}));

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

app.Run();

// ============================================================================
// Helper Methods
// ============================================================================

static string GetHomePage(string endpoint, string protocol, string serviceName, 
    bool configValid, List<string> errors) => $"""
<!DOCTYPE html>
<html>
<head>
    <title>OpenTelemetry OTLP Export Example</title>
</head>

<body>
    <h1>🔭 OpenTelemetry OTLP Export Example</h1>
    
    <div class="config {(configValid ? "config-valid" : "config-invalid")}">
        <h3>Configuration Status: 
            <span class="status {(configValid ? "status-ok" : "status-error")}">
                {(configValid ? "✓ Valid" : "✗ Invalid")}
            </span>
        </h3>
        <p><strong>Service Name:</strong> <code>{serviceName}</code></p>
        <p><strong>OTLP Endpoint:</strong> <code>{endpoint}</code></p>
        <p><strong>OTLP Protocol:</strong> <code>{protocol}</code></p>
        {(errors.Count > 0 ? $@"
        <h4>Configuration Errors:</h4>
        <ul class=""error-list"">
            {string.Join("\n", errors.Select(e => $"<li>{e}</li>"))}
        </ul>" : "")}
    </div>
    
    <h3>Available Endpoints</h3>
    <ul class="endpoints">
        <li>📊 <a href="/api/data">/api/data</a> - Returns sample data with trace information</li>
        <li>🌐 <a href="/api/external">/api/external</a> - Makes an external HTTP call (demonstrates distributed tracing)</li>
        <li>❌ <a href="/api/error">/api/error</a> - Simulates an error (demonstrates error tracing)</li>
        <li>⚙️ <a href="/api/config">/api/config</a> - Returns current OTLP configuration</li>
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
docker run -d --name jaeger \
  -p 16686:16686 \
  -p 4317:4317 \
  -p 4318:4318 \
  jaegertracing/all-in-one:latest
    </pre>
    <p>Then access Jaeger UI at <a href="http://localhost:16686">http://localhost:16686</a></p>
</body>
</html>
""";