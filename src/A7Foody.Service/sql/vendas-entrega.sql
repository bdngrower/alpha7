-- MODELO: tabela hipotética "out_vendaentrega" (aguardando a Alpha7 indicar a origem real das vendas do PDV).
-- Contrato de parâmetros e colunas: sql/LEIAME.md
SELECT
    v.o_codigo                       AS venda_id,
    v.do_status                      AS alterado_em,
    v.o_datahoravenda                AS criado_em,
    (v.o_status = 'C')               AS cancelada,
    v.o_valortotal                   AS valor_total,
    v.o_taxaentrega                  AS taxa_entrega,
    v.o_formapagamento               AS forma_pagamento,
    v.o_trocoentrega                 AS troco_para,
    v.o_observacaoentrega            AS observacao,
    v.o_nomecliente                  AS cliente_nome,
    v.o_dddcelular || v.o_celular    AS cliente_telefone,
    v.o_email                        AS cliente_email,
    v.o_logradouroentrega            AS end_logradouro,
    v.o_numeroentrega                AS end_numero,
    v.o_bairroentrega                AS end_bairro,
    v.o_cidadeentrega                AS end_cidade,
    v.o_estadoentrega                AS end_uf,
    v.o_cepentrega                   AS end_cep,
    v.o_complementoentrega           AS end_complemento,
    v.o_referencia                   AS end_referencia
FROM out_vendaentrega v
WHERE v.o_retiradaloja = false
  AND (v.do_status > @desde OR (v.do_status = @desde AND v.o_codigo > @desde_id))
  AND v.do_status <= @ate
ORDER BY v.do_status, v.o_codigo
LIMIT @limite
