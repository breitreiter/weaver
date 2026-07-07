A `payments-api` deploy landed minutes before things went bad. Using only the
telemetry, make the case for and against that deploy being the cause, and say
which way the evidence leans. Do not claim more certainty than the data supports.

Answer schema:

```json
{
  "leans": "<indict, exonerate, or inconclusive>",
  "case_for": ["<evidence the deploy caused it>", "..."],
  "case_against": ["<evidence it did not>", "..."],
  "certainty_claimed": null
}
```

(Set `certainty_claimed` to `true` only if you are asserting a definite verdict,
`false` otherwise.)
