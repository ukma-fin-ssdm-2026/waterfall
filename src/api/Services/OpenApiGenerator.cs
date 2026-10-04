// Код згенеровано за допомогою ШІ-асистента, перевірено та відредаговано вручну (включаючи коментарі).

using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.Swagger;

namespace Waterfall.Api.Services;

public static class OpenApiGenerator
{
    public static void ExportOpenApiYaml(IServiceProvider services, IWebHostEnvironment environment, ILogger logger)
    {
        try
        {
            ISwaggerProvider swaggerProvider = services.GetRequiredService<ISwaggerProvider>();
            OpenApiDocument doc = swaggerProvider.GetSwagger("v1");

            string solutionOrDocsDir = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "docs", "api"));
            Directory.CreateDirectory(solutionOrDocsDir);
            string yamlPath = Path.Combine(solutionOrDocsDir, "openapi-generated.yaml");

            using (StringWriter textWriter = new StringWriter())
            {
                OpenApiYamlWriter yamlWriter = new OpenApiYamlWriter(textWriter);
                doc.SerializeAsV3(yamlWriter);
                File.WriteAllText(yamlPath, textWriter.ToString());
            }

            logger.LogInformation("OpenAPI YAML специфікацію успішно збережено у: {Path}", yamlPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Помилка під час автоматичної генерації OpenAPI YAML специфікації");
        }
    }
}
