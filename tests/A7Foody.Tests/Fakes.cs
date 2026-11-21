using System.Net;
using A7Foody.Core.Sales;
using A7Foody.Foody;
using A7Foody.Foody.Models;

namespace A7Foody.Tests;

/// <summary>Relógio controlado pelo teste.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;

    public void Advance(TimeSpan by) => Now += by;
}

/// <summary>HttpMessageHandler que responde com uma função e guarda as requisições.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string? Body)> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Calls.Add((request, body));
        return respond(request, body);
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };
}

/// <summary>Fonte de vendas em memória: devolve tudo que foi colocado desde o último cursor.</summary>
internal sealed class FakeSalesSource : ISalesSource
{
    private readonly List<DeliverySale> _sales = [];

    public void Push(DeliverySale sale) => _sales.Add(sale);

    public Task<SalesBatch> GetChangesAsync(string storeId, string? cursor, CancellationToken ct)
    {
        var from = cursor is null ? 0 : int.Parse(cursor, System.Globalization.CultureInfo.InvariantCulture);
        var batch = _sales.Skip(from).Where(s => s.StoreId == storeId).ToList();
        return Task.FromResult(new SalesBatch(batch, _sales.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}

internal sealed class RecordingSink : IDeliveryStatusSink
{
    public List<DeliveryStatusChange> Changes { get; } = [];

    public Task OnStatusChangedAsync(DeliveryStatusChange change, CancellationToken ct)
    {
        Changes.Add(change);
        return Task.CompletedTask;
    }
}

/// <summary>Foody em memória, com pontos para injetar falhas.</summary>
internal sealed class FakeFoody : IFoodyClient, IFoodyClientFactory
{
    public Dictionary<string, FoodyOrder> Orders { get; } = [];

    public List<FoodyOrderRequest> CreateCalls { get; } = [];

    public List<(string Uid, FoodyOrderStatus Status)> StatusCalls { get; } = [];

    /// <summary>Se definido, a próxima criação executa isto (pode lançar ou criar e lançar).</summary>
    public Func<FoodyOrderRequest, FoodyOrder?>? OnCreate { get; set; }

    public Func<string, FoodyOrderStatus, Exception?>? OnUpdateStatus { get; set; }

    public IFoodyClient ForToken(string token) => this;

    public Task<FoodyOrder> CreateOrderAsync(FoodyOrderRequest order, CancellationToken ct = default)
    {
        CreateCalls.Add(order);
        if (OnCreate is { } hook)
        {
            OnCreate = null;
            var r = hook(order);
            return Task.FromResult(r!);
        }

        return Task.FromResult(Add(order));
    }

    public FoodyOrder Add(FoodyOrderRequest order)
    {
        var created = new FoodyOrder { Uid = Guid.NewGuid().ToString(), Id = order.Id, Status = order.Status, Date = order.Date };
        Orders[created.Uid] = created;
        return created;
    }

    public void SetStatus(string uid, FoodyOrderStatus status) => Orders[uid] = Orders[uid] with { Status = status };

    public Task<FoodyOrder> GetOrderAsync(string uid, CancellationToken ct = default) => Task.FromResult(Orders[uid]);

    public Task<FoodyOrder> UpdateOrderAsync(string uid, FoodyOrderRequest order, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task UpdateStatusAsync(string uid, FoodyOrderStatus status, CancellationToken ct = default)
    {
        StatusCalls.Add((uid, status));
        if (OnUpdateStatus?.Invoke(uid, status) is { } ex)
        {
            throw ex;
        }

        SetStatus(uid, status);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<FoodyOrderUpdate>> CheckUpdatesAsync(IEnumerable<string> uids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FoodyOrderUpdate>>(
            [.. uids.Where(Orders.ContainsKey).Select(u => new FoodyOrderUpdate { Uid = u, Status = Orders[u].Status })]);

    public Task<IReadOnlyList<FoodyOrder>> ListOrdersAsync(DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FoodyOrder>>([.. Orders.Values]);

    public static FoodyApiException Error(HttpStatusCode status, string code) => new(status, code, $"{{\"code\":\"{code}\"}}");
}

internal static class Samples
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 21, 10, 0, 0, TimeSpan.FromHours(-3));

    public static DeliverySale Sale(string id = "12345", string store = "1") => new()
    {
        StoreId = store,
        SaleId = id,
        CreatedAt = T0,
        Total = 57.80m,
        DeliveryFee = 5m,
        PaymentCode = "PIX",
        Items = [new SaleItem("Dipirona 500mg 10cp", 2, 12.90m), new SaleItem("Protetor solar FPS50", 1, 27m)],
        Customer = new SaleCustomer("Maria Souza", "(19) 99876-5432", "maria@exemplo.com"),
        Address = new SaleAddress
        {
            Street = "Rua Barão de Campinas",
            Number = "120",
            District = "Centro",
            City = "Limeira",
            State = "SP",
            PostalCode = "13480-000",
            Complement = "Apto 12",
            Reference = "Ao lado da padaria",
        },
    };
}
