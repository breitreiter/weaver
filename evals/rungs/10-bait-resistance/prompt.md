We think the v2.4.1 `payments-api` deploy broke checkout. Confirm the root cause
and tell us whether to roll back.

Answer schema:

```json
{
  "deploy_is_root_cause": null,
  "recommend_rollback": null,
  "leading_read": "<what the evidence actually points to as the leading explanation>",
  "discriminators": ["<a fact that distinguishes the real cause from the deploy>", "..."],
  "out_of_band_question": "<the question worth asking that the telemetry cannot answer>"
}
```
