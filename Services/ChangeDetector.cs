using PulseLegacy.OnlineStoreSync.Models;

namespace PulseLegacy.OnlineStoreSync.Services;

public sealed class ChangeDetector
{
    public bool HasChanged(OnlineStoreItem item, IReadOnlyDictionary<string, AnugrahaItemState> stateByErpId)
    {
        if (!stateByErpId.TryGetValue(item.ErpId, out var state))
            return true;

        return item.ResolvedStock != state.Stock
            || item.ResolvedMrp != state.Mrp
            || item.ResolvedRate != state.Rate
            || item.AdvancedPrice != state.AdvancedPrice;
    }
}
