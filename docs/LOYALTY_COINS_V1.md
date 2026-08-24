# Loyalty coins V1

The loyalty foundation is tenant-scoped and ledger-based. Deploy database script `V13-LoyaltyCoins.sql`, then open **Administration → Coin & Loyalty Settings**.

## Rule evaluation

- An explicit disabled product rule makes that product ineligible for coins.
- Product rules take precedence over category rules.
- With `PRODUCT_FIRST`, a product/category fixed rule wins and amount-based earning is calculated once over the remaining eligible order amount.
- With `PURCHASE_FIRST`, amount-based earning wins (except explicitly ineligible products).
- Fractional fixed coins and incomplete purchase-amount increments are rounded down.

## Lifecycle and idempotency

- Redemption is written inside the same SQL transaction that posts the sales invoice.
- Earning is posted after the configured `COMPLETED` or `DELIVERED` event.
- Cancellation/void/full return reverses earned coins. Redeemed coins are restored according to tenant refund settings.
- Each order event has a unique ledger key (`ORDER:{id}:EARN`, `REDEEM`, `REVERSE_EARN`, `RESTORE_REDEEM`), preventing duplicate processing.
- Partial returns do not reverse the entire award; V1 reverses it when the invoice becomes fully returned.

The signed ledger supports future transaction types (`BONUS`, `REFERRAL`, and `CAMPAIGN`) without changing customer balance storage. Customer balances and totals are derived from ledger entries rather than maintained as an independently mutable balance.
