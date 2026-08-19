INSERT INTO accounting_periods(accounting_period_id, accounting_book_id, period_key,
    starts_on, ends_on, status, closed_at, version)
SELECT randomblob(16), b.accounting_book_id, 'ESTABLISHMENT', '0001-01-01', '9999-12-31', 'OPEN', NULL, 1
FROM accounting_books AS b
WHERE b.book_kind = 'COMMERCIAL_BANK'
  AND NOT EXISTS(
    SELECT 1 FROM accounting_periods AS p
    WHERE p.accounting_book_id = b.accounting_book_id);
