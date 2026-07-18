# GrayMint Wallet Service — Product & Engineering Specification

> **Purpose of this document.** This is a complete, self-contained functional specification of an existing, working double-entry wallet/ledger microservice, written so another engineer or AI agent can **re-implement it in any stack** without reading the original C# source. It documents *what the system does and why*, not just its API shape.
>
> Source system: .NET 10, ASP.NET Core, EF Core, SQL Server. Namespace `EWallet`. Four projects: `Wallet` (domain+data), `Wallet.Api` (generated client), `Wallet.Server` (HTTP host), `Wallet.Test` (integration tests).

---

## 1. What this service is

A **multi-tenant, multi-currency, double-entry ledger** exposed over HTTP. It answers one question reliably: *who owns how much of what, and how did it get that way?*

It is **not** a payment processor. It moves internal balances. It has no concept of money leaving the system, no PSP integration, no KYC, no users. It is the accounting core that a product wraps.

Its defining characteristics:

1. **Double-entry.** Every movement writes two rows (a debit and a credit). Balances are derived from, and reconcilable against, an immutable transaction log.
2. **Card-network semantics.** Orders follow `Authorize → Capture → Void`, with funds held by a system wallet in between. This is the model that makes marketplaces, escrow, and delayed settlement possible.
3. **Credit lines, not just balances.** A wallet can spend beyond zero, up to a configured limit. This is how money is minted into the system.
4. **Atomic multi-hop transfers ("ladder").** One order can move funds across a chain of wallets (A→B→C→D) in a single transaction, where each hop can spend what the previous hop delivered.
5. **Tenant-isolated.** Every entity hangs off an `App`. Apps cannot see each other's data. Enforced at the query level, not just the auth level.

### 1.1 What a consuming product gets

If you are building on this, the primitives you get are:

| You want to build | You use |
|---|---|
| User balances | One `Wallet` per user |
| Points / credits / tokens / fiat side-by-side | Multiple `Currency` ids under one app |
| "Reserve funds now, settle later" | `Authorize` then `Capture` |
| Cancel a reservation or reverse a settled payment | `Void` |
| Marketplace split (buyer → seller + platform fee) | One order, multiple `ParticipantWallets` |
| Minting new money into the system | System wallet with a large credit line |
| Statements / transaction history | `GET /wallets/transactions` |
| Safe client retries | Idempotent order creation via caller-supplied `OrderId` |

---

## 2. Domain model

### 2.1 The six entities

```
App (tenant)
 ├── SystemWalletId ──────► Wallet          (one privileged wallet per app)
 ├── Currency[]                              (just an id + app; no metadata)
 ├── Wallet[]                                (containers, no balance of their own)
 │    └── WalletBalance[]                    (the actual money: one per wallet×currency)
 └── Order[]                                 (a unit of intent)
      └── OrderItem[]                        (one transfer leg: sender→receiver, amount)
           └── WalletTransaction[]           (the immutable ledger; 2 rows per leg)
```

### 2.2 Entity semantics

**App** — the tenant boundary and the unit of authentication. Has exactly one **system wallet**, created automatically with the app. Holds an `AuthorizationCode` used to invalidate issued API keys.

**Currency** — deliberately almost empty: `{ CurrencyId, AppId }`. There is **no name, no symbol, no decimal precision, no exchange rate**. It is an opaque partition key for balances. The consuming product is expected to hold the human meaning ("USD", "loyalty points") in its own tables and pass the integer id here.
> *Design note for the new product:* this is a real gap. Adding `Code`, `Name`, `DecimalPlaces` to the currency entity is the single most commonly wanted extension. Currencies also **cannot be deleted** and there is no cross-currency exchange — every order is single-currency.

**Wallet** — `{ WalletId, AppId, CreatedTime }`. A wallet is just an identity; it holds no balance itself. Wallets are **never deleted** (only via the whole-app `clear-all` dev endpoint).

**WalletBalance** — where money actually lives. One row per `(WalletId, CurrencyId)` — enforced by a unique index. Contains:
- `Balance` — funds actually held. Usually ≥ 0, but **legitimately goes negative** in two designed cases (see §4.5 and §3.4).
- `MinBalance` — the **credit line**, stored as a **negative number or zero**. `MinBalance = -1000` means "this wallet may go 1000 into the red."

  **Rows are created lazily** — a wallet has no balance row for a currency until it first receives funds or has a min-balance set. Code must treat "no row" as "balance 0, credit 0".

**Order** — a unit of intent, addressed by a **caller-supplied `OrderId` (GUID)**, unique per app. Internally it also has a surrogate `OrderId (long)`; the external one is `OrderReferenceNumber` in the database. Carries `CurrencyId`, `TransactionType`, `OrderTypeId`, and the lifecycle timestamps.

**OrderTypeId** — an opaque `int` the caller sets and the service never interprets. It exists purely so the product can tag orders ("purchase" = 1, "refund" = 2, "payout" = 3) and filter transaction history by it. It is indexed for that purpose. There is no validation and no enum.

**OrderItem** — one transfer leg: `{ SenderWalletId, ReceiverWalletId, Amount }`. An order has one or more.

**WalletTransaction** — the immutable double-entry ledger. Two rows per order item (one negative for the sender, one positive for the receiver), each snapshotting the wallet's `Balance` *after* the movement. This is the audit trail; balances should always be reconcilable against it.

### 2.3 The system wallet — the most important concept

Every app has exactly one, created automatically at app creation. It is **not** a normal wallet:

- **It is forbidden from being an explicit order participant.** Naming it as sender or receiver is rejected. It only ever participates implicitly, via the mechanics below.
- **When a currency is created, the system wallet is automatically granted a credit line of `-long.MaxValue`** for it — effectively infinite. This is deliberate and load-bearing.

It plays two distinct roles:

1. **The mint / source of funds.** Because it has an unbounded credit line, a `Sale` from the system wallet to a user wallet creates money in the system from nothing. This is how you fund accounts. (The service does not enforce that this is balanced against real-world money — that's the product's job.)
2. **The escrow holder.** During `Authorize`, funds leave the sender and sit in the system wallet. On `Capture` they move on to the real receiver; on `Void` they go back to the sender. The receiver never sees the money until capture.

> **Consequence to internalize:** the sum of all wallet balances in an app is **not** zero and is **not** conserved. The system wallet's balance goes negative as money is minted. Total system-wide invariant is `sum(all balances) == 0` *only if* you count the system wallet's negative balance. Reconciliation logic must include it.

---

## 3. Transaction types and the order lifecycle

### 3.1 The two transaction types

Set at creation, immutable thereafter. **Only these two are valid**; anything else is rejected.

| Type | Meaning | Money path at creation | Resulting status |
|---|---|---|---|
| `Authorize` (1) | Hold funds | sender → **system wallet** | `Authorized` |
| `Sale` (2) | Immediate settlement | sender → **receiver** | `Captured` |

`Sale` is exactly `Authorize + Capture` collapsed into one step.

### 3.2 Status is derived, never stored

There is no status column. Status is computed from timestamps, **in this precedence order**:

```
if (VoidedTime   != null) -> Voided
else if (CapturedTime != null) -> Captured
else                           -> Authorized
```

**Void wins over Capture.** A captured order that is later voided reads as `Voided`. Re-implementations must preserve this precedence or history queries will disagree with order reads.

### 3.3 Lifecycle

```
                  ┌──────────────────────────────────────┐
                  │                                      │
  create(Authorize)                                       │
        │                                                 ▼
        ▼                  capture()                  ┌────────┐
   ┌────────────┐  ─────────────────────────────────► │Captured│
   │ Authorized │                                     └────────┘
   └────────────┘                                          │
        │                                                  │ void()
        │ void()                                           ▼
        │                                             ┌────────┐
        └────────────────────────────────────────────►│ Voided │
                                                      └────────┘
                                                          ▲
  create(Sale) ──► Captured ───────── void() ─────────────┘
```

**Transition rules (all enforced):**
- `capture()` requires status `Authorized`. Capturing a `Captured` order → `OrderAlreadySetAsRequestedStateException`. Capturing a `Voided` order → `InvalidTransactionTypeException` ("works only on authorize status"). A `Sale` is already `Captured`, so it can never be captured.
- `void()` is allowed from **both** `Authorized` and `Captured`, and reverses the correct path for each. Voiding a `Voided` order → `OrderAlreadySetAsRequestedStateException`.
- **There is no partial capture and no partial void.** All-or-nothing on the whole order.
- **There is no expiry.** An authorization holds funds in the system wallet forever until captured or voided. If the product needs auth expiry, it must build a sweeper that calls `void()`.

### 3.4 Money movement per operation

Let `S` = sender, `R` = receiver, `SYS` = system wallet, `A` = amount.

| Operation | From | To | Notes |
|---|---|---|---|
| Create `Authorize` | S | SYS | R's balance is untouched. |
| Create `Sale` | S | R | Settles immediately. |
| `Capture` (of an Authorize) | SYS | R | Sender untouched — already debited at auth. |
| `Void` an `Authorized` order | SYS | S | Refund from escrow. R untouched. |
| `Void` a `Captured` order | R | S | Claws back from the receiver. **SYS untouched.** |

> **The clawback is the sharp edge.** Voiding a captured order takes money *from the receiver*, who may have already spent it. This void path therefore runs with **`forceTransfer = true`, which bypasses all balance and credit-limit checks and will drive the receiver's balance negative**, below their credit line, with no error. This is intentional in the original system (a void must always succeed) but it is a genuine product decision the new system must consciously re-make. The test `Void_sale_without_balance` locks in this behavior.

---

## 4. The balance & credit-line model (the core algorithm)

This is the part that most needs to be understood rather than copied.

### 4.1 The two-number representation

Each `WalletBalance` holds `Balance` (≥ 0) and `MinBalance` (≤ 0, the remaining credit).

```
available_to_spend = Balance + (-MinBalance)
```

**Both numbers are mutated as credit is consumed.** `Balance` drains to zero first, then `MinBalance` shrinks toward zero as the credit line is eaten. This is unusual — most systems keep the limit fixed and let the balance go negative. Here the *limit itself* is decremented, so `MinBalance` means **"credit remaining"**, not "credit granted".

> **Implication:** you **cannot** read a wallet's original credit limit back out after it has spent into it, and you cannot distinguish "was granted 1000, used 999" from "was granted 1". The grant is lost. If the product needs to display "credit limit" vs "credit used", it must store the original grant separately (or restructure to a fixed-limit + signed-balance model, which is the cleaner design and is recommended for a rewrite).

### 4.2 Two passes: validation, then execution

This is essential to understand, because the two passes **deliberately disagree**.

Every order runs:

1. **A validation pass** over an in-memory, *untracked* copy of the balances. It walks the ladder crediting the **real receiver** of each item — i.e. it models the order **as if it were a `Sale`**, regardless of the actual transaction type. If any sender can't cover its leg here, it throws `InsufficientBalanceException` and nothing is written. **This pass validates the order's net economics.**
2. **An execution pass** that writes the real ledger. For an `Authorize`, it routes every credit to the **system wallet**, not the named receiver (§3.4).

For an `Authorize` ladder, these two views differ: validation says "B receives 100 then spends 90"; execution says "B receives nothing (the system holds it) but still spends 90". Execution resolves this by **letting B go negative** — see §4.5. `Capture` then delivers the escrowed funds and the negative resolves.

**The validation pass is the gatekeeper. The execution pass trusts it.** Any re-implementation must keep this contract, or replace both passes with a single, unified computation (recommended — see §9.4).

### 4.3 Sufficiency check (validation pass)

A debit of `A` from a wallet is allowed iff:

```
row exists  AND  ( Balance >= A  OR  Balance + (-MinBalance) >= A )
```

Otherwise → `InsufficientBalanceException`. **A missing row always fails here** — a wallet with no balance row for the currency cannot send.

### 4.4 Debit algorithm

Given a debit of `A` from a row with `Balance = B`, `MinBalance = M` (M ≤ 0):

```
Case 1 — sufficient balance (B >= A):
    Balance    = B - A
    MinBalance = M            (untouched)

Case 2 — needs credit (B < A, but B + (-M) >= A):
    overdraft  = A - B        // the part that must come from credit
    Balance    = 0
    MinBalance = M + overdraft   // shrinks toward 0; credit consumed

Case 3 — insufficient:
    → validation pass: throw InsufficientBalanceException
    → execution pass:  see §4.5 (Authorize float / forceTransfer) — may go negative
```

### 4.5 The Authorize float — why balances go negative

In the **execution pass only**, when a sender cannot cover a leg (Case 3), there are two escape hatches that write a negative balance instead of throwing:

```
If transactionType == Authorize:
    if no row:  create row { Balance = -A, MinBalance = 0 }
    else:       Balance    = -A + (B + (-M))     // consume all credit, then go negative
                MinBalance = 0                   // credit line fully consumed

If forceTransfer (void of a captured order only, §3.4):
    if no row:  create row { Balance = -A, MinBalance = 0 }
    else:       Balance = B - A                  // straight to negative, no checks

Otherwise (a Sale that validation wrongly let through):
    throw ArgumentOutOfRangeException("Balance can not calculate.")   // → raw 500
```

**This is by design, not a bug.** In an `Authorize` ladder, a wallet may have to pay forward money it has not received yet, because its incoming funds are sitting in escrow. It floats negative for the duration of the authorization, and `Capture` settles it.

**Worked example** — verbatim from the `AuthorizeAndCapture` test. Wallet 1 starts with 100; wallets 2, 3, 4 have no balance row in this currency:

```
Authorize: [ W1→W2 100, W2→W3 90, W3→W4 80 ]

Validation pass (models it as a Sale — all legs cover, so the order is allowed):
    W1: 100→0     W2: 0→100→10     W3: 0→90→10     W4: 0→80

Execution pass (Authorize: every credit goes to SYS instead):
    W1→W2 100:  W1 = 0            SYS += 100   (W2 gets nothing)
    W2→W3  90:  W2 = -90  ← float SYS += 90    (no row → Authorize hatch)
    W3→W4  80:  W3 = -80  ← float SYS += 80    (no row → Authorize hatch)
                W4 = no row at all

    Balances now: W1=0, W2=-90, W3=-80, W4=(none), SYS=+270

Capture (SYS → each item's real receiver):
    SYS→W2 100:  W2 = -90 + 100 = 10
    SYS→W3  90:  W3 = -80 +  90 = 10
    SYS→W4  80:  W4 =   0 +  80 = 80

    Final: W1=0, W2=10, W3=10, W4=80   ← matches the validation pass exactly
```

> **The invariant:** *after capture*, balances equal what the validation pass predicted. **Between authorize and capture, intermediate wallets in a ladder can show negative balances that are not overdrafts** — they are unsettled escrow positions.
>
> **This has direct product consequences.** If you show `Balance` to an end user, they may see a negative number they cannot explain, and a naive "can this user spend?" check will read it as debt. A product built on this needs either (a) a derived *available balance* that accounts for pending authorizations, or (b) a redesign that tracks held/pending amounts explicitly rather than overloading `Balance`. **This is the strongest argument for the model change recommended in §13, item 5.**

**Worked example of Case 2** (from test `Success_Sale_combination_balance_and_min_balance`):
```
Wallet2: Balance = 100, MinBalance = -51    → available = 151
Send 150:
    B(100) >= 150?  no
    100 + 51 = 151 >= 150?  yes → Case 2
    overdraft = 150 - 100 = 50
    Balance    = 0
    MinBalance = -51 + 50 = -1     → 1 unit of credit remains
```

### 4.6 Credit algorithm

Given a credit of `A` to a wallet:

```
If no row exists:
    create row { Balance = A, MinBalance = 0 }
Else:
    Balance = Balance + A
    MinBalance unchanged
```

> **Receiving funds does NOT repay a consumed credit line.** If a wallet spent into credit (`MinBalance = -1`) and then receives 500, it ends at `Balance = 500, MinBalance = -1`. The credit stays consumed forever. Whether that is right is a product decision; if you want incoming funds to restore the line, you must add that explicitly. **This asymmetry is the single most surprising behavior in the system** and it is what makes the two-number model hard to reason about. A rewrite using `signed_balance` + fixed `credit_limit` gets this right for free.

### 4.7 Setting a credit line

`POST /wallets/{id}/min-balance` with `{ currencyId, minBalance }`. **Absolute set, not a delta** — it overwrites, creating the row if absent. There is **no validation that `minBalance <= 0`**; a positive value would invert the arithmetic and corrupt the wallet. Validate this in the new system.

---

## 5. The "ladder" — multi-item atomic orders

An order carries a **list** of `ParticipantWallets`. Items are processed **sequentially, in array order**, within one transaction. Each item sees the balances left by the previous item, so **funds can flow through a chain in a single order**:

```
Items: [ W1→W2 100, W2→W3 150, W3→W4 150, W4→W5 30, W5→W6 20, W5→W2 40 ]
Start:   W1=100  W2=150  W3=0  W4=20  W5=40  W6=0

W1→W2 100:  W1=0    W2=250
W2→W3 150:  W2=100  W3=150     ← spends what item 1 just delivered
W3→W4 150:  W3=0    W4=170
W4→W5  30:  W4=140  W5=70
W5→W6  20:  W5=50   W6=20
W5→W2  40:  W5=10   W2=140

Final:   W1=0  W2=140  W3=0  W4=140  W5=10  W6=20
```
*(This is verbatim the `Sale` integration test — use it as an acceptance test for any re-implementation.)*

This is why order **matters** and why the whole order is one atomic unit: any item failing rolls back all of them.

> ⚠️ **Latent bug to fix in the rewrite.** Item order is whatever the database returns from the `Include` of order items — **there is no explicit `ORDER BY`**. It works in practice because rows come back in insertion/PK order, but it is not guaranteed and is a correctness landmine for a ladder whose semantics depend entirely on sequence. **Make the ordering explicit** (persist and sort by an item sequence number).

### 5.1 Order validation rules

All applied at creation, all reject the entire order:

| Rule | Error |
|---|---|
| `TransactionType` ∈ {Authorize, Sale} | `InvalidOperationException` |
| Currency must exist for the app | `NotExistsException` |
| Every amount > 0 (zero and negative rejected) | `InvalidOperationException` — "Amounts must be positive." |
| Sender ≠ receiver within an item | `InvalidOperationException` |
| All wallets belong to the app | `InvalidOperationException` — lists offending ids |
| System wallet is not an explicit participant | `InvalidOperationException` |
| No two items identical on `(Sender, Receiver, Amount)` | `InvalidOperationException` — "duplicate records." |

> The duplicate rule is a **blunt instrument**: two legitimate identical transfers (e.g. two 10-unit fees from the same payer to the same payee) in one order are rejected. It exists to catch client double-submission. Consider replacing it with an explicit client-supplied item key.

---

## 6. Idempotency

Order creation is idempotent on the caller-supplied `OrderId` (GUID), scoped per app. On create, the service looks up the existing order and returns it unchanged **iff all of these match**:

- `CurrencyId`
- `OrderTypeId`
- `TransactionType`
- the sequence of `(SenderWalletId, ReceiverWalletId)` pairs, **in order**

> 🐞 **`Amount` is NOT compared.** Re-sending the same `OrderId` with the same wallets but a **different amount silently returns the original order and the new amount is ignored — no error**. A client that retries with a corrected amount will believe it succeeded. **This is a real bug; fix it by including `Amount` in the comparison.**

> 🐞 **A genuine mismatch produces a bad error.** If the fields don't match, the code treats it as "not idempotent" and proceeds to insert — which then violates the unique index on `(OrderReferenceNumber, AppId)` and surfaces as a raw database exception, not a clean `409 Conflict`. **Fix by explicitly returning a conflict error when an order with that id exists but doesn't match.**

`Capture` and `Void` are **not** idempotent — repeating them throws `OrderAlreadySetAsRequestedStateException`. Clients must treat that exception as success-equivalent, or the new system should make these idempotent (recommended).

---

## 7. API surface

Base: `/api/apps/{appId}`. All bodies JSON. Enums serialize as **strings** (`"Authorize"`, `"Captured"`).

### 7.1 Apps

| Method | Route | Auth | Notes |
|---|---|---|---|
| `POST` | `/api/apps` | `AppCreate` (system only) | Creates app **and** its system wallet atomically. → `201`, `{ appId, systemWalletId }` |
| `GET` | `/api/apps/{appId}` | `AppReadWrite` | → `{ appId, systemWalletId }` |
| `POST` | `/api/apps/{appId}/clear-all` | `AppReadWrite` | **Destructive.** Deletes all orders, wallets, currencies; recreates the system wallet. Hard-blocked in Production. Test-fixture only. → `204` |

### 7.2 Currencies

| Method | Route | Notes |
|---|---|---|
| `POST` | `/currencies` | No body. → `201` with a bare `int` currency id. Also grants the system wallet a `-long.MaxValue` credit line for it, atomically. |
| `GET` | `/currencies` | → `int[]` |

### 7.3 Wallets

| Method | Route | Notes |
|---|---|---|
| `POST` | `/wallets` | No body. → `201`, `Wallet` |
| `GET` | `/wallets/{walletId}` | → `Wallet` with `currencies[]` balances |
| `GET` | `/wallets?walletIds=1,2,3` | **Comma-separated string**, not repeated params. Unparseable input → unhandled `500`. |
| `POST` | `/wallets/{walletId}/min-balance` | `{ currencyId, minBalance }`. Absolute set. → `Wallet` |
| `GET` | `/wallets/transactions?walletId=…` | History. See §7.5. |

### 7.4 Orders

| Method | Route | Notes |
|---|---|---|
| `POST` | `/orders` | `CreateOrderRequest`. Idempotent. → `Order` |
| `POST` | `/orders/batch` | `CreateSingleTransferRequest[]`. See §7.6. → `Order[]` |
| `GET` | `/orders/{orderId:guid}` | → `Order` |
| `POST` | `/orders/{orderId:guid}/capture` | → `Order` |
| `POST` | `/orders/{orderId:guid}/void` | → `Order` |

### 7.5 Transaction history — constraints that will bite

`GET /wallets/transactions`

Params: `walletId` (required), `participantWalletId`, `beginTime`, `endTime`, `orderTypeId`, `pageSize`, `pageNumber`.

- Returns legs where the wallet is **either** sender or receiver.
- **Hard 31-day window.** A range wider than 31 days throws. Omitted dates default to *the last 31 days*, silently — a caller expecting "all history" gets a month.
- Only one bound given → the other is derived ±31 days.
- **Paging is quirky:** `pageNumber` defaults to `-1`, which means *unpaged — returns everything* (`pageSize = int.MaxValue`). When paging, `pageNumber` is **1-based** and an unset/negative `pageSize` becomes `101`.
- **No total count is returned**, so a client cannot render a pager.
- Sorted by `OrderId` desc in SQL, then re-sorted by `CreatedTime` desc in memory *after* paging.
- Returns `OrderItemView` (order-item level), **not** the `WalletTransaction` ledger rows. **The raw double-entry ledger is not exposed by any endpoint** — it is write-only from the API's perspective. Expose it if the product needs true statements.

### 7.6 Batch orders

`POST /orders/batch` takes a flat array of **single-leg** transfers, each becoming its own independent order:

```jsonc
[{ "orderId": "guid", "senderWalletId": 1, "receiverWalletId": 2,
   "currencyId": 1, "amount": 100, "orderTypeId": 5, "transactionType": "Sale" }]
```

- **All orders share one transaction — any failure rolls back the entire batch.**
- Each may use a **different currency** and transaction type (a batch is not currency-scoped).
- Each is individually idempotent on its own `orderId`.
- Batch items are one-leg-only — no ladders in a batch. For multi-leg, use `POST /orders`.

### 7.7 Canonical request/response shapes

```jsonc
// CreateOrderRequest
{
  "orderId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",  // caller-supplied; idempotency key
  "currencyId": 1,
  "orderTypeId": 100,                                  // opaque, caller-defined
  "transactionType": "Authorize",                      // "Authorize" | "Sale"
  "participantWallets": [                              // ordered — the ladder
    { "senderWalletId": 10, "receiverWalletId": 11, "amount": 100.0 }
  ]
}

// Order
{
  "orderId": "3fa85f64-…",        // the caller's GUID, not the internal key
  "currencyId": 1,
  "transactionType": "Authorize",
  "orderTypeId": 100,
  "status": "Authorized",          // derived: Voided > Captured > Authorized
  "items": [ { "orderItemId": 1, "senderWalletId": 10,
               "receiverWalletId": 11, "amount": 100.0 } ],
  "createdTime": "2024-01-01T00:00:00Z",
  "authorizedTime": "2024-01-01T00:00:00Z",  // ⚠ always == createdTime, even for Sale
  "capturedTime": null,
  "voidedTime": null
}

// Wallet
{
  "walletId": 10,
  "createdTime": "2024-01-01T00:00:00Z",
  "currencies": [ { "currencyId": 1, "balance": 100.0, "minBalance": -50.0 } ]
}
```

> ⚠️ `authorizedTime` in the DTO is **hardcoded to `createdTime`**, ignoring the stored `AuthorizedTime` column (which is null for `Sale` orders). So a `Sale` reports an `authorizedTime` it never had. Harmless today; do not depend on it. Fix in the rewrite.

---

## 8. Authentication & authorization

Provided by the external `GrayMint.Authorization.MicroserviceAuthorization` package. If re-implementing, you need to **replace this entirely** — the concepts, not the library:

- **Two permissions:** `AppCreate` (create apps — system only) and `AppReadWrite` (everything else).
- **Two principal kinds:**
  - **System key** — minted from a shared secret in config (`Auth:Secret`); holds both permissions on a root resource.
  - **App key** — `userId` **is the `appId` as a string**. Grants `AppReadWrite` scoped to that app id only.
- Controllers are decorated with an app-id-scoped permission check: the `{appId}` in the route must match the principal's resource. This is the tenant isolation.
- Bearer tokens. Each app row holds an `AuthorizationCode`; **rotating it invalidates all previously issued keys for that app** — the revocation mechanism.
- The system key can mint an app key for any app (used by tests and provisioning).

**Provisioning flow:** system key → `POST /api/apps` → get `appId` → mint an app key for that `appId` → that key is what the product uses from then on.

---

## 9. Concurrency & consistency — read this before scaling

The current design has **three real limitations** that a new implementation should address deliberately.

### 9.1 An in-process lock serializes each app

Every mutating order endpoint takes an **in-process** lock keyed `"appId: {id}"` (10-minute timeout) for the whole request.

- ✅ Gives correct ladder arithmetic and prevents balance races **on a single instance**.
- ❌ **Breaks completely under horizontal scaling.** Two instances hold two different locks and will interleave, corrupting balances. **This service cannot be safely run with more than one replica as written.**
- ❌ Serializes *all* orders for an app, even for unrelated wallets/currencies — a throughput ceiling per tenant.
- ❌ It is a lock, not a transaction: it does not protect against out-of-band DB writes.

**For the rewrite, pick one:** a distributed lock (Redis/etcd) keyed per app; or database-level pessimistic locking (`SELECT … FOR UPDATE` on the touched balance rows, in a deterministic order to avoid deadlock); or optimistic concurrency (a version/rowversion column on `WalletBalance` + retry). **Row-level locking on the affected balance rows, ordered by wallet id, is the recommended design** — it keeps unrelated wallets concurrent while remaining correct.

### 9.2 Ledger ids are assigned by `MAX(id) + 1`

`WalletTransactionId` is **not** database-generated (`ValueGeneratedNever`). The code reads the global max and increments (+2 per item, since each leg writes two rows).

- The max is **global across all apps**, but the lock is **per app** — so two concurrent orders in *different* apps can read the same max and collide on the primary key.
- **Use a database identity/sequence instead.** There is no reason for this scheme.

### 9.3 Transaction boundaries are inconsistent

`Create`, `CreateBatchOrders`, `Void`, `CreateCurrency` and app creation open explicit transactions. **`Capture` does not** — it relies on a single `SaveChanges` (implicitly atomic) plus the app lock. It works, but the asymmetry is a trap. **Wrap every mutating use case in an explicit transaction.**

### 9.4 Other correctness notes for the rewrite

- **Validation and execution are two separate code paths** computing balances differently (§4.2). The divergence is *intentional* for the Authorize float (§4.5) — but it means the sufficiency rules live in two places that must stay in agreement, and **they currently don't**:

  🐞 **The validation pass has a sign error when consuming credit.** Where execution correctly computes `MinBalance + (A - B)`, validation computes `MinBalance - (A + B)`. With `Balance = 100, MinBalance = -51`, a 150 debit should leave `MinBalance = -1`; validation instead records `-301`. It errs **permissive** — it inflates the wallet's apparent remaining credit ~6× in this example. Consequences: in a multi-leg order where an early leg dips into credit, a later leg can pass validation and then hit execution's `ArgumentOutOfRangeException("Balance can not calculate.")` → a raw **500** instead of a clean `InsufficientBalanceException`. Single-leg orders are unaffected (validation's corrupted value is never re-read).

  **Recommendation: collapse to one computation.** Model escrow explicitly (a `held`/`pending` amount, or a real escrow account) so the ladder can be evaluated once, correctly, without needing a float. This removes the sign bug, the dual-path drift, and the confusing negative balances in one move.
- Money is `decimal(19,4)` by convention; ledger `Amount`/`Balance` are `decimal(23,4)`. **Never use floating point.**
- `-long.MaxValue` as the system credit line is a sentinel that can overflow if arithmetic ever sums two of them. Prefer an explicit "unlimited" flag or a `NULL` limit meaning unbounded.

---

## 10. Data model (target-schema neutral)

```
apps(app_id PK, system_wallet_id FK→wallets NULL, authorization_code VARCHAR(50), created_time)
currencies(currency_id PK, app_id FK→apps CASCADE)
wallets(wallet_id PK, app_id FK→apps CASCADE, created_time)

wallet_balances(
  wallet_balance_id PK,
  wallet_id FK→wallets CASCADE,
  currency_id FK→currencies,
  balance     DECIMAL(23,4) NOT NULL DEFAULT 0,   -- >= 0 except on forced void
  min_balance DECIMAL(23,4) NOT NULL DEFAULT 0,   -- <= 0; remaining credit
  modified_time,
  UNIQUE (wallet_id, currency_id)                 -- critical
)

orders(
  order_id PK,                                    -- internal surrogate
  order_reference_number UUID NOT NULL,           -- the caller's OrderId
  app_id FK→apps CASCADE,
  currency_id FK→currencies,
  transaction_type_id SMALLINT FK→transaction_types,
  order_type_id INT NOT NULL,                     -- opaque
  authorized_time NULL, captured_time NULL, voided_time NULL,
  created_time, modified_time, process_time NULL,
  UNIQUE (order_reference_number, app_id),        -- idempotency + tenant isolation
  INDEX (app_id, created_time) INCLUDE (order_type_id)   -- history queries
)

order_items(
  order_item_id PK,
  order_id FK→orders CASCADE,
  sender_wallet_id INT, receiver_wallet_id INT,
  amount DECIMAL(19,4)
  -- ⚠ ADD: sequence INT NOT NULL — the ladder depends on order (see §5)
)

wallet_transactions(                              -- immutable ledger
  wallet_transaction_id PK,                       -- ⚠ make this an identity/sequence
  order_item_id FK→order_items CASCADE,
  wallet_id FK→wallets,
  amount  DECIMAL(23,4),                          -- signed: -A sender, +A receiver
  balance DECIMAL(23,4),                          -- snapshot AFTER this movement
  receiver_wallet_id FK→wallets,                  -- the *actual* receiver, even in escrow
  wallet_transaction_reference_id NULL FK→self,   -- present but never populated
  created_time
)

transaction_types(transaction_type_id PK, name VARCHAR(50))  -- enum lookup, seeded at boot
```

**Notes for the port:**
- Default schema is `dbo` (SQL Server). Adjust freely.
- `order_items` has **no FK constraints on the wallet ids** — validated in code only. Add real FKs.
- `wallet_transaction_reference_id` is modeled and indexed but **never written**. Dead. Drop it or use it.
- `receiver_wallet_id` on a ledger row is the *actual intended* receiver, not the counterparty of that row. During an `Authorize`, the row crediting the **system** wallet still records the **real** receiver here. This is what lets you reconstruct intent from escrowed movements — preserve it, and document it, because it is not obvious.
- The `transaction_types` lookup table is reconciled from the code enum at every startup.

---

## 11. Errors

| Exception | Meaning | Suggested HTTP |
|---|---|---|
| `InsufficientBalanceException` | Sender lacks balance + credit | `400` / `422` |
| `InvalidTransactionTypeException` | Illegal lifecycle transition | `409` |
| `OrderAlreadySetAsRequestedStateException` | Already captured/voided | `409` |
| `InvalidOperationException` | Validation failure (§5.1) | `400` |
| `NotExistsException` | Unknown app/currency | `404` |
| `UnauthorizedAccessException` | `clear-all` in production | `403` |

The original serializes an `ExceptionTypeName` in the body and clients switch on that string — a **type-name-as-contract** coupling. **Use stable machine-readable error codes instead** (e.g. `INSUFFICIENT_BALANCE`), decoupled from class names.

Unhandled paths that currently 500 and should be handled properly: malformed `walletIds` query string; unknown wallet id (a "not found" surfaces as a single-row-expected failure); idempotency mismatch (§6).

---

## 12. Reference flows

### 12.1 Bootstrap
```
POST /api/apps                          → { appId, systemWalletId }
   (creates app + system wallet atomically)
POST /api/apps/{appId}/currencies       → currencyId
   (system wallet auto-granted -long.MaxValue credit for it)
```

### 12.2 Onboard a user and fund them
```
POST /api/apps/{appId}/wallets                      → { walletId: 10 }

// Mint 1000 into the user wallet, from the system wallet.
// Note: senderWalletId is NOT the system wallet — that's forbidden.
// You fund by having a *funded* wallet send, or by giving wallet 10
// a credit line and letting it spend. The system wallet enters only
// implicitly. To seed the very first funds, give the source wallet a
// credit line:
POST /api/apps/{appId}/wallets/10/min-balance
  { "currencyId": 1, "minBalance": -1000 }          // may now spend up to 1000
```

> **Read this carefully.** Because the system wallet cannot be an explicit participant, "minting" is done by **granting a wallet a credit line and letting it spend into the red** — the counterpart lands in the system wallet's balance automatically. The test suite's "system wallet dom" is a *normal* wallet with a large credit line, **not** the app's actual system wallet. This naming collision in the tests is confusing and is worth renaming in a rewrite (`TreasuryWallet` vs. the internal `SystemWallet`).

### 12.3 Escrowed purchase with a marketplace fee
```
// Buyer(10) pays 100 → Seller(11); platform(12) takes a 5 fee from the seller.
POST /api/apps/{appId}/orders
{
  "orderId": "<guid>", "currencyId": 1, "orderTypeId": 1,
  "transactionType": "Authorize",
  "participantWallets": [
    { "senderWalletId": 10, "receiverWalletId": 11, "amount": 100 },
    { "senderWalletId": 11, "receiverWalletId": 12, "amount": 5 }
  ]
}
// Validation models this as a Sale, so the ladder is accepted: leg 2 is
// deemed to spend what leg 1 delivered.
//
// But under Authorize, BOTH legs' funds go to the system wallet — wallet 11
// never actually receives the 100. So wallet 11 pays the 5 fee out of money
// it doesn't have yet and FLOATS NEGATIVE (balance -5) until capture (§4.5).
// This is expected. Do not "fix" it by pre-funding wallet 11.

POST /api/apps/{appId}/orders/<guid>/capture
// System wallet now pays out to the real receivers:
//   wallet 11: -5 + 100 = 95     wallet 12: 0 + 5 = 5
// The float resolves and balances match what validation predicted.

// or
POST /api/apps/{appId}/orders/<guid>/void       // return escrow to senders
```

> **Do not read a wallet's `Balance` as spendable while it has open authorizations.** Between authorize and capture it may be negative for reasons that have nothing to do with debt (§4.5).

### 12.4 Statement
```
GET /api/apps/{appId}/wallets/transactions
      ?walletId=10&beginTime=2024-01-01&endTime=2024-01-31&pageNumber=1&pageSize=50
// Remember: max 31 days; no total count; omitting dates = last 31 days only.
```

---

## 13. Recommendations for the new product

Ordered by importance. Items 1–4 are **correctness**; the rest are product maturity.

1. **Replace the in-process lock** (§9.1). The system is single-instance-only as written. Row-level DB locking on the touched balance rows, acquired in a deterministic order, is the recommended replacement.
2. **Make ledger ids database-generated** (§9.2). `MAX+1` under a per-app lock with a global id space is a live PK-collision bug.
3. **Fix idempotency** (§6): include `Amount` in the comparison, and return a clean `409` on mismatch instead of a raw DB error.
4. **Make item order explicit** (§5) with a sequence column — the ladder's semantics depend on an ordering the schema does not guarantee.
5. **Restructure the balance model** (§4) — the highest-leverage design change, fixing four problems at once. Replace `Balance`/`MinBalance` with **`signed_balance` + a fixed `credit_limit` + an explicit `held` (escrowed) amount**. This:
   - preserves the original credit grant instead of destroying it (§4.1);
   - makes "receiving funds restores credit" fall out naturally (§4.6);
   - **models escrow explicitly, so authorized funds no longer force intermediate wallets negative** (§4.5) — the negative-balance confusion disappears and `available = signed_balance + credit_limit - held` becomes a number you can safely show a user;
   - collapses validation and execution into one computation, eliminating the sign bug in §9.4.
6. **Give `Currency` real fields** — code, name, decimal places (§2.2).
7. **Expose the ledger.** The double-entry rows are written but unreadable via the API; real statements and reconciliation need them.
8. **Fix history ergonomics** (§7.5): return a total count, don't silently window to 31 days, make `pageNumber` consistently 1-based.
9. **Stable error codes**, not exception class names (§11).
10. **Decide consciously about the void clawback** (§3.4) — forcing a receiver negative past their limit with no error is defensible but must be a choice, not an inherited accident.
11. **Consider adding:** authorization expiry; partial capture/void; `Refund` as a first-class type; per-wallet metadata/external-ref; webhooks on state change; cross-currency orders.

---

## 14. Acceptance tests to port

The original integration suite is the real specification. Any re-implementation should reproduce at minimum:

- **Ladder arithmetic** — the six-leg `Sale` chain in §5, asserting the exact final balances `[0, 140, 0, 140, 10, 20]`.
- **The Authorize float** (§4.5) — the highest-value test in the suite, and the behavior most likely to be lost in a rewrite:
  - The same six-leg chain as an `Authorize` asserts `[0, 0, -150, -10, -20, no-row]` — negative intermediates, and the last receiver has **no balance row at all**.
  - `AuthorizeAndCapture`: `[W1→W2 100, W2→W3 90, W3→W4 80]` from a single funded wallet gives `[0, -90, -80, none]` after authorize, then **exactly** `[0, 10, 10, 80]` after capture.
  - The general invariant to assert: **post-capture balances equal what a pure `Sale` of the same order would have produced.**
- **Currency creation** grants the system wallet `MinBalance = -long.MaxValue, Balance = 0`.
- **Credit-line consumption** — `Balance 100 / MinBalance -51`, send 150 → `Balance 0 / MinBalance -1`. *(Assert this on the persisted row; it is where the §9.4 sign bug hides.)*
- **Lifecycle guards** — capture-after-capture, capture-after-void, void-after-void, capture-a-Sale all reject with the documented exceptions.
- **Void from both `Authorized` and `Captured`**, asserting the *different* money paths (§3.4), including `Void_sale_without_balance` (the forced negative).
- **All seven validation rules** in §5.1.
- **Tenant isolation** — an order naming a wallet from another app is rejected, even when sender and receiver are consistent with each other.
- **Idempotency** — identical re-post returns the same order. *(And add the missing case: differing amount must NOT silently succeed.)*
- **Batch atomicity** — one bad item rolls back the whole batch; mixed currencies in one batch succeed.
- **Authorize holds in escrow** — receiver's balance is unchanged until capture.
