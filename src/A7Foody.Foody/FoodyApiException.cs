using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace A7Foody.Foody;

/// <summary>Erro devolvido pela API da Foody (códigos E09, E17, E22... da documentação).</summary>
public sealed partial class FoodyApiException : Exception
{
    /// <summary>Cria a exceção.</summary>
    public FoodyApiException(HttpStatusCode statusCode, string? errorCode, string? responseBody)
        : base(BuildMessage(statusCode, errorCode, responseBody))
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ResponseBody = responseBody;
    }

    /// <summary>Status HTTP.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Código Foody (ex.: "E17"), quando identificado no corpo.</summary>
    public string? ErrorCode { get; }

    /// <summary>Corpo bruto da resposta, para log.</summary>
    public string? ResponseBody { get; }

    /// <summary>
    /// Erro causado pelos dados enviados (endereço, campo obrigatório, valor inválido...).
    /// Repetir a mesma requisição não resolve: precisa de correção no cadastro.
    /// </summary>
    public bool IsDataError => StatusCode == HttpStatusCode.BadRequest;

    /// <summary>Erro que pode passar sozinho (rate limit, falha do servidor).</summary>
    public bool IsTransient => StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout
        || (int)StatusCode >= 500;

    /// <summary>
    /// Extrai o código de erro do corpo. A documentação lista os códigos, mas não o formato do corpo;
    /// por isso tenta campos JSON comuns e, em último caso, procura o padrão no texto.
    /// </summary>
    internal static string? TryExtractErrorCode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in (string[])["code", "errorCode", "error_code", "error"])
                {
                    if (doc.RootElement.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
                        && CodePattern().Match(p.GetString()!) is { Success: true } m)
                    {
                        return m.Value;
                    }
                }
            }
        }
        catch (JsonException)
        {
            // corpo não é JSON; cai na busca textual
        }

        return CodePattern().Match(body) is { Success: true } match ? match.Value : null;
    }

    private static string BuildMessage(HttpStatusCode status, string? code, string? body)
    {
        var snippet = body is { Length: > 300 } ? body[..300] + "…" : body;
        return $"Foody respondeu {(int)status} {code ?? ""}: {snippet}".Trim();
    }

    [GeneratedRegex(@"\b(EC_[A-Z]\d{3}|E\d{2})\b")]
    private static partial Regex CodePattern();
}
