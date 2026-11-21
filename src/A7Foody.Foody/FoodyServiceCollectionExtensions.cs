using A7Foody.Foody;
using Microsoft.Extensions.Http.Resilience;

#pragma warning disable IDE0130 // convenção: extensões de DI no namespace do container
namespace Microsoft.Extensions.DependencyInjection;
#pragma warning restore IDE0130

/// <summary>Configurações da API Foody.</summary>
public sealed class FoodyOptions
{
    /// <summary>Seção do appsettings.</summary>
    public const string Section = "Foody";

    /// <summary>URL base da API v1.2 (termina com barra).</summary>
    public Uri BaseUrl { get; set; } = new("https://app.foodydelivery.com/rest/1.2/");

    /// <summary>Timeout de cada tentativa.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(20);
}

/// <summary>Registro do cliente Foody no container de DI.</summary>
public static class FoodyServiceCollectionExtensions
{
    /// <summary>Registra <see cref="IFoodyClientFactory"/> com HttpClient resiliente.</summary>
    public static IServiceCollection AddFoodyClient(this IServiceCollection services, Action<FoodyOptions>? configure = null)
    {
        var options = new FoodyOptions();
        configure?.Invoke(options);

        services.AddHttpClient(FoodyClientFactory.HttpClientName, c =>
            {
                c.BaseAddress = options.BaseUrl;
                c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .AddStandardResilienceHandler(r =>
            {
                r.AttemptTimeout.Timeout = options.AttemptTimeout;
                r.TotalRequestTimeout.Timeout = options.AttemptTimeout * 4;
                r.CircuitBreaker.SamplingDuration = options.AttemptTimeout * 2;

                // POST/PUT NÃO são repetidos aqui: um POST /orders que deu timeout pode ter criado o
                // pedido, e repetir duplicaria a entrega. Quem decide repetir é o motor de sincronização,
                // que antes confere na Foody se o pedido já existe. Retentativas automáticas só para GET.
                r.Retry.DisableForUnsafeHttpMethods();
            });

        services.AddSingleton<IFoodyClientFactory, FoodyClientFactory>();
        return services;
    }
}
