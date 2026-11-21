using A7Foody.Core.Mapping;
using A7Foody.Core.Sales;
using A7Foody.Foody.Models;

namespace A7Foody.Tests;

public class MapperTests
{
    private static FoodyOrderMapper Mapper(Action<MappingOptions>? configure = null)
    {
        var o = new MappingOptions
        {
            PaymentMethods = new() { ["pix"] = FoodyPaymentMethod.Pix, ["CONVENIO"] = FoodyPaymentMethod.OnCredit },
        };
        configure?.Invoke(o);
        return new FoodyOrderMapper(o);
    }

    [Fact]
    public void Maps_a_complete_sale()
    {
        var r = Mapper().Map(Samples.Sale());

        Assert.True(r.IsValid);
        var o = r.Order!;
        Assert.Equal("12345", o.Id);
        Assert.Equal(FoodyOrderStatus.Ready, o.Status);
        Assert.Equal(FoodyPaymentMethod.Pix, o.PaymentMethod); // de-para sem diferenciar maiúsculas
        Assert.Equal(57.80m, o.OrderTotal);
        Assert.Equal("Rua Barão de Campinas, 120, Centro", o.DeliveryPoint.Address);
        Assert.Equal("13480-000", o.DeliveryPoint.PostalCode);
        Assert.Equal("Apto 12", o.DeliveryPoint.Complement);
        Assert.Null(o.DeliveryPoint.Coordinates);
        Assert.Equal("+5519998765432", o.Customer!.CustomerPhone);
        Assert.Equal("2x Dipirona 500mg 10cp | R$ 25,80\n1x Protetor solar FPS50 | R$ 27,00", o.OrderDetails);
        Assert.Equal("Referência: Ao lado da padaria", o.Notes);
    }

    [Fact]
    public void Id_longer_than_10_chars_is_a_data_error()
    {
        var r = Mapper(o => o.OrderIdFormat = "L{store}-{sale}").Map(Samples.Sale(id: "1234567", store: "12"));

        Assert.False(r.IsValid);
        Assert.Contains(r.Errors, e => e.Contains("OrderIdFormat", StringComparison.Ordinal));
    }

    [Fact]
    public void Store_prefix_is_supported()
    {
        var r = Mapper(o => o.OrderIdFormat = "{store}-{sale}").Map(Samples.Sale(id: "998", store: "03"));
        Assert.Equal("03-998", r.Order!.Id);
    }

    [Fact]
    public void Missing_street_is_a_data_error()
    {
        var sale = Samples.Sale() with { Address = new SaleAddress { Number = "10" } };
        Assert.False(Mapper().Map(sale).IsValid);
    }

    [Fact]
    public void Unknown_payment_uses_default()
    {
        var sale = Samples.Sale() with { PaymentCode = "BOLETO" };
        Assert.Null(Mapper().Map(sale).Order!.PaymentMethod);
        Assert.Equal(FoodyPaymentMethod.Money,
            Mapper(o => o.DefaultPaymentMethod = FoodyPaymentMethod.Money).Map(sale).Order!.PaymentMethod);
    }

    [Fact]
    public void Coordinates_go_only_with_complete_address()
    {
        var sale = Samples.Sale();
        var complete = sale with { Address = sale.Address with { Latitude = -22.56, Longitude = -47.40 } };
        var incomplete = sale with { Address = sale.Address with { Latitude = -22.56, Longitude = -47.40, City = null } };

        var p = Mapper().Map(complete).Order!.DeliveryPoint;
        Assert.NotNull(p.Coordinates);
        Assert.Equal(("Rua Barão de Campinas", "120", "Limeira", "SP", "BR"), (p.Street, p.HouseNumber, p.City, p.Region, p.Country));

        Assert.Null(Mapper().Map(incomplete).Order!.DeliveryPoint.Coordinates);
    }

    [Theory]
    [InlineData("13480000", "13480-000")]
    [InlineData("13480-000", "13480-000")]
    [InlineData("1348000", null)]
    [InlineData("00000000", null)]
    [InlineData(null, null)]
    public void Postal_code(string? input, string? expected) => Assert.Equal(expected, FoodyOrderMapper.NormalizePostalCode(input));

    [Theory]
    [InlineData("(19) 99876-5432", "+5519998765432")]
    [InlineData("1934561234", "+551934561234")]
    [InlineData("+55 19 99876-5432", "+5519998765432")]
    [InlineData("019 99876-5432", "+5519998765432")]
    [InlineData("ramal 22", "ramal 22")]
    public void Phone(string input, string expected) => Assert.Equal(expected, FoodyOrderMapper.NormalizePhone(input));

    [Fact]
    public void Fractional_quantity_and_change_for()
    {
        var sale = Samples.Sale() with { Items = [new SaleItem("Granel", 1.5m, 10m)], ChangeFor = 100m, Address = Samples.Sale().Address with { Reference = null } };
        var o = Mapper().Map(sale).Order!;
        Assert.Equal("1,5x Granel | R$ 15,00", o.OrderDetails);
        Assert.Equal("Troco para R$ 100,00", o.Notes);
    }
}
