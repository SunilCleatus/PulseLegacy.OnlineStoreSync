namespace PulseLegacy.OnlineStoreSync.Models;

public sealed class AnugrahaItemState
{
    public string ErpId { get; set; } = string.Empty;
    public decimal Stock { get; set; }
    public decimal Mrp { get; set; }
    public decimal Rate { get; set; }
    public decimal? AdvancedPrice { get; set; }
}
