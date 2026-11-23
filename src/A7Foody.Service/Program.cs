using A7Foody.A7;
using A7Foody.Core.Mapping;
using A7Foody.Core.Sales;
using A7Foody.Core.Sync;
using A7Foody.Service;
using A7Foody.Storage;
using Microsoft.Extensions.Options;
using Serilog;

// Comandos de diagnóstico (rodam e saem): A7FoodyConnector.exe testar-foody | testar-a7 | ajuda
var command = args.Length > 0 && !args[0].StartsWith('-') ? args[0].ToLowerInvariant() : null;
var hostArgs = command is null ? args : args[1..];

var builder = Host.CreateApplicationBuilder(hostArgs);

// Configuração extra fora da pasta do executável (onde ficam os tokens em produção), além de
// appsettings.json e variáveis de ambiente.
var dataDir = builder.Configuration["DataDirectory"]
    ?? (OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "A7FoodyConnector")
        : "/var/lib/a7foody-connector");
builder.Configuration.AddJsonFile(Path.Combine(dataDir, "appsettings.local.json"), optional: true, reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables("A7FOODY_");
builder.Configuration.AddCommandLine(hostArgs);

// Serviço Windows ou systemd (Linux): cada um só tem efeito quando o processo foi iniciado por ele.
builder.Services.AddWindowsService(o => o.ServiceName = "A7FoodyConnector");
builder.Services.AddSystemd();
builder.Services.AddSerilog((_, lc) =>
{
    // Assemblies explícitos: no publish "single file" o Serilog não consegue descobri-los sozinho.
    lc.ReadFrom.Configuration(builder.Configuration, new Serilog.Settings.Configuration.ConfigurationReaderOptions(
        typeof(ConsoleLoggerConfigurationExtensions).Assembly,
        typeof(FileLoggerConfigurationExtensions).Assembly));
    if (command is null)
    {
        lc.WriteTo.File(Path.Combine(dataDir, "logs", "connector-.log"),
            rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30, formatProvider: null);
    }
});

builder.Services.AddOptions<SyncOptions>().Bind(builder.Configuration.GetSection(SyncOptions.Section));
builder.Services.AddOptions<MappingOptions>().Bind(builder.Configuration.GetSection(MappingOptions.Section));
builder.Services.AddOptions<A7SourceOptions>().Bind(builder.Configuration.GetSection(A7SourceOptions.Section));
builder.Services.AddOptions<List<StoreOptions>>()
    .Bind(builder.Configuration.GetSection("Stores"))
    .Validate(s => s.Count > 0, "Configure ao menos uma loja em Stores.")
    .Validate(s => s.Where(x => x.Enabled).All(x => !string.IsNullOrWhiteSpace(x.StoreId) && !string.IsNullOrWhiteSpace(x.FoodyToken)),
        "Toda loja ativa precisa de StoreId e FoodyToken.")
    .Validate(s => s.Select(x => x.StoreId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == s.Count,
        "StoreId repetido em Stores.")
    .ValidateOnStart();

builder.Services.AddFoodyClient(o => builder.Configuration.GetSection(FoodyOptions.Section).Bind(o));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IOrderStore>(_ =>
    new SqliteOrderStore(builder.Configuration["DatabasePath"] ?? Path.Combine(dataDir, "connector.db")));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<SyncOptions>>().Value);
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<A7SourceOptions>>().Value);
builder.Services.AddSingleton(sp => new FoodyOrderMapper(sp.GetRequiredService<IOptions<MappingOptions>>().Value));
builder.Services.AddSingleton<IA7ConnectionFactory, NpgsqlA7ConnectionFactory>();
builder.Services.AddSingleton<A7SqlSalesSource>();
builder.Services.AddSingleton<NotConfiguredSalesSource>();
builder.Services.AddSingleton<ISalesSource>(sp =>
    sp.GetRequiredService<A7SourceOptions>().ConnectionStrings.Count > 0
        ? sp.GetRequiredService<A7SqlSalesSource>()
        : sp.GetRequiredService<NotConfiguredSalesSource>());
builder.Services.AddSingleton<IDeliveryStatusSink, NullDeliveryStatusSink>();
builder.Services.AddSingleton<OrderSyncEngine>();

if (command is null)
{
    builder.Services.AddHostedService<SyncWorker>();
    await builder.Build().RunAsync().ConfigureAwait(false);
    return 0;
}

using var host = builder.Build();
return await Diagnostics.RunAsync(command, host.Services, builder.Configuration).ConfigureAwait(false);
