using System.Globalization;
using System.Text.Json;
using A7Foody.Core.Mapping;
using A7Foody.Core.Sales;
using A7Foody.Core.Sync;
using A7Foody.Foody;
using A7Foody.Foody.Json;

namespace A7Foody.Service;

/// <summary>
/// Comandos de linha de comando para instalação e homologação. Nenhum deles cria pedido na Foody nem
/// grava no banco do conector.
/// </summary>
internal static class Diagnostics
{
    private static readonly JsonSerializerOptions Pretty = new(FoodyJson.Options) { WriteIndented = true };

    public static async Task<int> RunAsync(string command, IServiceProvider services, IConfiguration config)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            return command switch
            {
                "testar-foody" => await TestFoodyAsync(services, config, cts.Token).ConfigureAwait(false),
                "testar-a7" => await TestA7Async(services, config, cts.Token).ConfigureAwait(false),
                _ => Help(),
            };
        }
#pragma warning disable CA1031 // ferramenta de diagnóstico: qualquer erro vira mensagem legível
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Console.Error.WriteLine($"ERRO: {ex.Message}");
            return 1;
        }
    }

    private static int Help()
    {
        Console.WriteLine("""
            A7FoodyConnector — comandos de diagnóstico (não enviam pedidos):

              testar-foody --loja <id>            Valida o token da loja na Foody (consulta pedidos das últimas 24 h).
              testar-foody --token <token>        Idem, com um token informado na hora.
              testar-a7 --loja <id> [--desde "AAAA-MM-DD HH:MM"] [--limite 10]
                                                  Lê vendas com entrega no A7 e mostra como iriam para a Foody.

            Sem comando, roda como serviço.
            """);
        return 0;
    }

    private static async Task<int> TestFoodyAsync(IServiceProvider services, IConfiguration config, CancellationToken ct)
    {
        var token = config["token"];
        if (string.IsNullOrWhiteSpace(token))
        {
            var store = FindStore(config);
            token = store.FoodyToken;
            Console.WriteLine($"Loja {store.DisplayName}");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("Token vazio. Informe --token ou configure Stores[].FoodyToken.");
            return 1;
        }

        var client = services.GetRequiredService<IFoodyClientFactory>().ForToken(token);
        var now = DateTimeOffset.Now;
        try
        {
            var orders = await client.ListOrdersAsync(now.AddDays(-1), now, ct).ConfigureAwait(false);
            Console.WriteLine($"OK: token aceito. {orders.Count} pedido(s) nas últimas 24 h.");
            return 0;
        }
        catch (FoodyApiException ex)
        {
            Console.Error.WriteLine(ex.ErrorCode switch
            {
                "E19" => "Token recusado (E19). Confira em Foody → Minha Conta → APIs e Hooks.",
                "E11" => "O plano atual da Foody não permite uso da API (E11). É preciso um plano pago.",
                _ => $"Foody respondeu com erro: {ex.Message}",
            });
            return 2;
        }
    }

    private static async Task<int> TestA7Async(IServiceProvider services, IConfiguration config, CancellationToken ct)
    {
        var store = FindStore(config);
        var source = services.GetRequiredService<ISalesSource>();
        if (source is NotConfiguredSalesSource)
        {
            Console.Error.WriteLine("A7:ConnectionStrings não configurado.");
            return 1;
        }

        var limit = int.TryParse(config["limite"], CultureInfo.InvariantCulture, out var l) ? l : 10;
        var since = DateTime.TryParse(config["desde"], CultureInfo.GetCultureInfo("pt-BR"), DateTimeStyles.None, out var d)
            ? d
            : DateTime.Now.AddHours(-2);
        services.GetRequiredService<A7.A7SourceOptions>().BatchSize = limit;

        // Cursor montado na hora: não usa nem altera o cursor gravado do serviço.
        var cursor = string.Create(CultureInfo.InvariantCulture, $"v1|{since.Ticks}|");
        var batch = await source.GetChangesAsync(store.StoreId, cursor, ct).ConfigureAwait(false);
        var mapper = services.GetRequiredService<FoodyOrderMapper>();

        Console.WriteLine($"Loja {store.DisplayName}: {batch.Sales.Count} venda(s) com entrega desde {since:g}\n");
        foreach (var sale in batch.Sales)
        {
            Console.WriteLine($"── Venda {sale.SaleId} ({sale.CreatedAt:g})");
            if (sale.State == SaleState.Cancelled)
            {
                Console.WriteLine("CANCELADA: se já tiver sido enviada, o pedido será cancelado na Foody.\n");
                continue;
            }

            var result = mapper.Map(sale);
            Console.WriteLine(result.IsValid
                ? JsonSerializer.Serialize(result.Order, Pretty)
                : "NÃO SERIA ENVIADA: " + string.Join("; ", result.Errors));
            Console.WriteLine();
        }

        return 0;
    }

    private static StoreOptions FindStore(IConfiguration config)
    {
        var id = config["loja"];
        // Lido direto da configuração: os testes funcionam mesmo antes de todas as lojas terem token.
        var stores = config.GetSection("Stores").Get<List<StoreOptions>>() ?? [];
        return stores.FirstOrDefault(s => string.Equals(s.StoreId, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Loja \"{id}\" não está em Stores. Use --loja <StoreId>.");
    }
}
