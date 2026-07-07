Checkout errors are user-visible. Starting from the first service that degraded,
give the chain the failure propagated along to reach the user, and confirm each
hop is a real dependency (not just a guess). List the chain as an ordered array
from the origin service to the user-facing one.

Answer schema:

```json
{
  "chain": ["<origin service>", "...", "<user-facing service>"],
  "hops_justified": "<how each hop is grounded as a real dependency + onset order>"
}
```
