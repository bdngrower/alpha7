namespace A7Foody.A7;

/// <summary>Leitura das vendas no A7Pharma (seção <c>A7</c> do appsettings).</summary>
public sealed class A7SourceOptions
{
    /// <summary>Seção do appsettings.</summary>
    public const string Section = "A7";

    /// <summary>
    /// String de conexão PostgreSQL por loja (chave = <c>StoreId</c>). Pode apontar para a Base de
    /// Integração da Alpha7 ou, com acesso de leitura, para o banco do A7Pharma. Lojas que compartilham o
    /// mesmo banco repetem a mesma string; a consulta filtra por <c>@loja</c>.
    /// </summary>
    public Dictionary<string, string> ConnectionStrings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Arquivo SQL que devolve as vendas com entrega (contrato em <c>sql/LEIAME.md</c>).</summary>
    public string SalesQueryFile { get; set; } = "sql/vendas-entrega.sql";

    /// <summary>Arquivo SQL que devolve os itens de uma venda.</summary>
    public string ItemsQueryFile { get; set; } = "sql/itens-venda.sql";

    /// <summary>Máximo de vendas lidas por ciclo, por loja.</summary>
    public int BatchSize { get; set; } = 200;

    /// <summary>
    /// Só lê registros alterados há pelo menos este tempo. Evita perder uma venda cuja transação ainda não
    /// tinha sido confirmada no momento da leitura (ela teria data anterior ao cursor quando aparecesse).
    /// </summary>
    public TimeSpan CommitLag { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Na primeira execução de uma loja (sem cursor), quanto do passado ler. Padrão zero: só vendas novas.
    /// Nunca use um valor grande em produção — vendas antigas seriam enviadas à Foody e chamariam motoboy.
    /// </summary>
    public TimeSpan FirstRunLookback { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Fuso das colunas de data do banco da Alpha7 quando são <c>timestamp</c> sem fuso (o normal).
    /// </summary>
    public string DatabaseTimeZone { get; set; } = "America/Sao_Paulo";
}
