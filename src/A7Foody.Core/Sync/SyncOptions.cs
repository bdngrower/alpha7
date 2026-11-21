namespace A7Foody.Core.Sync;

/// <summary>Configuração do ciclo de sincronização (seção <c>Sync</c>).</summary>
public sealed class SyncOptions
{
    /// <summary>Seção do appsettings.</summary>
    public const string Section = "Sync";

    /// <summary>Mínimo exigido pela Foody para polling de status.</summary>
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(30);

    /// <summary>Intervalo entre ciclos. Valores abaixo de 30 s são elevados para 30 s.</summary>
    public TimeSpan Interval { get; set; } = MinimumInterval;

    /// <summary>Máximo de registros processados por etapa em cada ciclo, por loja.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Quantos UIDs mandar por chamada de checkupdates.</summary>
    public int CheckUpdatesBatchSize { get; set; } = 100;

    /// <summary>Enviar <c>closed</c> automaticamente quando a Foody marcar como entregue.</summary>
    public bool AutoCloseDelivered { get; set; } = true;

    /// <summary>Primeiro intervalo de espera após falha; dobra a cada tentativa.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Espera máxima entre tentativas.</summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Depois desse tempo sem chegar a um status final, a entrega vai para NeedsAttention e deixa de ser consultada.</summary>
    public TimeSpan TrackingMaxAge { get; set; } = TimeSpan.FromHours(48);

    /// <summary>Intervalo efetivo, respeitando o mínimo da Foody.</summary>
    public TimeSpan EffectiveInterval => Interval < MinimumInterval ? MinimumInterval : Interval;
}

/// <summary>Uma loja atendida pelo conector (seção <c>Stores</c>, uma entrada por loja).</summary>
public sealed class StoreOptions
{
    /// <summary>Código da loja no A7Pharma.</summary>
    public string StoreId { get; set; } = "";

    /// <summary>Nome para logs.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Token da API Foody (Menu → Minha Conta → APIs e Hooks). Não versionar: usar variável de ambiente
    /// <c>Stores__0__FoodyToken</c> ou arquivo de configuração fora do repositório.
    /// </summary>
    public string FoodyToken { get; set; } = "";

    /// <summary>Permite desligar uma loja sem apagar a configuração.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Nome para logs.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? StoreId : $"{StoreId} ({Name})";
}
