# TCG Signal Inventory.Service

Inventory is an independent service because its workload is materially different from card pricing:

- store availability changes in minutes, not days;
- retailer integrations fail and evolve independently;
- a bad retailer adapter must not take down TCG Signal's pricing API;
- release-day traffic and polling need to scale independently;
- inventory claims require their own evidence and confidence rules.

## Accuracy contract

The service never fabricates quantity or marks a product in stock from a generic product page.

A listing is returned only when an inventory provider supplies store-specific evidence. The first provider is Best Buy's official Developer API, whose documented SKU availability endpoint returns only stores where the SKU is in stock and describes availability as near-real-time.

Best Buy low-stock observations are still shown as Confirmed, but receive a lower confidence score and are explicitly labeled low stock.

Target, Walmart and GameStop are represented as planned coverage until a source can meet the same evidence standard. This is intentional: an empty result is preferable to sending a collector to a store on a false positive.

## Privacy

User geolocation is passed into a request to calculate nearby stores and distance. TCG Signal does not persist the user's latitude/longitude. Only retailer store coordinates and short-lived inventory observations are stored.

## Configuration

- ConnectionStrings__DefaultConnection
- Inventory__BestBuyApiKey

The Best Buy key is wired from the Cloudflare Worker secret TCG_BESTBUY_API_KEY.
