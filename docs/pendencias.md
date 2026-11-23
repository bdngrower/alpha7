# Pendências

## Alpha7: de onde vêm as vendas do PDV

**O que a documentação mostrou** ("Base de Dados para Integrações Externas", v2, recebida em 23/11/2026):
- A Base Externa é um banco PostgreSQL ou MySQL **hospedado pelo integrador**. O A7 grava nas tabelas `out_` e lê as `in_`. Não há campo de loja: a configuração parece ser **por loja**.
- O desenho é **e-commerce → A7**. `in_pedido`/`in_itempedido`/`in_pessoa` criam orçamentos no A7. As saídas são `out_embalagem` (produtos/estoque), `out_documentofiscalpedido` (NF) e `out_entregaremessapedido` (remessas de entrega).
- **Não existe `out_` com as vendas do PDV.** A `out_entregaremessapedido` é ligada ao código de um `in_pedido` e não traz endereço, cliente nem itens.

**Respostas da Alpha7:**
- As tabelas da base externa servem só à integração deles. **Caminho escolhido: usuário somente leitura no banco do A7Pharma** (PostgreSQL 14).
- A base externa é uma por CNPJ. Pode ser centralizada, mas aí não separa estoque nem pedidos por loja.
- A base externa atualiza a cada ~20 s. Não se aplica à leitura direta.

**Pendente:**
- [ ] Usuário de leitura no PostgreSQL do A7Pharma, liberado para o IP da VM do conector. Qual é o nome do banco?
- [ ] Tabelas de venda/orçamento com entrega, itens, cliente/endereço, pagamento/troco, cancelamento e remessa de entrega. Há coluna de data de alteração?
- [ ] As outras lojas usam o mesmo servidor ou cada uma tem o seu?
- [ ] VM para o conector: criar uma VM Linux pequena (recomendado) e definir o IP dela.

**Como o conector já está preparado:** o leitor executa as consultas em `src/A7Foody.Service/sql/` (contrato em `sql/LEIAME.md`). Qualquer que seja a resposta, só os arquivos `.sql` e a string de conexão mudam. Os modelos atuais apontam para uma tabela hipotética `out_vendaentrega`.

## Foody

- [ ] **Token de API.** Plano pago obrigatório (E11 no Free).
- [ ] Existe **ambiente de homologação (sandbox)**?
- [ ] Multi-loja: **uma conta com vários pontos de coleta** ou **uma conta e um token por loja**? O conector suporta os dois.
- [ ] O pedido é fechado automaticamente após `delivered` ou precisamos enviar `closed`? Hoje o conector envia (`Sync:AutoCloseDelivered`).
- [ ] **Formato do corpo de erro** da API. A documentação lista os códigos, mas não o JSON; o conector extrai o código de forma tolerante.
- [ ] Nomes dos campos de **motivo de cancelamento** exigidos no E25 (pedido já coletado).
- [ ] Atende **farmácia** sem restrição? Existe algo para medicamento controlado ou retenção de receita? Hoje essa informação iria em `notes`.
- [ ] Limite de UIDs por chamada de `checkupdates`. Hoje o conector usa 100.

## Fase 2

- Webhooks da Foody no lugar do polling (exige uma URL pública e um receptor que responda 200 em menos de 10 s).
- Atualizar o pedido na Foody (`PUT /orders/{uid}`) quando a venda mudar no A7 antes do despacho.
- Painel ou relatório de pedidos em `NeedsAttention`.
