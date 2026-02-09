using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Read OpenTelemetry configuration from environment variables with fallback to configuration
var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") 
    ?? builder.Configuration["OpenTelemetry:OtlpEndpoint"] 
    ?? "http://localhost:4317";

var otlpProtocol = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_PROTOCOL") 
    ?? builder.Configuration["OpenTelemetry:OtlpProtocol"] 
    ?? "grpc";

var serviceName = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME") 
    ?? builder.Configuration["OpenTelemetry:ServiceName"] 
    ?? "opentelemetry-5-demo";

Console.WriteLine($"OpenTelemetry Configuration:");
Console.WriteLine($"  Service Name: {serviceName}");
Console.WriteLine($"  OTLP Endpoint: {otlpEndpoint}");
Console.WriteLine($"  OTLP Protocol: {otlpProtocol}");

// Configure OpenTelemetry
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(serviceName: serviceName, serviceVersion: "1.0.0"))
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            .AddAspNetCoreInstrumentation(options =>
            {
                // Enrich spans with additional information
                options.RecordException = true;
                options.EnrichWithHttpRequest = (activity, httpRequest) =>
                {
                    activity.SetTag("http.request.path", httpRequest.Path);
                };
                options.EnrichWithHttpResponse = (activity, httpResponse) =>
                {
                    activity.SetTag("http.response.status_code", httpResponse.StatusCode);
                };
            })
            .AddHttpClientInstrumentation()
            .AddSource(serviceName)
            .AddConsoleExporter() // Also export to console for debugging
            .AddOtlpExporter(otlpOptions =>
            {
                otlpOptions.Endpoint = new Uri(otlpEndpoint);
                
                // Set the protocol based on environment variable
                otlpOptions.Protocol = otlpProtocol.ToLower() switch
                {
                    "http/protobuf" => OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf,
                    "grpc" => OpenTelemetry.Exporter.OtlpExportProtocol.Grpc,
                    _ => OpenTelemetry.Exporter.OtlpExportProtocol.Grpc
                };
            });
    });

// Create a custom activity source for manual instrumentation
var activitySource = new ActivitySource(serviceName);

// Add HttpClient for the external endpoint
builder.Services.AddHttpClient();

var app = builder.Build();

// Simple GET endpoint
app.MapGet("/", () =>
{
    using var activity = activitySource.StartActivity("HomeEndpoint");
    activity?.SetTag("endpoint", "/");
    activity?.SetTag("custom.message", "Welcome to OpenTelemetry OTLP Example");
    
    return Results.Ok(new 
    { 
        message = "OpenTelemetry OTLP Example - Exporting traces to " + otlpEndpoint,
        serviceName,
        traceId = Activity.Current?.TraceId.ToString(),
        spanId = Activity.Current?.SpanId.ToString()
    });
});

// Endpoint that demonstrates nested spans
app.MapGet("/nested", async () =>
{
    using var parentActivity = activitySource.StartActivity("NestedEndpoint");
    parentActivity?.SetTag("operation", "parent");
    
    // Simulate some work
    await Task.Delay(100);
    
    // Create a child span
    using (var childActivity = activitySource.StartActivity("ChildOperation"))
    {
        childActivity?.SetTag("operation", "child");
        childActivity?.SetTag("step", "processing");
        await Task.Delay(50);
    }
    
    return Results.Ok(new 
    { 
        message = "Nested operation completed",
        traceId = Activity.Current?.TraceId.ToString()
    });
});

// Endpoint that makes an HTTP request (demonstrates distributed tracing)
app.MapGet("/external", async (IHttpClientFactory httpClientFactory) =>
{
    using var activity = activitySource.StartActivity("ExternalApiCall");
    activity?.SetTag("operation", "external-request");
    
    try
    {
        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync("https://api.github.com/");
        
        activity?.SetTag("external.status_code", (int)response.StatusCode);
        
        return Results.Ok(new 
        { 
            message = "External API call completed",
            statusCode = (int)response.StatusCode,
            traceId = Activity.Current?.TraceId.ToString()
        });
    }
    catch (Exception ex)
    {
        activity?.SetTag("error", true);
        activity?.SetTag("error.message", ex.Message);
        activity?.RecordException(ex);
        throw;
    }
});

// Endpoint that demonstrates error handling
app.MapGet("/error", () =>
{
    using var activity = activitySource.StartActivity("ErrorEndpoint");
    activity?.SetTag("will.fail", true);
    
    try
    {
        throw new InvalidOperationException("This is a test exception for OpenTelemetry");
    }
    catch (Exception ex)
    {
        activity?.SetTag("error", true);
        activity?.RecordException(ex);
        throw;
    }
});

Console.WriteLine("Application started. Try these endpoints:");
Console.WriteLine("  GET /          - Simple traced endpoint");
Console.WriteLine("  GET /nested    - Demonstrates nested spans");
Console.WriteLine("  GET /external  - Demonstrates distributed tracing");
Console.WriteLine("  GET /error     - Demonstrates error tracing");
Console.WriteLine();
Console.WriteLine("To configure OTLP endpoint, set environment variables:");
Console.WriteLine("  OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317");
Console.WriteLine("  OTEL_EXPORTER_OTLP_PROTOCOL=grpc (or http/protobuf)");
Console.WriteLine("  OTEL_SERVICE_NAME=my-service-name");

app.Run();