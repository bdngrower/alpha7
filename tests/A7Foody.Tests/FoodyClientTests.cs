using System.Net;
using System.Text.Json;
using A7Foody.Foody;
using A7Foody.Foody.Models;

namespace A7Foody.Tests;

public class FoodyClientTests
{
    private static (FoodyClient Client, StubHandler Handler) Create(Func<HttpRequestMessage, string?, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://app.foodydelivery.com/rest/1.2/") };
        return (new FoodyClient(http, "tok-123"), handler);
    }

    private static FoodyOrderRequest Request() => new()
    {
        Id = "301",
        Status = FoodyOrderStatus.Ready,
        Date = new DateTimeOffset(2020, 6, 18, 14, 13, 27, 500, TimeSpan.FromHours(-3)),
        PaymentMethod = FoodyPaymentMethod.OnCredit,
        OrderTotal = 84.5m,
        DeliveryPoint = new FoodyDeliveryPoint { Address = "Rua José Neves, 590" },
    };

    [Fact]
    public async Task Create_sends_raw_token_and_foody_formats()
    {
        var (client, handler) = Create((_, _) => StubHandler.Json(HttpStatusCode.OK,
            """{"uid":"0afa5b6c","id":"301","status":"ready","date":"2020-06-18T14:13:27-03:00"}"""));

        var created = await client.CreateOrderAsync(Request());

        Assert.Equal("0afa5b6c", created.Uid);
        Assert.Equal(FoodyOrderStatus.Ready, created.Status);

        var (req, body) = handler.Calls.Single();
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://app.foodydelivery.com/rest/1.2/orders", req.RequestUri!.ToString());
        Assert.Equal("tok-123", req.Headers.GetValues("Authorization").Single()); // sem "Bearer"
        Assert.NotNull(req.Content!.Headers.ContentLength); // sem chunked

        using var json = JsonDocument.Parse(body!);
        var root = json.RootElement;
        Assert.Equal("2020-06-18T14:13:27-03:00", root.GetProperty("date").GetString()); // sem milissegundos
        Assert.Equal("on_credit", root.GetProperty("paymentMethod").GetString());
        Assert.Equal("ready", root.GetProperty("status").GetString());
        Assert.False(root.TryGetProperty("notes", out _)); // nulos não são enviados
        Assert.Equal("Rua José Neves, 590", root.GetProperty("deliveryPoint").GetProperty("address").GetString());
    }

    [Fact]
    public async Task Create_rejects_status_other_than_open_or_ready()
    {
        var (client, _) = Create((_, _) => throw new InvalidOperationException("não deveria chamar"));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateOrderAsync(Request() with { Status = FoodyOrderStatus.Closed }));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"code":"E17","message":"Propriedade obrigatória ausente"}""", "E17", true)]
    [InlineData(HttpStatusCode.Forbidden, """{"errorCode":"E11"}""", "E11", false)]
    [InlineData(HttpStatusCode.TooManyRequests, "EC_B010 - Too many requests", "EC_B010", false)]
    [InlineData(HttpStatusCode.InternalServerError, "", null, false)]
    public async Task Errors_are_parsed(HttpStatusCode status, string body, string? code, bool isDataError)
    {
        var (client, _) = Create((_, _) => StubHandler.Json(status, body));

        var ex = await Assert.ThrowsAsync<FoodyApiException>(() => client.GetOrderAsync("x"));

        Assert.Equal(status, ex.StatusCode);
        Assert.Equal(code, ex.ErrorCode);
        Assert.Equal(isDataError, ex.IsDataError);
    }

    [Fact]
    public async Task Unknown_status_does_not_break_deserialization()
    {
        var (client, _) = Create((_, _) => StubHandler.Json(HttpStatusCode.OK, """{"uid":"u","id":"1","status":"novoStatusFoody"}"""));
        var order = await client.GetOrderAsync("u");
        Assert.Equal(FoodyOrderStatus.Unknown, order.Status);
    }

    [Fact]
    public async Task UpdateStatus_only_allows_ready_closed_cancelled()
    {
        var (client, handler) = Create((_, _) => StubHandler.Json(HttpStatusCode.OK, "{}"));

        await client.UpdateStatusAsync("abc", FoodyOrderStatus.Cancelled);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.UpdateStatusAsync("abc", FoodyOrderStatus.Delivered));

        var (req, body) = handler.Calls.Single();
        Assert.Equal(HttpMethod.Put, req.Method);
        Assert.EndsWith("/orders/updatestatus/abc", req.RequestUri!.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("""{"status":"cancelled"}""", body);
    }

    [Fact]
    public async Task CheckUpdates_posts_uid_list()
    {
        var (client, handler) = Create((_, _) => StubHandler.Json(HttpStatusCode.OK,
            """[{"uid":"a","updateDate":"2020-06-19T17:42:12Z","status":"onGoing"}]"""));

        var updates = await client.CheckUpdatesAsync(["a", "b"]);

        Assert.Equal(FoodyOrderStatus.OnGoing, updates.Single().Status);
        Assert.Equal(TimeSpan.Zero, updates.Single().UpdateDate!.Value.Offset);
        Assert.Equal("""["a","b"]""", handler.Calls.Single().Body);
    }

    [Fact]
    public async Task CheckUpdates_with_no_uids_makes_no_request()
    {
        var (client, handler) = Create((_, _) => throw new InvalidOperationException());
        Assert.Empty(await client.CheckUpdatesAsync([]));
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task ListOrders_follows_pages_of_500()
    {
        var start = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-3));
        var call = 0;
        var (client, handler) = Create((_, _) =>
        {
            call++;
            var count = call == 1 ? FoodyClient.PageSize : 3;
            var offset = call == 1 ? 0 : FoodyClient.PageSize - 1; // repete o último da página anterior
            var items = Enumerable.Range(offset, count).Select(i =>
                $$"""{"uid":"u{{i}}","id":"{{i}}","status":"closed","date":"{{start.AddMinutes(i):yyyy-MM-dd'T'HH:mm:sszzz}}"}""");
            return StubHandler.Json(HttpStatusCode.OK, "[" + string.Join(',', items) + "]");
        });

        var orders = await client.ListOrdersAsync(start, start.AddDays(10));

        Assert.Equal(FoodyClient.PageSize + 2, orders.Count); // sem duplicado
        Assert.Equal(2, handler.Calls.Count);
        var secondQuery = Uri.UnescapeDataString(handler.Calls[1].Request.RequestUri!.Query);
        Assert.Contains($"startDate={start.AddMinutes(FoodyClient.PageSize - 1):yyyy-MM-dd'T'HH:mm:sszzz}", secondQuery, StringComparison.Ordinal);
    }
}
