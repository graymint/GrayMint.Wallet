The REST API of GrayMint Wallet: a double-entry ledger of wallets and transfers, for a service that keeps its
orders in a database of its own and its money here.

## The API key

Only the system calls this API: a service that holds one of the server's API keys. Every request carries the key as
`Authorization: Bearer <key>`; a request without one of the server's keys answers 401.

## Tenants, wallets, ids and retries

- A tenant is a partition of the caller's that money never leaves, such as one store of a platform; its id is a
  number of the caller's choosing.
- A wallet belongs to one tenant, one owner and one currency. Each tenant has a root wallet in each currency, which
  money enters the tenant from and leaves it to, and which may go below zero.
- A wallet's transactions are its statement: one for each of its transfers, with the amount below 0 when the money
  went out, and the balances the transfer left.
- Reconciliation compares the books of every tenant and reports where they disagree, changing nothing. Nothing
  runs it on its own: it reads every wallet and transfer, so ask for the report when you want it.
- Every id, in an address or in a body, is text, such as `"42"`. An id that is no number answers 404.
- Every new transfer carries an external reference key of the caller's, unique in its tenant. A caller that gets
  no answer, or a 500, sends the same request again: what was made before comes back, and nothing is made twice.

## Answers

- **400**: the request is wrong in itself.
- **401**: the request carries no key of the server's.
- **404**: a wallet or a transfer that is not in the tenant, or an id that is no number.
- **409**: the tenant's state refuses it, such as too little to spend, a key of another transfer, a wallet that is
  there already, or a pending transfer settled the other way.
- **500**: a failure of the server's own, such as its database's. Send the same request again.
