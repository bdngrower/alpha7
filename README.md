# Conector A7Pharma → Foody Delivery

Serviço Windows que envia as vendas com entrega do **A7Pharma (Alpha7)** para a **Foody Delivery** e acompanha o status de cada entrega.
Roda **na matriz** e atende **várias lojas**.

```
Lojas (A7Pharma) ──grava──> [Base de Integração PostgreSQL]
                                  │ leitura por SQL configurável
                          [A7FoodyConnector — serviço Windows]
                           ├─ SQLite local: vínculo venda ↔ pedido Foody, outbox, cursores
                           └─ REST ──> Foody API v1.2 (um token por loja/conta)
```

## Situação

| Parte | Status |
| --- | --- |
| Cliente da API Foody v1.2 (criar, status, polling, listagem paginada, erros E*) | pronto, com testes |
| De-para venda → pedido Foody (endereço, CEP, telefone, pagamento, itens) | pronto, com testes |
| Motor de sincronização (outbox, retry, anti-duplicidade, cancelamento, polling) | pronto, com testes |
| Banco local SQLite | pronto |
| Serviço Windows, configuração e logs | pronto |
| Leitor do A7 por consultas SQL configuráveis (PostgreSQL), conexão por loja | pronto, com testes e validado contra PostgreSQL |
| **Consultas SQL reais das vendas do PDV** | **aguardando a Alpha7 indicar a origem** ([pendências](docs/pendencias.md)) |
| Comandos de diagnóstico `testar-foody` e `testar-a7` | pronto |
| Retorno do status ao A7Pharma | depende da Alpha7 permitir escrita |
| Webhooks da Foody (no lugar do polling) | fase 2 |

## Estrutura

| Projeto | Responsabilidade |
| --- | --- |
| `src/A7Foody.Foody` | Cliente HTTP da Foody: modelos, JSON (datas `-03:00`, enums `on_credit`/`onGoing`), erros, resiliência. Não conhece o A7. |
| `src/A7Foody.Core` | Modelo neutro da venda (`DeliverySale`), de-para, motor (`OrderSyncEngine`), portas `ISalesSource` / `IDeliveryStatusSink` / `IOrderStore`. |
| `src/A7Foody.Storage` | `IOrderStore` em SQLite (Dapper). |
| `src/A7Foody.A7` | Leitor das vendas do A7 (`ISalesSource`) por consultas em arquivo; cursor incremental por data de alteração + id. |
| `src/A7Foody.Service` | Host: serviço Windows, configuração, logs (Serilog), laço de ciclos. |
| `tests/A7Foody.Tests` | xUnit: cliente com HTTP simulado, de-para e motor com Foody falsa e SQLite real. |

**Nenhum nome de tabela do A7 está no código.** O leitor executa `sql/vendas-entrega.sql` e `sql/itens-venda.sql`, e o contrato de colunas está em [`sql/LEIAME.md`](src/A7Foody.Service/sql/LEIAME.md). Se a Alpha7 mudar o banco, criar uma tabela nova ou liberar leitura direta, só os arquivos `.sql` mudam, sem recompilar.

## Como funciona um ciclo (a cada 30 s, por loja)

1. **Ingestão:** lê no A7 as vendas com entrega novas ou alteradas desde o último cursor e grava na outbox local, já convertidas para o formato da Foody.
2. **Envio:** processa a outbox. Cria os pedidos com `POST /orders`, já como `ready` (configurável) e envia os cancelamentos.
3. **Acompanhamento:** chama `checkupdates` com os pedidos em andamento, repassa as mudanças de status e envia `closed` quando o pedido é entregue.

### Estados no conector

```
             cadastro inválido / 400 ─────────────> NeedsAttention ──(corrigido no A7)──┐
                                                                                         │
PendingCreate ──POST ok──> Tracking ──delivered→closed / cancelled / rejected──> Done   │
   │    ▲                     │                                                          │
   │    └──não achou──┐       └──cancelada no A7──> PendingCancel ──ok──> Done          │
   │                  │                                 └──E25 (já coletado)──> NeedsAttention
   └─timeout/5xx──> CreateUncertain ──achou na Foody──> Tracking
                                                                         PendingCreate <─┘
```

**Por que existe o `CreateUncertain`:** o `POST /orders` não é idempotente. Se der timeout, a Foody pode ter criado o pedido mesmo assim. Reenviar às cegas chamaria **dois motoboys**. Por isso o conector procura o pedido na Foody (`GET /orders` pelo período, comparando o `id`) antes de reenviar. Pelo mesmo motivo, o HttpClient **não** repete POST/PUT automaticamente.

Outras decisões:
- **Dado inválido não fica sendo reenviado.** Um erro 400 vai para `NeedsAttention`. Quando o cadastro é corrigido no A7 e a venda aparece de novo como alterada, ela volta para a fila sozinha.
- **Erros 429, 401 e 403** (rate limit, token errado, plano Free) entram em backoff exponencial de 30 s até 30 min. A requisição foi recusada, então reenviar é seguro.
- **Cancelamento de pedido já coletado** (E25) exige motivos que só uma pessoa sabe informar. Vai para `NeedsAttention` com a orientação de cancelar no portal.
- **Entrega sem status final depois de 48 h** vai para `NeedsAttention` e deixa de ser consultada.
- **Erro em uma loja não para as outras.** Erro inesperado num ciclo não derruba o serviço.

## Configuração

O `appsettings.json` ao lado do executável traz os padrões. **Tokens não vão no repositório.** Configure em `%ProgramData%\A7FoodyConnector\appsettings.local.json`:

```json
{
  "Stores": [
    { "StoreId": "1", "Name": "Matriz",  "FoodyToken": "<token da Foody>", "Enabled": true },
    { "StoreId": "2", "Name": "Loja 02", "FoodyToken": "<token da Foody>", "Enabled": true }
  ],
  "Mapping": {
    "OrderIdFormat": "{sale}",
    "PaymentMethods": { "DINHEIRO": "Money", "PIX": "Pix", "CONVENIO": "OnCredit" }
  }
}
```

| Chave | Padrão | Observação |
| --- | --- | --- |
| `Stores[].FoodyToken` | — | Na Foody, em *Menu → Minha Conta → APIs e Hooks*. Plano Free não tem API (E11). |
| `Mapping:OrderIdFormat` | `{sale}` | Máximo de 10 caracteres. Use `{store}` se as lojas compartilharem uma conta Foody e a numeração de venda se repetir entre lojas. |
| `Mapping:PaymentMethods` | exemplos | Códigos do A7 → `Money`, `Card`, `Online`, `OnCredit`, `Pix`, `EWallet`. |
| `Mapping:CreateAsReady` | `true` | Use `false` se a separação acontecer depois do caixa. |
| `Sync:Interval` | `00:00:30` | A Foody pede no mínimo 30 s; valores menores são ignorados. |
| `Sync:AutoCloseDelivered` | `true` | Envia `closed` depois de `delivered`. |
| `A7:ConnectionStrings:<StoreId>` | — | PostgreSQL da Base de Integração (ou do A7) da loja. Sem nenhuma, o conector não lê vendas. |
| `A7:FirstRunLookback` | `00:00:00` | Na 1ª execução, só lê vendas novas. **Não** aumente em produção: venda antiga chamaria motoboy. |
| `DataDirectory` | `%ProgramData%\A7FoodyConnector` | Pasta do banco, dos logs e do `appsettings.local.json`. |

Variáveis de ambiente com prefixo `A7FOODY_` também funcionam, por exemplo `A7FOODY_Stores__0__FoodyToken`.

## Build, testes e instalação

Pré-requisito: [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0). O servidor **não** precisa do runtime, porque o publish é self-contained.

```bash
dotnet test
```

```bash
dotnet publish src/A7Foody.Service -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

No servidor da matriz, em um PowerShell como administrador:

```powershell
sc.exe create A7FoodyConnector binPath= "C:\A7FoodyConnector\A7FoodyConnector.exe" start= delayed-auto
sc.exe failure A7FoodyConnector reset= 86400 actions= restart/60000/restart/60000/restart/60000
sc.exe start A7FoodyConnector
```

## Diagnóstico (homologação)

Os dois comandos abaixo não criam pedido nem gravam no banco do conector:

```bash
A7FoodyConnector.exe testar-foody --loja 1
```

```bash
A7FoodyConnector.exe testar-a7 --loja 1 --desde "2026-11-20 08:00"
```

`testar-foody` valida o token, ou acusa E19 (token errado) e E11 (plano sem API). `testar-a7` lista as vendas encontradas e o JSON que iria para a Foody, ou o motivo de não enviar.

## Operação

- **Logs:** ficam em `%ProgramData%\A7FoodyConnector\logs\connector-AAAAMMDD.log`, com 30 dias de retenção.
- **Banco local:** `%ProgramData%\A7FoodyConnector\connector.db`. Abre no [DB Browser for SQLite](https://sqlitebrowser.org/). Para ver o que precisa de atenção:

```sql
SELECT store_id, sale_id, foody_uid, last_error, datetime(updated_at/10000000 - 62135596800, 'unixepoch') AS atualizado
FROM orders WHERE state = 5 ORDER BY updated_at DESC;   -- 5 = NeedsAttention
```

## Referências

- Documentação Foody compilada: [Integração Foody Delivery × Alpha7 — API v1.2 e Webhooks](https://claude.ai/artifact/JWzFwAZeLPn43gVfccYpm8)
- [Pendências e perguntas para Alpha7 e Foody](docs/pendencias.md)
