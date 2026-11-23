using System.Net;
using A7Foody.Core.Mapping;
using A7Foody.Core.Sales;
using A7Foody.Core.Sync;
using A7Foody.Foody;
using A7Foody.Foody.Models;
using A7Foody.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace A7Foody.Tests;

public sealed class SyncEngineTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"a7foody-{Guid.NewGuid():N}.db");
    private readonly SqliteOrderStore _store;
    private readonly FakeSalesSource _source = new();
    private readonly FakeFoody _foody = new();
    private readonly RecordingSink _sink = new();
    private readonly ManualClock _clock = new(Samples.T0.AddMinutes(1));
    private readonly SyncOptions _options = new();
    private readonly StoreOptions _loja = new() { StoreId = "1", FoodyToken = "tok" };
    private readonly OrderSyncEngine _engine;

    public SyncEngineTests()
    {
        _store = new SqliteOrderStore(_dbPath);
        _store.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        _engine = new OrderSyncEngine(_source, _store, _foody, _sink,
            new FoodyOrderMapper(new MappingOptions { PaymentMethods = new() { ["PIX"] = FoodyPaymentMethod.Pix } }),
            _options, _clock, NullLogger<OrderSyncEngine>.Instance);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            File.Delete(f);
        }
    }

    private Task Cycle() => _engine.RunOnceAsync(_loja, CancellationToken.None);

    private Task<TrackedOrder?> Tracked(string sale = "12345") => _store.GetAsync("1", sale, CancellationToken.None);

    [Fact]
    public async Task New_sale_is_created_on_foody_and_tracked()
    {
        _source.Push(Samples.Sale());

        await Cycle();

        var t = await Tracked();
        Assert.Equal(SyncState.Tracking, t!.State);
        Assert.NotNull(t.FoodyUid);
        Assert.Single(_foody.CreateCalls);
        Assert.Equal("12345", _foody.CreateCalls[0].Id);
    }

    [Fact]
    public async Task Same_sale_read_twice_is_not_sent_twice()
    {
        _source.Push(Samples.Sale());
        await Cycle();
        _source.Push(Samples.Sale()); // A7 devolveu a mesma venda de novo (alterada)
        await Cycle();

        Assert.Single(_foody.CreateCalls);
    }

    [Fact]
    public async Task Timeout_that_created_the_order_does_not_duplicate()
    {
        // A Foody criou o pedido, mas a resposta se perdeu (timeout).
        _foody.OnCreate = req => { _foody.Add(req); throw new TaskCanceledException("timeout"); };
        _source.Push(Samples.Sale());

        await Cycle();
        Assert.Equal(SyncState.CreateUncertain, (await Tracked())!.State);

        _clock.Advance(TimeSpan.FromMinutes(5));
        await Cycle();

        var t = await Tracked();
        Assert.Equal(SyncState.Tracking, t!.State);
        Assert.Equal(_foody.Orders.Keys.Single(), t.FoodyUid);
        Assert.Single(_foody.CreateCalls); // não reenviou
    }

    [Fact]
    public async Task Timeout_that_did_not_create_is_resent()
    {
        _foody.OnCreate = _ => throw new HttpRequestException("conexão caiu");
        _source.Push(Samples.Sale());

        await Cycle();
        _clock.Advance(TimeSpan.FromMinutes(5));
        await Cycle(); // confere na Foody, não acha → volta para PendingCreate
        _clock.Advance(TimeSpan.FromMinutes(1));
        await Cycle();

        Assert.Equal(SyncState.Tracking, (await Tracked())!.State);
        Assert.Equal(2, _foody.CreateCalls.Count);
        Assert.Single(_foody.Orders);
    }

    [Fact]
    public async Task Data_error_waits_for_fix_in_A7_then_sends()
    {
        _foody.OnCreate = _ => throw FakeFoody.Error(HttpStatusCode.BadRequest, "E12");
        _source.Push(Samples.Sale());

        await Cycle();
        var t = await Tracked();
        Assert.Equal(SyncState.NeedsAttention, t!.State);
        Assert.Contains("E12", t.LastError, StringComparison.Ordinal);

        _clock.Advance(TimeSpan.FromHours(1));
        _source.Push(Samples.Sale()); // relida sem mudança
        await Cycle();
        Assert.Single(_foody.CreateCalls); // não fica insistindo com dado ruim

        var fixedSale = Samples.Sale() with { Address = Samples.Sale().Address with { Number = "120A" } };
        _source.Push(fixedSale); // cadastro corrigido no A7 → venda volta como alterada
        await Cycle();
        Assert.Equal(SyncState.Tracking, (await Tracked())!.State);
        Assert.Equal(2, _foody.CreateCalls.Count);
    }

    [Fact]
    public async Task Rereading_a_pending_sale_keeps_the_backoff()
    {
        _foody.OnCreate = _ => throw FakeFoody.Error(HttpStatusCode.TooManyRequests, "EC_B010");
        _source.Push(Samples.Sale());
        await Cycle();
        var before = (await Tracked())!;

        _source.Push(Samples.Sale()); // mesma venda
        _source.Push(Samples.Sale() with { Notes = "Interfone 12" }); // alterada antes do envio
        await Cycle();

        var after = (await Tracked())!;
        Assert.Equal(before.NextAttemptAt, after.NextAttemptAt);
        Assert.Equal(1, after.Attempts);
        Assert.Contains("Interfone 12", after.PayloadJson, StringComparison.Ordinal);
        Assert.Single(_foody.CreateCalls);
    }

    [Fact]
    public async Task Rate_limit_backs_off_without_marking_uncertain()
    {
        _foody.OnCreate = _ => throw FakeFoody.Error(HttpStatusCode.TooManyRequests, "EC_B010");
        _source.Push(Samples.Sale());

        await Cycle();
        var t = await Tracked();
        Assert.Equal(SyncState.PendingCreate, t!.State);
        Assert.Equal(_clock.Now + _options.RetryBaseDelay, t.NextAttemptAt);

        await Cycle(); // ainda no backoff
        Assert.Single(_foody.CreateCalls);

        _clock.Advance(_options.RetryBaseDelay);
        await Cycle();
        Assert.Equal(SyncState.Tracking, (await Tracked())!.State);
    }

    [Fact]
    public async Task Status_changes_are_reported_and_delivered_is_closed()
    {
        _source.Push(Samples.Sale());
        await Cycle();
        var uid = (await Tracked())!.FoodyUid!;

        _foody.SetStatus(uid, FoodyOrderStatus.OnGoing);
        await Cycle();
        _foody.SetStatus(uid, FoodyOrderStatus.Delivered);
        await Cycle();

        Assert.Equal(
            [(FoodyOrderStatus.Ready, FoodyOrderStatus.OnGoing), (FoodyOrderStatus.OnGoing, FoodyOrderStatus.Delivered), (FoodyOrderStatus.Delivered, FoodyOrderStatus.Closed)],
            _sink.Changes.Select(c => (c.Previous, c.Current)));
        Assert.Contains((uid, FoodyOrderStatus.Closed), _foody.StatusCalls);
        Assert.Equal(SyncState.Done, (await Tracked())!.State);
    }

    [Fact]
    public async Task Cancel_in_A7_cancels_on_foody()
    {
        _source.Push(Samples.Sale());
        await Cycle();
        var uid = (await Tracked())!.FoodyUid!;

        _source.Push(Samples.Sale() with { State = SaleState.Cancelled });
        await Cycle();

        Assert.Contains((uid, FoodyOrderStatus.Cancelled), _foody.StatusCalls);
        var t = await Tracked();
        Assert.Equal(SyncState.Done, t!.State);
        Assert.Equal(FoodyOrderStatus.Cancelled, t.FoodyStatus);
    }

    [Fact]
    public async Task Cancel_after_pickup_needs_a_person()
    {
        _source.Push(Samples.Sale());
        await Cycle();
        _foody.OnUpdateStatus = (_, s) => s == FoodyOrderStatus.Cancelled ? FakeFoody.Error(HttpStatusCode.Forbidden, "E25") : null;

        _source.Push(Samples.Sale() with { State = SaleState.Cancelled });
        await Cycle();

        var t = await Tracked();
        Assert.Equal(SyncState.NeedsAttention, t!.State);
        Assert.Contains("portal Foody", t.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sale_cancelled_before_sending_never_reaches_foody()
    {
        _foody.OnCreate = _ => throw FakeFoody.Error(HttpStatusCode.TooManyRequests, "EC_B010");
        _source.Push(Samples.Sale());
        await Cycle();

        _source.Push(Samples.Sale() with { State = SaleState.Cancelled });
        _clock.Advance(TimeSpan.FromHours(1));
        await Cycle();

        Assert.Equal(SyncState.Done, (await Tracked())!.State);
        Assert.Single(_foody.CreateCalls); // só a tentativa recusada pelo rate limit
    }

    [Fact]
    public async Task Cancel_during_uncertain_create_cancels_once_order_is_found()
    {
        _foody.OnCreate = req => { _foody.Add(req); throw new TaskCanceledException("timeout"); };
        _source.Push(Samples.Sale());
        await Cycle();

        _source.Push(Samples.Sale() with { State = SaleState.Cancelled });
        _clock.Advance(TimeSpan.FromMinutes(5));
        await Cycle(); // acha o pedido → PendingCancel
        await Cycle(); // cancela

        Assert.Single(_foody.StatusCalls, c => c.Status == FoodyOrderStatus.Cancelled);
        Assert.Equal(SyncState.Done, (await Tracked())!.State);
    }

    [Fact]
    public async Task Tracking_stops_after_max_age()
    {
        _source.Push(Samples.Sale());
        await Cycle();

        _clock.Advance(_options.TrackingMaxAge + TimeSpan.FromMinutes(1));
        await Cycle();

        Assert.Equal(SyncState.NeedsAttention, (await Tracked())!.State);
    }

    [Fact]
    public async Task Invalid_sale_is_not_sent()
    {
        _source.Push(Samples.Sale() with { SaleId = "12345678901" });
        await Cycle();

        Assert.Equal(SyncState.NeedsAttention, (await Tracked("12345678901"))!.State);
        Assert.Empty(_foody.CreateCalls);
    }
}
