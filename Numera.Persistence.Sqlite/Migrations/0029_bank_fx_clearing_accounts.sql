INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
    account_kind, accounting_type, normal_side, currency_id, posting_allowed, owner_reference_type,
    owner_reference_id, status, created_at, version)
SELECT randomblob(16), a.accounting_book_id, NULL, '1450', 'FX_CLEARING_RECEIVABLE', 'ASSET', 'DEBIT',
    a.currency_id, 1, NULL, NULL, 'ACTIVE', 0, 1
FROM ledger_accounts AS a
WHERE a.account_code = '2400'
  AND NOT EXISTS(
    SELECT 1 FROM ledger_accounts AS e
    WHERE e.accounting_book_id = a.accounting_book_id AND e.account_code = '1450');

INSERT INTO ledger_accounts(ledger_account_id, accounting_book_id, parent_account_id, account_code,
    account_kind, accounting_type, normal_side, currency_id, posting_allowed, owner_reference_type,
    owner_reference_id, status, created_at, version)
SELECT randomblob(16), a.accounting_book_id, NULL, '2450', 'FX_CLEARING_PAYABLE', 'LIABILITY', 'CREDIT',
    a.currency_id, 1, NULL, NULL, 'ACTIVE', 0, 1
FROM ledger_accounts AS a
WHERE a.account_code = '2400'
  AND NOT EXISTS(
    SELECT 1 FROM ledger_accounts AS e
    WHERE e.accounting_book_id = a.accounting_book_id AND e.account_code = '2450');
