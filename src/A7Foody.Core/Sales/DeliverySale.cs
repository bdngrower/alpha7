namespace A7Foody.Core.Sales;

/// <summary>
/// Venda com entrega, no formato neutro do conector. O adaptador do A7Pharma converte os dados do
/// banco da Alpha7 para este modelo; o resto do sistema não conhece o banco da Alpha7.
/// </summary>
public sealed record DeliverySale
{
    /// <summary>Código da loja no A7Pharma (precisa bater com <c>Stores[].StoreId</c> na configuração).</summary>
    public required string StoreId { get; init; }

    /// <summary>Identificador da venda/orçamento no A7Pharma.</summary>
    public required string SaleId { get; init; }

    /// <summary>Data/hora em que a venda foi registrada.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Prazo de entrega prometido ao cliente, se houver.</summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>Situação no A7Pharma.</summary>
    public SaleState State { get; init; } = SaleState.Active;

    /// <summary>Valor total da venda.</summary>
    public decimal Total { get; init; }

    /// <summary>Taxa de entrega cobrada do cliente.</summary>
    public decimal? DeliveryFee { get; init; }

    /// <summary>Código/descrição da forma de pagamento no A7Pharma (convertido por <c>Mapping:PaymentMethods</c>).</summary>
    public string? PaymentCode { get; init; }

    /// <summary>Troco para (quando pagamento em dinheiro).</summary>
    public decimal? ChangeFor { get; init; }

    /// <summary>Itens da venda.</summary>
    public IReadOnlyList<SaleItem> Items { get; init; } = [];

    /// <summary>Cliente.</summary>
    public SaleCustomer? Customer { get; init; }

    /// <summary>Endereço de entrega.</summary>
    public required SaleAddress Address { get; init; }

    /// <summary>Observações da venda.</summary>
    public string? Notes { get; init; }

    /// <summary>Localizador iFood, quando a venda veio do iFood.</summary>
    public string? IfoodLocalizer { get; init; }
}

/// <summary>Situação da venda no A7Pharma.</summary>
public enum SaleState
{
    /// <summary>Venda válida, deve ser entregue.</summary>
    Active,

    /// <summary>Venda cancelada no A7Pharma.</summary>
    Cancelled,
}

/// <summary>Item vendido.</summary>
public sealed record SaleItem(string Description, decimal Quantity, decimal UnitPrice);

/// <summary>Cliente.</summary>
public sealed record SaleCustomer(string? Name, string? Phone, string? Email);

/// <summary>Endereço de entrega.</summary>
public sealed record SaleAddress
{
    /// <summary>Logradouro.</summary>
    public string? Street { get; init; }

    /// <summary>Número.</summary>
    public string? Number { get; init; }

    /// <summary>Bairro.</summary>
    public string? District { get; init; }

    /// <summary>Cidade.</summary>
    public string? City { get; init; }

    /// <summary>UF.</summary>
    public string? State { get; init; }

    /// <summary>CEP (com ou sem máscara).</summary>
    public string? PostalCode { get; init; }

    /// <summary>Complemento.</summary>
    public string? Complement { get; init; }

    /// <summary>Ponto de referência.</summary>
    public string? Reference { get; init; }

    /// <summary>Latitude, só se for exata (geocodificação ruim atrapalha a rota).</summary>
    public double? Latitude { get; init; }

    /// <summary>Longitude, só se for exata.</summary>
    public double? Longitude { get; init; }
}
