using A7Foody.Foody.Models;

namespace A7Foody.Core.Mapping;

/// <summary>Regras de conversão venda A7 → pedido Foody (seção <c>Mapping</c> do appsettings).</summary>
public sealed class MappingOptions
{
    /// <summary>Seção do appsettings.</summary>
    public const string Section = "Mapping";

    /// <summary>
    /// Formato do campo <c>id</c> enviado à Foody (máx. 10 caracteres). Marcadores: <c>{store}</c> e
    /// <c>{sale}</c>. Use <c>{store}</c> se várias lojas compartilharem a mesma conta Foody e os números
    /// de venda puderem se repetir entre lojas.
    /// </summary>
    public string OrderIdFormat { get; set; } = "{sale}";

    /// <summary>
    /// De-para forma de pagamento do A7 (código ou descrição, sem diferenciar maiúsculas) → Foody.
    /// </summary>
    public Dictionary<string, FoodyPaymentMethod> PaymentMethods { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Forma usada quando o código do A7 não está no de-para. <c>null</c> = não envia.</summary>
    public FoodyPaymentMethod? DefaultPaymentMethod { get; set; }

    /// <summary>
    /// Criar o pedido já como <c>ready</c>. Na farmácia a venda costuma sair do caixa separada e conferida;
    /// se a loja tiver etapa de separação depois do caixa, use <c>false</c> e envie <c>ready</c> depois.
    /// </summary>
    public bool CreateAsReady { get; set; } = true;

    /// <summary>Taxa do entregador fixa por pedido (opcional).</summary>
    public decimal? CourierFee { get; set; }
}
