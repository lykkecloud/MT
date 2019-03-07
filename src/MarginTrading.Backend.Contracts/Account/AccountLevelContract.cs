namespace MarginTrading.Backend.Contracts.Account
{
    /// <summary>
    /// Account margin usage level
    /// </summary>
    public enum AccountLevelContract
    {
        None = 0,
        MarginCall1 = 1,
        MarginCall2 = 2,
        OvernightMarginCall = 3,
        StopOut = 4,
    }
}