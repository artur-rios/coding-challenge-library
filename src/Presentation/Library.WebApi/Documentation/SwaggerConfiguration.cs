using System.Reflection;
// Microsoft.OpenApi v2, which Swashbuckle 10 depends on, moved OpenApiInfo out of the .Models
// namespace and into the root one.
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Library.WebApi;

/// <summary>Describes the API for Swagger UI.</summary>
public static class SwaggerConfiguration
{
    /// <summary>Applies the document metadata and pulls in the controllers' XML comments.</summary>
    public static void Configure(SwaggerGenOptions options)
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "Library API",
            Version = "v1",
            Description =
                "Catalogue and lending for a library, with concurrency-safe availability, " +
                "idempotent loan creation, and a business audit trail."
        });

        // The controllers carry per-endpoint documentation; without this the page shows only method
        // names, and the whole point of documenting the idempotency contract there is that a caller
        // reads it before they write the client.
        var xmlPath = Path.Combine(
            AppContext.BaseDirectory,
            $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");

        if (File.Exists(xmlPath))
        {
            options.IncludeXmlComments(xmlPath);
        }
    }
}
