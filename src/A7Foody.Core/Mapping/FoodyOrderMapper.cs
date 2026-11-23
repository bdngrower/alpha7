using System.Globalization;
using System.Text;
using A7Foody.Core.Sales;
using A7Foody.Foody.Models;

namespace A7Foody.Core.Mapping;

/// <summary>Resultado da conversão: o pedido ou a lista de problemas de cadastro.</summary>
public sealed record MappingResult(FoodyOrderRequest? Order, IReadOnlyList<string> Errors)
{
    /// <summary>Conversão bem-sucedida.</summary>
    public bool IsValid => Order is not null;
}

/// <summary>Converte <see cref="DeliverySale"/> em <see cref="FoodyOrderRequest"/>, validando as regras da Foody.</summary>
public sealed class FoodyOrderMapper(MappingOptions options)
{
    /// <summary>Tamanho máximo do campo id na Foody.</summary>
    public const int MaxOrderIdLength = 10;

    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    // Cópia sem diferenciar maiúsculas e sem espaços nas pontas, independente de como a config foi carregada.
    private readonly Dictionary<string, FoodyPaymentMethod> _payments = options.PaymentMethods
        .ToDictionary(kv => kv.Key.Trim(), kv => kv.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>Monta o id enviado à Foody.</summary>
    public string BuildOrderId(DeliverySale sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        return options.OrderIdFormat
            .Replace("{store}", sale.StoreId.Trim(), StringComparison.Ordinal)
            .Replace("{sale}", sale.SaleId.Trim(), StringComparison.Ordinal);
    }

    /// <summary>Converte a venda. Nunca lança por dado ruim: devolve os erros para registro.</summary>
    public MappingResult Map(DeliverySale sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        var errors = new List<string>();

        var id = BuildOrderId(sale);
        if (id.Length is 0 or > MaxOrderIdLength)
        {
            errors.Add($"Id \"{id}\" tem {id.Length} caracteres; a Foody aceita de 1 a {MaxOrderIdLength}. Ajuste Mapping:OrderIdFormat.");
        }

        var address = BuildAddressLine(sale.Address);
        if (address is null)
        {
            errors.Add("Endereço de entrega sem logradouro/número.");
        }

        if (errors.Count > 0)
        {
            return new MappingResult(null, errors);
        }

        var order = new FoodyOrderRequest
        {
            Id = id,
            Date = sale.CreatedAt,
            DeliveryDueDate = sale.DueAt,
            Status = options.CreateAsReady ? FoodyOrderStatus.Ready : FoodyOrderStatus.Open,
            DeliveryFee = Money(sale.DeliveryFee),
            CourierFee = Money(options.CourierFee),
            OrderTotal = Money(sale.Total),
            PaymentMethod = MapPayment(sale.PaymentCode),
            OrderDetails = BuildDetails(sale.Items),
            Notes = BuildNotes(sale),
            IfoodLocalizer = string.IsNullOrWhiteSpace(sale.IfoodLocalizer) ? null : sale.IfoodLocalizer.Trim(),
            DeliveryPoint = BuildDeliveryPoint(sale.Address, address!),
            Customer = BuildCustomer(sale.Customer),
        };

        return new MappingResult(order, []);
    }

    /// <summary>A Foody usa 2 casas; o A7 guarda numeric(15,4).</summary>
    private static decimal? Money(decimal? value) =>
        value is null ? null : decimal.Round(value.Value, 2, MidpointRounding.AwayFromZero);

    private FoodyPaymentMethod? MapPayment(string? code) =>
        code is not null && _payments.TryGetValue(code.Trim(), out var m) ? m : options.DefaultPaymentMethod;

    /// <summary>"logradouro, número, bairro" — formato recomendado pela Foody para geocodificação.</summary>
    internal static string? BuildAddressLine(SaleAddress a)
    {
        if (string.IsNullOrWhiteSpace(a.Street))
        {
            return null;
        }

        var parts = new[] { a.Street, a.Number, a.District }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!.Trim());
        return string.Join(", ", parts);
    }

    private static FoodyDeliveryPoint BuildDeliveryPoint(SaleAddress a, string addressLine)
    {
        // Coordenadas só seguem se os campos que a Foody exige junto com elas estiverem completos;
        // sem elas a Foody geocodifica pelo endereço, o que é mais seguro que mandar dado incompleto.
        var hasCoordinates = a.Latitude is not null && a.Longitude is not null
            && !string.IsNullOrWhiteSpace(a.Street) && !string.IsNullOrWhiteSpace(a.Number)
            && !string.IsNullOrWhiteSpace(a.City) && !string.IsNullOrWhiteSpace(a.State);

        return new FoodyDeliveryPoint
        {
            Address = addressLine,
            PostalCode = NormalizePostalCode(a.PostalCode),
            Complement = string.IsNullOrWhiteSpace(a.Complement) ? null : a.Complement.Trim(),
            Coordinates = hasCoordinates ? new FoodyCoordinates(a.Latitude!.Value, a.Longitude!.Value) : null,
            Street = hasCoordinates ? a.Street!.Trim() : null,
            HouseNumber = hasCoordinates ? a.Number!.Trim() : null,
            City = hasCoordinates ? a.City!.Trim() : null,
            Region = hasCoordinates ? a.State!.Trim().ToUpperInvariant() : null,
            Country = hasCoordinates ? "BR" : null,
        };
    }

    /// <summary>CEP com 8 dígitos vira 00000-000; qualquer outra coisa é omitida (CEP inválido faz a Foody rejeitar).</summary>
    internal static string? NormalizePostalCode(string? cep)
    {
        var digits = Digits(cep);
        return digits.Length == 8 && digits != "00000000" ? $"{digits[..5]}-{digits[5..]}" : null;
    }

    /// <summary>Telefone em +55DDDNÚMERO quando reconhecível; senão o texto original.</summary>
    internal static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return null;
        }

        var d = Digits(phone).TrimStart('0');
        return d.Length switch
        {
            10 or 11 => "+55" + d,
            12 or 13 when d.StartsWith("55", StringComparison.Ordinal) => "+" + d,
            _ => phone.Trim(),
        };
    }

    private static FoodyCustomer? BuildCustomer(SaleCustomer? c)
    {
        if (c is null)
        {
            return null;
        }

        var customer = new FoodyCustomer
        {
            CustomerName = string.IsNullOrWhiteSpace(c.Name) ? null : c.Name.Trim(),
            CustomerPhone = NormalizePhone(c.Phone),
            CustomerEmail = IsPlausibleEmail(c.Email) ? c.Email!.Trim() : null,
        };
        return customer is { CustomerName: null, CustomerPhone: null, CustomerEmail: null } ? null : customer;
    }

    private static bool IsPlausibleEmail(string? e) =>
        !string.IsNullOrWhiteSpace(e) && e.Contains('@', StringComparison.Ordinal) && e.Trim().IndexOf('@', StringComparison.Ordinal) > 0
        && e.Trim().LastIndexOf('.') > e.IndexOf('@', StringComparison.Ordinal);

    /// <summary>Uma linha por item: "2x Dipirona 500mg | R$ 25,80" (total da linha).</summary>
    internal static string? BuildDetails(IReadOnlyList<SaleItem> items)
    {
        if (items.Count == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var i in items)
        {
            var qty = i.Quantity == decimal.Truncate(i.Quantity)
                ? decimal.Truncate(i.Quantity).ToString(CultureInfo.InvariantCulture)
                : i.Quantity.ToString("0.###", PtBr);
            sb.Append(qty).Append("x ").Append(i.Description.Trim())
              .Append(" | ").Append((i.Quantity * i.UnitPrice).ToString("C2", PtBr)).Append('\n');
        }

        return sb.ToString().TrimEnd('\n');
    }

    private static string? BuildNotes(DeliverySale sale)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(sale.Notes))
        {
            parts.Add(sale.Notes.Trim());
        }

        if (!string.IsNullOrWhiteSpace(sale.Address.Reference))
        {
            parts.Add("Referência: " + sale.Address.Reference.Trim());
        }

        if (sale.ChangeFor is > 0)
        {
            parts.Add("Troco para " + sale.ChangeFor.Value.ToString("C2", PtBr));
        }

        return parts.Count == 0 ? null : string.Join(" | ", parts);
    }

    private static string Digits(string? s) => s is null ? "" : new string([.. s.Where(char.IsAsciiDigit)]);
}
