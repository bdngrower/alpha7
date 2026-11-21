namespace A7Foody.Foody.Models;

/// <summary>Corpo do POST /orders e do PUT /orders/{uid}.</summary>
public sealed record FoodyOrderRequest
{
    /// <summary>Identificador do pedido no sistema de origem. Obrigatório, máx. 10 caracteres.</summary>
    public required string Id { get; init; }

    /// <summary>Data/hora de criação. Se omitida, a Foody usa a data corrente. Não vai no PUT.</summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>Prazo de entrega. Se omitido, a Foody usa o prazo padrão da conta.</summary>
    public DateTimeOffset? DeliveryDueDate { get; init; }

    /// <summary>Na criação só são aceitos <see cref="FoodyOrderStatus.Open"/> e <see cref="FoodyOrderStatus.Ready"/>.</summary>
    public required FoodyOrderStatus Status { get; init; }

    /// <summary>Valor da entrega cobrado do cliente.</summary>
    public decimal? DeliveryFee { get; init; }

    /// <summary>Meio de pagamento.</summary>
    public FoodyPaymentMethod? PaymentMethod { get; init; }

    /// <summary>Observações livres.</summary>
    public string? Notes { get; init; }

    /// <summary>Taxa paga ao entregador.</summary>
    public decimal? CourierFee { get; init; }

    /// <summary>Valor total do pedido.</summary>
    public decimal? OrderTotal { get; init; }

    /// <summary>Itens em texto estruturado, ex.: "1x Dipirona 500mg | R$ 12,90".</summary>
    public string? OrderDetails { get; init; }

    /// <summary>Localizador iFood (8 caracteres), quando o pedido veio do iFood.</summary>
    public string? IfoodLocalizer { get; init; }

    /// <summary>Endereço de entrega. Obrigatório.</summary>
    public required FoodyDeliveryPoint DeliveryPoint { get; init; }

    /// <summary>Cliente.</summary>
    public FoodyCustomer? Customer { get; init; }
}

/// <summary>Endereço de entrega.</summary>
public sealed record FoodyDeliveryPoint
{
    /// <summary>Endereço completo, de preferência "logradouro, número, bairro". Obrigatório.</summary>
    public required string Address { get; init; }

    /// <summary>Rua. Obrigatório quando há coordenadas.</summary>
    public string? Street { get; init; }

    /// <summary>Número. Obrigatório quando há coordenadas.</summary>
    public string? HouseNumber { get; init; }

    /// <summary>CEP. Opcional, mas se enviado precisa ser válido.</summary>
    public string? PostalCode { get; init; }

    /// <summary>Coordenadas exatas. Se omitidas, a Foody geocodifica pelo endereço.</summary>
    public FoodyCoordinates? Coordinates { get; init; }

    /// <summary>Cidade. Obrigatória quando há coordenadas.</summary>
    public string? City { get; init; }

    /// <summary>UF. Obrigatória quando há coordenadas.</summary>
    public string? Region { get; init; }

    /// <summary>País ISO 3166-1. O POST usa "BR"; o GET pode devolver "BRA".</summary>
    public string? Country { get; init; }

    /// <summary>Complemento.</summary>
    public string? Complement { get; init; }
}

/// <summary>Latitude/longitude.</summary>
public sealed record FoodyCoordinates(double Lat, double Lng);

/// <summary>Cliente do pedido.</summary>
public sealed record FoodyCustomer
{
    /// <summary>Telefone no formato +[país][DDD][número], ex.: +5511999998888.</summary>
    public string? CustomerPhone { get; init; }

    /// <summary>Nome.</summary>
    public string? CustomerName { get; init; }

    /// <summary>E-mail.</summary>
    public string? CustomerEmail { get; init; }
}

/// <summary>Pedido como a Foody devolve (POST, PUT, GET).</summary>
public sealed record FoodyOrder
{
    /// <summary>ID do pedido na Foody. Guardar vinculado à venda do A7.</summary>
    public string Uid { get; init; } = "";

    /// <summary>ID no sistema de origem.</summary>
    public string Id { get; init; } = "";

    /// <summary>Status atual.</summary>
    public FoodyOrderStatus Status { get; init; }

    /// <summary>Data do pedido.</summary>
    public DateTimeOffset? Date { get; init; }

    /// <summary>Link público de rastreio para o cliente.</summary>
    public string? OrderTrackerUrl { get; init; }

    /// <summary>Modo de despacho (ex.: "manual").</summary>
    public string? DespatchMode { get; init; }

    /// <summary>Entregador, quando atribuído.</summary>
    public FoodyCourier? Courier { get; init; }

    /// <summary>Data em que ficou pronto.</summary>
    public DateTimeOffset? ReadyDate { get; init; }

    /// <summary>Data do despacho.</summary>
    public DateTimeOffset? DespatchDate { get; init; }

    /// <summary>Data da coleta.</summary>
    public DateTimeOffset? CollectedDate { get; init; }

    /// <summary>Data da entrega.</summary>
    public DateTimeOffset? DeliveryDate { get; init; }

    /// <summary>Última atualização.</summary>
    public DateTimeOffset? UpdateDate { get; init; }
}

/// <summary>Entregador.</summary>
public sealed record FoodyCourier
{
    /// <summary>Nome.</summary>
    public string? CourierName { get; init; }

    /// <summary>Telefone.</summary>
    public string? CourierPhone { get; init; }

    /// <summary>Tipo (ex.: "internal").</summary>
    public string? CourierType { get; init; }
}

/// <summary>Item da resposta de POST /orders/checkupdates.</summary>
public sealed record FoodyOrderUpdate
{
    /// <summary>ID do pedido na Foody.</summary>
    public string Uid { get; init; } = "";

    /// <summary>Data da última atualização.</summary>
    public DateTimeOffset? UpdateDate { get; init; }

    /// <summary>Status atual.</summary>
    public FoodyOrderStatus Status { get; init; }
}
