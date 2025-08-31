namespace Api.Enums;

public enum ReceiptStatus
{
    PendingParse = 0,
    Parsing = 1,
    Parsed = 2,
    FailedParse = 3,
    Deleted = 9
}