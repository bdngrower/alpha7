# Consultas de leitura do A7Pharma

O conector **não tem nomes de tabela do A7 no código**. Ele executa estes dois arquivos e espera as colunas abaixo. Para apontar para outra tabela, view ou banco, **edite só os arquivos `.sql`**; não precisa recompilar.

Os arquivos atuais são **modelos**. Eles apontam para uma tabela hipotética `out_vendaentrega`, porque a Alpha7 ainda vai dizer de onde vêm as vendas do PDV (ver `docs/pendencias.md`).

## `vendas-entrega.sql`

**Parâmetros recebidos:**

| Parâmetro | Tipo | Uso |
| --- | --- | --- |
| `@loja` | texto | `StoreId` da configuração. Use para filtrar quando o banco tiver várias lojas. |
| `@desde` | timestamp | Data de alteração da última venda já lida. |
| `@desde_id` | texto | `venda_id` da última venda já lida (desempate de mesma data). |
| `@ate` | timestamp | Agora menos `A7:CommitLag`. Não ler nada depois disso. |
| `@limite` | inteiro | Máximo de linhas. |

**Filtro e ordem obrigatórios.** Sem eles o conector pula ou repete vendas:

```sql
WHERE (alterado_em > @desde OR (alterado_em = @desde AND venda_id > @desde_id))
  AND alterado_em <= @ate
ORDER BY alterado_em, venda_id
LIMIT @limite
```

Se `venda_id` for numérico no banco, devolva-o como texto (`CAST(... AS varchar)`). Faça a comparação com `@desde_id` sobre a mesma expressão de texto e, de preferência, com zeros à esquerda (`lpad`), para a ordem de texto bater com a numérica.

**Colunas devolvidas** (`*` = obrigatória):

| Coluna | Tipo | Observação |
| --- | --- | --- |
| `venda_id` * | texto | Identifica a venda; vai para a Foody conforme `Mapping:OrderIdFormat` (máx. 10 caracteres). |
| `alterado_em` * | timestamp | Última alteração; é o cursor. Precisa mudar quando a venda for cancelada. |
| `criado_em` * | timestamp | Data da venda. |
| `cancelada` | booleano / 'S' / 1 | Venda cancelada. |
| `prazo_entrega` | timestamp | Prazo prometido ao cliente. |
| `valor_total` | numérico | |
| `taxa_entrega` | numérico | |
| `forma_pagamento` | texto | Convertida por `Mapping:PaymentMethods`. |
| `troco_para` | numérico | Só para dinheiro. |
| `observacao` | texto | |
| `ifood_localizador` | texto | Se a venda veio do iFood. |
| `cliente_nome`, `cliente_telefone`, `cliente_email` | texto | Telefone com DDD, com ou sem máscara. |
| `end_logradouro` | texto | Sem ele a venda não é enviada (vai para "precisa de atenção"). |
| `end_numero`, `end_bairro`, `end_cidade`, `end_uf`, `end_cep`, `end_complemento`, `end_referencia` | texto | |
| `end_latitude`, `end_longitude` | numérico | Só se forem coordenadas exatas. |

**Devolva somente vendas com entrega**, sem retirada na loja. Inclua as **canceladas** (`cancelada = true`), para o conector cancelar o pedido na Foody.

## `itens-venda.sql`

Parâmetros: `@loja` e `@venda_id`. Colunas obrigatórias: `descricao`, `quantidade` e `valor_unitario`.

## Testar sem enviar nada à Foody

```bash
A7FoodyConnector.exe testar-a7 --loja 1 --desde "2026-11-20 08:00"
```

O comando mostra as vendas encontradas e como cada uma seria enviada, ou os erros de cadastro. Ele não grava nada no banco do conector.
