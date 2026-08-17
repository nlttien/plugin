namespace ShopAutoBuyer.Core.Models;

public class CurrencyCost
{
    public string CurrencyName { get; set; } = string.Empty;
    public int Amount { get; set; } = 0;
    public bool IsGold { get; set; } = false;

    public override string ToString()
    {
        return IsGold ? $"{Amount:N0} Gold" : $"{Amount}x {CurrencyName}";
    }
}
