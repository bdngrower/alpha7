using System.Text.Json.Serialization;
using A7Foody.Foody.Json;

namespace A7Foody.Foody.Models;

/// <summary>Status de um pedido na Foody (doc API v1.2, seção "Status do pedido").</summary>
[JsonConverter(typeof(FoodyEnumConverter<FoodyOrderStatus>))]
public enum FoodyOrderStatus
{
    /// <summary>Valor não reconhecido. A Foody pode criar status novos; nunca falhamos por isso.</summary>
    Unknown = 0,

    /// <summary>Pedido adicionado no sistema da Foody.</summary>
    [JsonStringEnumMemberName("open")] Open,

    /// <summary>Pronto para ser despachado.</summary>
    [JsonStringEnumMemberName("ready")] Ready,

    /// <summary>Despachado (atribuído) para entregadores.</summary>
    [JsonStringEnumMemberName("dispatched")] Dispatched,

    /// <summary>Aceito pelo entregador.</summary>
    [JsonStringEnumMemberName("accepted")] Accepted,

    /// <summary>A caminho da entrega.</summary>
    [JsonStringEnumMemberName("onGoing")] OnGoing,

    /// <summary>Entregue.</summary>
    [JsonStringEnumMemberName("delivered")] Delivered,

    /// <summary>Finalizado (passou por todo o ciclo de vida).</summary>
    [JsonStringEnumMemberName("closed")] Closed,

    /// <summary>Cancelado.</summary>
    [JsonStringEnumMemberName("cancelled")] Cancelled,

    /// <summary>Rejeitado.</summary>
    [JsonStringEnumMemberName("rejected")] Rejected,
}

/// <summary>Meios de pagamento aceitos pela Foody.</summary>
[JsonConverter(typeof(FoodyEnumConverter<FoodyPaymentMethod>))]
public enum FoodyPaymentMethod
{
    /// <summary>Valor não reconhecido.</summary>
    Unknown = 0,

    /// <summary>Dinheiro.</summary>
    [JsonStringEnumMemberName("money")] Money,

    /// <summary>Cartão (na entrega).</summary>
    [JsonStringEnumMemberName("card")] Card,

    /// <summary>Pago online.</summary>
    [JsonStringEnumMemberName("online")] Online,

    /// <summary>Fiado / crediário / convênio.</summary>
    [JsonStringEnumMemberName("on_credit")] OnCredit,

    /// <summary>Pix.</summary>
    [JsonStringEnumMemberName("pix")] Pix,

    /// <summary>Carteira digital.</summary>
    [JsonStringEnumMemberName("e_wallet")] EWallet,
}

/// <summary>Extensões de status.</summary>
public static class FoodyOrderStatusExtensions
{
    /// <summary>Status a partir do qual a Foody não muda mais o pedido.</summary>
    public static bool IsFinal(this FoodyOrderStatus status) =>
        status is FoodyOrderStatus.Closed or FoodyOrderStatus.Cancelled or FoodyOrderStatus.Rejected;
}
