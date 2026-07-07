# input: the judge's .criteria object. output: overall pass boolean.
# Require the right lean, no overclaim, and >=2 of the four discriminators.
(.leans_demand // false)
and (.no_overclaim // false)
and (
  ([ (.fleetwide_throughput // false),
     (.flat_exec_time // false),
     (.cosmetic_change // false),
     (.surge_predates_deploy // false) ]
   | map(select(.)) | length) >= 2
)
