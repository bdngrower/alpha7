# Pendências

## Alpha7: bloqueia a leitura das vendas

O caminho previsto é a **Integração Via Base Externa**, um banco compartilhado que exige assinar um termo de consentimento com o administrativo da Alpha7. Uma alternativa é o **acesso de leitura ao banco do A7Pharma**. Referências: [Base Externa](https://kb.a7.net.br/A7Pharma:O_Que_%C3%A9_a_Integra%C3%A7%C3%A3o_Via_Base_Externa%3F) e [acesso de leitura](https://kb.a7.net.br/A7Pharma:Acesso_de_Leitura_ao_banco_de_dados_do_A7Pharma).

- [ ] Documento **"Base de Dados para Integrações Externas"**: tabelas, campos e SGBD (SQL Server, PostgreSQL, Firebird?).
- [ ] Onde ficam as **vendas ou orçamentos com entrega do PDV**, e qual campo indica que a venda é de entrega.
- [ ] Em que momento a venda está **pronta para despachar**: orçamento ou venda fechada no caixa?
- [ ] Campos disponíveis: endereço (logradouro, número, bairro, cidade, UF, CEP, complemento, referência), cliente, telefone, itens, total, taxa de entrega, forma de pagamento e troco.
- [ ] Como identificar uma **venda cancelada**.
- [ ] Campo confiável para servir de **cursor incremental**: data de alteração, ID sequencial ou rowversion.
- [ ] Tamanho e formato do **número da venda**. A Foody aceita no máximo 10 caracteres.
- [ ] As vendas de **todas as lojas** chegam ao banco da matriz? Com qual atraso de replicação?
- [ ] É possível **gravar o status da entrega** de volta (despachado, em rota, entregue)? Em qual tabela?
- [ ] Custo, prazo e termo de consentimento.

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
