using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using A7Foody.Foody.Json;
using A7Foody.Foody.Models;

namespace A7Foody.Foody;

/// <summary>Operações da API Foody Delivery v1.2 para UMA conta (um token).</summary>
public interface IFoodyClient
{
    /// <summary>POST /orders. Atenção: não é idempotente — ver <c>OrderSyncEngine</c> para o tratamento de duplicidade.</summary>
    Task<FoodyOrder> CreateOrderAsync(FoodyOrderRequest order, CancellationToken ct = default);

    /// <summary>GET /orders/{uid}.</summary>
    Task<FoodyOrder> GetOrderAsync(string uid, CancellationToken ct = default);

    /// <summary>PUT /orders/{uid}. Só funciona com o pedido em open/ready (E22).</summary>
    Task<FoodyOrder> UpdateOrderAsync(string uid, FoodyOrderRequest order, CancellationToken ct = default);

    /// <summary>PUT /orders/updatestatus/{uid}. Aceita apenas ready, closed e cancelled.</summary>
    Task UpdateStatusAsync(string uid, FoodyOrderStatus status, CancellationToken ct = default);

    /// <summary>POST /orders/checkupdates. Polling: intervalo mínimo de 30 s recomendado pela Foody.</summary>
    Task<IReadOnlyList<FoodyOrderUpdate>> CheckUpdatesAsync(IEnumerable<string> uids, CancellationToken ct = default);

    /// <summary>GET /orders/?startDate&amp;endDate, percorrendo todas as páginas de 500.</summary>
    Task<IReadOnlyList<FoodyOrder>> ListOrdersAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default);
}

/// <summary>Cria clientes por token (cada loja pode ter a sua conta Foody).</summary>
public interface IFoodyClientFactory
{
    /// <summary>Cliente autenticado com o token informado.</summary>
    IFoodyClient ForToken(string token);
}

internal sealed class FoodyClientFactory(IHttpClientFactory httpClientFactory) : IFoodyClientFactory
{
    internal const string HttpClientName = "Foody";

    public IFoodyClient ForToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new FoodyClient(httpClientFactory.CreateClient(HttpClientName), token);
    }
}

internal sealed class FoodyClient(HttpClient http, string token) : IFoodyClient
{
    /// <summary>Limite de pedidos por página em GET /orders.</summary>
    internal const int PageSize = 500;

    private static readonly HashSet<FoodyOrderStatus> AllowedStatusUpdates =
        [FoodyOrderStatus.Ready, FoodyOrderStatus.Closed, FoodyOrderStatus.Cancelled];

    public async Task<FoodyOrder> CreateOrderAsync(FoodyOrderRequest order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.Status is not (FoodyOrderStatus.Open or FoodyOrderStatus.Ready))
        {
            throw new ArgumentException("Na criação o status deve ser open ou ready.", nameof(order));
        }

        return await SendAsync<FoodyOrder>(HttpMethod.Post, "orders", order, ct).ConfigureAwait(false);
    }

    public Task<FoodyOrder> GetOrderAsync(string uid, CancellationToken ct = default) =>
        SendAsync<FoodyOrder>(HttpMethod.Get, $"orders/{Uri.EscapeDataString(uid)}", null, ct);

    public Task<FoodyOrder> UpdateOrderAsync(string uid, FoodyOrderRequest order, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(order);

        // O PUT não aceita "date" (nem uid).
        return SendAsync<FoodyOrder>(HttpMethod.Put, $"orders/{Uri.EscapeDataString(uid)}", order with { Date = null }, ct);
    }

    public async Task UpdateStatusAsync(string uid, FoodyOrderStatus status, CancellationToken ct = default)
    {
        if (!AllowedStatusUpdates.Contains(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "A Foody só aceita ready, closed ou cancelled.");
        }

        using var _ = await SendRawAsync(HttpMethod.Put, $"orders/updatestatus/{Uri.EscapeDataString(uid)}",
            new { status }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FoodyOrderUpdate>> CheckUpdatesAsync(IEnumerable<string> uids, CancellationToken ct = default)
    {
        var list = uids.ToList();
        if (list.Count == 0)
        {
            return [];
        }

        return await SendAsync<List<FoodyOrderUpdate>>(HttpMethod.Post, "orders/checkupdates", list, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FoodyOrder>> ListOrdersAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default)
    {
        var result = new List<FoodyOrder>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cursor = startDate;

        while (true)
        {
            var path = string.Create(CultureInfo.InvariantCulture,
                $"orders/?startDate={Uri.EscapeDataString(FormatDate(cursor))}&endDate={Uri.EscapeDataString(FormatDate(endDate))}");
            var page = await SendAsync<List<FoodyOrder>>(HttpMethod.Get, path, null, ct).ConfigureAwait(false);

            var added = 0;
            foreach (var o in page)
            {
                if (seen.Add(o.Uid))
                {
                    result.Add(o);
                    added++;
                }
            }

            if (page.Count < PageSize)
            {
                break;
            }

            // A doc manda repetir com startDate > date do último pedido. As datas têm precisão de
            // segundos, então reconsultamos a partir do mesmo segundo e removemos repetidos; se a página
            // não trouxer nada novo (500+ pedidos no mesmo segundo), avançamos 1 s para não entrar em loop.
            var last = page[^1].Date ?? cursor;
            cursor = added == 0 || last <= cursor ? last.AddSeconds(1) : last;
            if (cursor > endDate)
            {
                break;
            }
        }

        return result;
    }

    private static string FormatDate(DateTimeOffset d) =>
        d.ToString(FoodyDateTimeOffsetConverter.Format, CultureInfo.InvariantCulture);

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await SendRawAsync(method, path, body, ct).ConfigureAwait(false);
        var value = await response.Content.ReadFromJsonAsync<T>(FoodyJson.Options, ct).ConfigureAwait(false);
        return value ?? throw new FoodyApiException(response.StatusCode, null, "Resposta vazia da Foody.");
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);

        // A Foody usa o token puro no header Authorization (sem "Bearer").
        request.Headers.TryAddWithoutValidation("Authorization", token);
        if (body is not null)
        {
            // Corpo com Content-Length (StringContent) em vez de JsonContent, que envia em "chunked":
            // servidores mais antigos não leem corpo sem tamanho declarado.
            var json = JsonSerializer.Serialize(body, body.GetType(), FoodyJson.Options);
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        }

        var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw new FoodyApiException(response.StatusCode, FoodyApiException.TryExtractErrorCode(text), text);
        }
    }
}
