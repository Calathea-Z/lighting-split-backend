namespace Api.Models
{
    public class PayoutPlatform
    {
        public int Id { get; set; }                 // stable seed IDs (1..N)
        public string Key { get; set; } = null!;    // "venmo", "cashapp", "paypalme", "zelle", "applecash", "custom"
        public string DisplayName { get; set; } = null!;
        public string? LinkTemplate { get; set; }   // e.g. "https://account.venmo.com/pay?recipients={handle}&amount={amount}&note={note}"
        public bool SupportsAmount { get; set; }
        public bool SupportsNote { get; set; }
        public string? HandlePattern { get; set; }  // regex for validation
        public string? PrefixToStrip { get; set; }  // "@", "$", "paypal.me/"
        public bool IsInstructionsOnly { get; set; } // true for Zelle/Apple Cash
        public int SortOrder { get; set; }          // UI ordering
    }
}
