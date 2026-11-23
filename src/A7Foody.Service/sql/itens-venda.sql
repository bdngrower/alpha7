-- MODELO: itens da tabela hipotética "out_itemvendaentrega". Contrato: sql/LEIAME.md
SELECT
    e.o_descricao        AS descricao,
    i.o_quantidade       AS quantidade,
    i.o_valorunitario    AS valor_unitario
FROM out_itemvendaentrega i
JOIN out_embalagem e ON e.o_id = i.o_idembalagem
WHERE i.o_codigovenda = @venda_id
ORDER BY i.o_id
